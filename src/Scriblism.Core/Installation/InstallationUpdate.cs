using System.Security.Cryptography;

namespace Scriblism.Core.Installation;

public static class InstallationUpdate
{
    public static InstallManifest VerifySource(string source)
    {
        var manifest = InstallFiles.ReadManifest(source);
        foreach (var (name, hash) in manifest.Files)
            if (!Matches(InstallFiles.Resolve(source, name), hash))
                throw new InvalidDataException($"安裝檔案校驗失敗：{name}");
        // Microsoft.WinUI.dll rather than Microsoft.UI.Xaml.dll: the managed projection is
        // present in both layouts, while the native XAML binary only exists in the standalone
        // one, where the whole Windows App SDK is copied into the installation. Naming the
        // native binary here would refuse every shared-runtime release, including the upgrade
        // that converts an existing standalone installation into one.
        foreach (var required in new[] { "Scriblism.exe", "Scriblism.Setup.exe", "Uninstall.exe", "coreclr.dll", "Microsoft.WinUI.dll" })
            if (!manifest.Files.ContainsKey(required)) throw new InvalidDataException($"安裝資料夾缺少 {required}");
        return manifest;
    }

    // Only changed files are staged/backed up. Unchanged runtimes retain their files and timestamps.
    // Rollback covers file and registration errors; this is not a power-loss journal.
    public static void Apply(string source, string target, Action<InstallManifest> register)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("請從安裝位置以外的發布資料夾執行安裝程式。");
        var next = VerifySource(source);
        InstallFiles.RejectReparsePoints(target);
        var previous = File.Exists(Path.Combine(target, InstallFiles.ManifestName)) ? InstallFiles.ReadManifest(target) : null;
        if (previous is null && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException("目標資料夾不是受管理的 Scriblism 安裝。");
        var changed = next.Files.Where(pair => !Matches(InstallFiles.Resolve(target, pair.Key), pair.Value)).Select(pair => pair.Key).ToList();
        foreach (var name in next.Files.Keys)
        {
            if (File.Exists(InstallFiles.Resolve(target, name)) && previous is not null && !previous.Files.ContainsKey(name))
            {
                if (name.StartsWith("Uninstall.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(next.Product + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                throw new IOException($"更新會覆蓋自行加入的檔案：{name}");
            }
        }
        var removed = previous?.Files.Keys.Where(name => !next.Files.ContainsKey(name)).ToArray() ?? [];
        var parent = Path.GetDirectoryName(target)!;
        var work = Path.Combine(parent, ".scriblism-update-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(work, "new");
        var backup = Path.Combine(work, "old");
        var written = new List<string>();
        var backedUp = new List<string>();
        var rollbackFailed = false;
        try
        {
            foreach (var name in changed.Append(InstallFiles.ManifestName))
            {
                var destination = InstallFiles.Resolve(staged, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(InstallFiles.Resolve(source, name), destination);
                if (next.Files.TryGetValue(name, out var hash) && !Matches(destination, hash))
                    throw new IOException($"安裝來源在複製時變更：{name}");
            }
            Directory.CreateDirectory(target);
            foreach (var name in changed.Concat(removed).Append(InstallFiles.ManifestName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destination = InstallFiles.Resolve(target, name);
                if (File.Exists(destination))
                {
                    var saved = InstallFiles.Resolve(backup, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Move(destination, saved);
                    backedUp.Add(name);
                }
                var replacement = InstallFiles.Resolve(staged, name);
                if (File.Exists(replacement))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(replacement, destination);
                    written.Add(name);
                }
            }
            register(next);
        }
        catch (Exception failure)
        {
            try
            {
                foreach (var name in written.AsEnumerable().Reverse()) File.Delete(InstallFiles.Resolve(target, name));
                foreach (var name in backedUp.AsEnumerable().Reverse())
                    File.Move(InstallFiles.Resolve(backup, name), InstallFiles.Resolve(target, name));
            }
            catch (Exception rollback)
            {
                rollbackFailed = true;
                throw new AggregateException($"更新回復未完成，原檔案保留於 {backup}", failure, rollback);
            }
            throw;
        }
        finally
        {
            if (!rollbackFailed && Directory.Exists(work))
            {
                InstallFiles.RejectReparseTree(work);
                // Exact generated sibling; never delete the installation or its parent.
                if (Path.GetDirectoryName(work) != parent || !Path.GetFileName(work).StartsWith(".scriblism-update-", StringComparison.Ordinal))
                    throw new IOException("更新暫存位置不符。");
                try { Directory.Delete(work, true); }
                catch (IOException) { /* Successful update remains valid if temporary cleanup is delayed. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool Matches(string path, string hash)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Scriblism.Core.Installation;
using Microsoft.Win32;

namespace Scriblism.Setup;

public sealed class InstallationService
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scriblism";
    public static string? InstalledPath
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            var registered = InstallationDiscovery.FindExisting(key?.GetValue("InstallLocation") as string, DefaultPath);
            return registered ?? (IsLegacyInstallation(DefaultPath) ? DefaultPath : null);
        }
    }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Scriblism");
    private static string StartShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Scriblism.lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Scriblism.lnk");

    public static string ValidateTarget(string target)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (target.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetPathRoot(target) == target ||
            !Path.GetFileName(target).Equals("Scriblism", StringComparison.OrdinalIgnoreCase))
            throw new IOException("請選擇本機磁碟中名為 Scriblism 的專用資料夾。");
        InstallFiles.RejectReparsePoints(target);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (!string.Equals(target, InstalledPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("請選擇空資料夾，或更新目前登錄的安裝位置。");
            if (!IsLegacyInstallation(target)) InstallFiles.ReadManifest(target);
        }
        if (File.Exists(target)) throw new IOException("安裝位置已被檔案佔用。");
        return target;
    }

    public static bool HasDesktopShortcut
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("DesktopShortcut") is int value) return value == 1;
            if (!File.Exists(DesktopLink) || InstalledPath is not { } target) return false;
            var matches = false;
            WithShortcut(DesktopLink, shortcut => matches = string.Equals((string)shortcut.TargetPath,
                Path.Combine(target, "Scriblism.exe"), StringComparison.OrdinalIgnoreCase));
            return matches;
        }
    }

    public static void Install(string target, bool desktop)
    {
        target = ValidateTarget(target);
        var updating = InstalledPath is not null;
        if (updating && !string.Equals(target, InstalledPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("更新必須使用目前安裝位置。");
        if (!updating && (File.Exists(StartShortcut) || (desktop && File.Exists(DesktopLink))))
            throw new IOException("已存在 Scriblism 捷徑，請先移除或重新命名該捷徑。");
        InstalledAppShutdown.Stop(target);
        var oldStart = File.Exists(StartShortcut) ? File.ReadAllBytes(StartShortcut) : null;
        var oldDesktop = File.Exists(DesktopLink) ? File.ReadAllBytes(DesktopLink) : null;
        using var existing = Registry.CurrentUser.OpenSubKey(UninstallKey);
        var oldValues = existing?.GetValueNames().ToDictionary(name => name,
            name => (Value: existing.GetValue(name)!, Kind: existing.GetValueKind(name)));
        existing?.Close();
        string? legacyBackup = null;
        try
        {
            if (IsLegacyInstallation(target))
            {
                legacyBackup = target + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(target, legacyBackup);
            }
            InstallationUpdate.Apply(AppContext.BaseDirectory, target, manifest =>
            {
                CreateShortcut(StartShortcut, target);
                if (desktop) CreateShortcut(DesktopLink, target);
                using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
                key.SetValue("DisplayName", "Scriblism");
                key.SetValue("DisplayVersion", manifest.Version);
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", Path.Combine(target, "Scriblism.exe"));
                key.SetValue("UninstallString", MaintenanceLauncher.CreateCommand(target));
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("DesktopShortcut", desktop ? 1 : 0, RegistryValueKind.DWord);
            });
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            if (oldValues is not null)
            {
                using var restored = Registry.CurrentUser.CreateSubKey(UninstallKey);
                foreach (var (name, entry) in oldValues) restored.SetValue(name, entry.Value, entry.Kind);
            }
            if (oldStart is null) File.Delete(StartShortcut); else File.WriteAllBytes(StartShortcut, oldStart);
            if (oldDesktop is null) File.Delete(DesktopLink); else File.WriteAllBytes(DesktopLink, oldDesktop);
            if (legacyBackup is not null && Directory.Exists(legacyBackup))
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(legacyBackup, target);
            }
            throw;
        }
        // Older releases cached the entire packed installer. It is no longer needed.
        var legacyCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scriblism", "Installer");
        try
        {
            if (Directory.Exists(legacyCache))
            {
                InstallFiles.RejectReparseTree(legacyCache);
                Directory.Delete(legacyCache, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Uninstall(bool keepData)
    {
        var target = InstalledPath ?? throw new IOException("找不到 Scriblism 安裝紀錄。");
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!Path.GetFileName(target).Equals("Scriblism", StringComparison.OrdinalIgnoreCase))
            throw new IOException("安裝位置不符，已停止解除安裝。");
        InstallFiles.ReadManifest(target);
        if (string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("請從 Windows「已安裝的應用程式」啟動解除安裝。");
        InstalledAppShutdown.Stop(target);
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (run?.GetValue("Scriblism") is string command &&
                command.StartsWith($"\"{Path.Combine(target, "Scriblism.exe")}\"", StringComparison.OrdinalIgnoreCase))
                run.DeleteValue("Scriblism", false);
        }
        InstallFiles.RemoveInstallation(target, keepData);
        RemoveMatchingShortcut(StartShortcut, target);
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            if (key?.GetValue("DesktopShortcut") is int value && value == 1) RemoveMatchingShortcut(DesktopLink, target);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        if (!keepData)
        {
            var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var data = Path.GetFullPath(Path.Combine(localAppData, "Scriblism"));
            if (Path.GetDirectoryName(data) != localAppData) throw new IOException("本機資料位置不符，已停止清理。");
            if (Directory.Exists(data))
            {
                InstallFiles.RejectReparseTree(data);
                Directory.Delete(data, true);
            }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "uninstall-complete"), keepData ? "keep" : "remove");
    }

    private static void CreateShortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WithShortcut(path, shortcut =>
        {
            shortcut.TargetPath = Path.Combine(target, "Scriblism.exe");
            shortcut.IconLocation = Path.Combine(target, "Assets", "Scriblism.ico") + ",0";
            shortcut.WorkingDirectory = target;
            shortcut.Description = "Scriblism 文字、程式碼與 Markdown 編輯器";
            shortcut.Save();
        });
    }

    private static void RemoveMatchingShortcut(string path, string target)
    {
        if (!File.Exists(path)) return;
        var matches = false;
        WithShortcut(path, shortcut => matches = string.Equals((string)shortcut.TargetPath,
            Path.Combine(target, "Scriblism.exe"), StringComparison.OrdinalIgnoreCase));
        if (matches) File.Delete(path);
    }

    private static void WithShortcut(string path, Action<dynamic> action)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try { shortcut = ((dynamic)shell).CreateShortcut(path); action(shortcut); }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool IsLegacyInstallation(string target) =>
        File.Exists(Path.Combine(target, "scriblism-package.json")) &&
        File.Exists(Path.Combine(target, "scriblism-files.json")) &&
        File.Exists(Path.Combine(target, "Scriblism.exe"));
}

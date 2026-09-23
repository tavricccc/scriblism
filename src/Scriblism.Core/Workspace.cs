using System.Diagnostics;
using System.Text.Json;

namespace Scriblism.Core;

public sealed record FileEntry(string Path, string Name, bool IsDirectory, bool IsLink);
public sealed record DirectoryListing(IReadOnlyList<FileEntry> Entries, bool Truncated);
public static class Workspace
{
    public static DirectoryListing List(string path, bool showHidden = false)
    {
        var entries = new List<FileEntry>();
        var truncated = false;
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (!showHidden && (item.Name.StartsWith('.') || (item.Attributes & FileAttributes.Hidden) != 0)) continue;
            if (entries.Count >= 5000) { truncated = true; break; }
            entries.Add(new(item.FullName, item.Name, (item.Attributes & FileAttributes.Directory) != 0,
                (item.Attributes & FileAttributes.ReparsePoint) != 0));
        }
        return new(entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), truncated);
    }

    public static string ChildPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' '))
            throw new IOException("請輸入有效的檔案名稱，不要包含路徑或結尾的空白／句點。");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9')
            throw new IOException("這是 Windows 保留名稱，請使用其他名稱。");
        return Path.Combine(directory, name);
    }
}

public sealed class EditorSettings
{
    public string Theme { get; set; } = "Default";
    public double FontSize { get; set; } = 15;
    public bool WordWrap { get; set; } = true;
    public bool SidebarVisible { get; set; } = true;
    public bool ShowHidden { get; set; }
    public List<string> RecentFiles { get; set; } = [];
}

public sealed record RecoveryDocument(string? Path, string Name, string Text, string LanguageId,
    string EncodingName, bool Bom, string NewLine, string? ExpectedHash);
public sealed record RecoverySession(int ProcessId, long StartedTicks, DateTime SavedAt, IReadOnlyList<RecoveryDocument> Documents);

public sealed class LocalStateStore
{
    public string Root { get; }
    private readonly string _session;
    private readonly long _started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    public LocalStateStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scriblism");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "Recovery"));
        _session = Path.Combine(Root, "Recovery", $"{Environment.ProcessId}-{Guid.NewGuid():N}.json");
    }
    public EditorSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(Root, "settings.json");
            if (new FileInfo(path).Length > 1024 * 1024) return new();
            var settings = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path)) ?? new();
            settings.FontSize = double.IsFinite(settings.FontSize) ? Math.Clamp(settings.FontSize, 10, 32) : 15;
            settings.RecentFiles = (settings.RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(15).ToList();
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public async Task SaveSettingsAsync(EditorSettings settings)
    {
        await _settingsLock.WaitAsync();
        try { await AtomicJson(Path.Combine(Root, "settings.json"), settings); }
        finally { _settingsLock.Release(); }
    }
    public async Task SaveRecoveryAsync(IReadOnlyList<RecoveryDocument> documents)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (documents.Count == 0) { if (File.Exists(_session)) File.Delete(_session); return; }
            await AtomicJson(_session, new RecoverySession(Environment.ProcessId, _started, DateTime.UtcNow, documents));
        }
        finally { _writeLock.Release(); }
    }
    public IReadOnlyList<(string File, RecoverySession Session)> FindRecoverable()
    {
        var results = new List<(string, RecoverySession)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Recovery"), "*.json"))
        {
            if (file == _session) continue;
            try
            {
                if (new FileInfo(file).Length > 128 * 1024 * 1024) continue;
                var session = JsonSerializer.Deserialize<RecoverySession>(File.ReadAllText(file));
                if (session?.Documents is null || session.Documents.Count == 0 || IsAlive(session)) continue;
                if (session.Documents.Any(d => d is null || d.Text is null || d.Text.Length > TextFileStore.MaximumBytes || d.Name is null)) continue;
                results.Add((file, session));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return results;
    }
    private static bool IsAlive(RecoverySession session)
    {
        try { using var p = Process.GetProcessById(session.ProcessId); return p.StartTime.ToUniversalTime().Ticks == session.StartedTicks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
    public void AcknowledgeRecovery(string file)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(file)) == Path.Combine(Root, "Recovery")) File.Delete(file);
    }
    private static async Task AtomicJson<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

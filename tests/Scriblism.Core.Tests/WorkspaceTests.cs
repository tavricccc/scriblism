using System.Text.Json;
using Scriblism.Core;

namespace Scriblism.Core.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Scriblism.Tests", Guid.NewGuid().ToString("N"));
    public WorkspaceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    [Fact] public void DirectoriesAreFirstAndDotFilesOptional()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src")); File.WriteAllText(Path.Combine(_root, "a.cs"), ""); File.WriteAllText(Path.Combine(_root, ".env"), "");
        var entries = Workspace.List(_root); Assert.Equal(2, entries.Entries.Count); Assert.True(entries.Entries[0].IsDirectory);
        Assert.Equal(3, Workspace.List(_root, true).Entries.Count);
    }
    [Theory]
    [InlineData("../escape")][InlineData("..\\escape")][InlineData(".")][InlineData("..")] [InlineData("bad.")][InlineData("bad ")][InlineData("NUL.txt")][InlineData("CON")][InlineData("COM1")][InlineData("LPT9.log")]
    public void InvalidNamesAreRefused(string name) => Assert.Throws<IOException>(() => Workspace.ChildPath(_root, name));
    [Fact] public async Task SettingsRoundTrip()
    {
        var file = Path.Combine(_root, "a.cs");
        var store = new LocalStateStore(_root); var settings = new EditorSettings { Theme = "Dark", FontSize = 17,
            SourceFonts = "Consolas, Cascadia Mono", MarkdownFonts = "Segoe UI, Microsoft JhengHei UI", RecentFiles = ["a.cs"],
            ReopenFilesOnStartup = true, LastOpenFiles = [file], LastActiveFile = file };
        await store.SaveSettingsAsync(settings); var loaded = store.LoadSettings(); Assert.Equal("Dark", loaded.Theme); Assert.Equal(17, loaded.FontSize); Assert.Single(loaded.RecentFiles);
        Assert.Equal(settings.SourceFonts, loaded.SourceFonts); Assert.Equal(settings.MarkdownFonts, loaded.MarkdownFonts);
        Assert.True(loaded.ReopenFilesOnStartup); Assert.Equal([file], loaded.LastOpenFiles); Assert.Equal(file, loaded.LastActiveFile);
    }
    [Fact] public void FontListsKeepOrderAndRemoveInvalidEntries()
    {
        Assert.Equal("Consolas, Cascadia Mono", EditorSettings.NormalizeFonts(" Consolas, , Consolas, Cascadia Mono ", EditorSettings.DefaultSourceFonts));
        Assert.Equal(EditorSettings.DefaultSourceFonts, EditorSettings.NormalizeFonts("\n", EditorSettings.DefaultSourceFonts));
    }
    [Fact] public void CorruptSettingsUseDefaults()
    { var store = new LocalStateStore(_root); File.WriteAllText(Path.Combine(_root, "settings.json"), "bad json"); Assert.Equal(15, store.LoadSettings().FontSize); }
    [Fact] public async Task RecoveryIgnoresLiveSessionButFindsAbandonedOne()
    {
        var store = new LocalStateStore(_root);
        var document = new RecoveryDocument(null, "未命名", "unsaved", "text", "utf-8", false, "\n", null);
        await store.SaveRecoveryAsync([document]);
        Assert.Empty(new LocalStateStore(_root).FindRecoverable());
        var abandoned = Path.Combine(_root, "Recovery", "abandoned.json");
        await File.WriteAllTextAsync(abandoned, JsonSerializer.Serialize(new RecoverySession(int.MaxValue, 0, DateTime.UtcNow, [document])));
        var found = store.FindRecoverable(); Assert.Single(found); Assert.Equal("unsaved", found[0].Session.Documents[0].Text);
        store.AcknowledgeRecovery(abandoned); Assert.Empty(store.FindRecoverable());
        await store.SaveRecoveryAsync([]); Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Recovery")));
    }
    [Fact] public async Task RecoveryWritesAreSerialized()
    {
        var store = new LocalStateStore(_root); var doc = new RecoveryDocument(null, "x", "content", "text", "utf-8", false, "\n", null);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.SaveRecoveryAsync([doc])));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "Recovery")));
        Assert.NotNull(JsonSerializer.Deserialize<RecoverySession>(await File.ReadAllTextAsync(file)));
    }
}

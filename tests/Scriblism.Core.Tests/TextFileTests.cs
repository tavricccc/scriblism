using System.Text;
using Scriblism.Core;

namespace Scriblism.Core.Tests;

public sealed class TextFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Scriblism.Tests", Guid.NewGuid().ToString("N"));
    public TextFileTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData("utf-8", false, "\n")]
    [InlineData("utf-8", true, "\r\n")]
    [InlineData("utf-16", true, "\r\n")]
    [InlineData("utf-16BE", true, "\r")]
    [InlineData("utf-32", true, "\n")]
    [InlineData("utf-32BE", true, "\n")]
    public async Task EncodingAndNewLinesRoundTrip(string encoding, bool bom, string newline)
    {
        const string text = "繁體中文😀\n第二行\n\n";
        var path = Path.Combine(_directory, "文件.txt");
        var bytes = TextFileStore.Encode(text, encoding, bom, newline);
        await File.WriteAllBytesAsync(path, bytes);
        var file = await TextFileStore.ReadAsync(path);
        Assert.Equal(text, file.Text); Assert.Equal(bom, file.Bom); Assert.Equal(newline, file.NewLine);
        await TextFileStore.SaveAsync(path, file.Text, file.EncodingName, file.Bom, file.NewLine, file.Hash);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a\n")]
    [InlineData("a\n\n")]
    [InlineData("\n")]
    public void TrailingNewlinesAreNotTrimmed(string text)
    {
        var decoded = TextFileStore.Decode("test", Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, decoded.Text);
    }
    [Fact] public void MixedNewlinesDetected()
    {
        var file = TextFileStore.Decode("test", Encoding.UTF8.GetBytes("a\r\nb\nc\r\n"));
        Assert.True(file.MixedNewLines); Assert.Equal("\r\n", file.NewLine); Assert.Equal("a\nb\nc\n", file.Text);
    }
    [Fact] public void InvalidUtf8Refused() => Assert.Throws<IOException>(() => TextFileStore.Decode("test", [0xFF, 0x00, 0xFA]));
    [Fact] public void BinaryRefused() => Assert.Throws<IOException>(() => TextFileStore.Decode("test", [65, 0, 66]));
    [Theory]
    [InlineData("a\u2028b")][InlineData("a\u2029b")][InlineData("a\uFFFCb")]
    public void NativeIncompatibleUnicodeIsExplicitlyRefused(string text)
    { Assert.Throws<IOException>(() => TextFileStore.Decode("test", Encoding.UTF8.GetBytes(text))); }
    [Fact] public async Task InvalidTextNeverReplacesDestination()
    {
        var path = Path.Combine(_directory, "safe.txt");
        var original = await TextFileStore.SaveAsync(path, "safe", "utf-8", false, "\n", null);
        await Assert.ThrowsAsync<IOException>(() => TextFileStore.SaveAsync(path, "unsafe\u2028text", "utf-8", false, "\n", original.Hash));
        Assert.Equal("safe", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task SaveIsConflictAware()
    {
        var path = Path.Combine(_directory, "file.md");
        var original = await TextFileStore.SaveAsync(path, "original", "utf-8", false, "\n", null);
        await File.WriteAllTextAsync(path, "external");
        await Assert.ThrowsAsync<FileConflictException>(() => TextFileStore.SaveAsync(path, "mine", "utf-8", false, "\n", original.Hash));
        Assert.Equal("external", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
        var saved = await TextFileStore.SaveAsync(path, "mine", "utf-8", false, "\n", null, true);
        Assert.Equal("mine", saved.Text);
    }
    [Fact] public async Task DeletedOriginalRequiresConfirmation()
    {
        var path = Path.Combine(_directory, "file.md");
        var original = await TextFileStore.SaveAsync(path, "original", "utf-8", false, "\n", null);
        File.Delete(path);
        await Assert.ThrowsAsync<FileConflictException>(() => TextFileStore.SaveAsync(path, "mine", "utf-8", false, "\n", original.Hash));
    }
    [Fact] public async Task ExistingDestinationIsNotSilentlyOverwritten()
    {
        var path = Path.Combine(_directory, "file.md"); await File.WriteAllTextAsync(path, "keep");
        await Assert.ThrowsAsync<FileConflictException>(() => TextFileStore.SaveAsync(path, "replace", "utf-8", false, "\n", null));
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task ReadLimitIsExplicit()
    {
        var path = Path.Combine(_directory, "large.txt");
        using (var stream = File.Create(path)) stream.SetLength(TextFileStore.MaximumBytes + 1L);
        await Assert.ThrowsAsync<IOException>(() => TextFileStore.ReadAsync(path));
    }
    [Fact] public async Task SaveFailureLeavesOriginalUntouched()
    {
        var path = Path.Combine(_directory, "keep.txt");
        var original = await TextFileStore.SaveAsync(path, "keep", "utf-8", false, "\n", null);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => TextFileStore.SaveAsync(path, "new", "utf-8", false, "\n", original.Hash)); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }
}

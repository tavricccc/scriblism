using System.Security.Cryptography;
using System.Text;

namespace Scriblism.Core;

public sealed record TextFile(string Path, string Text, string EncodingName, bool Bom, string NewLine,
    string Hash, bool MixedNewLines);

public sealed class FileConflictException(string path) : IOException($"檔案已被其他程式修改或刪除：{path}");

public static class TextFileStore
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static async Task<TextFile> ReadAsync(string path, CancellationToken cancellation = default)
    {
        path = System.IO.Path.GetFullPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumBytes) throw new IOException("檔案超過 16 MiB，請使用大型檔案編輯器。");
        using var memory = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (memory.Length + count > MaximumBytes) throw new IOException("檔案超過 16 MiB。");
            memory.Write(buffer, 0, count);
        }
        return Decode(path, memory.ToArray());
    }

    public static TextFile Decode(string path, byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false, true);
        var skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        string raw;
        try { raw = encoding.GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) { throw new IOException("無法以 UTF-8 或帶 BOM 的 Unicode 讀取。為避免損壞內容，未開啟這個檔案。"); }
        ValidateEditableText(raw);
        var crlf = 0; var lf = 0; var cr = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\r') { if (i + 1 < raw.Length && raw[i + 1] == '\n') { crlf++; i++; } else cr++; }
            else if (raw[i] == '\n') lf++;
        }
        var newline = crlf >= lf && crlf >= cr && crlf > 0 ? "\r\n" : cr > lf ? "\r" : "\n";
        return new(path, Normalize(raw), encoding.WebName, skip > 0, newline,
            Convert.ToHexString(SHA256.HashData(bytes)), new[] { crlf, lf, cr }.Count(x => x > 0) > 1);
    }

    public static void ValidateEditableText(string text)
    {
        if (text.Any(c => c == '\0' || c is >= '\x01' and <= '\x08' || c is '\x0b' or '\x0c'))
            throw new IOException("內容含有二進位控制字元，無法作為文字編輯。");
        if (text.Any(c => c is '\u2028' or '\u2029' or '\uFFFC'))
            throw new IOException("內容含有 U+2028、U+2029 或 U+FFFC。Windows RichEdit 會改寫這些字元，為避免損壞，未開啟或貼上內容。");
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static byte[] Encode(string text, string encodingName, bool bom, string newline)
    {
        if (newline is not ("\n" or "\r\n" or "\r")) throw new ArgumentException("換行格式無效。", nameof(newline));
        if (!bom && !string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("UTF-16／32 必須保留 BOM，才能再次辨識編碼。", nameof(bom));
        Encoding encoding = encodingName.ToLowerInvariant() switch
        {
            "utf-16" => new UnicodeEncoding(false, bom, true),
            "utf-16BE" or "utf-16be" => new UnicodeEncoding(true, bom, true),
            "utf-32" => new UTF32Encoding(false, bom, true),
            "utf-32BE" or "utf-32be" => new UTF32Encoding(true, bom, true),
            "utf-8" => new UTF8Encoding(bom, true),
            _ => throw new ArgumentException("不支援的文字編碼。", nameof(encodingName))
        };
        var content = encoding.GetBytes(Normalize(text).Replace("\n", newline, StringComparison.Ordinal));
        return [.. encoding.GetPreamble(), .. content];
    }

    public static async Task<TextFile> SaveAsync(string path, string text, string encodingName, bool bom,
        string newline, string? expectedHash, bool overwrite = false, CancellationToken cancellation = default)
    {
        path = System.IO.Path.GetFullPath(path);
        ValidateEditableText(text);
        var bytes = Encode(text, encodingName, bom, newline);
        if (bytes.Length > MaximumBytes) throw new IOException("文件超過 16 MiB，無法儲存。");
        var saved = Decode(path, bytes); // Validate round-trip before touching the destination.
        if (!overwrite) await CheckConflict(path, expectedHash, cancellation);
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellation);
                await output.FlushAsync(cancellation);
                output.Flush(true);
            }
            // Recheck after writing the temporary file; never truncate the destination first.
            if (!overwrite) await CheckConflict(path, expectedHash, cancellation);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path, false);
            return saved;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task CheckConflict(string path, string? expectedHash, CancellationToken cancellation)
    {
        if (!File.Exists(path)) { if (expectedHash is not null) throw new FileConflictException(path); return; }
        if (expectedHash is null) throw new FileConflictException(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
        if (!StringComparer.Ordinal.Equals(actual, expectedHash)) throw new FileConflictException(path);
    }
}

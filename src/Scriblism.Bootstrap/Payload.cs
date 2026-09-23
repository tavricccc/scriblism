using System.IO.Compression;

namespace Scriblism.Bootstrap;

/// <summary>
/// The installation payload, carried at the end of this executable.
/// </summary>
/// <remarks>
/// Everything Scriblism installs used to sit in a <c>resources</c> folder beside the setup
/// executable, which meant an installer that stopped working the moment someone moved the one
/// file that looks like the installer. The payload now travels inside the executable itself.
///
/// It is appended after the file rather than embedded as a managed resource: the payload is
/// most of two hundred megabytes of already-compressed binaries, and putting that through the
/// compiler and the single-file bundler costs minutes of build time and a great deal of memory
/// to produce a byte-identical result. Appending also leaves the .NET single-file bundle
/// untouched, because the bundle is located from a header written into the host at publish
/// time rather than by scanning backwards from the end of the file.
///
/// The trailer is read backwards from the end: the magic identifies a file that carries a
/// payload at all, and the length says where it starts.
/// </remarks>
internal static class Payload
{
    /// <summary>Last bytes of the file, identifying an executable that carries a payload.</summary>
    private static readonly byte[] Magic = "SCRIBLISM-PAYLOAD"u8.ToArray();

    private const int TrailerLength = 8 + 17;

    /// <summary>Entries under this prefix are the Windows App SDK packages, not installed files.</summary>
    public const string RuntimePrefix = "runtime/";

    /// <summary>Files identical in both layouts, extracted whichever one is chosen.</summary>
    public const string CommonPrefix = "common/";

    /// <summary>The layout that uses the shared Windows App SDK runtime package.</summary>
    public const string SharedPrefix = "shared/";

    /// <summary>The layout that carries its own copy of the Windows App SDK.</summary>
    public const string StandalonePrefix = "standalone/";

    /// <summary>
    /// Opens the appended archive. The caller keeps it open for as long as it extracts from it;
    /// the underlying file is this running executable, which Windows already holds open.
    /// </summary>
    public static ZipArchive Open()
    {
        var executable = Environment.ProcessPath
            ?? throw new IOException("無法取得安裝程式的路徑。");

        var file = File.OpenRead(executable);
        try
        {
            if (file.Length < TrailerLength) throw new InvalidDataException("安裝程式不完整。");

            var trailer = new byte[TrailerLength];
            file.Position = file.Length - TrailerLength;
            file.ReadExactly(trailer);

            if (!trailer.AsSpan(8).SequenceEqual(Magic))
                throw new InvalidDataException("這個安裝程式沒有帶上安裝內容，請重新下載。");

            var length = BitConverter.ToInt64(trailer, 0);
            var start = file.Length - TrailerLength - length;
            if (length <= 0 || start < 0) throw new InvalidDataException("安裝內容的長度不合理，請重新下載。");

            // Handed to ZipArchive as a window over this file, so nothing is copied to open it.
            return new ZipArchive(new Window(file, start, length), ZipArchiveMode.Read);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Extracts every entry under one of <paramref name="prefixes"/> into
    /// <paramref name="destination"/>, with the prefix removed. Several prefixes are extracted
    /// in one call so the progress reported covers the whole job rather than restarting at each
    /// of them.
    /// </summary>
    public static void Extract(ZipArchive archive, string[] prefixes, string destination, Action<long, long>? progress = null)
    {
        var entries = archive.Entries
            .Select(entry => (Entry: entry, Prefix: prefixes.FirstOrDefault(prefix =>
                entry.FullName.StartsWith(prefix, StringComparison.Ordinal))))
            .Where(pair => pair.Prefix is not null && !pair.Entry.FullName.EndsWith('/'))
            .ToArray();

        var total = entries.Sum(pair => pair.Entry.Length);
        var done = 0L;

        foreach (var (entry, prefix) in entries)
        {
            var path = Resolve(destination, entry.FullName[prefix!.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
            done += entry.Length;
            progress?.Invoke(done, total);
        }
    }

    /// <summary>
    /// Refuses an entry name that would write outside the extraction folder. The archive is
    /// built by our own release script and travels inside a file nobody is expected to edit,
    /// but a path check costs nothing and the alternative is writing wherever a name says.
    /// </summary>
    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("安裝內容的檔案路徑不合法。");

        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(full, relative));
        if (!result.StartsWith(full, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安裝內容超出解開的資料夾。");
        return result;
    }

    /// <summary>A read-only slice of a stream, so the archive sees only the appended bytes.</summary>
    private sealed class Window(Stream inner, long offset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));

        public override int Read(Span<byte> buffer)
        {
            var available = (int)Math.Min(buffer.Length, length - _position);
            if (available <= 0) return 0;

            inner.Position = offset + _position;
            var read = inner.Read(buffer[..available]);
            _position += read;
            return read;
        }

        public override long Seek(long target, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => target,
                SeekOrigin.Current => _position + target,
                _ => length + target,
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

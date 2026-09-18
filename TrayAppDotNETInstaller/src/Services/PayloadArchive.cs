using System.Security.Cryptography;
using System.Text;

namespace TrayAppDotNETInstaller.Services;

/// <summary>One payload file inside the archive appended to a stamped installer.</summary>
public sealed record PayloadArchiveEntry(string FileName, long DataOffset, long DataLength, string DataSHA256);

/// <summary>
/// Read side of the container the stamping step appends after the factory PE image:
/// [ factory PE image ][ entry data 1 ][ entry data 2 ] ... [ directory ][ trailer ].
/// Every integer is little-endian. The trailer sits at the very end of the file so the running image can
/// find its own payload without parsing the PE headers, and the directory carries a SHA-256 per entry so a
/// truncated or patched download is rejected before anything is extracted.
/// </summary>
public sealed class PayloadArchive : IDisposable
{
    // Keep in sync with MagicBytes below
    public const string Magic = "TADNPKG1";
    public const int FormatVersion = 1;
    public const int TrailerLength = 56;

    internal const int MagicLength = 8;
    internal const int HashLength = 32;
    internal const int Int32Length = 4;
    internal const int Int64Length = 8;
    internal const int DirectoryHeaderLength = Int32Length + Int32Length;
    internal const int EntryFixedLength = Int32Length + Int64Length + Int64Length + HashLength;
    // One megabyte keeps a 70 MB bundle out of memory while still filling the disk queue
    internal const int CopyBufferLength = 1024 * 1024;
    // Sanity ceilings so a corrupt trailer can never make the reader allocate wildly
    private const int MaximumDirectoryLength = 1024 * 1024;
    private const int MaximumEntryCount = 256;
    private const int MaximumNameByteLength = 512;

    // The .NET Framework has no Span, so the u8 literal the reader used becomes a byte array. It is never
    // handed out past this assembly, and nothing writes through it.
    internal static byte[] MagicBytes { get; } = Encoding.ASCII.GetBytes(Magic);

    private readonly string _filePath;
    private readonly FileStream _stream;
    private bool _disposed;

    /// <summary>The appended payloads, in the order the stamping step wrote them.</summary>
    public IReadOnlyList<PayloadArchiveEntry> Entries { get; }

    private PayloadArchive(string filePath, FileStream stream, IReadOnlyList<PayloadArchiveEntry> entries)
    {
        _filePath = filePath;
        _stream = stream;
        Entries = entries;
    }

    /// <summary>
    /// Opens the archive appended to <paramref name="filePath"/>. Returns null, after logging the reason,
    /// when the file is too short, carries no trailer magic, or fails directory validation. Entry data is
    /// not hashed here; that happens in <see cref="OpenEntry"/>.
    /// </summary>
    public static PayloadArchive? TryOpen(string filePath, Action<string>? log = null)
    {
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));

        FileStream? stream = null;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            List<PayloadArchiveEntry>? entries = TryReadDirectory(stream, filePath, log);
            if (entries == null)
            {
                stream.Dispose();
                return null;
            }

            return new PayloadArchive(filePath, stream, entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Report(log, $"PayloadArchive: cannot read {filePath}: {exception.Message}");
            stream?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Verifies the entry's SHA-256 over its byte range and returns a fresh seekable read-only stream limited
    /// to that range. The stream owns its own file handle, so it is independently disposable and never
    /// disturbs another reader. Throws <see cref="InvalidDataException"/> when the hash does not match.
    /// </summary>
    public Stream OpenEntry(PayloadArchiveEntry entry)
    {
        FrameworkCompatibility.ThrowIfNull(entry, nameof(entry));
        FrameworkCompatibility.ThrowIfDisposed(_disposed, this);

        FileStream stream = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            string actualHash = ComputeRangeHash(stream, entry.DataOffset, entry.DataLength);
            if (!string.Equals(actualHash, entry.DataSHA256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The appended payload {entry.FileName} is corrupt: expected SHA-256 {entry.DataSHA256} but the file contains {actualHash}.");
            }

            return new PayloadEntryStream(stream, entry.DataOffset, entry.DataLength);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _stream.Dispose();
    }

    /// <summary>Reads and validates the trailer and directory. Returns null after logging any rejection.</summary>
    private static List<PayloadArchiveEntry>? TryReadDirectory(FileStream stream, string filePath, Action<string>? log)
    {
        long fileLength = stream.Length;
        if (fileLength < TrailerLength)
        {
            Report(log, $"PayloadArchive: {filePath} is {fileLength} bytes, shorter than the {TrailerLength} byte trailer");
            return null;
        }

        long trailerOffset = fileLength - TrailerLength;
        byte[] trailer = new byte[TrailerLength];
        stream.Position = trailerOffset;
        // FrameworkCompatibility stands in for Stream.ReadExactly and BinaryPrimitives, neither of which the
        // .NET Framework carries
        FrameworkCompatibility.ReadExactly(stream, trailer, offset: 0, TrailerLength);
        if (!FrameworkCompatibility.RangesEqual(trailer, leftOffset: 0, MagicBytes, rightOffset: 0, MagicLength))
        {
            Report(log, $"PayloadArchive: {filePath} carries no payload trailer");
            return null;
        }

        long directoryOffset = FrameworkCompatibility.ReadInt64LittleEndian(trailer, MagicLength);
        long directoryLength = FrameworkCompatibility.ReadInt64LittleEndian(trailer, MagicLength + Int64Length);
        if (directoryLength < DirectoryHeaderLength || directoryLength > MaximumDirectoryLength)
        {
            Report(log, $"PayloadArchive: {filePath} declares an implausible directory length of {directoryLength}");
            return null;
        }

        if (directoryOffset < 0 || directoryOffset > trailerOffset - directoryLength)
        {
            Report(log, $"PayloadArchive: {filePath} places its {directoryLength} byte directory at {directoryOffset}, outside the file");
            return null;
        }

        byte[] directoryBytes = new byte[(int)directoryLength];
        stream.Position = directoryOffset;
        FrameworkCompatibility.ReadExactly(stream, directoryBytes, offset: 0, directoryBytes.Length);
        byte[] actualDirectoryHash = FrameworkCompatibility.ComputeSHA256(directoryBytes);
        if (!FrameworkCompatibility.RangesEqual(
                actualDirectoryHash,
                leftOffset: 0,
                trailer,
                MagicLength + Int64Length + Int64Length,
                HashLength))
        {
            Report(log, $"PayloadArchive: the directory of {filePath} does not match its recorded SHA-256");
            return null;
        }

        return TryParseDirectory(directoryBytes, directoryOffset, filePath, log);
    }

    /// <summary>Parses the already hash-checked directory bytes, bounds-checking every entry range.</summary>
    private static List<PayloadArchiveEntry>? TryParseDirectory(
        byte[] directoryBytes,
        long directoryOffset,
        string filePath,
        Action<string>? log)
    {
        int formatVersion = FrameworkCompatibility.ReadInt32LittleEndian(directoryBytes, offset: 0);
        if (formatVersion != FormatVersion)
        {
            Report(log, $"PayloadArchive: {filePath} uses payload format {formatVersion}, but this installer understands {FormatVersion}");
            return null;
        }

        int entryCount = FrameworkCompatibility.ReadInt32LittleEndian(directoryBytes, Int32Length);
        if (entryCount < 0 || entryCount > MaximumEntryCount)
        {
            Report(log, $"PayloadArchive: {filePath} declares {entryCount} entries, which is out of range");
            return null;
        }

        List<PayloadArchiveEntry> entries = [];
        int cursor = DirectoryHeaderLength;
        for (int index = 0; index < entryCount; index++)
        {
            if (directoryBytes.Length - cursor < Int32Length)
            {
                Report(log, $"PayloadArchive: the directory of {filePath} ends inside entry {index}");
                return null;
            }

            int nameByteLength = FrameworkCompatibility.ReadInt32LittleEndian(directoryBytes, cursor);
            cursor += Int32Length;
            if (nameByteLength <= 0 || nameByteLength > MaximumNameByteLength)
            {
                Report(log, $"PayloadArchive: entry {index} of {filePath} declares a name of {nameByteLength} bytes");
                return null;
            }

            if (directoryBytes.Length - cursor < nameByteLength + EntryFixedLength - Int32Length)
            {
                Report(log, $"PayloadArchive: the directory of {filePath} ends inside entry {index}");
                return null;
            }

            string fileName = Encoding.UTF8.GetString(directoryBytes, cursor, nameByteLength);
            cursor += nameByteLength;
            long dataOffset = FrameworkCompatibility.ReadInt64LittleEndian(directoryBytes, cursor);
            cursor += Int64Length;
            long dataLength = FrameworkCompatibility.ReadInt64LittleEndian(directoryBytes, cursor);
            cursor += Int64Length;
            // FrameworkCompatibility.ToHexStringLower stands in for Convert.ToHexStringLower over a span
            string dataHash = FrameworkCompatibility.ToHexStringLower(directoryBytes, cursor, HashLength);
            cursor += HashLength;

            // Entry data always precedes the directory, so this also proves the range misses the trailer.
            // The comparisons are written as subtractions because the file supplies both operands.
            bool rangeIsInsideTheData =
                dataOffset >= 0 && dataLength >= 0 && dataOffset <= directoryOffset && dataLength <= directoryOffset - dataOffset;
            if (!rangeIsInsideTheData)
            {
                Report(log, $"PayloadArchive: entry {fileName} of {filePath} spans {dataOffset}+{dataLength}, outside the payload area");
                return null;
            }

            entries.Add(new PayloadArchiveEntry(fileName, dataOffset, dataLength, dataHash));
        }

        if (cursor != directoryBytes.Length)
        {
            Report(log, $"PayloadArchive: the directory of {filePath} has {directoryBytes.Length - cursor} trailing bytes");
            return null;
        }

        return entries;
    }

    /// <summary>Hashes a byte range in 1 MB chunks so a large payload never lands in memory whole.</summary>
    private static string ComputeRangeHash(FileStream stream, long dataOffset, long dataLength)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[CopyBufferLength];
        stream.Position = dataOffset;
        long remaining = dataLength;
        while (remaining > 0)
        {
            int wanted = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, wanted);
            if (read <= 0) throw new InvalidDataException("The appended payload ends before its recorded length.");

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return FrameworkCompatibility.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Rejections always reach somewhere: the caller's sink when it supplied one, else the log file.</summary>
    private static void Report(Action<string>? log, string message)
    {
        if (log == null)
        {
            InstallerLog.Write(message);
            return;
        }

        log(message);
    }

    /// <summary>
    /// Read-only window over one entry's byte range. It owns its own file handle so entries can be read
    /// concurrently and disposing one stream never moves another one's position.
    /// </summary>
    private sealed class PayloadEntryStream : Stream
    {
        private readonly FileStream _source;
        private readonly long _dataOffset;
        private readonly long _dataLength;
        private long _position;

        public PayloadEntryStream(FileStream source, long dataOffset, long dataLength)
        {
            _source = source;
            _dataOffset = dataOffset;
            _dataLength = dataLength;
            _source.Position = dataOffset;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _dataLength;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        // The .NET Framework Stream has no span overloads, so the byte array overload is the real
        // implementation and the span ones are gone
        public override int Read(byte[] buffer, int offset, int count)
        {
            FrameworkCompatibility.ThrowIfNull(buffer, nameof(buffer));
            if (offset < 0 || count < 0 || buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count), "The requested range lies outside the buffer.");

            long remaining = _dataLength - _position;
            if (remaining <= 0) return 0;

            int wanted = (int)Math.Min(count, remaining);
            _source.Position = _dataOffset + _position;
            int read = _source.Read(buffer, offset, wanted);
            _position += read;
            return read;
        }

        public override int ReadByte()
        {
            byte[] single = new byte[1];
            return Read(single, offset: 0, count: 1) == 0 ? -1 : single[0];
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _dataLength + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, message: "Unsupported seek origin.")
            };
            if (target < 0) throw new IOException("Cannot seek before the start of a payload entry.");

            _position = target;
            return _position;
        }

        // A read-only window has nothing buffered
        public override void Flush()
        {
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException("A payload entry stream is read-only.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("A payload entry stream is read-only.");

        protected override void Dispose(bool disposing)
        {
            if (disposing) _source.Dispose();

            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// Write side of the payload container. The stamping step copies the factory image, replaces its icon, and
/// then calls <see cref="Append"/> exactly once on that copy.
/// </summary>
public static class PayloadArchiveWriter
{
    /// <summary>
    /// Streams every payload in <paramref name="payloadFilePaths"/> onto the end of
    /// <paramref name="targetFilePath"/>, then writes the directory and trailer. The target must already hold
    /// the factory image and must not carry a payload archive yet.
    /// </summary>
    public static void Append(string targetFilePath, IReadOnlyList<string> payloadFilePaths, Action<string> log)
    {
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(targetFilePath, nameof(targetFilePath));
        FrameworkCompatibility.ThrowIfNull(payloadFilePaths, nameof(payloadFilePaths));
        FrameworkCompatibility.ThrowIfNull(log, nameof(log));

        if (payloadFilePaths.Count == 0)
            throw new ArgumentException("At least one payload is required.", nameof(payloadFilePaths));

        if (!File.Exists(targetFilePath))
            throw new FileNotFoundException($"The image to append to was not found: {targetFilePath}", targetFilePath);

        List<string> resolvedPaths = ResolvePayloadPaths(payloadFilePaths);
        using (PayloadArchive? existing = PayloadArchive.TryOpen(targetFilePath, log))
        {
            if (existing != null)
            {
                throw new InvalidOperationException(
                    $"{targetFilePath} already carries a payload archive; append refuses to stamp a stamped image.");
            }
        }

        List<PayloadArchiveEntry> entries = [];
        using FileStream target = new(targetFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        target.Position = target.Length;
        byte[] buffer = new byte[PayloadArchive.CopyBufferLength];
        foreach (string payloadPath in resolvedPaths)
        {
            entries.Add(AppendOne(target, payloadPath, buffer, log));
        }

        byte[] directoryBytes = BuildDirectory(entries);
        long directoryOffset = target.Position;
        // The .NET Framework Stream has no span-shaped Write, so every call carries an explicit range
        target.Write(directoryBytes, offset: 0, directoryBytes.Length);
        target.Write(PayloadArchive.MagicBytes, offset: 0, PayloadArchive.MagicLength);
        WriteInt64(target, directoryOffset);
        WriteInt64(target, directoryBytes.Length);
        byte[] directoryHash = FrameworkCompatibility.ComputeSHA256(directoryBytes);
        target.Write(directoryHash, offset: 0, directoryHash.Length);
        target.Flush();
        log($"PayloadArchive: wrote a {directoryBytes.Length} byte directory for {entries.Count} payload(s) at offset {directoryOffset}");
    }

    /// <summary>Validates the payload list and returns absolute paths in the order given.</summary>
    private static List<string> ResolvePayloadPaths(IReadOnlyList<string> payloadFilePaths)
    {
        List<string> resolvedPaths = [];
        HashSet<string> seenFileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string payloadFilePath in payloadFilePaths)
        {
            if (string.IsNullOrWhiteSpace(payloadFilePath))
                throw new ArgumentException("A payload path is empty.", nameof(payloadFilePaths));

            string fullPath = Path.GetFullPath(payloadFilePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Payload not found: {fullPath}", fullPath);

            string fileName = Path.GetFileName(fullPath);
            if (!seenFileNames.Add(fileName))
            {
                throw new ArgumentException(
                    $"Two payloads share the file name {fileName}; appended names must be unique.",
                    nameof(payloadFilePaths));
            }

            resolvedPaths.Add(fullPath);
        }

        return resolvedPaths;
    }

    /// <summary>Copies one payload onto the end of the target while hashing it in 1 MB chunks.</summary>
    private static PayloadArchiveEntry AppendOne(FileStream target, string payloadPath, byte[] buffer, Action<string> log)
    {
        long dataOffset = target.Position;
        long dataLength = 0;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (FileStream source = new(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                target.Write(buffer, 0, read);
                dataLength += read;
            }
        }

        string fileName = Path.GetFileName(payloadPath);
        log($"PayloadArchive: appended {fileName}, {dataLength} bytes at offset {dataOffset}");
        return new PayloadArchiveEntry(fileName, dataOffset, dataLength, FrameworkCompatibility.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>Serializes the directory: format version, entry count, then one record per entry.</summary>
    private static byte[] BuildDirectory(List<PayloadArchiveEntry> entries)
    {
        using MemoryStream directoryStream = new();
        WriteInt32(directoryStream, PayloadArchive.FormatVersion);
        WriteInt32(directoryStream, entries.Count);
        foreach (PayloadArchiveEntry entry in entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FileName);
            WriteInt32(directoryStream, nameBytes.Length);
            directoryStream.Write(nameBytes, offset: 0, nameBytes.Length);
            WriteInt64(directoryStream, entry.DataOffset);
            WriteInt64(directoryStream, entry.DataLength);
            // FrameworkCompatibility.FromHexString stands in for Convert.FromHexString
            byte[] hashBytes = FrameworkCompatibility.FromHexString(entry.DataSHA256);
            directoryStream.Write(hashBytes, offset: 0, hashBytes.Length);
        }

        return directoryStream.ToArray();
    }

    // A short heap buffer stands in for the stackalloc span the modern BinaryPrimitives calls used
    private static void WriteInt32(Stream stream, int value)
    {
        byte[] scratch = new byte[PayloadArchive.Int32Length];
        FrameworkCompatibility.WriteInt32LittleEndian(scratch, offset: 0, value);
        stream.Write(scratch, offset: 0, scratch.Length);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        byte[] scratch = new byte[PayloadArchive.Int64Length];
        FrameworkCompatibility.WriteInt64LittleEndian(scratch, offset: 0, value);
        stream.Write(scratch, offset: 0, scratch.Length);
    }
}

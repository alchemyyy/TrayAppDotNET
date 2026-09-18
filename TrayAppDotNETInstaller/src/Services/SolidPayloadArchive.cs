using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TrayAppDotNETInstaller.Compression;

namespace TrayAppDotNETInstaller.Services;

/// <summary>One file or directory inside a solid payload.</summary>
public sealed record SolidPayloadEntry(string Name, bool IsDirectory, long Length);

/// <summary>The fixed header and entry table of a solid payload, read before anything is decompressed.</summary>
public sealed record SolidPayloadHeader(
    long RawLength,
    string RawSHA256,
    byte[] Properties,
    IReadOnlyList<SolidPayloadEntry> Entries,
    long CompressedDataOffset);

/// <summary>What packing one solid payload achieved, for the stamping log.</summary>
public sealed record SolidPayloadStatistics(int EntryCount, long RawLength, long PackedLength);

/// <summary>
/// The container a stamped installer carries instead of a release zip.
///
/// A zip compresses every entry on its own with DEFLATE and a 32 KB window, which throws away almost all of
/// the redundancy inside a 30 MB ahead of time compiled executable and all of the redundancy between files.
/// This container concatenates the file data and runs one LZMA stream over the whole thing, so matches reach
/// across the entire payload. On a release package that is worth roughly a sixth of the compressed size.
///
/// Layout:
///   [ 72 byte header ][ entry table, uncompressed ][ LZMA stream over the concatenated file data ]
/// The table stays uncompressed so the contents can be listed, and the payload verified against its recorded
/// length, without paying for a decompression pass.
/// </summary>
public static class SolidPayloadArchive
{
    public const string Magic = "TADNSLD1";
    public const int FormatVersion = 1;
    public const int HeaderLength = 72;
    public const string FileExtension = ".tadn";

    // Packing needs the whole payload resident plus the match finder tables. Anything past this is left as a
    // plain zip rather than risking the build machine.
    public const long MaximumUncompressedLength = 512L * 1024 * 1024;

    internal const int MagicLength = 8;
    internal const int HashLength = 32;
    internal const int Int32Length = 4;
    internal const int Int64Length = 8;
    private const int MagicOffset = 0;
    private const int FormatVersionOffset = 8;
    private const int EntryCountOffset = 12;
    private const int TableLengthOffset = 16;
    private const int RawLengthOffset = 24;
    private const int PropertiesOffset = 32;
    private const int RawHashOffset = 40;
    private const int DirectoryAttribute = 1;
    private const int MaximumEntryCount = 65536;
    private const int MaximumTableLength = 8 * 1024 * 1024;
    private const int MaximumNameByteLength = 1024;
    private const int CopyBufferLength = 1024 * 1024;
    private const char EntrySeparator = '/';

    private static byte[] MagicBytes { get; } = Encoding.ASCII.GetBytes(Magic);

    /// <summary>True when the leading bytes of a payload are a solid archive rather than a zip.</summary>
    public static bool StartsWithMagic(byte[] leadingBytes, int length)
    {
        FrameworkCompatibility.ThrowIfNull(leadingBytes, nameof(leadingBytes));

        return length >= MagicLength
               && FrameworkCompatibility.RangesEqual(leadingBytes, leftOffset: 0, MagicBytes, rightOffset: 0, MagicLength);
    }

    /// <summary>Counts what packing a zip would have to hold resident, without reading any entry data.</summary>
    public static void MeasureZip(string zipFilePath, out int entryCount, out long uncompressedLength)
    {
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(zipFilePath, nameof(zipFilePath));

        entryCount = 0;
        uncompressedLength = 0;
        using ZipArchive archive = ZipFile.OpenRead(zipFilePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            entryCount++;
            uncompressedLength += entry.Length;
        }
    }

    /// <summary>
    /// Repacks <paramref name="sourceZipFilePath"/> as a solid payload at <paramref name="destinationFilePath"/>.
    /// <paramref name="shouldInclude"/> receives each normalized entry name and decides whether it travels.
    /// </summary>
    public static SolidPayloadStatistics Create(
        string sourceZipFilePath,
        string destinationFilePath,
        Func<string, bool> shouldInclude,
        Action<string> log)
    {
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(sourceZipFilePath, nameof(sourceZipFilePath));
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(destinationFilePath, nameof(destinationFilePath));
        FrameworkCompatibility.ThrowIfNull(shouldInclude, nameof(shouldInclude));
        FrameworkCompatibility.ThrowIfNull(log, nameof(log));

        using ZipArchive archive = ZipFile.OpenRead(sourceZipFilePath);
        List<SolidPayloadEntry> entries = [];
        List<ZipArchiveEntry> dataEntries = [];
        long rawLength = 0;
        foreach (ZipArchiveEntry zipEntry in archive.Entries)
        {
            string name = NormalizeName(zipEntry.FullName);
            if (name.Length == 0) continue;
            if (!shouldInclude(name))
            {
                log($"SolidPayloadArchive: skipping {name}");
                continue;
            }

            ValidateName(name);
            if (IsDirectoryName(zipEntry.FullName))
            {
                entries.Add(new SolidPayloadEntry(name, IsDirectory: true, Length: 0));
                continue;
            }

            entries.Add(new SolidPayloadEntry(name, IsDirectory: false, zipEntry.Length));
            dataEntries.Add(zipEntry);
            rawLength += zipEntry.Length;
        }

        if (entries.Count == 0)
            throw new InvalidDataException($"{sourceZipFilePath} holds no entries to pack.");

        if (rawLength > MaximumUncompressedLength)
        {
            throw new InvalidDataException(
                $"{sourceZipFilePath} expands to {rawLength} bytes, past the {MaximumUncompressedLength} byte solid packing limit.");
        }

        // The whole payload has to be resident: the match finder addresses it as one buffer, which is what
        // lets matches reach across file boundaries
        byte[] body = new byte[rawLength];
        int cursor = 0;
        foreach (ZipArchiveEntry zipEntry in dataEntries)
        {
            using Stream source = zipEntry.Open();
            int wanted = checked((int)zipEntry.Length);
            FrameworkCompatibility.ReadExactly(source, body, cursor, wanted);
            cursor += wanted;
        }

        byte[] table = BuildTable(entries);
        byte[] rawHash = FrameworkCompatibility.ComputeSHA256(body);

        using FileStream destination = new(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        // The header is written last because the properties are only known once the body is compressed
        destination.Position = HeaderLength + table.Length;
        byte[] properties = LzmaEncoder.Compress(body, body.Length, destination);
        long packedLength = destination.Length;

        destination.Position = 0;
        destination.Write(BuildHeader(entries.Count, table.Length, rawLength, rawHash, properties), offset: 0, HeaderLength);
        destination.Write(table, offset: 0, table.Length);
        destination.Flush();

        log($"SolidPayloadArchive: packed {entries.Count} entries, {rawLength} bytes into {packedLength} "
            + $"({PercentText(packedLength, rawLength)})");
        return new SolidPayloadStatistics(entries.Count, rawLength, packedLength);
    }

    /// <summary>
    /// Reads and validates the header and entry table. The stream is left at the first compressed byte.
    /// Throws <see cref="InvalidDataException"/> for anything that does not parse.
    /// </summary>
    public static SolidPayloadHeader Read(Stream stream)
    {
        FrameworkCompatibility.ThrowIfNull(stream, nameof(stream));

        byte[] header = new byte[HeaderLength];
        FrameworkCompatibility.ReadExactly(stream, header, offset: 0, HeaderLength);
        if (!StartsWithMagic(header, HeaderLength))
            throw new InvalidDataException("The payload does not begin with a solid archive header.");

        int formatVersion = FrameworkCompatibility.ReadInt32LittleEndian(header, FormatVersionOffset);
        if (formatVersion != FormatVersion)
        {
            throw new InvalidDataException(
                $"The payload uses solid archive format {formatVersion}, but this installer understands {FormatVersion}.");
        }

        int entryCount = FrameworkCompatibility.ReadInt32LittleEndian(header, EntryCountOffset);
        if (entryCount <= 0 || entryCount > MaximumEntryCount)
            throw new InvalidDataException($"The payload declares {entryCount} entries, which is out of range.");

        int tableLength = FrameworkCompatibility.ReadInt32LittleEndian(header, TableLengthOffset);
        if (tableLength <= 0 || tableLength > MaximumTableLength)
            throw new InvalidDataException($"The payload declares a {tableLength} byte entry table, which is out of range.");

        long rawLength = FrameworkCompatibility.ReadInt64LittleEndian(header, RawLengthOffset);
        if (rawLength < 0)
            throw new InvalidDataException($"The payload declares a negative uncompressed length of {rawLength}.");

        byte[] properties = new byte[LzmaConstants.PropertiesLength];
        Array.Copy(header, PropertiesOffset, properties, destinationIndex: 0, properties.Length);
        string rawHash = FrameworkCompatibility.ToHexStringLower(header, RawHashOffset, HashLength);

        byte[] table = new byte[tableLength];
        FrameworkCompatibility.ReadExactly(stream, table, offset: 0, tableLength);
        List<SolidPayloadEntry> entries = ParseTable(table, entryCount, rawLength);
        return new SolidPayloadHeader(rawLength, rawHash, properties, entries, HeaderLength + tableLength);
    }

    /// <summary>
    /// Decompresses the payload. When <paramref name="destinationDirectory"/> is null nothing is written and
    /// the pass only proves the stream decodes to the recorded hash, which is what verification needs.
    /// Returns the number of files written.
    /// </summary>
    public static int Extract(
        Stream stream,
        SolidPayloadHeader header,
        string? destinationDirectory,
        Action<int, SolidPayloadEntry>? onEntry,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(stream, nameof(stream));
        FrameworkCompatibility.ThrowIfNull(header, nameof(header));

        string? destinationRoot = null;
        if (destinationDirectory != null)
        {
            destinationRoot = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
            Directory.CreateDirectory(destinationRoot);
            CreateDirectoryEntries(header, destinationRoot);
        }

        using SolidExtractionStream sink = new(header, destinationRoot, onEntry, cancellationToken);
        LzmaDecoder.Decode(stream, sink, header.Properties, header.RawLength);
        sink.Complete();
        return sink.FilesWritten;
    }

    /// <summary>Normalizes a zip entry path to the separator and shape the table stores.</summary>
    internal static string NormalizeName(string entryFullName) =>
        entryFullName.Replace('\\', EntrySeparator).TrimEnd(EntrySeparator);

    private static bool IsDirectoryName(string entryFullName) =>
        entryFullName.Length > 0 && (entryFullName[^1] == EntrySeparator || entryFullName[^1] == '\\');

    /// <summary>
    /// Rejects anything that could resolve outside the destination. Checked while the table is parsed, so a
    /// hostile payload is refused before a single byte is decompressed.
    /// </summary>
    private static void ValidateName(string name)
    {
        if (name.Length == 0 || name.Length > MaximumNameByteLength)
            throw new InvalidDataException($"A solid payload entry name of {name.Length} characters is out of range.");

        if (name[0] == EntrySeparator || name.Contains(":", StringComparison.Ordinal))
            throw new InvalidDataException($"The solid payload entry {name} is not a relative path.");

        foreach (string segment in name.Split(EntrySeparator))
        {
            if (segment.Length != 0 && segment != ".." && segment != ".") continue;

            throw new InvalidDataException($"The solid payload entry {name} contains a traversal segment.");
        }

        if (name.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidDataException($"The solid payload entry {name} contains characters a path cannot hold.");
    }

    private static byte[] BuildTable(List<SolidPayloadEntry> entries)
    {
        using MemoryStream table = new();
        byte[] scratch = new byte[Int64Length];
        foreach (SolidPayloadEntry entry in entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            if (nameBytes.Length > MaximumNameByteLength)
                throw new InvalidDataException($"The entry name {entry.Name} does not fit the table.");

            FrameworkCompatibility.WriteInt32LittleEndian(scratch, offset: 0, nameBytes.Length);
            table.Write(scratch, offset: 0, Int32Length);
            table.Write(nameBytes, offset: 0, nameBytes.Length);
            FrameworkCompatibility.WriteInt32LittleEndian(scratch, offset: 0, entry.IsDirectory ? DirectoryAttribute : 0);
            table.Write(scratch, offset: 0, Int32Length);
            FrameworkCompatibility.WriteInt64LittleEndian(scratch, offset: 0, entry.Length);
            table.Write(scratch, offset: 0, Int64Length);
        }

        return table.ToArray();
    }

    private static List<SolidPayloadEntry> ParseTable(byte[] table, int entryCount, long rawLength)
    {
        List<SolidPayloadEntry> entries = [];
        long totalDataLength = 0;
        int cursor = 0;
        for (int index = 0; index < entryCount; index++)
        {
            if (table.Length - cursor < Int32Length)
                throw new InvalidDataException($"The solid payload table ends inside entry {index}.");

            int nameByteLength = FrameworkCompatibility.ReadInt32LittleEndian(table, cursor);
            cursor += Int32Length;
            if (nameByteLength <= 0 || nameByteLength > MaximumNameByteLength)
                throw new InvalidDataException($"Entry {index} declares a name of {nameByteLength} bytes.");

            if (table.Length - cursor < nameByteLength + Int32Length + Int64Length)
                throw new InvalidDataException($"The solid payload table ends inside entry {index}.");

            string name = Encoding.UTF8.GetString(table, cursor, nameByteLength);
            cursor += nameByteLength;
            int attributes = FrameworkCompatibility.ReadInt32LittleEndian(table, cursor);
            cursor += Int32Length;
            long length = FrameworkCompatibility.ReadInt64LittleEndian(table, cursor);
            cursor += Int64Length;

            ValidateName(name);
            bool isDirectory = (attributes & DirectoryAttribute) != 0;
            if (length < 0 || (isDirectory && length != 0))
                throw new InvalidDataException($"Entry {name} declares an impossible length of {length}.");

            totalDataLength += length;
            entries.Add(new SolidPayloadEntry(name, isDirectory, length));
        }

        if (cursor != table.Length)
            throw new InvalidDataException($"The solid payload table has {table.Length - cursor} trailing bytes.");

        if (totalDataLength != rawLength)
        {
            throw new InvalidDataException(
                $"The solid payload entries add up to {totalDataLength} bytes but the header declares {rawLength}.");
        }

        return entries;
    }

    private static byte[] BuildHeader(int entryCount, int tableLength, long rawLength, byte[] rawHash, byte[] properties)
    {
        byte[] header = new byte[HeaderLength];
        Array.Copy(MagicBytes, sourceIndex: 0, header, MagicOffset, MagicLength);
        FrameworkCompatibility.WriteInt32LittleEndian(header, FormatVersionOffset, FormatVersion);
        FrameworkCompatibility.WriteInt32LittleEndian(header, EntryCountOffset, entryCount);
        FrameworkCompatibility.WriteInt32LittleEndian(header, TableLengthOffset, tableLength);
        FrameworkCompatibility.WriteInt64LittleEndian(header, RawLengthOffset, rawLength);
        Array.Copy(properties, sourceIndex: 0, header, PropertiesOffset, properties.Length);
        Array.Copy(rawHash, sourceIndex: 0, header, RawHashOffset, HashLength);
        return header;
    }

    /// <summary>Creates the recorded directories up front so empty ones survive the round trip.</summary>
    private static void CreateDirectoryEntries(SolidPayloadHeader header, string destinationRoot)
    {
        foreach (SolidPayloadEntry entry in header.Entries)
        {
            if (!entry.IsDirectory) continue;

            Directory.CreateDirectory(ResolveEntryPath(destinationRoot, entry.Name));
        }
    }

    /// <summary>Resolves an entry against the destination and refuses anything that escapes it.</summary>
    private static string ResolveEntryPath(string destinationRoot, string name)
    {
        string resolved = Path.GetFullPath(Path.Combine(destinationRoot, name.Replace(EntrySeparator, Path.DirectorySeparatorChar)));
        string prefix = destinationRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The solid payload entry {name} resolves outside its destination.");

        return resolved;
    }

    private static string PercentText(long packedLength, long rawLength) =>
        rawLength == 0 ? "n/a" : $"{100.0 * packedLength / rawLength:F1}%";

    /// <summary>
    /// The sink the decoder writes into. It walks the entry table as bytes arrive, so no part of the payload
    /// is ever buffered beyond the decoder's own dictionary, and hashes everything so a decoder fault cannot
    /// pass unnoticed.
    /// </summary>
    private sealed class SolidExtractionStream : Stream
    {
        private readonly SolidPayloadHeader _header;
        private readonly string? _destinationRoot;
        private readonly Action<int, SolidPayloadEntry>? _onEntry;
        private readonly CancellationToken _cancellationToken;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        private int _entryIndex = -1;
        private long _entryRemaining;
        private FileStream? _entryStream;
        private long _written;
        private bool _disposed;

        public int FilesWritten { get; private set; }

        public SolidExtractionStream(
            SolidPayloadHeader header,
            string? destinationRoot,
            Action<int, SolidPayloadEntry>? onEntry,
            CancellationToken cancellationToken)
        {
            _header = header;
            _destinationRoot = destinationRoot;
            _onEntry = onEntry;
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _header.RawLength;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException("A solid payload sink cannot seek.");
        }

        // The .NET Framework Stream has no span overloads, so the byte array overload is the implementation
        public override void Write(byte[] buffer, int offset, int count)
        {
            FrameworkCompatibility.ThrowIfNull(buffer, nameof(buffer));
            if (offset < 0 || count < 0 || buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count), "The supplied range lies outside the buffer.");

            _hash.AppendData(buffer, offset, count);
            _written += count;
            int cursor = offset;
            int end = offset + count;
            while (cursor < end)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_entryRemaining == 0 && !TryOpenNextEntry())
                    throw new InvalidDataException("The solid payload decoded more bytes than its entries account for.");

                int chunk = (int)Math.Min(_entryRemaining, end - cursor);
                _entryStream?.Write(buffer, cursor, chunk);
                _entryRemaining -= chunk;
                cursor += chunk;
                if (_entryRemaining == 0) CloseCurrentEntry();
            }
        }

        /// <summary>Finishes the last entry and checks that the payload decoded to exactly what its header recorded.</summary>
        public void Complete()
        {
            CloseCurrentEntry();
            // The write loop only advances while bytes are arriving, so entries carrying no data that trail
            // the last byte of payload are still waiting. Draining creates them; finding another entry that
            // wants data means the stream ended early.
            if (TryOpenNextEntry())
            {
                throw new InvalidDataException(
                    $"The solid payload ended before {_header.Entries[_entryIndex].Name} received its {_entryRemaining} bytes.");
            }

            if (_written != _header.RawLength)
            {
                throw new InvalidDataException(
                    $"The solid payload decoded to {_written} bytes but its header declares {_header.RawLength}.");
            }

            string actualHash = FrameworkCompatibility.ToHexStringLower(_hash.GetHashAndReset());
            if (string.Equals(actualHash, _header.RawSHA256, StringComparison.OrdinalIgnoreCase)) return;

            throw new InvalidDataException(
                $"The solid payload decoded to SHA-256 {actualHash} but its header records {_header.RawSHA256}.");
        }

        public override void Flush() => _entryStream?.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("A solid payload sink is write only.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("A solid payload sink cannot seek.");

        public override void SetLength(long value) =>
            throw new NotSupportedException("A solid payload sink cannot be resized.");

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _entryStream?.Dispose();
                _entryStream = null;
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Advances to the next entry that carries data, creating its file when extracting.</summary>
        private bool TryOpenNextEntry()
        {
            while (_entryIndex + 1 < _header.Entries.Count)
            {
                _entryIndex++;
                SolidPayloadEntry entry = _header.Entries[_entryIndex];
                if (entry.IsDirectory || entry.Length == 0)
                {
                    _onEntry?.Invoke(_entryIndex, entry);
                    if (!entry.IsDirectory) CreateEmptyFile(entry);
                    continue;
                }

                _onEntry?.Invoke(_entryIndex, entry);
                _entryRemaining = entry.Length;
                if (_destinationRoot == null) return true;

                string path = ResolveEntryPath(_destinationRoot, entry.Name);
                string? parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                _entryStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferLength);
                return true;
            }

            return false;
        }

        private void CreateEmptyFile(SolidPayloadEntry entry)
        {
            if (_destinationRoot == null) return;

            string path = ResolveEntryPath(_destinationRoot, entry.Name);
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            using FileStream empty = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            empty.Flush();
            FilesWritten++;
        }

        private void CloseCurrentEntry()
        {
            if (_entryStream == null) return;

            _entryStream.Dispose();
            _entryStream = null;
            FilesWritten++;
        }
    }
}

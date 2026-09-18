using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Covers the solid LZMA container a stamped installer carries instead of a release zip: packing a zip,
/// parsing the header and entry table, extracting, and every way a damaged or hostile payload is refused.
/// </summary>
public sealed class SolidPayloadArchiveTests : IDisposable
{
    private const int DeterministicSeed = 20260917;
    private const int PatternLength = 4096;

    // Past 2 MB so the payload outgrows the starting dictionary and the match finder has to produce real far
    // reaching matches rather than literals. The decoder sizes its output window to the smaller of the
    // dictionary and the declared length, so a payload this size reaches the window's flush boundary in one
    // pass: the sink receives the whole thing as a single write it has to split across both entries.
    private const int LargeEntryLength = (2 * 1024 * 1024) + 333;

    // Incompressible enough that the range coded body is large, which is what the tamper test needs
    private const int TamperEntryLength = 64 * 1024;

    private const string InstallScriptName = "install.bat";
    private const string ExecutableEntryName = "FakeTrayAppDotNET.exe";
    private const string ApplicationName = "FakeTrayAppDotNET";
    private const string PayloadName = "package.tadn";
    private const string SourceZipName = "package.zip";

    // Header layout, mirrored from the documentation in SolidPayloadArchive: magic at 0, format version at
    // 8, entry count at 12, table length at 16, raw length at 24, LZMA properties at 32, raw SHA-256 at 40.
    // The entry table follows the 72 byte header, each record being a name length, the UTF-8 name, an
    // attribute word and a 64 bit length.
    private const int FormatVersionOffset = 8;
    private const int TableLengthOffset = 16;
    private const int RawLengthOffset = 24;
    private const int RawHashOffset = 40;
    private const int FirstEntryNameOffset = SolidPayloadArchive.HeaderLength + SolidPayloadArchive.Int32Length;

    /// <summary>The local file header signature every zip opens with.</summary>
    private static readonly byte[] ZipLocalFileHeaderMagic = [0x50, 0x4B, 0x03, 0x04];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrayAppDotNETInstaller.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = [];

    public SolidPayloadArchiveTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp leftovers are harmless for the test outcome
        }
    }

    [Fact]
    public void CreateReadExtract_RoundTripsAMultiEntryPackage()
    {
        byte[] executable = TextBytes("not really an executable");
        byte[] license = TextBytes("license text");
        byte[] nested = TextBytes("nested resource bytes");
        string zipPath = CreateZip(
            SourceZipName,
            (ExecutableEntryName, executable),
            ("LICENSE.txt", license),
            ("tools/nested/resource.bin", nested));
        long expectedRawLength = executable.Length + license.Length + nested.Length;

        SolidPayloadStatistics statistics = CreatePayload(zipPath, out string payloadPath);

        Assert.Equal(3, statistics.EntryCount);
        Assert.Equal(expectedRawLength, statistics.RawLength);
        Assert.Equal(new FileInfo(payloadPath).Length, statistics.PackedLength);

        string destination = Path.Combine(_root, "round-trip");
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(3, filesWritten);
        Assert.Equal(expectedRawLength, header.RawLength);
        Assert.Equal(ComputeSHA256(Concatenate(executable, license, nested)), header.RawSHA256);
        List<string> entryNames = header.Entries.Select(entry => entry.Name).ToList();
        Assert.Equal([ExecutableEntryName, "LICENSE.txt", "tools/nested/resource.bin"], entryNames);
        Assert.All(header.Entries, entry => Assert.False(entry.IsDirectory));
        Assert.Equal(executable, File.ReadAllBytes(Path.Combine(destination, ExecutableEntryName)));
        Assert.Equal(license, File.ReadAllBytes(Path.Combine(destination, "LICENSE.txt")));
        Assert.Equal(nested, File.ReadAllBytes(Path.Combine(destination, "tools", "nested", "resource.bin")));
    }

    [Fact]
    public void CreateReadExtract_RoundTripsAMultiMegabyteEntry()
    {
        byte[] large = CreatePatternedContent(LargeEntryLength);
        byte[] neighbour = TextBytes("a short neighbour so the large entry is not the whole payload");
        string zipPath = CreateZip(SourceZipName, ("payload.bin", large), ("readme.txt", neighbour));
        long expectedRawLength = large.Length + neighbour.Length;

        SolidPayloadStatistics statistics = CreatePayload(zipPath, out string payloadPath);

        Assert.Equal(expectedRawLength, statistics.RawLength);
        // A 4 KB pattern repeated over 2 MB is almost entirely matches, so a working encoder collapses it
        Assert.True(
            statistics.PackedLength < statistics.RawLength / 4,
            $"A repeating {statistics.RawLength} byte payload packed to {statistics.PackedLength} bytes.");

        string destination = Path.Combine(_root, "large");
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(2, filesWritten);
        Assert.Equal(expectedRawLength, header.RawLength);
        string largePath = Path.Combine(destination, "payload.bin");
        Assert.Equal(large.LongLength, new FileInfo(largePath).Length);
        Assert.Equal(ComputeSHA256(large), ComputeSHA256(File.ReadAllBytes(largePath)));
        Assert.Equal(ComputeSHA256(neighbour), ComputeSHA256(File.ReadAllBytes(Path.Combine(destination, "readme.txt"))));
    }

    [Fact]
    public void Extract_WritesAnEmptyEntryAsAZeroLengthFile()
    {
        string zipPath = CreateZip(
            SourceZipName,
            ("first.txt", TextBytes("first")),
            ("empty.dat", Array.Empty<byte>()),
            ("last.txt", TextBytes("last")));

        SolidPayloadStatistics statistics = CreatePayload(zipPath, out string payloadPath);
        Assert.Equal(3, statistics.EntryCount);

        string destination = Path.Combine(_root, "empty-entry");
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(3, filesWritten);
        SolidPayloadEntry emptyEntry = header.Entries.Single(entry => entry.Name == "empty.dat");
        Assert.False(emptyEntry.IsDirectory);
        Assert.Equal(0L, emptyEntry.Length);
        string emptyPath = Path.Combine(destination, "empty.dat");
        Assert.True(File.Exists(emptyPath));
        Assert.Equal(0L, new FileInfo(emptyPath).Length);
        Assert.Equal("first", File.ReadAllText(Path.Combine(destination, "first.txt")));
        Assert.Equal("last", File.ReadAllText(Path.Combine(destination, "last.txt")));
    }

    [Fact]
    public void Extract_WritesATrailingEmptyEntryAsAZeroLengthFile()
    {
        // The write loop only advances the entry table while decoded bytes are arriving, so an empty entry
        // that trails the last byte of payload is only reached by the drain in Complete
        string zipPath = CreateZip(
            SourceZipName,
            ("first.txt", TextBytes("first")),
            ("trailing-empty.dat", Array.Empty<byte>()));

        string destination = Path.Combine(_root, "trailing-empty-entry");
        CreatePayload(zipPath, out string payloadPath);
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(2, filesWritten);
        Assert.Equal(0L, header.Entries[1].Length);
        string trailingPath = Path.Combine(destination, "trailing-empty.dat");
        Assert.True(File.Exists(trailingPath));
        Assert.Equal(0L, new FileInfo(trailingPath).Length);
    }

    [Fact]
    public void Extract_CreatesAnExplicitDirectoryEntryWithNoFilesUnderIt()
    {
        string zipPath = CreateZip(SourceZipName, (ExecutableEntryName, TextBytes("app")), ("logs/", null));

        SolidPayloadStatistics statistics = CreatePayload(zipPath, out string payloadPath);
        Assert.Equal(2, statistics.EntryCount);

        string destination = Path.Combine(_root, "directory-entry");
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(1, filesWritten);
        // The trailing separator is trimmed while the name is normalized; the attribute word carries the shape
        SolidPayloadEntry directoryEntry = header.Entries.Single(entry => entry.IsDirectory);
        Assert.Equal("logs", directoryEntry.Name);
        Assert.Equal(0L, directoryEntry.Length);
        string directoryPath = Path.Combine(destination, "logs");
        Assert.True(Directory.Exists(directoryPath));
        Assert.Empty(Directory.GetFileSystemEntries(directoryPath));
    }

    [Fact]
    public void Create_DropsEntriesTheFilterRejects()
    {
        string zipPath = CreateZip(
            SourceZipName,
            (InstallScriptName, TextBytes("batch installer")),
            (ExecutableEntryName, TextBytes("app")),
            ("tools/" + InstallScriptName, TextBytes("nested")));
        string payloadPath = Path.Combine(_root, PayloadName);

        SolidPayloadStatistics statistics = SolidPayloadArchive.Create(
            zipPath,
            payloadPath,
            entryName => !string.Equals(entryName, InstallScriptName, StringComparison.OrdinalIgnoreCase),
            _log.Add);

        Assert.Equal(2, statistics.EntryCount);
        Assert.Contains(_log, line => line.Contains("skipping " + InstallScriptName, StringComparison.Ordinal));

        string destination = Path.Combine(_root, "filtered");
        int filesWritten = ExtractPayload(payloadPath, destination, out SolidPayloadHeader header);

        Assert.Equal(2, filesWritten);
        Assert.DoesNotContain(header.Entries, entry => string.Equals(entry.Name, InstallScriptName, StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(destination, InstallScriptName)));
        // A nested script belongs to the app's own layout and must survive
        Assert.True(File.Exists(Path.Combine(destination, "tools", InstallScriptName)));
    }

    [Fact]
    public void StartsWithMagic_IsTrueForASolidPayloadAndFalseForAZip()
    {
        string zipPath = CreateZip(SourceZipName, (ExecutableEntryName, TextBytes("app")));
        CreatePayload(zipPath, out string payloadPath);

        byte[] payloadLeadingBytes = ReadLeadingBytes(payloadPath, SolidPayloadArchive.MagicLength);
        byte[] zipLeadingBytes = ReadLeadingBytes(zipPath, SolidPayloadArchive.MagicLength);

        Assert.Equal(SolidPayloadArchive.Magic, Encoding.ASCII.GetString(payloadLeadingBytes));
        Assert.True(SolidPayloadArchive.StartsWithMagic(payloadLeadingBytes, payloadLeadingBytes.Length));
        Assert.Equal(ZipLocalFileHeaderMagic, TakeBytes(zipLeadingBytes, ZipLocalFileHeaderMagic.Length));
        Assert.False(SolidPayloadArchive.StartsWithMagic(zipLeadingBytes, zipLeadingBytes.Length));
        // A stream shorter than the magic can never match, which is how a stub with no payload is refused
        Assert.False(SolidPayloadArchive.StartsWithMagic(payloadLeadingBytes, SolidPayloadArchive.MagicLength - 1));
    }

    [Fact]
    public void Read_RejectsAWrongMagic()
    {
        string payloadPath = CreateSimplePayload();
        FlipByte(payloadPath, offset: 3);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ReadHeader(payloadPath));
        Assert.Contains("solid archive header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsAnUnknownFormatVersion()
    {
        string payloadPath = CreateSimplePayload();
        PatchInt32(payloadPath, FormatVersionOffset, SolidPayloadArchive.FormatVersion + 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ReadHeader(payloadPath));
        Assert.Contains("format", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsATruncatedEntryTable()
    {
        string payloadPath = CreateSimplePayload();
        SolidPayloadHeader header = ReadHeader(payloadPath);
        int tableLength = (int)(header.CompressedDataOffset - SolidPayloadArchive.HeaderLength);

        // Shortening the recorded table leaves the last record without its attribute and length words
        PatchInt32(payloadPath, TableLengthOffset, tableLength - SolidPayloadArchive.Int64Length);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ReadHeader(payloadPath));
        Assert.Contains("ends inside entry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsARawLengthThatDisagreesWithTheEntryTable()
    {
        string payloadPath = CreateSimplePayload();
        SolidPayloadHeader header = ReadHeader(payloadPath);

        PatchInt64(payloadPath, RawLengthOffset, header.RawLength + 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ReadHeader(payloadPath));
        Assert.Contains("add up to", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_ThrowsWhenTheCompressedBodyWasTampered()
    {
        byte[] content = CreateRandomContent(TamperEntryLength);
        string zipPath = CreateZip(SourceZipName, ("payload.bin", content));
        SolidPayloadStatistics statistics = CreatePayload(zipPath, out string payloadPath);
        SolidPayloadHeader header = ReadHeader(payloadPath);

        // A quarter of the way into the range coded body: far enough past the coder's initialisation that the
        // flip cannot be shrugged off, and far enough from the end that the decoder still has input to read
        long bodyLength = statistics.PackedLength - header.CompressedDataOffset;
        FlipByte(payloadPath, header.CompressedDataOffset + (bodyLength / 4));

        string destination = Path.Combine(_root, "tampered");
        // NOTE: the recorded SHA-256 is only compared once the whole body has decoded, so that check is the
        // backstop rather than the first line of defence. A sufficiently damaged stream can also starve the
        // range decoder and surface as EndOfStreamException instead. This offset was chosen by running the
        // flip at a sixteenth, an eighth, a quarter and a half of the body: all four desynchronise the range
        // coder into an impossible match distance, which the decoder rejects with InvalidDataException well
        // before either of the other two outcomes, so the precise assertion holds.
        Assert.Throws<InvalidDataException>(() => ExtractPayload(payloadPath, destination, out SolidPayloadHeader _));
    }

    [Fact]
    public void Create_RejectsATraversalEntryNameInTheSourceZip()
    {
        string zipPath = CreateZip(SourceZipName, (ExecutableEntryName, TextBytes("app")), ("../evil.txt", TextBytes("escaped")));
        string payloadPath = Path.Combine(_root, PayloadName);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SolidPayloadArchive.Create(zipPath, payloadPath, _ => true, _log.Add));

        Assert.Contains("traversal segment", exception.Message, StringComparison.Ordinal);
        // The table is built before the destination is opened, so a hostile zip leaves no payload behind
        Assert.False(File.Exists(payloadPath));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public void Read_RejectsATraversalEntryNamePatchedIntoTheTable()
    {
        // Create normalizes names out of the zip, so a payload that carries one has to be forged: the first
        // entry name is the same byte length as its replacement, which keeps the rest of the table aligned
        string zipPath = CreateZip(SourceZipName, ("aa/inner.txt", TextBytes("inner")), (ExecutableEntryName, TextBytes("app")));
        CreatePayload(zipPath, out string payloadPath);
        PatchBytes(payloadPath, FirstEntryNameOffset, Encoding.ASCII.GetBytes(".."));

        string destination = Path.Combine(_root, "traversal");
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtractPayload(payloadPath, destination, out SolidPayloadHeader _));

        Assert.Contains("traversal segment", exception.Message, StringComparison.Ordinal);
        // The table is validated before a single byte is decompressed, so nothing reached the disk at all
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(_root, "inner.txt")));
    }

    [Fact]
    public void Extract_WithoutADestinationVerifiesTheHashAndWritesNothing()
    {
        byte[] content = TextBytes("the bytes --verify-installer has to prove decode cleanly");
        string zipPath = CreateZip(SourceZipName, (ExecutableEntryName, content), ("tools/note.txt", TextBytes("note")));
        CreatePayload(zipPath, out string payloadPath);
        string destination = Path.Combine(_root, "never-created");

        int filesWritten;
        using (FileStream stream = File.OpenRead(payloadPath))
        {
            SolidPayloadHeader header = SolidPayloadArchive.Read(stream);
            filesWritten = SolidPayloadArchive.Extract(stream, header, destinationDirectory: null, onEntry: null, CancellationToken.None);
        }

        Assert.Equal(0, filesWritten);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(_root));

        // The verification pass is only worth anything if the recorded hash is still checked with no sink
        string corruptedPath = Path.Combine(_root, "corrupted" + SolidPayloadArchive.FileExtension);
        File.Copy(payloadPath, corruptedPath);
        FlipByte(corruptedPath, RawHashOffset);

        using FileStream corrupted = File.OpenRead(corruptedPath);
        SolidPayloadHeader corruptedHeader = SolidPayloadArchive.Read(corrupted);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SolidPayloadArchive.Extract(corrupted, corruptedHeader, destinationDirectory: null, onEntry: null, CancellationToken.None));
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractArchive_RoutesASolidPayloadAndReportsProgress()
    {
        string zipPath = CreateZip(
            SourceZipName,
            (ExecutableEntryName, TextBytes("app")),
            ("tools/helper.dll", TextBytes("helper")));
        CreatePayload(zipPath, out string payloadPath);
        string destination = Path.Combine(_root, "engine-out");
        List<InstallProgressLine> reports = [];
        ProgressSlice slice = new(0, 50);

        int filesWritten;
        using (FileStream stream = File.OpenRead(payloadPath))
        {
            filesWritten = InstallEngine.ExtractArchive(
                stream,
                destination,
                ApplicationName,
                slice,
                new ListProgress(reports),
                CancellationToken.None);
        }

        Assert.Equal(2, filesWritten);
        Assert.Equal("app", File.ReadAllText(Path.Combine(destination, ExecutableEntryName)));
        Assert.Equal("helper", File.ReadAllText(Path.Combine(destination, "tools", "helper.dll")));
        // One report per entry plus the completion report
        Assert.Equal(3, reports.Count);
        Assert.All(reports, report => Assert.False(report.IsFailure));
        Assert.All(reports, report => Assert.InRange(report.Percent, slice.StartPercent, slice.EndPercent));
        Assert.Equal(slice.EndPercent, reports[reports.Count - 1].Percent);
        for (int index = 1; index < reports.Count; index++)
            Assert.True(reports[index].Percent >= reports[index - 1].Percent, "progress must not go backwards");
        Assert.Contains(ExecutableEntryName, reports[0].Message, StringComparison.Ordinal);
        Assert.Contains("tools/helper.dll", reports[1].Message, StringComparison.Ordinal);
    }

    private string CreateSimplePayload()
    {
        string zipPath = CreateZip(SourceZipName, (ExecutableEntryName, TextBytes("app")), ("LICENSE.txt", TextBytes("license")));
        CreatePayload(zipPath, out string payloadPath);
        return payloadPath;
    }

    private SolidPayloadStatistics CreatePayload(string zipPath, out string payloadPath)
    {
        payloadPath = Path.Combine(_root, PayloadName);
        return SolidPayloadArchive.Create(zipPath, payloadPath, _ => true, _log.Add);
    }

    private string CreateZip(string fileName, params (string EntryName, byte[]? Content)[] entries)
    {
        string zipPath = Path.Combine(_root, fileName);
        using (FileStream stream = new(zipPath, FileMode.Create, FileAccess.ReadWrite))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string entryName, byte[]? content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                if (content == null) continue;

                using Stream entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }

        return zipPath;
    }

    private static SolidPayloadHeader ReadHeader(string payloadPath)
    {
        using FileStream stream = File.OpenRead(payloadPath);
        return SolidPayloadArchive.Read(stream);
    }

    private static int ExtractPayload(string payloadPath, string? destinationDirectory, out SolidPayloadHeader header)
    {
        using FileStream stream = File.OpenRead(payloadPath);
        header = SolidPayloadArchive.Read(stream);
        return SolidPayloadArchive.Extract(stream, header, destinationDirectory, onEntry: null, CancellationToken.None);
    }

    private static byte[] TextBytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A repeating pseudo-random block: deterministic, and dense with matches the encoder must find.</summary>
    private static byte[] CreatePatternedContent(int length)
    {
        byte[] pattern = CreateRandomContent(PatternLength);
        byte[] content = new byte[length];
        for (int index = 0; index < length; index++) content[index] = pattern[index % PatternLength];
        return content;
    }

    /// <summary>The .NET Framework Random has no NextBytes-returning overload, so the buffer is filled here.</summary>
    private static byte[] CreateRandomContent(int length)
    {
        byte[] content = new byte[length];
        new Random(DeterministicSeed).NextBytes(content);
        return content;
    }

    private static byte[] Concatenate(params byte[][] blocks)
    {
        int total = 0;
        foreach (byte[] block in blocks) total += block.Length;

        byte[] combined = new byte[total];
        int cursor = 0;
        foreach (byte[] block in blocks)
        {
            Array.Copy(block, sourceIndex: 0, combined, cursor, block.Length);
            cursor += block.Length;
        }

        return combined;
    }

    private static string ComputeSHA256(byte[] content)
    {
        using SHA256 algorithm = SHA256.Create();
        byte[] digest = algorithm.ComputeHash(content);
        StringBuilder text = new(digest.Length * 2);
        foreach (byte value in digest) text.Append(value.ToString("x2"));
        return text.ToString();
    }

    private static byte[] ReadLeadingBytes(string filePath, int length)
    {
        byte[] leadingBytes = new byte[length];
        using FileStream stream = File.OpenRead(filePath);
        int read = stream.Read(leadingBytes, 0, length);
        Assert.Equal(length, read);
        return leadingBytes;
    }

    private static byte[] TakeBytes(byte[] source, int length)
    {
        byte[] taken = new byte[length];
        Array.Copy(source, sourceIndex: 0, taken, destinationIndex: 0, length);
        return taken;
    }

    private static void FlipByte(string filePath, long offset)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        int original = stream.ReadByte();
        stream.Position = offset;
        stream.WriteByte((byte)(original ^ 0xFF));
    }

    private static void PatchBytes(string filePath, long offset, byte[] replacement)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        stream.Write(replacement, 0, replacement.Length);
    }

    private static void PatchInt32(string filePath, long offset, int value)
    {
        byte[] encoded = new byte[SolidPayloadArchive.Int32Length];
        FrameworkCompatibility.WriteInt32LittleEndian(encoded, offset: 0, value);
        PatchBytes(filePath, offset, encoded);
    }

    private static void PatchInt64(string filePath, long offset, long value)
    {
        byte[] encoded = new byte[SolidPayloadArchive.Int64Length];
        FrameworkCompatibility.WriteInt64LittleEndian(encoded, offset: 0, value);
        PatchBytes(filePath, offset, encoded);
    }

    private sealed class ListProgress(List<InstallProgressLine> lines) : IProgress<InstallProgressLine>
    {
        public void Report(InstallProgressLine value) => lines.Add(value);
    }
}

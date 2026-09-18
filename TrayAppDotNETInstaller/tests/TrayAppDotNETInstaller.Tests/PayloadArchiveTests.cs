using System.IO.Compression;
using System.Text;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class PayloadArchiveTests : IDisposable
{
    private const int FakeImageLength = 4096;
    private const string FirstPayloadName = "VolumeTrayAppDotNET_270.zip";
    private const string SecondPayloadName = "BatteryTrayAppDotNET_31.zip";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrayAppDotNETInstaller.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = [];

    public PayloadArchiveTests()
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
    public void Append_RoundTripsEntriesAndLeavesTheImageUntouched()
    {
        byte[] factoryImage = CreateFakeImage(out string targetPath);
        string firstPayload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        string secondPayload = CreateZip(SecondPayloadName, ("BatteryTrayAppDotNET.exe", "battery"), ("LICENSE.txt", "license"));

        PayloadArchiveWriter.Append(targetPath, [firstPayload, secondPayload], _log.Add);

        using PayloadArchive? archive = PayloadArchive.TryOpen(targetPath, _log.Add);
        Assert.NotNull(archive);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(FirstPayloadName, archive.Entries[0].FileName);
        Assert.Equal(SecondPayloadName, archive.Entries[1].FileName);
        Assert.Equal(new FileInfo(firstPayload).Length, archive.Entries[0].DataLength);
        Assert.Equal(new FileInfo(secondPayload).Length, archive.Entries[1].DataLength);
        Assert.Equal(FakeImageLength, archive.Entries[0].DataOffset);
        Assert.Equal(File.ReadAllBytes(firstPayload), ReadEntry(archive, archive.Entries[0]));
        Assert.Equal(File.ReadAllBytes(secondPayload), ReadEntry(archive, archive.Entries[1]));

        // Array ranges need RuntimeHelpers.GetSubArray, which the .NET Framework does not carry, so the
        // leading image is copied out by hand
        byte[] stamped = File.ReadAllBytes(targetPath);
        byte[] leadingImage = new byte[FakeImageLength];
        Array.Copy(stamped, sourceIndex: 0, leadingImage, destinationIndex: 0, FakeImageLength);
        Assert.Equal(factoryImage, leadingImage);
    }

    [Fact]
    public void OpenEntry_ReadsTheAppendedZipThroughZipArchive()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"), ("readme.txt", "hello"));

        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);

        using PayloadArchive? archive = PayloadArchive.TryOpen(targetPath, _log.Add);
        Assert.NotNull(archive);
        using Stream entryStream = archive.OpenEntry(archive.Entries[0]);
        using ZipArchive zip = new(entryStream, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        using StreamReader reader = new(zip.GetEntry("readme.txt")!.Open());
        Assert.Equal("hello", reader.ReadToEnd());
    }

    [Fact]
    public void OpenEntry_ThrowsWhenTheEntryDataWasFlipped()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);

        FlipByte(targetPath, FakeImageLength + 16);

        using PayloadArchive? archive = PayloadArchive.TryOpen(targetPath, _log.Add);
        Assert.NotNull(archive);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(archive.Entries[0]));
        Assert.Contains(FirstPayloadName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryOpen_ReturnsNullForATruncatedFile()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);
        Truncate(targetPath, PayloadArchive.TrailerLength / 2);

        Assert.Null(PayloadArchive.TryOpen(targetPath, _log.Add));
        Assert.Contains(_log, line => line.Contains("PayloadArchive", StringComparison.Ordinal));
    }

    [Fact]
    public void TryOpen_ReturnsNullForAFileShorterThanTheTrailer()
    {
        string stubPath = Path.Combine(_root, "Stub.exe");
        File.WriteAllBytes(stubPath, new byte[PayloadArchive.TrailerLength - 1]);

        Assert.Null(PayloadArchive.TryOpen(stubPath, _log.Add));
        Assert.Contains(_log, line => line.Contains("shorter than", StringComparison.Ordinal));
    }

    [Fact]
    public void TryOpen_ReturnsNullWhenTheMagicIsCorrupt()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);

        long magicOffset = new FileInfo(targetPath).Length - PayloadArchive.TrailerLength;
        FlipByte(targetPath, magicOffset + 2);

        Assert.Null(PayloadArchive.TryOpen(targetPath, _log.Add));
    }

    [Fact]
    public void TryOpen_ReturnsNullWhenTheDirectoryWasPatched()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);

        // The directory sits immediately before the trailer, so its first byte is the format version
        long directoryOffset;
        using (PayloadArchive? archive = PayloadArchive.TryOpen(targetPath, _log.Add))
        {
            Assert.NotNull(archive);
            directoryOffset = archive.Entries[0].DataOffset + archive.Entries[0].DataLength;
        }

        FlipByte(targetPath, directoryOffset);

        Assert.Null(PayloadArchive.TryOpen(targetPath, _log.Add));
    }

    [Fact]
    public void TryOpen_ReturnsNullForAPlainFactoryImage()
    {
        CreateFakeImage(out string targetPath);

        Assert.Null(PayloadArchive.TryOpen(targetPath, _log.Add));
    }

    [Fact]
    public void Append_RefusesAnAlreadyStampedImage()
    {
        CreateFakeImage(out string targetPath);
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        PayloadArchiveWriter.Append(targetPath, [payload], _log.Add);
        long lengthAfterTheFirstAppend = new FileInfo(targetPath).Length;

        Assert.Throws<InvalidOperationException>(() => PayloadArchiveWriter.Append(targetPath, [payload], _log.Add));
        Assert.Equal(lengthAfterTheFirstAppend, new FileInfo(targetPath).Length);
    }

    [Fact]
    public void Append_RejectsDuplicateLeafNames()
    {
        CreateFakeImage(out string targetPath);
        string first = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));
        string duplicateDirectory = Path.Combine(_root, "second");
        Directory.CreateDirectory(duplicateDirectory);
        string second = Path.Combine(duplicateDirectory, FirstPayloadName);
        File.Copy(first, second);

        Assert.Throws<ArgumentException>(() => PayloadArchiveWriter.Append(targetPath, [first, second], _log.Add));
        Assert.Equal(FakeImageLength, new FileInfo(targetPath).Length);
    }

    [Fact]
    public void Append_RejectsAnEmptyPayloadList()
    {
        CreateFakeImage(out string targetPath);

        Assert.Throws<ArgumentException>(() => PayloadArchiveWriter.Append(targetPath, [], _log.Add));
    }

    [Fact]
    public void Append_RejectsAMissingPayloadFile()
    {
        CreateFakeImage(out string targetPath);

        Assert.Throws<FileNotFoundException>(() =>
            PayloadArchiveWriter.Append(targetPath, [Path.Combine(_root, "absent.zip")], _log.Add));
        Assert.Equal(FakeImageLength, new FileInfo(targetPath).Length);
    }

    [Fact]
    public void Append_RejectsAMissingTargetImage()
    {
        string payload = CreateZip(FirstPayloadName, ("VolumeTrayAppDotNET.exe", "volume"));

        Assert.Throws<FileNotFoundException>(() =>
            PayloadArchiveWriter.Append(Path.Combine(_root, "absent.exe"), [payload], _log.Add));
    }

    private byte[] CreateFakeImage(out string targetPath)
    {
        byte[] image = new byte[FakeImageLength];
        // The .NET Framework has no Random.Shared; a per-call instance is equivalent for filler bytes
        new Random().NextBytes(image);
        targetPath = Path.Combine(_root, "Factory.exe");
        File.WriteAllBytes(targetPath, image);
        return image;
    }

    private string CreateZip(string fileName, params (string EntryName, string Content)[] entries)
    {
        string zipPath = Path.Combine(_root, fileName);
        using (FileStream stream = new(zipPath, FileMode.Create, FileAccess.ReadWrite))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string entryName, string content) in entries)
            {
                using Stream entryStream = archive.CreateEntry(entryName).Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return zipPath;
    }

    private static byte[] ReadEntry(PayloadArchive archive, PayloadArchiveEntry entry)
    {
        using Stream stream = archive.OpenEntry(entry);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void FlipByte(string filePath, long offset)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = offset;
        int original = stream.ReadByte();
        stream.Position = offset;
        stream.WriteByte((byte)(original ^ 0xFF));
    }

    private static void Truncate(string filePath, long bytesToRemove)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.ReadWrite);
        stream.SetLength(stream.Length - bytesToRemove);
    }
}

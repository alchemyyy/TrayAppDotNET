using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

// NOTE: FrameworkCompatibility stands in for System.Buffers.Binary.BinaryPrimitives throughout this file,
// because that type needs spans the .NET Framework does not carry

/// <summary>Covers the pure ICO parsing and RT_GROUP_ICON layout, with no file system or Win32 involved.</summary>
public sealed class IconStamperTests
{
    private const int IconDirectoryHeaderSize = 6;
    private const int FileDirectoryEntrySize = 16;
    private const int GroupDirectoryEntrySize = 14;
    private const int CommonDirectoryEntrySize = 12;
    private const ushort IconDirectoryTypeIcon = 1;
    private const ushort IconDirectoryTypeCursor = 2;

    // The synthetic images only need distinct lengths and contents; nothing parses their pixels
    private const byte SmallImageDimension = 16;
    private const byte LargeImageDimension = 0;
    private const int SmallImageLength = 4;
    private const int LargeImageLength = 7;

    [Fact]
    public void TryParseIconDirectory_ReadsEveryDirectoryField()
    {
        byte[] iconBytes = CreateTwoImageIconFile();

        bool parsed = IconStamper.TryParseIconDirectory(
            iconBytes,
            out IconDirectoryEntry[]? entries,
            out string? failureReason);

        Assert.True(parsed, failureReason);
        Assert.Null(failureReason);
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Length);

        int tableEnd = IconDirectoryHeaderSize + (FileDirectoryEntrySize * 2);
        Assert.Equal(SmallImageDimension, entries[0].Width);
        Assert.Equal(SmallImageDimension, entries[0].Height);
        Assert.Equal(0, entries[0].ColorCount);
        Assert.Equal(0, entries[0].Reserved);
        Assert.Equal(1, entries[0].Planes);
        Assert.Equal(32, entries[0].BitCount);
        Assert.Equal((uint)SmallImageLength, entries[0].BytesInResource);
        Assert.Equal((uint)tableEnd, entries[0].ImageOffset);

        // A 256 pixel image is stored as a zero dimension, which must survive the round trip untouched
        Assert.Equal(LargeImageDimension, entries[1].Width);
        Assert.Equal(LargeImageDimension, entries[1].Height);
        Assert.Equal((uint)LargeImageLength, entries[1].BytesInResource);
        Assert.Equal((uint)(tableEnd + SmallImageLength), entries[1].ImageOffset);
    }

    [Fact]
    public void BuildGroupDirectory_UsesFourteenByteEntriesEndingInTheResourceID()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        Assert.True(IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out _));
        Assert.NotNull(entries);

        byte[] groupDirectory = IconStamper.BuildGroupDirectory(entries);

        Assert.Equal(IconDirectoryHeaderSize + (GroupDirectoryEntrySize * 2), groupDirectory.Length);
        Assert.Equal(0, FrameworkCompatibility.ReadUInt16LittleEndian(groupDirectory, offset: 0));
        Assert.Equal(IconDirectoryTypeIcon, FrameworkCompatibility.ReadUInt16LittleEndian(groupDirectory, offset: 2));
        Assert.Equal(2, FrameworkCompatibility.ReadUInt16LittleEndian(groupDirectory, offset: 4));

        for (int index = 0; index < entries.Length; index++)
        {
            int fileOffset = IconDirectoryHeaderSize + (FileDirectoryEntrySize * index);
            int groupOffset = IconDirectoryHeaderSize + (GroupDirectoryEntrySize * index);
            Assert.Equal(
                Slice(iconBytes, fileOffset, CommonDirectoryEntrySize),
                Slice(groupDirectory, groupOffset, CommonDirectoryEntrySize));
            Assert.Equal(
                (ushort)(index + 1),
                FrameworkCompatibility.ReadUInt16LittleEndian(groupDirectory, groupOffset + CommonDirectoryEntrySize));
        }
    }

    [Fact]
    public void TryParseIconDirectory_RejectsEmptyBuffer()
    {
        bool parsed = IconStamper.TryParseIconDirectory([], out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void TryParseIconDirectory_RejectsCursorType()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 2, IconDirectoryTypeCursor);

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.NotNull(failureReason);
        Assert.Contains("Type", failureReason);
    }

    [Fact]
    public void TryParseIconDirectory_RejectsZeroCount()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 4, value: 0);

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.NotNull(failureReason);
        Assert.Contains("Count", failureReason);
    }

    [Fact]
    public void TryParseIconDirectory_RejectsNonZeroReserved()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 0, value: 7);

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.NotNull(failureReason);
        Assert.Contains("Reserved", failureReason);
    }

    [Fact]
    public void TryParseIconDirectory_RejectsImageOffsetPastTheEndOfTheBuffer()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        int secondEntryOffset = IconDirectoryHeaderSize + FileDirectoryEntrySize;
        FrameworkCompatibility.WriteUInt32LittleEndian(
            iconBytes,
            secondEntryOffset + CommonDirectoryEntrySize,
            (uint)(iconBytes.Length * 2));

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.NotNull(failureReason);
        Assert.Contains("outside", failureReason);
    }

    [Fact]
    public void TryParseIconDirectory_RejectsImageLengthPastTheEndOfTheBuffer()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        int secondEntryOffset = IconDirectoryHeaderSize + FileDirectoryEntrySize;
        FrameworkCompatibility.WriteUInt32LittleEndian(
            iconBytes,
            secondEntryOffset + 8,
            (uint)(iconBytes.Length * 2));

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.NotNull(failureReason);
        Assert.Contains("outside", failureReason);
    }

    [Fact]
    public void TryParseIconDirectory_RejectsDirectoryTableLongerThanTheBuffer()
    {
        byte[] iconBytes = CreateTwoImageIconFile();
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 4, value: 64);

        bool parsed = IconStamper.TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? failureReason);

        Assert.False(parsed);
        Assert.Null(entries);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void LowestResourceID_PicksTheIDExplorerWouldShow()
    {
        Assert.Equal(7, IconStamper.LowestResourceID([32512, 7, 99], fallback: 1));
        Assert.Equal(1, IconStamper.LowestResourceID([], fallback: 1));
    }

    [Fact]
    public void TryGetIntegerResourceID_SeparatesIntegerIDsFromStringNames()
    {
        Assert.True(IconStamper.TryGetIntegerResourceID(new IntPtr(32512), out ushort resourceID));
        Assert.Equal(32512, resourceID);
        Assert.False(IconStamper.TryGetIntegerResourceID(new IntPtr(0x0001_0000), out _));
    }

    /// <summary>
    /// Builds a two image ICO whose entries differ in every field the group directory copies, so a
    /// transcription mistake in the layout cannot pass unnoticed.
    /// </summary>
    private static byte[] CreateTwoImageIconFile()
    {
        byte[] smallImage = [0x11, 0x22, 0x33, 0x44];
        byte[] largeImage = [0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7];
        Assert.Equal(SmallImageLength, smallImage.Length);
        Assert.Equal(LargeImageLength, largeImage.Length);

        int tableEnd = IconDirectoryHeaderSize + (FileDirectoryEntrySize * 2);
        byte[] iconBytes = new byte[tableEnd + smallImage.Length + largeImage.Length];
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 0, value: 0);
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 2, IconDirectoryTypeIcon);
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, offset: 4, value: 2);

        WriteDirectoryEntry(
            iconBytes,
            IconDirectoryHeaderSize,
            SmallImageDimension,
            smallImage.Length,
            tableEnd);
        WriteDirectoryEntry(
            iconBytes,
            IconDirectoryHeaderSize + FileDirectoryEntrySize,
            LargeImageDimension,
            largeImage.Length,
            tableEnd + smallImage.Length);

        Array.Copy(smallImage, sourceIndex: 0, iconBytes, tableEnd, smallImage.Length);
        Array.Copy(largeImage, sourceIndex: 0, iconBytes, tableEnd + smallImage.Length, largeImage.Length);
        return iconBytes;
    }

    /// <summary>Copies a byte range out; array ranges need a runtime helper the .NET Framework lacks.</summary>
    private static byte[] Slice(byte[] buffer, int offset, int length)
    {
        byte[] slice = new byte[length];
        Array.Copy(buffer, offset, slice, destinationIndex: 0, length);
        return slice;
    }

    private static void WriteDirectoryEntry(
        byte[] iconBytes,
        int entryOffset,
        byte dimension,
        int bytesInResource,
        int imageOffset)
    {
        iconBytes[entryOffset] = dimension;
        iconBytes[entryOffset + 1] = dimension;
        iconBytes[entryOffset + 2] = 0;
        iconBytes[entryOffset + 3] = 0;
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, entryOffset + 4, value: 1);
        FrameworkCompatibility.WriteUInt16LittleEndian(iconBytes, entryOffset + 6, value: 32);
        FrameworkCompatibility.WriteUInt32LittleEndian(iconBytes, entryOffset + 8, (uint)bytesInResource);
        FrameworkCompatibility.WriteUInt32LittleEndian(iconBytes, entryOffset + CommonDirectoryEntrySize, (uint)imageOffset);
    }
}

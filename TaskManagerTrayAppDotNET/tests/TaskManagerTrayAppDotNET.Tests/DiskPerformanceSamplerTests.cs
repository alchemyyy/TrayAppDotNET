using System.Buffers.Binary;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DiskPerformanceSamplerTests
{
    [Fact]
    public void ParsesSortedDistinctPhysicalDriveNamesFromDeviceMultiString()
    {
        const string deviceNames =
            "C:\0PhysicalDrive12\0physicaldrive2\0Harddisk0Partition0\0"
            + "PhysicalDrive12\0PhysicalDriveNotANumber\0PhysicalDrive4294967296\0\0";

        uint[] physicalDiskNumbers = DiskPerformanceSampler.ParsePhysicalDiskNumbers(deviceNames);

        Assert.Equal(new uint[] { 2, 12 }, physicalDiskNumbers);
    }

    [Theory]
    [InlineData("PhysicalDrive0", 0U)]
    [InlineData("PhysicalDrive4294967295", uint.MaxValue)]
    public void ParsesPhysicalDriveNameWithoutTrailingTerminator(
        string deviceName,
        uint expectedDiskNumber)
    {
        uint[] physicalDiskNumbers = DiskPerformanceSampler.ParsePhysicalDiskNumbers(deviceName);

        Assert.Equal(new[] { expectedDiskNumber }, physicalDiskNumbers);
    }

    [Fact]
    public void SelectsStrongestDeviceAssociatedPage83IdentifierDeterministically()
    {
        TestStorageIdentifier scsiName = new(CodeSet: 3, Type: 8, Association: 0, "eui.weak"u8.ToArray());
        TestStorageIdentifier naa = new(CodeSet: 1, Type: 3, Association: 0, [0x60, 0x01, 0x02, 0x03]);
        byte[] forwardDescriptor = CreatePage83Descriptor(scsiName, naa);
        byte[] reverseDescriptor = CreatePage83Descriptor(naa, scsiName);

        bool parsedForward = DiskPerformanceSampler.TryCreatePage83DeviceID(
            forwardDescriptor,
            out string forwardDeviceID);
        bool parsedReverse = DiskPerformanceSampler.TryCreatePage83DeviceID(
            reverseDescriptor,
            out string reverseDeviceID);

        Assert.True(parsedForward);
        Assert.True(parsedReverse);
        Assert.Equal(expected: "disk:vpd83:3:1:60010203", forwardDeviceID);
        Assert.Equal(forwardDeviceID, reverseDeviceID);
    }

    [Fact]
    public void IgnoresPortAssociatedAndZeroPage83Identifiers()
    {
        // Zero bytes model an invalid identifier, not encoded text
        // ReSharper disable once UseUtf8StringLiteral
        byte[] descriptor = CreatePage83Descriptor(
            new TestStorageIdentifier(CodeSet: 1, Type: 3, Association: 1, [1, 2, 3]),
            new TestStorageIdentifier(CodeSet: 1, Type: 2, Association: 0, [0, 0, 0]));

        bool parsed = DiskPerformanceSampler.TryCreatePage83DeviceID(
            descriptor,
            out string deviceID);

        Assert.False(parsed);
        Assert.Equal(string.Empty, deviceID);
    }

    [Fact]
    public void RejectsTruncatedAndLoopingPage83Descriptors()
    {
        byte[] descriptor = CreatePage83Descriptor(
            new TestStorageIdentifier(CodeSet: 1, Type: 3, Association: 0, [1, 2, 3]),
            new TestStorageIdentifier(CodeSet: 1, Type: 2, Association: 0, [4, 5, 6]));
        byte[] truncated = descriptor[..^1];
        byte[] loopingOffset = [.. descriptor];
        BinaryPrimitives.WriteUInt16LittleEndian(loopingOffset.AsSpan(22), value: 1);

        Assert.False(DiskPerformanceSampler.TryCreatePage83DeviceID(truncated, out _));
        Assert.False(DiskPerformanceSampler.TryCreatePage83DeviceID(loopingOffset, out _));
    }

    [Fact]
    public void CalculatesRatesFromKernelDiskCounters()
    {
        DiskPerformanceCounters previous = new(
            BytesRead: 1_000,
            BytesWritten: 2_000,
            ReadTime: 10_000,
            WriteTime: 20_000,
            IdleTime: 30_000,
            ReadCount: 10,
            WriteCount: 20,
            QueryTime: 100_000);
        DiskPerformanceCounters current = new(
            BytesRead: 3_000,
            BytesWritten: 6_000,
            ReadTime: 4_010_000,
            WriteTime: 2_020_000,
            IdleTime: 5_030_000,
            ReadCount: 12,
            WriteCount: 21,
            QueryTime: 20_100_000);

        bool calculated = DiskPerformanceSampler.TryCalculatePerformance(
            previous,
            current,
            out DiskPerformanceDelta delta);

        Assert.True(calculated);
        Assert.Equal(expected: 75, delta.ActiveTimePercent, precision: 8);
        Assert.Equal(expected: 1_000, delta.ReadBytesPerSecond, precision: 8);
        Assert.Equal(expected: 2_000, delta.WriteBytesPerSecond, precision: 8);
        Assert.Equal(expected: 200, delta.AverageResponseTimeMilliseconds, precision: 8);
    }

    [Fact]
    public void RejectsResetKernelDiskCounters()
    {
        DiskPerformanceCounters previous = new(BytesRead: 100, BytesWritten: 100, ReadTime: 100, WriteTime: 100,
            IdleTime: 100, ReadCount: 10, WriteCount: 10, QueryTime: 100);
        DiskPerformanceCounters current = new(BytesRead: 99, BytesWritten: 100, ReadTime: 100, WriteTime: 100,
            IdleTime: 100, ReadCount: 10, WriteCount: 10, QueryTime: 200);

        bool calculated = DiskPerformanceSampler.TryCalculatePerformance(
            previous,
            current,
            out DiskPerformanceDelta delta);

        Assert.False(calculated);
        Assert.Equal(expected: default, delta);
    }

    [Fact]
    public void NativeDiscoverySamplesEveryExposedPhysicalDrive()
    {
        uint[] physicalDiskNumbers = DiskPerformanceSampler.EnumeratePhysicalDiskNumbers();
        DiskPerformanceSampler sampler = new();

        DiskPerformanceSnapshot[] firstSnapshots = sampler.Sample();
        DiskPerformanceSnapshot[] secondSnapshots = sampler.Sample();

        Assert.NotEmpty(physicalDiskNumbers);
        Assert.True(firstSnapshots.Length >= physicalDiskNumbers.Length);
        Assert.Equal(
            firstSnapshots.Length,
            firstSnapshots.Select(static snapshot => snapshot.DeviceID).Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        for (int diskIndex = 0; diskIndex < physicalDiskNumbers.Length; diskIndex++)
        {
            int expectedSortKey = checked((int)Math.Min(physicalDiskNumbers[diskIndex], int.MaxValue));
            DiskPerformanceSnapshot firstSnapshot = Assert.Single(
                firstSnapshots,
                snapshot => snapshot.SortKey == expectedSortKey);
            DiskPerformanceSnapshot secondSnapshot = Assert.Single(
                secondSnapshots,
                snapshot => snapshot.SortKey == expectedSortKey);
            Assert.Equal(firstSnapshot.DeviceID, secondSnapshot.DeviceID);
        }
    }

    private static byte[] CreatePage83Descriptor(params TestStorageIdentifier[] identifiers)
    {
        int descriptorSize = 12;
        for (int identifierIndex = 0; identifierIndex < identifiers.Length; identifierIndex++)
            descriptorSize = checked(descriptorSize + 16 + identifiers[identifierIndex].Value.Length);

        byte[] descriptor = new byte[descriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, value: 12);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), (uint)descriptor.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(8), (uint)identifiers.Length);
        int offset = 12;
        for (int identifierIndex = 0; identifierIndex < identifiers.Length; identifierIndex++)
        {
            TestStorageIdentifier identifier = identifiers[identifierIndex];
            BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(offset), identifier.CodeSet);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(offset + 4), identifier.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(
                descriptor.AsSpan(offset + 8),
                checked((ushort)identifier.Value.Length));
            ushort nextOffset = identifierIndex + 1 < identifiers.Length
                ? checked((ushort)(16 + identifier.Value.Length))
                : (ushort)0;
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(offset + 10), nextOffset);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(offset + 12), identifier.Association);
            identifier.Value.CopyTo(descriptor.AsSpan(offset + 16));
            offset += 16 + identifier.Value.Length;
        }

        return descriptor;
    }

    private readonly record struct TestStorageIdentifier(
        int CodeSet,
        int Type,
        int Association,
        byte[] Value);
}

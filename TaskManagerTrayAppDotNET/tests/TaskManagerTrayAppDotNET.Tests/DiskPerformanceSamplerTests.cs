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

        Assert.Equal(new uint[] { expectedDiskNumber }, physicalDiskNumbers);
    }

    [Fact]
    public void SelectsStrongestDeviceAssociatedPage83IdentifierDeterministically()
    {
        TestStorageIdentifier scsiName = new(3, 8, 0, "eui.weak"u8.ToArray());
        TestStorageIdentifier naa = new(1, 3, 0, new byte[] { 0x60, 0x01, 0x02, 0x03 });
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
        Assert.Equal("disk:vpd83:3:1:60010203", forwardDeviceID);
        Assert.Equal(forwardDeviceID, reverseDeviceID);
    }

    [Fact]
    public void IgnoresPortAssociatedAndZeroPage83Identifiers()
    {
        byte[] descriptor = CreatePage83Descriptor(
            new TestStorageIdentifier(1, 3, 1, new byte[] { 1, 2, 3 }),
            new TestStorageIdentifier(1, 2, 0, new byte[] { 0, 0, 0 }));

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
            new TestStorageIdentifier(1, 3, 0, new byte[] { 1, 2, 3 }),
            new TestStorageIdentifier(1, 2, 0, new byte[] { 4, 5, 6 }));
        byte[] truncated = descriptor[..^1];
        byte[] loopingOffset = [.. descriptor];
        BinaryPrimitives.WriteUInt16LittleEndian(loopingOffset.AsSpan(22), 1);

        Assert.False(DiskPerformanceSampler.TryCreatePage83DeviceID(truncated, out _));
        Assert.False(DiskPerformanceSampler.TryCreatePage83DeviceID(loopingOffset, out _));
    }

    [Fact]
    public void CalculatesRatesFromKernelDiskCounters()
    {
        DiskPerformanceCounters previous = new(
            1_000,
            2_000,
            10_000,
            20_000,
            30_000,
            10,
            20,
            100_000);
        DiskPerformanceCounters current = new(
            3_000,
            6_000,
            4_010_000,
            2_020_000,
            5_030_000,
            12,
            21,
            20_100_000);

        bool calculated = DiskPerformanceSampler.TryCalculatePerformance(
            previous,
            current,
            out DiskPerformanceDelta delta);

        Assert.True(calculated);
        Assert.Equal(75, delta.ActiveTimePercent, precision: 8);
        Assert.Equal(1_000, delta.ReadBytesPerSecond, precision: 8);
        Assert.Equal(2_000, delta.WriteBytesPerSecond, precision: 8);
        Assert.Equal(200, delta.AverageResponseTimeMilliseconds, precision: 8);
    }

    [Fact]
    public void RejectsResetKernelDiskCounters()
    {
        DiskPerformanceCounters previous = new(100, 100, 100, 100, 100, 10, 10, 100);
        DiskPerformanceCounters current = new(99, 100, 100, 100, 100, 10, 10, 200);

        bool calculated = DiskPerformanceSampler.TryCalculatePerformance(
            previous,
            current,
            out DiskPerformanceDelta delta);

        Assert.False(calculated);
        Assert.Equal(default, delta);
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
            firstSnapshots.Select(static snapshot => snapshot.DeviceID).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 12);
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

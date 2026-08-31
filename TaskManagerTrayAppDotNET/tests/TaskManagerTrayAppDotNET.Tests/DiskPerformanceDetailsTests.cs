using System.Buffers.Binary;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DiskPerformanceDetailsTests
{
    [Fact]
    public void ParsesVolumeDiskExtentsUsingNativeX64Alignment()
    {
        byte[] descriptor = CreateVolumeDiskExtentsDescriptor(
            new DiskVolumeExtent(PhysicalDiskNumber: 12, ExtentLengthBytes: 1_000),
            new DiskVolumeExtent(PhysicalDiskNumber: 4, ExtentLengthBytes: 2_000));

        bool parsed = DiskDeviceMetadataReader.TryParseVolumeDiskExtents(
            descriptor,
            out DiskVolumeExtent[] extents);

        Assert.True(parsed);
        Assert.Equal(
            new DiskVolumeExtent[]
            {
                new(PhysicalDiskNumber: 12, ExtentLengthBytes: 1_000),
                new(PhysicalDiskNumber: 4, ExtentLengthBytes: 2_000)
            },
            extents);
    }

    [Fact]
    public void RejectsTruncatedOrNonpositiveVolumeExtents()
    {
        byte[] descriptor = CreateVolumeDiskExtentsDescriptor(
            new DiskVolumeExtent(PhysicalDiskNumber: 0, ExtentLengthBytes: 1_000));
        byte[] truncated = descriptor[..^1];
        byte[] zeroLength = [.. descriptor];
        BinaryPrimitives.WriteInt64LittleEndian(zeroLength.AsSpan(24), value: 0);

        Assert.False(DiskDeviceMetadataReader.TryParseVolumeDiskExtents(truncated, out _));
        Assert.False(DiskDeviceMetadataReader.TryParseVolumeDiskExtents(zeroLength, out _));
    }

    [Fact]
    public void AllocatesLogicalBytesExactlyAcrossCollapsedPhysicalExtents()
    {
        DiskVolumeExtent[] extents =
        [
            new(PhysicalDiskNumber: 5, ExtentLengthBytes: 1),
            new(PhysicalDiskNumber: 2, ExtentLengthBytes: 1),
            new(PhysicalDiskNumber: 5, ExtentLengthBytes: 1)
        ];

        DiskByteAllocation[] allocations = DiskDeviceMetadataReader.AllocateBytesByDisk(
            byteCount: 10,
            extents);

        Assert.Equal(
            new DiskByteAllocation[] { new(PhysicalDiskNumber: 2, Bytes: 3), new(PhysicalDiskNumber: 5, Bytes: 7) },
            allocations);
        Assert.Equal(expected: 10UL, allocations.Aggregate(seed: 0UL, static (sum, value) => sum + value.Bytes));
    }

    [Fact]
    public void AllocationHandlesProductsBeyondUInt64WithoutOverflow()
    {
        DiskVolumeExtent[] extents =
        [
            new(PhysicalDiskNumber: 0, ulong.MaxValue),
            new(PhysicalDiskNumber: 1, ulong.MaxValue)
        ];

        DiskByteAllocation[] allocations = DiskDeviceMetadataReader.AllocateBytesByDisk(
            ulong.MaxValue,
            extents);

        Assert.Equal(expected: 2, allocations.Length);
        Assert.Equal(expected: 9_223_372_036_854_775_808UL, allocations[0].Bytes);
        Assert.Equal(expected: 9_223_372_036_854_775_807UL, allocations[1].Bytes);
    }

    [Theory]
    [InlineData(false, (int)DiskMediaKind.SolidState)]
    [InlineData(true, (int)DiskMediaKind.Rotational)]
    public void ParsesSeekPenaltyMediaKind(bool incursSeekPenalty, int expectedValue)
    {
        byte[] descriptor = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, (uint)descriptor.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), (uint)descriptor.Length);
        descriptor[8] = incursSeekPenalty ? (byte)1 : (byte)0;

        DiskMediaKind actual = DiskDeviceMetadataReader.ParseSeekPenaltyDescriptor(descriptor);

        Assert.Equal((DiskMediaKind)expectedValue, actual);
    }

    [Fact]
    public void RejectsMalformedSeekPenaltyDescriptor()
    {
        byte[] descriptor = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, value: 8);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), value: 12);

        Assert.Equal(
            DiskMediaKind.Unknown,
            DiskDeviceMetadataReader.ParseSeekPenaltyDescriptor(descriptor));
    }

    [Theory]
    [InlineData(@"\??\C:\pagefile.sys", @"C:\pagefile.sys")]
    [InlineData(@"\SystemRoot\swapfile.sys", @"C:\Windows\swapfile.sys")]
    public void NormalizesNativePageFilePaths(string path, string expected)
    {
        Assert.Equal(
            expected,
            DiskDeviceMetadataReader.NormalizePageFilePath(path, windowsDirectory: @"C:\Windows"));
    }

    [Theory]
    [InlineData((int)DiskMediaKind.SolidState, "NVMe", "SSD (NVMe)")]
    [InlineData((int)DiskMediaKind.Rotational, "SATA", "HDD (SATA)")]
    [InlineData((int)DiskMediaKind.SolidState, "Disk", "SSD")]
    [InlineData((int)DiskMediaKind.Unknown, "Storage Spaces", "Storage Spaces")]
    [InlineData((int)DiskMediaKind.Unknown, "", "Disk")]
    [InlineData((int)DiskMediaKind.SolidState, "SSD (NVMe)", "SSD (NVMe)")]
    public void FormatsTaskManagerStyleDeviceType(
        int mediaKindValue,
        string busType,
        string expected)
    {
        Assert.Equal(
            expected,
            DiskPerformanceDetailsFactory.FormatDeviceType(
                (DiskMediaKind)mediaKindValue,
                busType));
    }

    [Fact]
    public void CreatesNormalizedCompleteDiskDetails()
    {
        DiskPerformanceSnapshot performance = CreatePerformanceSnapshot(
            hasPerformanceSample: true,
            activeTimePercent: 125,
            readBytesPerSecond: 4_000,
            writeBytesPerSecond: 6_000,
            averageResponseTimeMilliseconds: -1);
        DiskDeviceMetadataSnapshot metadata = new(
            HasDeviceData: true,
            PhysicalDiskNumber: 3,
            HasVolumeData: true,
            VolumeNames: "C:",
            FormattedCapacityBytes: 900_000,
            AvailableBytes: 400_000,
            HasSystemDiskData: true,
            IsSystemDisk: true,
            HasPageFileData: true,
            HasPageFile: true,
            DiskMediaKind.SolidState);

        DiskPerformanceDetailsSnapshot details = DiskPerformanceDetailsFactory.Create(
            performance,
            metadata);

        Assert.Equal(expected: "disk:test", details.DeviceID);
        Assert.Equal(expected: "Model", details.Model);
        Assert.Equal(expected: "C:", details.VolumeNames);
        Assert.Equal(expected: "SSD (NVMe)", details.DeviceType);
        Assert.Equal(expected: 100, details.ActiveTimePercent);
        Assert.Equal(expected: 10_000, details.TransferBytesPerSecond);
        Assert.Equal(expected: 4_000, details.ReadBytesPerSecond);
        Assert.Equal(expected: 6_000, details.WriteBytesPerSecond);
        Assert.Equal(expected: 0, details.AverageResponseTimeMilliseconds);
        Assert.Equal(expected: 900_000UL, details.FormattedCapacityBytes);
        Assert.True(details.HasSystemDiskData);
        Assert.True(details.IsSystemDisk);
        Assert.True(details.HasPageFileData);
        Assert.True(details.HasPageFile);
    }

    [Fact]
    public void IgnoresMetadataForAnotherPhysicalDisk()
    {
        DiskPerformanceSnapshot performance = CreatePerformanceSnapshot(
            hasPerformanceSample: false,
            double.NaN,
            double.PositiveInfinity,
            writeBytesPerSecond: -1,
            double.NaN);
        DiskDeviceMetadataSnapshot metadata = new(
            HasDeviceData: true,
            PhysicalDiskNumber: 4,
            HasVolumeData: true,
            VolumeNames: "X:",
            FormattedCapacityBytes: 900_000,
            AvailableBytes: 400_000,
            HasSystemDiskData: true,
            IsSystemDisk: true,
            HasPageFileData: true,
            HasPageFile: true,
            DiskMediaKind.Rotational);

        DiskPerformanceDetailsSnapshot details = DiskPerformanceDetailsFactory.Create(
            performance,
            metadata);

        Assert.False(details.HasPerformanceSample);
        Assert.Equal(expected: 0, details.ActiveTimePercent);
        Assert.Equal(expected: 0, details.TransferBytesPerSecond);
        Assert.Equal(expected: "D:", details.VolumeNames);
        Assert.Equal(expected: "NVMe", details.DeviceType);
        Assert.Equal(expected: 800_000UL, details.FormattedCapacityBytes);
        Assert.False(details.HasSystemDiskData);
        Assert.False(details.IsSystemDisk);
        Assert.False(details.HasPageFileData);
        Assert.False(details.HasPageFile);
    }

    [Fact]
    public void NativeReaderReturnsEveryExposedDiskAndFindsTheSystemDisk()
    {
        uint[] exposedDiskNumbers = DiskPerformanceSampler.EnumeratePhysicalDiskNumbers();

        DiskDeviceMetadataSnapshot[] metadata = DiskDeviceMetadataReader.Read(exposedDiskNumbers);

        Assert.NotEmpty(exposedDiskNumbers);
        for (int diskIndex = 0; diskIndex < exposedDiskNumbers.Length; diskIndex++)
        {
            uint expectedDiskNumber = exposedDiskNumbers[diskIndex];
            DiskDeviceMetadataSnapshot disk = Assert.Single(
                metadata,
                candidate => candidate.PhysicalDiskNumber == expectedDiskNumber);
            Assert.True(disk.HasDeviceData);
        }

        Assert.Contains(metadata, static disk => disk.IsSystemDisk);
    }

    private static DiskPerformanceSnapshot CreatePerformanceSnapshot(
        bool hasPerformanceSample,
        double activeTimePercent,
        double readBytesPerSecond,
        double writeBytesPerSecond,
        double averageResponseTimeMilliseconds) => new(
        DeviceID: "disk:test",
        PerformanceDeviceKind.Disk,
        SortKey: 3,
        Name: "Model",
        VolumeNames: "D:",
        DeviceType: "NVMe",
        hasPerformanceSample,
        activeTimePercent,
        readBytesPerSecond,
        writeBytesPerSecond,
        averageResponseTimeMilliseconds,
        QueueDepth: 1,
        CapacityBytes: 1_000_000,
        FormattedCapacityBytes: 800_000,
        AvailableBytes: 300_000);

    private static byte[] CreateVolumeDiskExtentsDescriptor(
        params DiskVolumeExtent[] extents)
    {
        byte[] descriptor = new byte[8 + extents.Length * 24];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, (uint)extents.Length);
        for (int extentIndex = 0; extentIndex < extents.Length; extentIndex++)
        {
            int extentOffset = 8 + extentIndex * 24;
            DiskVolumeExtent extent = extents[extentIndex];
            BinaryPrimitives.WriteUInt32LittleEndian(
                descriptor.AsSpan(extentOffset),
                extent.PhysicalDiskNumber);
            BinaryPrimitives.WriteInt64LittleEndian(
                descriptor.AsSpan(extentOffset + 8),
                extentIndex * 10_000);
            BinaryPrimitives.WriteInt64LittleEndian(
                descriptor.AsSpan(extentOffset + 16),
                checked((long)extent.ExtentLengthBytes));
        }

        return descriptor;
    }
}

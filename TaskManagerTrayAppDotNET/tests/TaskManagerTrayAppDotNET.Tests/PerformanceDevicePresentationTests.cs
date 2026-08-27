using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDevicePresentationTests
{
    [Fact]
    public void EmptySnapshotStillExposesCPUAndMemoryRowsAsUnavailable()
    {
        List<PerformanceDevicePresentation> devices =
            PerformanceDevicePresentationFactory.Create(PerformanceSnapshot.Empty);

        Assert.Equal(["cpu", "memory"], devices.Select(
            static (PerformanceDevicePresentation device) => device.DeviceID));
        Assert.All(devices, static device => Assert.False(device.HasUtilizationSample));
    }

    [Fact]
    public void NetworkGraphUsesTheBusiestDirectionRelativeToLinkSpeed()
    {
        NetworkPerformanceSnapshot network = new(
            "network:test",
            PerformanceDeviceKind.Network,
            0,
            "Ethernet 3",
            "Test adapter",
            "Ethernet",
            true,
            true,
            ReceiveBytesPerSecond: 50_000_000,
            SendBytesPerSecond: 10_000_000,
            LinkSpeedBitsPerSecond: 1_000_000_000,
            TotalBytesReceived: 1_000_000_000,
            TotalBytesSent: 500_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Networks = new NetworkPerformanceSnapshot[] { network }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "network:test");

        Assert.True(device.HasUtilizationSample);
        Assert.Equal(40, device.UtilizationPercent, precision: 8);
        Assert.Equal("Ethernet", device.Title);
        Assert.Equal("Ethernet 3", device.Subtitle);
    }

    [Fact]
    public void NetworkGraphDoesNotInventUtilizationWithoutALinkSpeed()
    {
        NetworkPerformanceSnapshot network = new(
            "network:test",
            PerformanceDeviceKind.Network,
            0,
            "Ethernet",
            "Test adapter",
            "Ethernet",
            true,
            true,
            ReceiveBytesPerSecond: 50_000_000,
            SendBytesPerSecond: 10_000_000,
            LinkSpeedBitsPerSecond: 0,
            TotalBytesReceived: 1_000_000_000,
            TotalBytesSent: 500_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Networks = new NetworkPerformanceSnapshot[] { network }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "network:test");

        Assert.False(device.HasUtilizationSample);
        Assert.Equal(0, device.UtilizationPercent);
    }

    [Fact]
    public void DiskUsesPhysicalNumberVolumesAndHardwareName()
    {
        DiskPerformanceSnapshot disk = new(
            "disk:test",
            PerformanceDeviceKind.Disk,
            12,
            "Samsung SSD 990 PRO 4TB",
            "C:, D:",
            "SSD (NVMe)",
            true,
            ActiveTimePercent: 25,
            ReadBytesPerSecond: 1_000,
            WriteBytesPerSecond: 2_000,
            AverageResponseTimeMilliseconds: 0.5,
            QueueDepth: 1,
            CapacityBytes: 4_000_000_000_000,
            FormattedCapacityBytes: 3_900_000_000_000,
            AvailableBytes: 1_000_000_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Disks = new DiskPerformanceSnapshot[] { disk }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "disk:test");

        Assert.Equal("Disk 12 (C:, D:)", device.Title);
        Assert.Equal("Samsung SSD 990 PRO 4TB", device.Subtitle);
        Assert.Equal("Samsung SSD 990 PRO 4TB", device.HardwareName);
        Assert.Equal("25%", device.Summary);
        Assert.DoesNotContain("NVMe", device.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void DiskTitleOmitsVolumeParenthesesWhenNoVolumesAreMounted()
    {
        DiskPerformanceSnapshot disk = new(
            "disk:test",
            PerformanceDeviceKind.Disk,
            4,
            "Microsoft Storage Space Device",
            string.Empty,
            "Storage Spaces",
            false,
            ActiveTimePercent: 0,
            ReadBytesPerSecond: 0,
            WriteBytesPerSecond: 0,
            AverageResponseTimeMilliseconds: 0,
            QueueDepth: 0,
            CapacityBytes: 1_000_000_000,
            FormattedCapacityBytes: 0,
            AvailableBytes: 0);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Disks = new DiskPerformanceSnapshot[] { disk }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "disk:test");

        Assert.Equal("Disk 4", device.Title);
        Assert.Equal("Microsoft Storage Space Device", device.Subtitle);
        Assert.Equal("Microsoft Storage Space Device", device.HardwareName);
    }

    [Theory]
    [InlineData(1, "% Utilization over 1 minute")]
    [InlineData(5, "% Utilization over 5 minutes")]
    public void GraphLabelUsesConfiguredHistoryLength(
        int historyLengthMinutes,
        string expectedLabel)
    {
        PerformanceDevicePresentation CPU = PerformanceDevicePresentationFactory.Create(
                PerformanceSnapshot.Empty,
                historyLengthMinutes)
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU);

        Assert.Equal(expectedLabel, CPU.GraphLabel);
        Assert.DoesNotContain("60 seconds", CPU.GraphLabel, StringComparison.Ordinal);
    }
}

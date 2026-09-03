using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Tray;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTrayTooltipFormatterTests
{
    [Fact]
    public void FormatsFourLineSnapshotUsingFirstSortedNetwork()
    {
        CPUPerformanceSnapshot CPU = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true,
            UtilizationPercent = 45.2
        };
        MemoryPerformanceSnapshot memory = MemoryPerformanceSnapshot.Empty with
        {
            HasMemoryData = true,
            UtilizationPercent = 54.2
        };
        DiskPerformanceSnapshot firstDisk = CreateDisk(sortKey: 0, activeTimePercent: 7.4);
        DiskPerformanceSnapshot secondDisk = CreateDisk(sortKey: 1, activeTimePercent: 12.1);
        NetworkPerformanceSnapshot ethernet = CreateNetwork(
            sortKey: 0,
            sendBytesPerSecond: 400_000,
            receiveBytesPerSecond: 6_000_000,
            linkSpeedBitsPerSecond: 100_000_000);
        NetworkPerformanceSnapshot wifi = CreateNetwork(
            sortKey: 1,
            sendBytesPerSecond: 12_500,
            receiveBytesPerSecond: 250_000,
            linkSpeedBitsPerSecond: 10_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            CPU = CPU,
            Memory = memory,
            Disks = new[] { firstDisk, secondDisk },
            Networks = new[] { ethernet, wifi }
        };

        string tooltip = TaskManagerTrayTooltipFormatter.Format(
            snapshot,
            performanceDeviceOrder: [wifi.DeviceID, ethernet.DeviceID],
            culture: CultureInfo.InvariantCulture);

        Assert.Equal(
            "CPU 45%\nMemory 54%\nDisk 12%\nNetwork 20%",
            tooltip);
    }

    [Fact]
    public void EmptySnapshotReportsUnavailableMetrics()
    {
        string tooltip = TaskManagerTrayTooltipFormatter.Format(
            PerformanceSnapshot.Empty,
            culture: CultureInfo.InvariantCulture);

        Assert.Equal(
            "CPU Unavailable\nMemory Unavailable\nDisk Unavailable\nNetwork Unavailable",
            tooltip);
    }

    private static DiskPerformanceSnapshot CreateDisk(
        int sortKey,
        double activeTimePercent) =>
        new(
            DeviceID: $"disk:{sortKey}",
            Kind: PerformanceDeviceKind.Disk,
            SortKey: sortKey,
            Name: $"Disk {sortKey}",
            VolumeNames: string.Empty,
            DeviceType: "SSD",
            HasPerformanceSample: true,
            ActiveTimePercent: activeTimePercent,
            ReadBytesPerSecond: 0,
            WriteBytesPerSecond: 0,
            AverageResponseTimeMilliseconds: 0,
            QueueDepth: 0,
            CapacityBytes: 0,
            FormattedCapacityBytes: 0,
            AvailableBytes: 0);

    private static NetworkPerformanceSnapshot CreateNetwork(
        int sortKey,
        double sendBytesPerSecond,
        double receiveBytesPerSecond,
        long linkSpeedBitsPerSecond) =>
        new(
            DeviceID: $"network:{sortKey}",
            Kind: PerformanceDeviceKind.Network,
            SortKey: sortKey,
            Name: $"Network {sortKey}",
            Description: string.Empty,
            InterfaceType: "Ethernet",
            IsOperational: true,
            HasThroughputSample: true,
            ReceiveBytesPerSecond: receiveBytesPerSecond,
            SendBytesPerSecond: sendBytesPerSecond,
            LinkSpeedBitsPerSecond: linkSpeedBitsPerSecond,
            TotalBytesReceived: 0,
            TotalBytesSent: 0);
}

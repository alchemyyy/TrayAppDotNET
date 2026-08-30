using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformancePageTests
{
    [Fact]
    public void CPUOverallHistoriesKeepAverageAndHighestCoreSamplesPaired()
    {
        PerformanceHistory utilizationHistory = new();
        PerformanceHistory highestCoreHistory = new();
        CPUPerformanceSnapshot snapshot = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true,
            UtilizationPercent = 37.5,
            HighestLogicalProcessorPercent = 82.25
        };

        PerformancePage.AppendCPUOverallHistories(
            utilizationHistory,
            highestCoreHistory,
            snapshot,
            capturedTimestamp: 123);

        Assert.Equal(1, utilizationHistory.Count);
        Assert.Equal(1, highestCoreHistory.Count);
        Assert.Equal(123, utilizationHistory.GetTimestampChronological(0));
        Assert.Equal(123, highestCoreHistory.GetTimestampChronological(0));
        Assert.Equal(37.5, utilizationHistory.GetChronological(0));
        Assert.Equal(82.25, highestCoreHistory.GetChronological(0));
    }

    [Fact]
    public void CPUOverallHistoriesAdvanceTogetherWhenUtilizationIsUnavailable()
    {
        PerformanceHistory utilizationHistory = new();
        PerformanceHistory highestCoreHistory = new();

        PerformancePage.AppendCPUOverallHistories(
            utilizationHistory,
            highestCoreHistory,
            CPUPerformanceSnapshot.Empty,
            capturedTimestamp: 456);

        Assert.Equal(456, utilizationHistory.CurrentTimestamp);
        Assert.Equal(456, highestCoreHistory.CurrentTimestamp);
        Assert.Equal(0, utilizationHistory.Count);
        Assert.Equal(0, highestCoreHistory.Count);
    }

    [Fact]
    public void CPUOverallHoverMetricShowsHighestThenOverall()
    {
        string metric = PerformancePage.FormatCPUOverallHoverMetric(82, 37);

        Assert.Equal("Highest CPU: 82%\nOverall util: 37%", metric);
    }

    [Fact]
    public void NetworkHoverMetricShowsSendThenReceive()
    {
        string metric = PerformancePage.FormatNetworkTransferHoverMetric(100, 200);

        Assert.Equal("Send: 100 B/s\nReceive: 200 B/s", metric);
    }

    [Fact]
    public void NetworkDeviceColumnHoverMetricUsesCompactLabels()
    {
        string metric = PerformancePage.FormatNetworkDeviceColumnHoverMetric(100, 200);

        Assert.Equal("S: 100 B/s\nR: 200 B/s", metric);
    }

    [Fact]
    public void DiskHoverMetricShowsReadThenWriteWithCompactLabels()
    {
        string metric = PerformancePage.FormatDiskTransferHoverMetric(100, 200);

        Assert.Equal("R: 100 B/s\nW: 200 B/s", metric);
    }

    [Fact]
    public void DiskTransferRateHistoriesKeepReadAndWriteSamplesPaired()
    {
        const long CapturedTimestamp = 123;
        PerformanceMetricHistory readHistory = new(1, 1_000);
        PerformanceMetricHistory writeHistory = new(1, 1_000);
        DiskPerformanceSnapshot snapshot = CreateDiskSnapshot(hasPerformanceSample: true);

        PerformancePage.AppendDiskTransferRateHistories(
            readHistory,
            writeHistory,
            snapshot,
            CapturedTimestamp);

        Assert.True(readHistory.TryGetExact(CapturedTimestamp, out double readBytesPerSecond));
        Assert.True(writeHistory.TryGetExact(CapturedTimestamp, out double writeBytesPerSecond));
        Assert.Equal(100, readBytesPerSecond);
        Assert.Equal(200, writeBytesPerSecond);
    }

    [Fact]
    public void DiskTransferRateHistoriesAdvanceTogetherWhenSampleIsUnavailable()
    {
        const long CapturedTimestamp = 456;
        PerformanceMetricHistory readHistory = new(1, 1_000);
        PerformanceMetricHistory writeHistory = new(1, 1_000);
        DiskPerformanceSnapshot snapshot = CreateDiskSnapshot(hasPerformanceSample: false);

        PerformancePage.AppendDiskTransferRateHistories(
            readHistory,
            writeHistory,
            snapshot,
            CapturedTimestamp);

        Assert.Equal(CapturedTimestamp, readHistory.CurrentTimestamp);
        Assert.Equal(CapturedTimestamp, writeHistory.CurrentTimestamp);
        Assert.Equal(0, readHistory.Count);
        Assert.Equal(0, writeHistory.Count);
    }

    [Fact]
    public void MemoryDeviceColumnHoverMetricUsesGigabytesWithCompactSuffix()
    {
        const double Gibibyte = 1_073_741_824;

        string metric = PerformancePage.FormatMemoryDeviceColumnHoverMetric(4.5 * Gibibyte);

        Assert.Equal("4.5 G", metric);
    }

    private static DiskPerformanceSnapshot CreateDiskSnapshot(bool hasPerformanceSample) => new(
        DeviceID: "disk:test",
        Kind: PerformanceDeviceKind.Disk,
        SortKey: 0,
        Name: "Test disk",
        VolumeNames: "C:",
        DeviceType: "SSD",
        HasPerformanceSample: hasPerformanceSample,
        ActiveTimePercent: 25,
        ReadBytesPerSecond: 100,
        WriteBytesPerSecond: 200,
        AverageResponseTimeMilliseconds: 1,
        QueueDepth: 0,
        CapacityBytes: 0,
        FormattedCapacityBytes: 0,
        AvailableBytes: 0);
}

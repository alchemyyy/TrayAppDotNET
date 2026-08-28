using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceSnapshotServiceTests
{
    [Fact]
    public void PeriodicSamplingRunsUntilDisposalAndAcceptsLiveConfiguration()
    {
        using PerformanceSnapshotService service = new(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            maximumHistoryCount: 8);
        service.Start();
        Assert.True(SpinWait.SpinUntil(
            () => service.GetLatestSnapshot().CapturedAt != DateTimeOffset.MinValue,
            TimeSpan.FromSeconds(5)));

        service.UpdateConfiguration(
            PerformanceSamplingSettings.MinimumSampleIntervalMilliseconds,
            maximumHistoryCount: 3);
        Assert.True(SpinWait.SpinUntil(
            () => service.GetSnapshotHistory().Count >= 3,
            TimeSpan.FromSeconds(5)));
        IReadOnlyList<PerformanceSnapshot> retainedHistory = service.GetSnapshotHistory();
        Assert.Equal(3, retainedHistory.Count);
        Assert.True(retainedHistory[0].CapturedTimestamp < retainedHistory[1].CapturedTimestamp);
        Assert.True(retainedHistory[1].CapturedTimestamp < retainedHistory[2].CapturedTimestamp);

        service.Dispose();
        DateTimeOffset disposedSnapshotTime = service.GetLatestSnapshot().CapturedAt;
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        Assert.Equal(disposedSnapshotTime, service.GetLatestSnapshot().CapturedAt);
    }

    [Fact]
    public void SnapshotHistoryRetainsNewestSamplesAcrossCapacityChanges()
    {
        using PerformanceSnapshotService service = new(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            maximumHistoryCount: 2);

        _ = service.SampleNow();
        PerformanceSnapshot secondSnapshot = service.SampleNow();
        PerformanceSnapshot thirdSnapshot = service.SampleNow();

        IReadOnlyList<PerformanceSnapshot> retainedHistory = service.GetSnapshotHistory();
        Assert.Equal(2, retainedHistory.Count);
        Assert.Equal(secondSnapshot.CapturedTimestamp, retainedHistory[0].CapturedTimestamp);
        Assert.Equal(thirdSnapshot.CapturedTimestamp, retainedHistory[1].CapturedTimestamp);

        service.UpdateConfiguration(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            maximumHistoryCount: 1);
        retainedHistory = service.GetSnapshotHistory();
        Assert.Single(retainedHistory);
        Assert.Equal(thirdSnapshot.CapturedTimestamp, retainedHistory[0].CapturedTimestamp);

        service.UpdateConfiguration(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            maximumHistoryCount: 3);
        PerformanceSnapshot fourthSnapshot = service.SampleNow();
        retainedHistory = service.GetSnapshotHistory();
        Assert.Equal(2, retainedHistory.Count);
        Assert.Equal(thirdSnapshot.CapturedTimestamp, retainedHistory[0].CapturedTimestamp);
        Assert.Equal(fourthSnapshot.CapturedTimestamp, retainedHistory[1].CapturedTimestamp);

        IReadOnlyList<PerformanceSnapshot> incrementalHistory =
            service.GetSnapshotHistoryAfter(thirdSnapshot.CapturedTimestamp);
        Assert.Single(incrementalHistory);
        Assert.Equal(fourthSnapshot.CapturedTimestamp, incrementalHistory[0].CapturedTimestamp);
        Assert.Empty(service.GetSnapshotHistoryAfter(fourthSnapshot.CapturedTimestamp));
    }

    [Fact]
    public void NativeSnapshotContainsDirectSystemDevicesAndStableIDs()
    {
        using PerformanceSnapshotService service = new();

        PerformanceSnapshot initialSnapshot = service.SampleNow();
        Thread.Sleep(25);
        PerformanceSnapshot snapshot = service.SampleNow();

        Assert.NotEqual(DateTimeOffset.MinValue, snapshot.CapturedAt);
        Assert.True(snapshot.CapturedTimestamp > 0);
        Assert.Equal(CPUPerformanceSnapshot.StableDeviceID, snapshot.CPU.DeviceID);
        Assert.Equal(PerformanceDeviceKind.CPU, snapshot.CPU.Kind);
        Assert.True(snapshot.CPU.LogicalProcessorCount > 0);
        Assert.Equal(
            snapshot.CPU.LogicalProcessorCount,
            snapshot.CPU.LogicalProcessorUtilizationPercents.Length);
        Assert.InRange(snapshot.CPU.UtilizationPercent, 0, 100);
        Assert.True(snapshot.CPU.HasFrequencyData);
        Assert.True(snapshot.CPU.HighestCurrentSpeedHertz > 0);
        Assert.True(snapshot.CPU.BaseSpeedHertz > 0);
        Assert.True(snapshot.CPU.HighestRecordedSpeedHertz >= snapshot.CPU.HighestCurrentSpeedHertz);
        Assert.True(
            snapshot.CPU.HighestRecordedSpeedHertz
            >= initialSnapshot.CPU.HighestRecordedSpeedHertz);
        Assert.Equal(MemoryPerformanceSnapshot.StableDeviceID, snapshot.Memory.DeviceID);
        Assert.Equal(PerformanceDeviceKind.Memory, snapshot.Memory.Kind);
        Assert.True(snapshot.Memory.HasMemoryData);
        Assert.True(snapshot.Memory.TotalPhysicalBytes > 0);
        Assert.True(snapshot.Memory.AvailablePhysicalBytes <= snapshot.Memory.TotalPhysicalBytes);
        Assert.Equal(
            snapshot.Memory.TotalPhysicalBytes - snapshot.Memory.AvailablePhysicalBytes,
            snapshot.Memory.UsedPhysicalBytes);

        Assert.All(snapshot.GPUs.ToArray(), static gpu =>
        {
            Assert.StartsWith("gpu:", gpu.DeviceID, StringComparison.Ordinal);
            Assert.Equal(PerformanceDeviceKind.GPU, gpu.Kind);
            Assert.InRange(gpu.UtilizationPercent, 0, 100);
        });
        Assert.All(snapshot.Networks.ToArray(), static network =>
        {
            Assert.StartsWith("network:", network.DeviceID, StringComparison.Ordinal);
            Assert.Equal(PerformanceDeviceKind.Network, network.Kind);
            Assert.True(network.ReceiveBytesPerSecond >= 0);
            Assert.True(network.SendBytesPerSecond >= 0);
        });
        Assert.NotEmpty(snapshot.Disks.ToArray());
        Assert.All(snapshot.Disks.ToArray(), static disk =>
        {
            Assert.StartsWith("disk:", disk.DeviceID, StringComparison.Ordinal);
            Assert.Equal(PerformanceDeviceKind.Disk, disk.Kind);
            Assert.InRange(disk.ActiveTimePercent, 0, 100);
        });
    }
}

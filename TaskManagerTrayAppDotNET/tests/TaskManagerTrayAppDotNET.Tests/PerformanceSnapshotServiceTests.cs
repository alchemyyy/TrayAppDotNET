using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceSnapshotServiceTests
{
    [Fact]
    public void PeriodicSamplingStopsWhileInactive()
    {
        using PerformanceSnapshotService service = new();
        service.Start();
        service.SetActive(true);
        Assert.True(SpinWait.SpinUntil(
            () => service.GetLatestSnapshot().CapturedAt != DateTimeOffset.MinValue,
            TimeSpan.FromSeconds(5)));

        service.SetActive(false);
        DateTimeOffset pausedSnapshotTime = service.GetLatestSnapshot().CapturedAt;
        Thread.Sleep(TimeSpan.FromMilliseconds(1_200));

        Assert.Equal(pausedSnapshotTime, service.GetLatestSnapshot().CapturedAt);
    }

    [Fact]
    public void NativeSnapshotContainsDirectSystemDevicesAndStableIDs()
    {
        using PerformanceSnapshotService service = new();

        _ = service.SampleNow();
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

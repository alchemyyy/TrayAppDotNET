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

        Assert.Equal(["cpu", "memory"],
            devices.Select(static device => device.DeviceID));
        Assert.All(devices, static device => Assert.False(device.HasUtilizationSample));
    }

    [Fact]
    public void CPUUsesHighestTurboSpeedAndUpdatedMetricLabels()
    {
        CPUPerformanceSnapshot CPU = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true,
            UtilizationPercent = 42,
            HighestLogicalProcessorPercent = 73,
            HasFrequencyData = true,
            HighestCurrentSpeedHertz = 5_400_000_000,
            BaseSpeedHertz = 4_200_000_000,
            HighestRecordedSpeedHertz = 5_700_000_000,
            CoreCount = 16,
            LogicalProcessorCount = 32
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { CPU = CPU };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU);
        PerformanceStatistic[] statistics = device.Statistics.ToArray();
        Dictionary<string, string> valuesByLabel = statistics.ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal(expected: "42%  5.40 GHz", device.Summary);
        Assert.Equal(expected: "5.40 GHz", valuesByLabel["Speed"]);
        Assert.Equal(expected: "5.70 GHz", valuesByLabel["Highest recorded speed"]);
        Assert.Equal(expected: "4.20 GHz", valuesByLabel["Base speed"]);
        Assert.Equal(expected: "73%", valuesByLabel["Highest logical processor"]);
        Assert.Equal(expected: "16", valuesByLabel["Physical cores"]);
        Assert.Equal(expected: "32", valuesByLabel["Logical processors"]);
        Assert.Equal(
            [
                "Utilization",
                "Speed",
                "Highest logical processor",
                "Processes",
                "Threads",
                "Handles",
                "Up time",
                "Highest recorded speed"
            ],
            statistics.Take(8).Select(static statistic => statistic.Label));
        Assert.DoesNotContain(statistics, static statistic => statistic.Label is
            "Maximum speed" or "Cores" or "Logical cores" or "Highest logical core");
    }

    [Fact]
    public void NetworkGraphUsesTheBusiestDirectionRelativeToLinkSpeed()
    {
        NetworkPerformanceSnapshot network = new(
            DeviceID: "network:test",
            PerformanceDeviceKind.Network,
            SortKey: 0,
            Name: "Ethernet 3",
            Description: "Test adapter",
            InterfaceType: "Ethernet",
            IsOperational: true,
            HasThroughputSample: true,
            ReceiveBytesPerSecond: 50_000_000,
            SendBytesPerSecond: 10_000_000,
            LinkSpeedBitsPerSecond: 1_000_000_000,
            TotalBytesReceived: 1_000_000_000,
            TotalBytesSent: 500_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Networks = new[] { network }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "network:test");

        Assert.True(device.HasUtilizationSample);
        Assert.Equal(expected: 40, device.UtilizationPercent, precision: 8);
        Assert.Equal(expected: "Ethernet", device.Title);
        Assert.Equal(expected: "Ethernet 3 - Test adapter", device.Subtitle);
        Assert.Equal(expected: "Test adapter", device.HardwareName);
    }

    [Fact]
    public void NetworkMenuEntryUsesTheReplacedHardwareAdapterName()
    {
        NetworkPerformanceSnapshot network = new(
            DeviceID: "network:test",
            PerformanceDeviceKind.Network,
            SortKey: 0,
            Name: "Ethernet 3",
            Description: "Intel(R) Ethernet Converged Network Adapter X540-T2",
            InterfaceType: "Ethernet",
            IsOperational: true,
            HasThroughputSample: true,
            ReceiveBytesPerSecond: 50_000_000,
            SendBytesPerSecond: 10_000_000,
            LinkSpeedBitsPerSecond: 1_000_000_000,
            TotalBytesReceived: 1_000_000_000,
            TotalBytesSent: 500_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Networks = new[] { network }
        };
        PerformanceHardwareNameResolver resolver = PerformanceHardwareNameResolver.Create(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Network,
                MatchPattern = "^Intel\\(R\\) Ethernet Converged Network Adapter (?<Model>.+)$",
                Replacement = "Intel Ethernet ${Model}"
            }
        ]);

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(
                snapshot,
                PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
                resolver)
            .Single(static candidate => candidate.DeviceID == "network:test");

        Assert.Equal(expected: "Ethernet 3 - Intel Ethernet X540-T2", device.Subtitle);
        Assert.Equal(expected: "Intel Ethernet X540-T2", device.HardwareName);
    }

    [Fact]
    public void NetworkGraphDoesNotInventUtilizationWithoutALinkSpeed()
    {
        NetworkPerformanceSnapshot network = new(
            DeviceID: "network:test",
            PerformanceDeviceKind.Network,
            SortKey: 0,
            Name: "Ethernet",
            Description: "Test adapter",
            InterfaceType: "Ethernet",
            IsOperational: true,
            HasThroughputSample: true,
            ReceiveBytesPerSecond: 50_000_000,
            SendBytesPerSecond: 10_000_000,
            LinkSpeedBitsPerSecond: 0,
            TotalBytesReceived: 1_000_000_000,
            TotalBytesSent: 500_000_000);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Networks = new[] { network }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "network:test");

        Assert.False(device.HasUtilizationSample);
        Assert.Equal(expected: 0, device.UtilizationPercent);
    }

    [Fact]
    public void DiskUsesPhysicalNumberVolumesAndHardwareName()
    {
        DiskPerformanceSnapshot disk = new(
            DeviceID: "disk:test",
            PerformanceDeviceKind.Disk,
            SortKey: 12,
            Name: "Samsung SSD 990 PRO 4TB",
            VolumeNames: "C:, D:",
            DeviceType: "SSD (NVMe)",
            HasPerformanceSample: true,
            ActiveTimePercent: 25,
            ReadBytesPerSecond: 1_000,
            WriteBytesPerSecond: 2_000,
            AverageResponseTimeMilliseconds: 0.5,
            QueueDepth: 1,
            CapacityBytes: 4_000_000_000_000,
            FormattedCapacityBytes: 3_900_000_000_000,
            AvailableBytes: 1_000_000_000_000)
        {
            Details = new DiskPerformanceDetailsSnapshot(
                DeviceID: "disk:test",
                PhysicalDiskNumber: 12,
                Model: "Samsung SSD 990 PRO 4TB",
                VolumeNames: "C:, D:",
                DeviceType: "SSD (NVMe)",
                HasPerformanceSample: true,
                ActiveTimePercent: 25,
                TransferBytesPerSecond: 3_000,
                ReadBytesPerSecond: 1_000,
                WriteBytesPerSecond: 2_000,
                AverageResponseTimeMilliseconds: 0.5,
                CapacityBytes: 4_000_000_000_000,
                FormattedCapacityBytes: 3_900_000_000_000,
                HasSystemDiskData: true,
                IsSystemDisk: true,
                HasPageFileData: true,
                HasPageFile: false)
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Disks = new[] { disk }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "disk:test");

        Assert.Equal(expected: "Disk 12 (C:, D:)", device.Title);
        Assert.Equal(expected: "Samsung SSD 990 PRO 4TB", device.Subtitle);
        Assert.Equal(expected: "Samsung SSD 990 PRO 4TB", device.HardwareName);
        Assert.Equal(expected: "25%", device.Summary);
        Assert.DoesNotContain(expectedSubstring: "NVMe", device.Subtitle, StringComparison.Ordinal);
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal(expected: "Yes", valuesByLabel["System disk"]);
        Assert.Equal(expected: "No", valuesByLabel["Page file"]);
        Assert.Equal(expected: "SSD (NVMe)", valuesByLabel["Type"]);
        Assert.Equal(
            ["Active time", "Average response time", "Read speed", "Write speed"],
            device.Statistics.Span[..4].ToArray().Select(static statistic => statistic.Label));
    }

    [Fact]
    public void GPUUsesInstalledVRAMAndOfficialDetailMetrics()
    {
        const ulong Gibibyte = 1_073_741_824;
        GPUPerformanceSnapshot GPU = new(
            DeviceID: "gpu:test",
            PerformanceDeviceKind.GPU,
            SortKey: 0,
            Name: "NVIDIA GeForce RTX Test",
            AdapterLUID: 123,
            PhysicalAdapterIndex: 0,
            HasUtilizationSample: true,
            UtilizationPercent: 31,
            ReadOnlyMemory<GPUPerformanceEngineSnapshot>.Empty,
            HasDedicatedMemoryData: true,
            3 * Gibibyte,
            15 * Gibibyte,
            HasSharedMemoryData: true,
            Gibibyte / 2,
            64 * Gibibyte)
        {
            Details = new GPUPerformanceDetailsSnapshot(
                HasDetailData: true,
                ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot>.Empty,
                HasTemperatureData: true,
                TemperatureCelsius: 32,
                DriverVersion: "32.0.15.9660",
                new DateOnly(year: 2026, month: 5, day: 22),
                DirectXVersion: "12",
                FeatureLevel: "12.2",
                PhysicalLocation: "PCI bus 33, device 0, function 0",
                HasHardwareReservedMemoryData: true,
                Gibibyte)
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { GPUs = new[] { GPU } };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "gpu:test");
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal(expected: "3.0/16.0 GB", valuesByLabel["Dedicated GPU memory"]);
        Assert.Equal(expected: "3.5/80.0 GB", valuesByLabel["GPU Memory"]);
        Assert.Equal(expected: "0.5/64.0 GB", valuesByLabel["Shared GPU memory"]);
        Assert.Equal(expected: "32 \u00B0C", valuesByLabel["Temperature"]);
        Assert.Equal(expected: "32.0.15.9660", valuesByLabel["Driver version"]);
        Assert.Equal(expected: "12 (FL 12.2)", valuesByLabel["DirectX version"]);
        Assert.Equal(expected: "PCI bus 33, device 0, function 0", valuesByLabel["Physical location"]);
        Assert.Equal(expected: "1.0 GB", valuesByLabel["Hardware reserved memory"]);
    }

    [Fact]
    public void GPUTotalMemoryIsUnavailableWhenEitherUsageCounterIsMissing()
    {
        const ulong Gibibyte = 1_073_741_824;
        GPUPerformanceSnapshot GPU = new(
            DeviceID: "gpu:test",
            PerformanceDeviceKind.GPU,
            SortKey: 0,
            Name: "GPU",
            AdapterLUID: 1,
            PhysicalAdapterIndex: 0,
            HasUtilizationSample: true,
            UtilizationPercent: 0,
            ReadOnlyMemory<GPUPerformanceEngineSnapshot>.Empty,
            HasDedicatedMemoryData: true,
            Gibibyte,
            8 * Gibibyte,
            HasSharedMemoryData: false,
            SharedMemoryBytes: 0,
            16 * Gibibyte);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { GPUs = new[] { GPU } };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "gpu:test");
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.NotEqual(expected: "Unavailable", valuesByLabel["Dedicated GPU memory"]);
        Assert.Equal(expected: "Unavailable", valuesByLabel["Shared GPU memory"]);
        Assert.Equal(expected: "Unavailable", valuesByLabel["GPU Memory"]);
    }

    [Fact]
    public void DiskTitleOmitsVolumeParenthesesWhenNoVolumesAreMounted()
    {
        DiskPerformanceSnapshot disk = new(
            DeviceID: "disk:test",
            PerformanceDeviceKind.Disk,
            SortKey: 4,
            Name: "Microsoft Storage Space Device",
            string.Empty,
            DeviceType: "Storage Spaces",
            HasPerformanceSample: false,
            ActiveTimePercent: 0,
            ReadBytesPerSecond: 0,
            WriteBytesPerSecond: 0,
            AverageResponseTimeMilliseconds: 0,
            QueueDepth: 0,
            CapacityBytes: 1_000_000_000,
            FormattedCapacityBytes: 0,
            AvailableBytes: 0)
        {
            Details = new DiskPerformanceDetailsSnapshot(
                DeviceID: "disk:test",
                PhysicalDiskNumber: 4,
                Model: "Microsoft Storage Space Device",
                string.Empty,
                DeviceType: "Storage Spaces",
                HasPerformanceSample: false,
                ActiveTimePercent: 0,
                TransferBytesPerSecond: 0,
                ReadBytesPerSecond: 0,
                WriteBytesPerSecond: 0,
                AverageResponseTimeMilliseconds: 0,
                CapacityBytes: 1_000_000_000,
                FormattedCapacityBytes: 0,
                HasSystemDiskData: false,
                IsSystemDisk: false,
                HasPageFileData: false,
                HasPageFile: false)
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Disks = new[] { disk }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "disk:test");

        Assert.Equal(expected: "Disk 4", device.Title);
        Assert.Equal(expected: "Microsoft Storage Space Device", device.Subtitle);
        Assert.Equal(expected: "Microsoft Storage Space Device", device.HardwareName);
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal(expected: "Unavailable", valuesByLabel["System disk"]);
        Assert.Equal(expected: "Unavailable", valuesByLabel["Page file"]);
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
        Assert.DoesNotContain(expectedSubstring: "60 seconds", CPU.GraphLabel, StringComparison.Ordinal);
    }
}

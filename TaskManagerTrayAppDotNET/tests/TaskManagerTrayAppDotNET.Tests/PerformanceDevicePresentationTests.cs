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

        Assert.Equal("42%  5.40 GHz", device.Summary);
        Assert.Equal("5.40 GHz", valuesByLabel["Speed"]);
        Assert.Equal("5.70 GHz", valuesByLabel["Highest recorded speed"]);
        Assert.Equal("4.20 GHz", valuesByLabel["Base speed"]);
        Assert.Equal("73%", valuesByLabel["Highest logical processor"]);
        Assert.Equal("16", valuesByLabel["Physical cores"]);
        Assert.Equal("32", valuesByLabel["Logical processors"]);
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
        Assert.Equal("Ethernet 3 - Test adapter", device.Subtitle);
        Assert.Equal("Test adapter", device.HardwareName);
    }

    [Fact]
    public void NetworkMenuEntryUsesTheReplacedHardwareAdapterName()
    {
        NetworkPerformanceSnapshot network = new(
            "network:test",
            PerformanceDeviceKind.Network,
            0,
            "Ethernet 3",
            "Intel(R) Ethernet Converged Network Adapter X540-T2",
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

        Assert.Equal("Ethernet 3 - Intel Ethernet X540-T2", device.Subtitle);
        Assert.Equal("Intel Ethernet X540-T2", device.HardwareName);
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
            AvailableBytes: 1_000_000_000_000)
        {
            Details = new DiskPerformanceDetailsSnapshot(
                "disk:test",
                12,
                "Samsung SSD 990 PRO 4TB",
                "C:, D:",
                "SSD (NVMe)",
                true,
                25,
                3_000,
                1_000,
                2_000,
                0.5,
                4_000_000_000_000,
                3_900_000_000_000,
                true,
                true,
                true,
                false)
        };
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
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal("Yes", valuesByLabel["System disk"]);
        Assert.Equal("No", valuesByLabel["Page file"]);
        Assert.Equal("SSD (NVMe)", valuesByLabel["Type"]);
        Assert.Equal(
            ["Active time", "Average response time", "Read speed", "Write speed"],
            device.Statistics.Span[..4].ToArray().Select(static statistic => statistic.Label));
    }

    [Fact]
    public void GPUUsesInstalledVRAMAndOfficialDetailMetrics()
    {
        const ulong Gibibyte = 1_073_741_824;
        GPUPerformanceSnapshot GPU = new(
            "gpu:test",
            PerformanceDeviceKind.GPU,
            0,
            "NVIDIA GeForce RTX Test",
            123,
            0,
            true,
            31,
            ReadOnlyMemory<GPUPerformanceEngineSnapshot>.Empty,
            true,
            3 * Gibibyte,
            15 * Gibibyte,
            true,
            Gibibyte / 2,
            64 * Gibibyte)
        {
            Details = new GPUPerformanceDetailsSnapshot(
                true,
                ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot>.Empty,
                true,
                32,
                "32.0.15.9660",
                new DateOnly(2026, 5, 22),
                "12",
                "12.2",
                "PCI bus 33, device 0, function 0",
                true,
                Gibibyte)
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            GPUs = new GPUPerformanceSnapshot[] { GPU }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "gpu:test");
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal("3.0/16.0 GB", valuesByLabel["Dedicated GPU memory"]);
        Assert.Equal("3.5/80.0 GB", valuesByLabel["GPU Memory"]);
        Assert.Equal("0.5/64.0 GB", valuesByLabel["Shared GPU memory"]);
        Assert.Equal("32 \u00B0C", valuesByLabel["Temperature"]);
        Assert.Equal("32.0.15.9660", valuesByLabel["Driver version"]);
        Assert.Equal("12 (FL 12.2)", valuesByLabel["DirectX version"]);
        Assert.Equal("PCI bus 33, device 0, function 0", valuesByLabel["Physical location"]);
        Assert.Equal("1.0 GB", valuesByLabel["Hardware reserved memory"]);
    }

    [Fact]
    public void GPUTotalMemoryIsUnavailableWhenEitherUsageCounterIsMissing()
    {
        const ulong Gibibyte = 1_073_741_824;
        GPUPerformanceSnapshot GPU = new(
            "gpu:test",
            PerformanceDeviceKind.GPU,
            0,
            "GPU",
            1,
            0,
            true,
            0,
            ReadOnlyMemory<GPUPerformanceEngineSnapshot>.Empty,
            true,
            Gibibyte,
            8 * Gibibyte,
            false,
            0,
            16 * Gibibyte);
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            GPUs = new GPUPerformanceSnapshot[] { GPU }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "gpu:test");
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.NotEqual("Unavailable", valuesByLabel["Dedicated GPU memory"]);
        Assert.Equal("Unavailable", valuesByLabel["Shared GPU memory"]);
        Assert.Equal("Unavailable", valuesByLabel["GPU Memory"]);
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
            AvailableBytes: 0)
        {
            Details = new DiskPerformanceDetailsSnapshot(
                "disk:test",
                4,
                "Microsoft Storage Space Device",
                string.Empty,
                "Storage Spaces",
                false,
                0,
                0,
                0,
                0,
                0,
                1_000_000_000,
                0,
                false,
                false,
                false,
                false)
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with
        {
            Disks = new DiskPerformanceSnapshot[] { disk }
        };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "disk:test");

        Assert.Equal("Disk 4", device.Title);
        Assert.Equal("Microsoft Storage Space Device", device.Subtitle);
        Assert.Equal("Microsoft Storage Space Device", device.HardwareName);
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal("Unavailable", valuesByLabel["System disk"]);
        Assert.Equal("Unavailable", valuesByLabel["Page file"]);
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

using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Performance;
using static TaskManagerTrayAppDotNET.Tests.TestLocalization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDevicePresentationTests
{
    private static string Unavailable => L(nameof(AppStrings.Perf_Unavailable));

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
    public void MemoryRowShowsUsedOfTotalThenAvailableWithInUsePercent()
    {
        MemoryPerformanceSnapshot memory = MemoryPerformanceSnapshot.Empty with
        {
            HasMemoryData = true,
            UtilizationPercent = 39,
            TotalPhysicalBytes = 32UL * 1_024 * 1_024 * 1_024,
            UsedPhysicalBytes = 12UL * 1_024 * 1_024 * 1_024,
            AvailablePhysicalBytes = 19UL * 1_024 * 1_024 * 1_024
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { Memory = memory };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.Memory);

        Assert.Equal(
            expected: string.Concat(
                PerformanceDevicePresentationFactory.FormatBytes(memory.UsedPhysicalBytes),
                " / ",
                PerformanceDevicePresentationFactory.FormatBytes(memory.TotalPhysicalBytes)),
            device.Subtitle);
        Assert.Equal(
            expected: F(
                nameof(AppStrings.Perf_Memory_DeviceFreeFormat),
                PerformanceDevicePresentationFactory.FormatBytes(memory.AvailablePhysicalBytes),
                "39%"),
            device.Summary);
    }

    [Fact]
    public void CPUUsesMeasuredTurboSpeedAndUpdatedMetricLabels()
    {
        CPUPerformanceSnapshot CPU = CPUPerformanceSnapshot.Empty with
        {
            HasUtilizationSample = true,
            UtilizationPercent = 42,
            HighestLogicalProcessorPercent = 73,
            HasFrequencyData = true,
            CurrentSpeedHertz = 5_400_000_000,
            BaseSpeedHertz = 4_200_000_000,
            HighestRecordedSpeedHertz = 5_700_000_000,
            CoreCount = 16,
            LogicalProcessorCount = 32,
            HasPowerData = true,
            PowerWatts = 157.6
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { CPU = CPU };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU);
        PerformanceStatistic[] statistics = device.Statistics.ToArray();
        Dictionary<string, string> valuesByLabel = statistics.ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal(expected: "42%  5.40 GHz", device.Summary);
        Assert.Equal(expected: "5.40 GHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_Speed))]);
        Assert.Equal(expected: "5.70 GHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_HighestRecordedSpeed))]);
        Assert.Equal(expected: "4.20 GHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_BaseSpeed))]);
        Assert.Equal(expected: "73%", valuesByLabel[L(nameof(AppStrings.Perf_Label_HighestLogicalProcessor))]);
        Assert.Equal(expected: "16", valuesByLabel[L(nameof(AppStrings.Perf_Label_PhysicalCores))]);
        Assert.Equal(expected: "32", valuesByLabel[L(nameof(AppStrings.Perf_Label_LogicalProcessors))]);
        Assert.Equal(expected: "158 W", valuesByLabel[L(nameof(AppStrings.Perf_Label_Power))]);
        Assert.Equal(
            [
                L(nameof(AppStrings.Perf_Label_Utilization)),
                L(nameof(AppStrings.Perf_Label_Speed)),
                L(nameof(AppStrings.Perf_Label_Power)),
                L(nameof(AppStrings.Perf_Label_HighestLogicalProcessor)),
                L(nameof(AppStrings.Perf_Label_Processes)),
                L(nameof(AppStrings.Perf_Label_Threads)),
                L(nameof(AppStrings.Perf_Label_Handles)),
                L(nameof(AppStrings.Perf_Label_UpTime)),
                L(nameof(AppStrings.Perf_Label_HighestRecordedSpeed))
            ],
            statistics.Take(9).Select(static statistic => statistic.Label));
        Assert.DoesNotContain(statistics, static statistic => statistic.Label is
            "Maximum speed" or "Cores" or "Logical cores" or "Highest logical core");
    }

    [Theory]
    [InlineData(true, "988", "19,241", "930,762")]
    [InlineData(false, null, null, null)]
    public void CPUProcessTotalsShowOnlyWhenMeasured(
        bool hasProcessTotals,
        string? expectedProcesses,
        string? expectedThreads,
        string? expectedHandles)
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            CPUPerformanceSnapshot CPU = CPUPerformanceSnapshot.Empty with
            {
                HasProcessTotals = hasProcessTotals,
                ProcessCount = 988,
                ThreadCount = 19_241,
                HandleCount = 930_762
            };
            PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { CPU = CPU };

            Dictionary<string, string> valuesByLabel = PerformanceDevicePresentationFactory.Create(snapshot)
                .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU)
                .Statistics.ToArray()
                .ToDictionary(static statistic => statistic.Label, static statistic => statistic.Value);

            Assert.Equal(expectedProcesses ?? Unavailable, valuesByLabel[L(nameof(AppStrings.Perf_Label_Processes))]);
            Assert.Equal(expectedThreads ?? Unavailable, valuesByLabel[L(nameof(AppStrings.Perf_Label_Threads))]);
            Assert.Equal(expectedHandles ?? Unavailable, valuesByLabel[L(nameof(AppStrings.Perf_Label_Handles))]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
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

        Assert.Equal(expected: F(nameof(AppStrings.Perf_Title_DiskWithVolumesFormat), 12, "C:, D:"), device.Title);
        Assert.Equal(expected: "Samsung SSD 990 PRO 4TB", device.Subtitle);
        Assert.Equal(expected: "Samsung SSD 990 PRO 4TB", device.HardwareName);
        Assert.Equal(expected: "25%", device.Summary);
        Assert.DoesNotContain(expectedSubstring: "NVMe", device.Subtitle, StringComparison.Ordinal);
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal(expected: L(nameof(AppStrings.Perf_Yes)), valuesByLabel[L(nameof(AppStrings.Perf_Label_SystemDisk))]);
        Assert.Equal(expected: L(nameof(AppStrings.Perf_No)), valuesByLabel[L(nameof(AppStrings.Perf_Label_PageFile))]);
        Assert.Equal(expected: "SSD (NVMe)", valuesByLabel[L(nameof(AppStrings.Perf_Label_Type))]);
        Assert.Equal(
            [L(nameof(AppStrings.Perf_Label_ActiveTime)), L(nameof(AppStrings.Perf_Label_AverageResponseTime)), L(nameof(AppStrings.Perf_Label_ReadSpeed)), L(nameof(AppStrings.Perf_Label_WriteSpeed))],
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
                ReadOnlyMemory<GPUPerformanceDetailEngineSnapshot>.Empty,
                HasTemperatureData: true,
                TemperatureCelsius: 32,
                HasPowerData: true,
                PowerPercent: 25,
                HasClockData: true,
                ClockHertz: 2_760_000_000,
                MaximumClockHertz: 3_090_000_000,
                HasMemoryClockData: true,
                MemoryClockHertz: 17_601_000_000,
                DriverVersion: "32.0.15.9660",
                new DateOnly(year: 2026, month: 5, day: 22),
                DirectXVersion: "12",
                FeatureLevel: "12.2",
                PhysicalLocation: "PCI bus 33, device 0, function 0",
                HasHardwareReservedMemoryData: true,
                Gibibyte,
                new GPUAdapterIdentity(
                    DeviceInstanceID: @"PCI\VEN_10DE&DEV_2C02&SUBSYS_20961771&REV_A1\4&1",
                    new GPUHardwareID(HasValue: true, 0x10DE, 0x2C02, 0x1771, 0x2096, 0xA1),
                    PublicDriverVersion: "596.60",
                    VBIOSVersion: "98.03.3B.C0.AB",
                    new GPUPCIeLink(HasValue: true, 4, 8, 5, 16),
                    new GPUPCIAddress(HasValue: true, 33, 0, 0)))
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { GPUs = new[] { GPU } };

        PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static candidate => candidate.DeviceID == "gpu:test");
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal(expected: "3.0/16.0 GB", valuesByLabel[L(nameof(AppStrings.Perf_Label_DedicatedGPUMemory))]);
        Assert.Equal(expected: "0.5/64.0 GB", valuesByLabel[L(nameof(AppStrings.Perf_Label_SharedGPUMemory))]);
        Assert.Equal(expected: "32 \u00B0C", valuesByLabel[L(nameof(AppStrings.Perf_Label_Temperature))]);
        Assert.Equal(expected: "25%", valuesByLabel[L(nameof(AppStrings.Perf_Label_Power))]);
        Assert.Equal(expected: "2,760 MHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_GPUClock))]);
        Assert.Equal(expected: "17,601 MHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_MemoryClock))]);
        Assert.Equal(expected: "3,090 MHz", valuesByLabel[L(nameof(AppStrings.Perf_Label_MaxGPUClock))]);
        Assert.Equal(expected: "596.60 (32.0.15.9660)", valuesByLabel[L(nameof(AppStrings.Perf_Label_DriverVersion))]);
        Assert.Equal(expected: "12 (FL 12.2)", valuesByLabel[L(nameof(AppStrings.Perf_Label_DirectXVersion))]);
        Assert.Equal(expected: "98.03.3B.C0.AB", valuesByLabel[L(nameof(AppStrings.Perf_Label_VBIOSVersion))]);
        Assert.Equal(expected: "10DE:2C02 (1771:2096, rev A1)", valuesByLabel[L(nameof(AppStrings.Perf_Label_DeviceID))]);
        Assert.Equal(expected: "Gen 4 x8 (max Gen 5 x16)", valuesByLabel[L(nameof(AppStrings.Perf_Label_PCIeLink))]);
        Assert.Equal(expected: "PCI bus 33, device 0, function 0", valuesByLabel[L(nameof(AppStrings.Perf_Label_PhysicalLocation))]);
        Assert.Equal(expected: "1.0 GB", valuesByLabel[L(nameof(AppStrings.Perf_Label_HardwareReservedMemory))]);

        // A saved limit read under the current vBIOS turns the same 25% into watts; one read under another does not
        GPUPowerLimitRecord record = new()
        {
            DeviceInstanceID = GPU.Details.Identity.DeviceInstanceID,
            VBIOSVersion = "98.03.3B.C0.AB",
            DefaultLimitMilliwatts = 360_000,
            ShowWatts = true
        };
        Assert.Equal(expected: "90 W", PowerFor(snapshot, record));
        record.VBIOSVersion = "98.03.3B.C0.AA";
        Assert.Equal(expected: "25%", PowerFor(snapshot, record));

        static string PowerFor(PerformanceSnapshot snapshot, GPUPowerLimitRecord record) =>
            PerformanceDevicePresentationFactory.Create(snapshot, 1, PerformanceHardwareNameResolver.Empty, [record])
                .Single(static candidate => candidate.DeviceID == "gpu:test")
                .Statistics.ToArray()
                .Single(static statistic => statistic.Label == L(nameof(AppStrings.Perf_Label_Power)))
                .Value;
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

        Assert.Equal(expected: F(nameof(AppStrings.Perf_Title_DiskFormat), 4), device.Title);
        Assert.Equal(expected: "Microsoft Storage Space Device", device.Subtitle);
        Assert.Equal(expected: "Microsoft Storage Space Device", device.HardwareName);
        Dictionary<string, string> valuesByLabel = device.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);
        Assert.Equal(expected: Unavailable, valuesByLabel[L(nameof(AppStrings.Perf_Label_SystemDisk))]);
        Assert.Equal(expected: Unavailable, valuesByLabel[L(nameof(AppStrings.Perf_Label_PageFile))]);
    }

    [Theory]
    [InlineData(1, nameof(AppStrings.Perf_MinuteFormat))]
    [InlineData(5, nameof(AppStrings.Perf_MinutesFormat))]
    public void GraphLabelUsesConfiguredHistoryLength(
        int historyLengthMinutes,
        string historyWindowKey)
    {
        PerformanceDevicePresentation CPU = PerformanceDevicePresentationFactory.Create(
                PerformanceSnapshot.Empty,
                historyLengthMinutes)
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU);

        Assert.Equal(
            F(nameof(AppStrings.Perf_Graph_UtilizationOverFormat), F(historyWindowKey, historyLengthMinutes)),
            CPU.GraphLabel);
        Assert.DoesNotContain(expectedSubstring: "60 seconds", CPU.GraphLabel, StringComparison.Ordinal);
    }
}

using System.Globalization;
using System.Net.NetworkInformation;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;
using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Formats one direct-OS device sample for the Performance page.</summary>
/// <param name="Title">The default card and details title, e.g. <c>GPU 0</c>.</param>
/// <param name="Description">The default device description, which card formats insert as <c>{description}</c>.</param>
/// <param name="HardwareName">The hardware name the details header shows.</param>
/// <param name="CardValues">The live values card line formats can insert by name.</param>
internal sealed record PerformanceDevicePresentation(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Title,
    string Description,
    string HardwareName,
    string GraphLabel,
    bool HasUtilizationSample,
    double UtilizationPercent,
    ReadOnlyMemory<PerformanceCardValue> CardValues,
    ReadOnlyMemory<PerformanceStatistic> Statistics)
{
    public PerformanceDeviceOrderItem OrderItem => new(DeviceID, Kind, SortKey);
}

/// <summary>One label/value pair in the selected device's detail pane.</summary>
internal readonly record struct PerformanceStatistic(string Label, string Value);

/// <summary>Builds display-ready device rows without changing the sampled values.</summary>
internal static class PerformanceDevicePresentationFactory
{
    private const double BytesPerKibibyte = 1_024;
    private const double BytesPerMebibyte = BytesPerKibibyte * 1_024;
    private const double BytesPerGibibyte = BytesPerMebibyte * 1_024;
    private const double BytesPerTebibyte = BytesPerGibibyte * 1_024;
    private const double BitsPerMegabit = 1_000_000;
    private const double HertzPerGigahertz = 1_000_000_000;
    private const double HertzPerMegahertz = 1_000_000;
    private const string NetworkDeviceNameSeparator = " - ";

    /// <summary>Projects one immutable snapshot into the complete live device list.</summary>
    public static List<PerformanceDevicePresentation> Create(PerformanceSnapshot snapshot) =>
        Create(
            snapshot,
            PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
            PerformanceHardwareNameResolver.Empty);

    /// <summary>Projects one immutable snapshot using the configured graph window label.</summary>
    public static List<PerformanceDevicePresentation> Create(
        PerformanceSnapshot snapshot,
        int historyLengthMinutes) =>
        Create(snapshot, historyLengthMinutes, PerformanceHardwareNameResolver.Empty);

    /// <summary>Projects one immutable snapshot using the configured hardware-name replacements.</summary>
    public static List<PerformanceDevicePresentation> Create(
        PerformanceSnapshot snapshot,
        int historyLengthMinutes,
        PerformanceHardwareNameResolver hardwareNameResolver) =>
        Create(snapshot, historyLengthMinutes, hardwareNameResolver, gpuPowerLimits: []);

    /// <summary>Projects one immutable snapshot, showing GPU power in watts where a saved power limit allows it.</summary>
    public static List<PerformanceDevicePresentation> Create(
        PerformanceSnapshot snapshot,
        int historyLengthMinutes,
        PerformanceHardwareNameResolver hardwareNameResolver,
        IReadOnlyList<GPUPowerLimitRecord> gpuPowerLimits)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(hardwareNameResolver);
        ArgumentNullException.ThrowIfNull(gpuPowerLimits);

        string graphWindow = FormatHistoryWindow(historyLengthMinutes);
        int capacity = 2 + snapshot.GPUs.Length + snapshot.Networks.Length + snapshot.Disks.Length;
        // Seed separately because the remaining capacity is populated from several spans
        // ReSharper disable once UseObjectOrCollectionInitializer
        List<PerformanceDevicePresentation> devices = new(capacity);
        devices.Add(CreateCPU(snapshot.CPU, graphWindow, hardwareNameResolver));
        devices.Add(CreateMemory(snapshot.Memory, graphWindow));

        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = snapshot.GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            GPUPerformanceSnapshot GPU = GPUs[GPUIndex];
            devices.Add(CreateGPU(
                GPU,
                graphWindow,
                hardwareNameResolver,
                GetGPUPowerLimitWatts(GPU, gpuPowerLimits)));
        }

        ReadOnlySpan<NetworkPerformanceSnapshot> networks = snapshot.Networks.Span;
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
            devices.Add(CreateNetwork(networks[networkIndex], graphWindow, hardwareNameResolver));

        ReadOnlySpan<DiskPerformanceSnapshot> disks = snapshot.Disks.Span;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
            devices.Add(CreateDisk(disks[diskIndex], graphWindow, hardwareNameResolver));

        return devices;
    }

    /// <summary>Gets the Task Manager-style graph accent for a device category.</summary>
    public static Color GetAccent(PerformanceDeviceKind kind)
    {
        TaskManagerWindowResources resources = TaskManagerWindowResources.Current;
        return kind switch
        {
            PerformanceDeviceKind.CPU => resources.AxamlTaskManagerPerformance.CPUAccentColor,
            PerformanceDeviceKind.Memory => resources.AxamlTaskManagerPerformance.MemoryAccentColor,
            PerformanceDeviceKind.GPU => resources.AxamlTaskManagerPerformance.GPUAccentColor,
            PerformanceDeviceKind.Network => resources.AxamlTaskManagerPerformance.NetworkAccentColor,
            PerformanceDeviceKind.Disk => resources.AxamlTaskManagerPerformance.DiskAccentColor,
            _ => resources.AxamlTaskManagerPerformance.CPUAccentColor
        };
    }

    /// <summary>Formats the configured graph duration for detail labels.</summary>
    public static string FormatHistoryWindow(int historyLengthMinutes)
    {
        int normalizedLength = PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
            historyLengthMinutes);
        return F(
            normalizedLength == 1 ? nameof(AppStrings.Perf_MinuteFormat) : nameof(AppStrings.Perf_MinutesFormat),
            normalizedLength);
    }

    /// <summary>Returns the saved default power limit in watts a GPU's power should be shown against, or NaN.</summary>
    public static double GetGPUPowerLimitWatts(
        GPUPerformanceSnapshot GPU,
        IReadOnlyList<GPUPowerLimitRecord> gpuPowerLimits)
    {
        GPUAdapterIdentity? identity = GPU.Details?.Identity;
        return identity != null
               && GPUPowerLimitCollection.TryGetDisplayLimitWatts(
                   gpuPowerLimits,
                   identity.DeviceInstanceID,
                   identity.VBIOSVersion,
                   out double limitWatts)
            ? limitWatts
            : double.NaN;
    }

    /// <summary>
    /// Formats GPU power, which Windows reports as a percentage of the board's default limit: watts when that limit is
    /// known, otherwise the percentage. It is not clamped, since a raised power limit lets a board exceed 100%.
    /// </summary>
    internal static string FormatGPUPower(double percentOfDefaultLimit, double limitWatts)
    {
        if (!double.IsFinite(percentOfDefaultLimit) || percentOfDefaultLimit < 0) return Unavailable;

        return double.IsFinite(limitWatts) && limitWatts > 0
            ? FormatWatts(percentOfDefaultLimit / 100 * limitWatts)
            : string.Concat(percentOfDefaultLimit.ToString(format: "N0", CultureInfo.CurrentCulture), "%");
    }

    /// <summary>Formats a power reading in whole watts.</summary>
    internal static string FormatWatts(double watts) =>
        string.Concat(watts.ToString(format: "N0", CultureInfo.CurrentCulture), " W");

    /// <summary>Calculates the normalized network value used by cards and history graphs.</summary>
    public static bool TryGetNetworkUtilization(
        NetworkPerformanceSnapshot sample,
        out double utilizationPercent)
    {
        bool hasUtilization = sample is { HasThroughputSample: true, LinkSpeedBitsPerSecond: > 0 };
        utilizationPercent = hasUtilization
            ? Math.Clamp(
                Math.Max(sample.ReceiveBytesPerSecond, sample.SendBytesPerSecond)
                * 8.0
                / sample.LinkSpeedBitsPerSecond
                * 100.0,
                min: 0,
                max: 100)
            : 0;
        return hasUtilization;
    }

    private static PerformanceDevicePresentation CreateCPU(
        CPUPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Name);
        string? utilization = FormatReportedPercent(sample.HasUtilizationSample, sample.UtilizationPercent);
        string? speed = sample is { HasFrequencyData: true, CurrentSpeedHertz: > 0 }
            ? FormatHertz(sample.CurrentSpeedHertz)
            : null;
        string? baseSpeed = sample is { HasFrequencyData: true, BaseSpeedHertz: > 0 }
            ? FormatHertz(sample.BaseSpeedHertz)
            : null;
        string? power = sample.HasPowerData && double.IsFinite(sample.PowerWatts) && sample.PowerWatts >= 0
            ? FormatWatts(sample.PowerWatts)
            : null;
        string? highestLogicalProcessor = FormatReportedPercent(
            sample.HasUtilizationSample,
            sample.HighestLogicalProcessorPercent);
        string? processes = sample.HasProcessTotals ? FormatCountValue(sample.ProcessCount) : null;
        string? threads = sample.HasProcessTotals ? FormatCountValue(sample.ThreadCount) : null;
        string? handles = sample.HasProcessTotals ? FormatCountValue(sample.HandleCount) : null;
        string uptime = FormatUptime(sample.Uptime);
        PerformanceCardValue[] cardValues =
        [
            new(PerformanceCardValueNames.Status, sample.HasUtilizationSample ? null : Unavailable),
            new(Name: "utilization", utilization),
            new(Name: "speed", speed),
            new(Name: "baseSpeed", baseSpeed),
            new(Name: "power", power),
            new(Name: "highestProcessor", highestLogicalProcessor),
            new(Name: "processes", processes),
            new(Name: "threads", threads),
            new(Name: "handles", handles),
            new(Name: "uptime", uptime)
        ];
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Utilization)), utilization ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Speed)), speed ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Power)), power ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_HighestLogicalProcessor)), highestLogicalProcessor ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Processes)), processes ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Threads)), threads ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Handles)), handles ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_UpTime)), uptime),
            new(
                Label: L(nameof(AppStrings.Perf_Label_HighestRecordedSpeed)),
                sample.HighestRecordedSpeedHertz > 0
                    ? FormatHertz(sample.HighestRecordedSpeedHertz)
                    : Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Sockets)), FormatCount(sample.SocketCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_PhysicalCores)), FormatCount(sample.CoreCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_LogicalProcessors)), FormatCount(sample.LogicalProcessorCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_Virtualization)), sample.IsVirtualizationFirmwareEnabled ? L(nameof(AppStrings.Perf_Enabled)) : L(nameof(AppStrings.Perf_Disabled))),
            new(Label: L(nameof(AppStrings.Perf_Label_L1Cache)), FormatOptionalBytes(sample.L1CacheBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_L2Cache)), FormatOptionalBytes(sample.L2CacheBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_L3Cache)), FormatOptionalBytes(sample.L3CacheBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_BaseSpeed)), baseSpeed ?? Unavailable)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            Title: L(nameof(AppStrings.Perf_Title_CPU)),
            hardwareName,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_UtilizationOverFormat), graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            cardValues,
            statistics);
    }

    private static PerformanceDevicePresentation CreateMemory(
        MemoryPerformanceSnapshot sample,
        string graphWindow)
    {
        string? used = sample.HasMemoryData ? FormatBytes(sample.UsedPhysicalBytes) : null;
        string? total = sample.HasMemoryData ? FormatBytes(sample.TotalPhysicalBytes) : null;
        string? available = sample.HasMemoryData ? FormatBytes(sample.AvailablePhysicalBytes) : null;
        string? compressed = sample.Composition.HasCompressionData
            ? FormatBytes(sample.Composition.CompressedBytes)
            : null;
        string inUse = compressed != null
            ? string.Concat(used ?? Unavailable, str1: " (", compressed, str3: ")")
            : used ?? Unavailable;
        string? committed = sample is { CommittedBytes: > 0, CommitLimitBytes: > 0 }
            ? FormatBytePair(sample.CommittedBytes, sample.CommitLimitBytes)
            : null;
        string? cached = sample.CachedBytes > 0 ? FormatBytes(sample.CachedBytes) : null;
        PerformanceCardValue[] cardValues =
        [
            new(PerformanceCardValueNames.Status, sample.HasMemoryData ? null : Unavailable),
            new(Name: "used", used),
            new(Name: "total", total),
            new(Name: "available", available),
            new(Name: "utilization", FormatReportedPercent(sample.HasMemoryData, sample.UtilizationPercent)),
            new(Name: "compressed", compressed),
            new(Name: "committed", committed),
            new(Name: "cached", cached)
        ];
        List<PerformanceStatistic> statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_InUseCompressed)), inUse),
            new(Label: L(nameof(AppStrings.Perf_Label_Available)), available ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Committed)), committed ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Cached)), cached ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_PagedPool)), FormatOptionalBytes(sample.PagedPoolBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_NonPagedPool)), FormatOptionalBytes(sample.NonPagedPoolBytes)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_Speed)),
                sample.Hardware.SpeedMegatransfersPerSecond > 0
                    ? string.Concat(
                        sample.Hardware.SpeedMegatransfersPerSecond.ToString(
                            format: "0",
                            CultureInfo.CurrentCulture),
                        str1: " MT/s")
                    : Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_SlotsUsed)),
                sample.Hardware is { UsedSlotCount: > 0, TotalSlotCount: > 0 }
                    ? string.Concat(
                        sample.Hardware.UsedSlotCount.ToString(CultureInfo.CurrentCulture),
                        str1: " of ",
                        sample.Hardware.TotalSlotCount.ToString(CultureInfo.CurrentCulture))
                    : Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_FormFactor)),
                string.Equals(sample.Hardware.FormFactor, b: "Unknown", StringComparison.Ordinal)
                    ? Unavailable
                    : sample.Hardware.FormFactor),
            new(
                Label: L(nameof(AppStrings.Perf_Label_HardwareReserved)),
                sample is { InstalledPhysicalBytes: > 0, HasMemoryData: true }
                    ? FormatBytes(sample.HardwareReservedBytes)
                    : Unavailable)
        ];
        MemoryFirmwarePerformance firmware = sample.FirmwarePerformance;
        if (firmware.HasLatency)
        {
            statistics.Add(new PerformanceStatistic(
                Label: L(nameof(AppStrings.Perf_Label_FirmwareLatency)),
                FormatFirmwareMetric(
                    firmware.AccessLatencyPicoseconds,
                    firmware.ReadLatencyPicoseconds,
                    firmware.WriteLatencyPicoseconds,
                    FormatLatencyRange)));
        }

        if (firmware.HasBandwidth)
        {
            statistics.Add(new PerformanceStatistic(
                Label: L(nameof(AppStrings.Perf_Label_FirmwareBandwidth)),
                FormatFirmwareMetric(
                    firmware.AccessBandwidthMegabytesPerSecond,
                    firmware.ReadBandwidthMegabytesPerSecond,
                    firmware.WriteBandwidthMegabytesPerSecond,
                    FormatBandwidthRange)));
        }

        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            Title: L(nameof(AppStrings.Perf_Title_Memory)),
            Description: string.Empty,
            HardwareName: string.Empty,
            F(nameof(AppStrings.Perf_Graph_MemoryUseOverFormat), graphWindow),
            sample.HasMemoryData,
            sample.UtilizationPercent,
            cardValues,
            statistics.ToArray());
    }

    /// <summary>Shows the combined access figure, or the read and write figures when only those are declared.</summary>
    private static string FormatFirmwareMetric(
        MemoryFirmwareMetricRange access,
        MemoryFirmwareMetricRange read,
        MemoryFirmwareMetricRange write,
        Func<MemoryFirmwareMetricRange, string> formatRange)
    {
        if (access.HasValue) return formatRange(access);
        if (!write.HasValue) return F(nameof(AppStrings.Perf_Memory_FirmwareReadFormat), formatRange(read));
        if (!read.HasValue) return F(nameof(AppStrings.Perf_Memory_FirmwareWriteFormat), formatRange(write));
        return F(nameof(AppStrings.Perf_Memory_FirmwareReadWriteFormat), formatRange(read), formatRange(write));
    }

    private static string FormatLatencyRange(MemoryFirmwareMetricRange picoseconds) =>
        string.Concat(
            FormatRange(picoseconds.Minimum / 1_000.0, picoseconds.Maximum / 1_000.0),
            str1: " ns");

    // HMAT bandwidth is in decimal megabytes per second
    private static string FormatBandwidthRange(MemoryFirmwareMetricRange megabytesPerSecond) =>
        string.Concat(
            FormatRange(megabytesPerSecond.Minimum / 1_000.0, megabytesPerSecond.Maximum / 1_000.0),
            str1: " GB/s");

    private static string FormatRange(double minimum, double maximum)
    {
        string formattedMinimum = minimum.ToString(format: "0.#", CultureInfo.CurrentCulture);
        string formattedMaximum = maximum.ToString(format: "0.#", CultureInfo.CurrentCulture);
        return string.Equals(formattedMinimum, formattedMaximum, StringComparison.Ordinal)
            ? formattedMinimum
            : string.Concat(formattedMinimum, "\u2013", formattedMaximum);
    }

    private static PerformanceDevicePresentation CreateGPU(
        GPUPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver,
        double powerLimitWatts)
    {
        GPUPerformanceDetailsSnapshot? details = sample.Details;
        GPUAdapterIdentity identity = details?.Identity ?? GPUAdapterIdentity.Empty;
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Name);
        string? utilization = FormatReportedPercent(sample.HasUtilizationSample, sample.UtilizationPercent);
        ulong dedicatedMemoryCapacityBytes = details?.HasHardwareReservedMemoryData == true
            ? SaturatingAdd(
                sample.DedicatedMemoryCapacityBytes,
                details.HardwareReservedMemoryBytes)
            : sample.DedicatedMemoryCapacityBytes;
        string? dedicatedMemory = sample.HasDedicatedMemoryData
            ? FormatBytePair(sample.DedicatedMemoryBytes, dedicatedMemoryCapacityBytes)
            : null;
        string? sharedMemory = sample.HasSharedMemoryData
            ? FormatBytePair(sample.SharedMemoryBytes, sample.SharedMemoryCapacityBytes)
            : null;
        string? temperature = details?.HasTemperatureData == true
            ? string.Concat(
                details.TemperatureCelsius.ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: " \u00B0C")
            : null;
        string directXVersion = details != null
                                && !string.IsNullOrWhiteSpace(details.DirectXVersion)
            ? string.IsNullOrWhiteSpace(details.FeatureLevel)
                ? details.DirectXVersion
                : string.Concat(
                    details.DirectXVersion,
                    str1: " (FL ",
                    details.FeatureLevel,
                    str3: ")")
            : Unavailable;
        string? power = details?.HasPowerData == true
            ? FormatGPUPower(details.PowerPercent, powerLimitWatts)
            : null;
        string? clock = details?.HasClockData == true ? FormatMegahertz(details.ClockHertz) : null;
        string? memoryClock = details?.HasMemoryClockData == true ? FormatMegahertz(details.MemoryClockHertz) : null;
        string driverVersion = details != null
            ? GPUVendorFormatting.FormatDriverVersionDisplay(identity.PublicDriverVersion, details.DriverVersion)
            : string.Empty;
        PerformanceCardValue[] cardValues =
        [
            new(PerformanceCardValueNames.Status, sample.HasUtilizationSample ? null : Unavailable),
            new(Name: "utilization", utilization),
            new(Name: "temperature", temperature),
            new(Name: "power", power),
            new(Name: "clock", clock),
            new(Name: "memoryClock", memoryClock),
            new(Name: "dedicatedMemory", dedicatedMemory),
            new(Name: "sharedMemory", sharedMemory)
        ];
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Utilization)), utilization ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_DedicatedGPUMemory)), dedicatedMemory ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_SharedGPUMemory)), sharedMemory ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Temperature)), temperature ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Power)), power ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_GPUClock)), clock ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_MemoryClock)), memoryClock ?? Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_MaxGPUClock)),
                details?.HasClockData == true ? FormatMegahertz(details.MaximumClockHertz) : Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_DriverVersion)), OrUnavailable(driverVersion)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_DriverDate)),
                details?.DriverDate?.ToString(format: "d", CultureInfo.CurrentCulture)
                ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_DirectXVersion)), directXVersion),
            new(Label: L(nameof(AppStrings.Perf_Label_VBIOSVersion)), OrUnavailable(identity.VBIOSVersion)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_DeviceID)),
                OrUnavailable(GPUVendorFormatting.FormatHardwareID(identity.HardwareID))),
            new(Label: L(nameof(AppStrings.Perf_Label_PCIeLink)), FormatGPUPCIeLink(identity.Link)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_PhysicalLocation)),
                OrUnavailable(details?.PhysicalLocation)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_HardwareReservedMemory)),
                details?.HasHardwareReservedMemoryData == true
                    ? FormatBytes(details.HardwareReservedMemoryBytes)
                    : Unavailable)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            F(nameof(AppStrings.Perf_Title_GPUFormat), sample.SortKey),
            hardwareName,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_UtilizationOverFormat), graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            cardValues,
            statistics);
    }

    private static string OrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unavailable : value;

    /// <summary>Formats a GPU's PCIe link, marking a link that only reaches a switch on the GPU's own card.</summary>
    internal static string FormatGPUPCIeLink(GPUPCIeLink link)
    {
        string formatted = GPUVendorFormatting.FormatPCIeLink(link);
        if (formatted.Length == 0) return Unavailable;

        return link.BehindOnCardSwitch
            ? F(nameof(AppStrings.Perf_PCIeLink_OnCardSwitchFormat), formatted)
            : formatted;
    }

    private static PerformanceDevicePresentation CreateNetwork(
        NetworkPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Description);
        bool hasNormalizedUtilization = TryGetNetworkUtilization(
            sample,
            out double utilizationPercent);
        string? send = sample.HasThroughputSample ? FormatBytesPerSecond(sample.SendBytesPerSecond) : null;
        string? receive = sample.HasThroughputSample ? FormatBytesPerSecond(sample.ReceiveBytesPerSecond) : null;
        string? linkSpeed = sample.LinkSpeedBitsPerSecond > 0 ? FormatBitRate(sample.LinkSpeedBitsPerSecond) : null;
        string? totalSent = FormatReportedBytes(sample.TotalBytesSent);
        string? totalReceived = FormatReportedBytes(sample.TotalBytesReceived);
        PerformanceCardValue[] cardValues =
        [
            new(
                PerformanceCardValueNames.Status,
                sample.HasThroughputSample
                    ? null
                    : sample.IsOperational
                        ? L(nameof(AppStrings.Perf_CollectingThroughput))
                        : L(nameof(AppStrings.Perf_Disconnected))),
            new(Name: "send", send),
            new(Name: "receive", receive),
            new(Name: "linkSpeed", linkSpeed),
            new(Name: "interface", string.IsNullOrWhiteSpace(sample.Name) ? null : sample.Name),
            new(Name: "adapter", string.IsNullOrWhiteSpace(hardwareName) ? null : hardwareName),
            new(Name: "totalSent", totalSent),
            new(Name: "totalReceived", totalReceived)
        ];
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Send)), send ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Receive)), receive ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_LinkSpeed)), linkSpeed ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Status)), sample.IsOperational ? L(nameof(AppStrings.Perf_Connected)) : L(nameof(AppStrings.Perf_Disconnected))),
            new(Label: L(nameof(AppStrings.Perf_Label_AdapterType)), sample.InterfaceType),
            new(Label: L(nameof(AppStrings.Perf_Label_TotalSent)), totalSent ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_TotalReceived)), totalReceived ?? Unavailable)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            FormatNetworkTitle(sample.InterfaceType),
            FormatNetworkSubtitle(sample.Name, hardwareName),
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_LinkUtilizationOverFormat), graphWindow),
            hasNormalizedUtilization,
            utilizationPercent,
            cardValues,
            statistics);
    }

    private static PerformanceDevicePresentation CreateDisk(
        DiskPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        DiskPerformanceDetailsSnapshot? details = sample.Details;
        string model = details?.Model ?? sample.Name;
        string volumeNames = details?.VolumeNames ?? sample.VolumeNames;
        string deviceType = details?.DeviceType ?? sample.DeviceType;
        bool hasPerformanceSample = details?.HasPerformanceSample ?? sample.HasPerformanceSample;
        double activeTimePercent = details?.ActiveTimePercent ?? sample.ActiveTimePercent;
        double readBytesPerSecond = details?.ReadBytesPerSecond ?? sample.ReadBytesPerSecond;
        double writeBytesPerSecond = details?.WriteBytesPerSecond ?? sample.WriteBytesPerSecond;
        double averageResponseTimeMilliseconds = details?.AverageResponseTimeMilliseconds
                                                 ?? sample.AverageResponseTimeMilliseconds;
        ulong capacityBytes = details?.CapacityBytes ?? sample.CapacityBytes;
        ulong formattedCapacityBytes = details?.FormattedCapacityBytes
                                       ?? sample.FormattedCapacityBytes;
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, model);
        string? utilization = FormatReportedPercent(hasPerformanceSample, activeTimePercent);
        string title = string.IsNullOrWhiteSpace(volumeNames)
            ? F(nameof(AppStrings.Perf_Title_DiskFormat), sample.SortKey)
            : F(nameof(AppStrings.Perf_Title_DiskWithVolumesFormat), sample.SortKey, volumeNames);
        string? responseTime = hasPerformanceSample
            ? string.Concat(
                averageResponseTimeMilliseconds.ToString(format: "N1", CultureInfo.CurrentCulture),
                str1: " ms")
            : null;
        string? read = hasPerformanceSample ? FormatBytesPerSecond(readBytesPerSecond) : null;
        string? write = hasPerformanceSample ? FormatBytesPerSecond(writeBytesPerSecond) : null;
        PerformanceCardValue[] cardValues =
        [
            new(
                PerformanceCardValueNames.Status,
                hasPerformanceSample ? null : L(nameof(AppStrings.Perf_CollectingDiskCounters))),
            new(Name: "activeTime", utilization),
            new(Name: "read", read),
            new(Name: "write", write),
            new(Name: "responseTime", responseTime),
            new(Name: "capacity", capacityBytes > 0 ? FormatBytes(capacityBytes) : null),
            new(Name: "volumes", string.IsNullOrWhiteSpace(volumeNames) ? null : volumeNames),
            new(Name: "type", string.IsNullOrWhiteSpace(deviceType) ? null : deviceType)
        ];
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_ActiveTime)), utilization ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_AverageResponseTime)), responseTime ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_ReadSpeed)), read ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_WriteSpeed)), write ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Capacity)), FormatOptionalBytes(capacityBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_Formatted)), FormatOptionalBytes(formattedCapacityBytes)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_SystemDisk)),
                details?.HasSystemDiskData == true
                    ? FormatBoolean(details.IsSystemDisk)
                    : Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_PageFile)),
                details?.HasPageFileData == true
                    ? FormatBoolean(details.HasPageFile)
                    : Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_Type)), string.IsNullOrWhiteSpace(deviceType) ? Unavailable : deviceType)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            title,
            hardwareName,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_ActiveTimeOverFormat), graphWindow),
            hasPerformanceSample,
            activeTimePercent,
            cardValues,
            statistics);
    }

    private static string FormatNetworkTitle(string interfaceType)
    {
        if (!Enum.TryParse(interfaceType, out NetworkInterfaceType parsedType))
            return L(nameof(AppStrings.Perf_Title_Network));

        return parsedType switch
        {
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ppp => "VPN",
            _ => L(nameof(AppStrings.Perf_Title_Network))
        };
    }

    private static string FormatNetworkSubtitle(string interfaceName, string hardwareName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName)) return hardwareName;
        if (string.IsNullOrWhiteSpace(hardwareName)
            || string.Equals(interfaceName, hardwareName, StringComparison.OrdinalIgnoreCase))
            return interfaceName;

        return string.Concat(interfaceName, NetworkDeviceNameSeparator, hardwareName);
    }

    internal static string FormatPercent(bool isAvailable, double value) =>
        FormatReportedPercent(isAvailable, value) ?? Unavailable;

    /// <summary>Formats a whole percentage, or returns null when it was not reported.</summary>
    private static string? FormatReportedPercent(bool isAvailable, double value) =>
        isAvailable && double.IsFinite(value)
            ? string.Concat(
                Math.Clamp(value, min: 0, max: 100).ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: "%")
            : null;

    private static string FormatCount(int value) =>
        value > 0 ? value.ToString(format: "N0", CultureInfo.CurrentCulture) : Unavailable;

    private static string FormatCountValue(uint value) =>
        value.ToString(format: "N0", CultureInfo.CurrentCulture);

    private static string FormatBoolean(bool value) => value ? L(nameof(AppStrings.Perf_Yes)) : L(nameof(AppStrings.Perf_No));

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private static string FormatOptionalBytes(ulong value) =>
        value > 0 ? FormatBytes(value) : Unavailable;

    private static string FormatBytePair(ulong value, ulong limit)
    {
        if (limit == 0) return Unavailable;

        double divisor;
        string suffix;
        if (limit >= BytesPerTebibyte)
        {
            divisor = BytesPerTebibyte;
            suffix = "TB";
        }
        else if (limit >= BytesPerGibibyte)
        {
            divisor = BytesPerGibibyte;
            suffix = "GB";
        }
        else if (limit >= BytesPerMebibyte)
        {
            divisor = BytesPerMebibyte;
            suffix = "MB";
        }
        else if (limit >= BytesPerKibibyte)
        {
            divisor = BytesPerKibibyte;
            suffix = "KB";
        }
        else
        {
            divisor = 1;
            suffix = "B";
        }

        return string.Concat(
            FormatScaledNumber(value / divisor),
            "/",
            FormatScaledNumber(limit / divisor),
            " ",
            suffix);
    }

    private static string? FormatReportedBytes(long value) =>
        value >= 0 ? FormatBytes((ulong)value) : null;

    internal static string FormatBytesPerSecond(double value) =>
        double.IsFinite(value) && value >= 0
            ? string.Concat(FormatBytes(value), str1: "/s")
            : Unavailable;

    internal static string FormatBytes(ulong value) => FormatBytes((double)value);

    internal static string FormatBytes(double value)
    {
        if (!double.IsFinite(value) || value < 0) return Unavailable;
        if (value >= BytesPerTebibyte)
            return FormatScaled(value / BytesPerTebibyte, suffix: "TB");
        if (value >= BytesPerGibibyte)
            return FormatScaled(value / BytesPerGibibyte, suffix: "GB");
        if (value >= BytesPerMebibyte)
            return FormatScaled(value / BytesPerMebibyte, suffix: "MB");
        if (value >= BytesPerKibibyte)
            return FormatScaled(value / BytesPerKibibyte, suffix: "KB");
        return string.Concat(value.ToString(format: "N0", CultureInfo.CurrentCulture), str1: " B");
    }

    private static string FormatScaled(double value, string suffix) =>
        string.Concat(FormatScaledNumber(value), str1: " ", suffix);

    private static string FormatScaledNumber(double value) =>
        value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture);

    private static string FormatHertz(ulong hertz) =>
        string.Concat(
            (hertz / HertzPerGigahertz).ToString(format: "N2", CultureInfo.CurrentCulture),
            str1: " GHz");

    /// <summary>Formats a GPU clock in megahertz, the unit GPU vendors publish clocks in.</summary>
    internal static string FormatMegahertz(double hertz) =>
        double.IsFinite(hertz) && hertz > 0
            ? string.Concat(
                (hertz / HertzPerMegahertz).ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: " MHz")
            : Unavailable;

    private static string FormatBitRate(long bitsPerSecond) =>
        bitsPerSecond > 0
            ? string.Concat(
                (bitsPerSecond / BitsPerMegabit).ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: " Mbps")
            : Unavailable;

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero) return Unavailable;
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{(int)uptime.TotalDays}:{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}");
    }
}

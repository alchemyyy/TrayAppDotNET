using System.Globalization;
using System.Net.NetworkInformation;
using Avalonia.Media;
using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Formats one direct-OS device sample for the Performance page.</summary>
internal sealed record PerformanceDevicePresentation(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Title,
    string Subtitle,
    string Summary,
    string HardwareName,
    string GraphLabel,
    bool HasUtilizationSample,
    double UtilizationPercent,
    Color Accent,
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
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(hardwareNameResolver);

        string graphWindow = FormatHistoryWindow(historyLengthMinutes);
        int capacity = 2 + snapshot.GPUs.Length + snapshot.Networks.Length + snapshot.Disks.Length;
        // Seed separately because the remaining capacity is populated from several spans
        // ReSharper disable once UseObjectOrCollectionInitializer
        List<PerformanceDevicePresentation> devices = new(capacity);
        devices.Add(CreateCPU(snapshot.CPU, graphWindow, hardwareNameResolver));
        devices.Add(CreateMemory(snapshot.Memory, graphWindow, hardwareNameResolver));

        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = snapshot.GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
            devices.Add(CreateGPU(GPUs[GPUIndex], graphWindow, hardwareNameResolver));

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
        string utilization = FormatPercent(sample.HasUtilizationSample, sample.UtilizationPercent);
        bool hasCurrentSpeed = sample is { HasFrequencyData: true, CurrentSpeedHertz: > 0 };
        string speed = hasCurrentSpeed
            ? FormatHertz(sample.CurrentSpeedHertz)
            : Unavailable;
        string summary = hasCurrentSpeed
            ? string.Concat(utilization, str1: "  ", speed)
            : utilization;
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Utilization)), utilization),
            new(Label: L(nameof(AppStrings.Perf_Label_Speed)), speed),
            new(
                Label: L(nameof(AppStrings.Perf_Label_HighestLogicalProcessor)),
                FormatPercent(sample.HasUtilizationSample, sample.HighestLogicalProcessorPercent)),
            new(Label: L(nameof(AppStrings.Perf_Label_Processes)), FormatOptionalCount(sample.HasProcessTotals, sample.ProcessCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_Threads)), FormatOptionalCount(sample.HasProcessTotals, sample.ThreadCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_Handles)), FormatOptionalCount(sample.HasProcessTotals, sample.HandleCount)),
            new(Label: L(nameof(AppStrings.Perf_Label_UpTime)), FormatUptime(sample.Uptime)),
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
            new(
                Label: L(nameof(AppStrings.Perf_Label_BaseSpeed)),
                sample is { HasFrequencyData: true, BaseSpeedHertz: > 0 }
                    ? FormatHertz(sample.BaseSpeedHertz)
                    : Unavailable)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            Title: L(nameof(AppStrings.Perf_Title_CPU)),
            hardwareName,
            summary,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_UtilizationOverFormat), graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            GetAccent(PerformanceDeviceKind.CPU),
            statistics);
    }

    private static PerformanceDevicePresentation CreateMemory(
        MemoryPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, hardwareName: L(nameof(AppStrings.Perf_PhysicalMemory)));
        string used = sample.HasMemoryData ? FormatBytes(sample.UsedPhysicalBytes) : Unavailable;
        string total = sample.HasMemoryData ? FormatBytes(sample.TotalPhysicalBytes) : Unavailable;
        string inUse = sample.Composition.HasCompressionData
            ? string.Concat(
                used,
                str1: " (",
                FormatBytes(sample.Composition.CompressedBytes),
                str3: ")")
            : used;
        string summary = sample.HasMemoryData
            ? string.Concat(
                used,
                "/",
                total,
                " (",
                FormatPercent(isAvailable: true, sample.UtilizationPercent),
                ")")
            : Unavailable;
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_InUseCompressed)), inUse),
            new(
                Label: L(nameof(AppStrings.Perf_Label_Available)),
                sample.HasMemoryData ? FormatBytes(sample.AvailablePhysicalBytes) : Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_Committed)),
                FormatOptionalBytePair(sample.CommittedBytes, sample.CommitLimitBytes)),
            new(Label: L(nameof(AppStrings.Perf_Label_Cached)), FormatOptionalBytes(sample.CachedBytes)),
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
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            Title: L(nameof(AppStrings.Perf_Title_Memory)),
            total,
            summary,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_MemoryUseOverFormat), graphWindow),
            sample.HasMemoryData,
            sample.UtilizationPercent,
            GetAccent(PerformanceDeviceKind.Memory),
            statistics);
    }

    private static PerformanceDevicePresentation CreateGPU(
        GPUPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        GPUPerformanceDetailsSnapshot? details = sample.Details;
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Name);
        string utilization = FormatPercent(sample.HasUtilizationSample, sample.UtilizationPercent);
        ulong dedicatedMemoryCapacityBytes = details?.HasHardwareReservedMemoryData == true
            ? SaturatingAdd(
                sample.DedicatedMemoryCapacityBytes,
                details.HardwareReservedMemoryBytes)
            : sample.DedicatedMemoryCapacityBytes;
        string dedicatedMemory = sample.HasDedicatedMemoryData
            ? FormatBytePair(sample.DedicatedMemoryBytes, dedicatedMemoryCapacityBytes)
            : Unavailable;
        string sharedMemory = sample.HasSharedMemoryData
            ? FormatBytePair(sample.SharedMemoryBytes, sample.SharedMemoryCapacityBytes)
            : Unavailable;
        bool hasGPUMemoryData = sample is { HasDedicatedMemoryData: true, HasSharedMemoryData: true };
        ulong totalGPUMemoryBytes = SaturatingAdd(
            sample.DedicatedMemoryBytes,
            sample.SharedMemoryBytes);
        ulong totalGPUMemoryCapacityBytes = SaturatingAdd(
            dedicatedMemoryCapacityBytes,
            sample.SharedMemoryCapacityBytes);
        string totalGPUMemory = hasGPUMemoryData && totalGPUMemoryCapacityBytes > 0
            ? FormatBytePair(totalGPUMemoryBytes, totalGPUMemoryCapacityBytes)
            : Unavailable;
        string temperature = details?.HasTemperatureData == true
            ? string.Concat(
                details.TemperatureCelsius.ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: " \u00B0C")
            : Unavailable;
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
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Utilization)), utilization),
            new(Label: L(nameof(AppStrings.Perf_Label_DedicatedGPUMemory)), dedicatedMemory),
            new(Label: L(nameof(AppStrings.Perf_Label_GPUMemory)), totalGPUMemory),
            new(Label: L(nameof(AppStrings.Perf_Label_SharedGPUMemory)), sharedMemory),
            new(Label: L(nameof(AppStrings.Perf_Label_Temperature)), temperature),
            new(
                Label: L(nameof(AppStrings.Perf_Label_DriverVersion)),
                details != null && !string.IsNullOrWhiteSpace(details.DriverVersion)
                    ? details.DriverVersion
                    : Unavailable),
            new(
                Label: L(nameof(AppStrings.Perf_Label_DriverDate)),
                details?.DriverDate?.ToString(format: "d", CultureInfo.CurrentCulture)
                ?? Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_DirectXVersion)), directXVersion),
            new(
                Label: L(nameof(AppStrings.Perf_Label_PhysicalLocation)),
                details != null && !string.IsNullOrWhiteSpace(details.PhysicalLocation)
                    ? details.PhysicalLocation
                    : Unavailable),
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
            utilization,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_UtilizationOverFormat), graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            GetAccent(PerformanceDeviceKind.GPU),
            statistics);
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
        string summary = sample.HasThroughputSample
            ? string.Concat(
                str0: "S: ",
                FormatBytesPerSecond(sample.SendBytesPerSecond),
                str2: "  R: ",
                FormatBytesPerSecond(sample.ReceiveBytesPerSecond))
            : sample.IsOperational
                ? L(nameof(AppStrings.Perf_CollectingThroughput))
                : L(nameof(AppStrings.Perf_Disconnected));
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_Send)), FormatOptionalBytesPerSecond(sample.HasThroughputSample, sample.SendBytesPerSecond)),
            new(
                Label: L(nameof(AppStrings.Perf_Label_Receive)),
                FormatOptionalBytesPerSecond(sample.HasThroughputSample, sample.ReceiveBytesPerSecond)),
            new(Label: L(nameof(AppStrings.Perf_Label_LinkSpeed)), FormatBitRate(sample.LinkSpeedBitsPerSecond)),
            new(Label: L(nameof(AppStrings.Perf_Label_Status)), sample.IsOperational ? L(nameof(AppStrings.Perf_Connected)) : L(nameof(AppStrings.Perf_Disconnected))),
            new(Label: L(nameof(AppStrings.Perf_Label_AdapterType)), sample.InterfaceType),
            new(Label: L(nameof(AppStrings.Perf_Label_TotalSent)), FormatSignedBytes(sample.TotalBytesSent)),
            new(Label: L(nameof(AppStrings.Perf_Label_TotalReceived)), FormatSignedBytes(sample.TotalBytesReceived))
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            FormatNetworkTitle(sample.InterfaceType),
            FormatNetworkSubtitle(sample.Name, hardwareName),
            summary,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_LinkUtilizationOverFormat), graphWindow),
            hasNormalizedUtilization,
            utilizationPercent,
            GetAccent(PerformanceDeviceKind.Network),
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
        string utilization = FormatPercent(hasPerformanceSample, activeTimePercent);
        string title = string.IsNullOrWhiteSpace(volumeNames)
            ? F(nameof(AppStrings.Perf_Title_DiskFormat), sample.SortKey)
            : F(nameof(AppStrings.Perf_Title_DiskWithVolumesFormat), sample.SortKey, volumeNames);
        string summary = hasPerformanceSample
            ? utilization
            : L(nameof(AppStrings.Perf_CollectingDiskCounters));
        PerformanceStatistic[] statistics =
        [
            new(Label: L(nameof(AppStrings.Perf_Label_ActiveTime)), utilization),
            new(
                Label: L(nameof(AppStrings.Perf_Label_AverageResponseTime)),
                hasPerformanceSample
                    ? string.Concat(
                        averageResponseTimeMilliseconds.ToString(format: "N1", CultureInfo.CurrentCulture),
                        str1: " ms")
                    : Unavailable),
            new(Label: L(nameof(AppStrings.Perf_Label_ReadSpeed)), FormatOptionalBytesPerSecond(hasPerformanceSample, readBytesPerSecond)),
            new(Label: L(nameof(AppStrings.Perf_Label_WriteSpeed)), FormatOptionalBytesPerSecond(hasPerformanceSample, writeBytesPerSecond)),
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
            summary,
            hardwareName,
            F(nameof(AppStrings.Perf_Graph_ActiveTimeOverFormat), graphWindow),
            hasPerformanceSample,
            activeTimePercent,
            GetAccent(PerformanceDeviceKind.Disk),
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
        isAvailable && double.IsFinite(value)
            ? string.Concat(
                Math.Clamp(value, min: 0, max: 100).ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: "%")
            : Unavailable;

    private static string FormatCount(int value) =>
        value > 0 ? value.ToString(format: "N0", CultureInfo.CurrentCulture) : Unavailable;

    private static string FormatOptionalCount(bool isAvailable, uint value) =>
        isAvailable ? value.ToString(format: "N0", CultureInfo.CurrentCulture) : Unavailable;

    private static string FormatBoolean(bool value) => value ? L(nameof(AppStrings.Perf_Yes)) : L(nameof(AppStrings.Perf_No));

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private static string FormatOptionalBytes(ulong value) =>
        value > 0 ? FormatBytes(value) : Unavailable;

    private static string FormatOptionalBytePair(ulong value, ulong limit) =>
        value > 0 && limit > 0
            ? FormatBytePair(value, limit)
            : Unavailable;

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

    private static string FormatSignedBytes(long value) =>
        value >= 0 ? FormatBytes((ulong)value) : Unavailable;

    private static string FormatOptionalBytesPerSecond(bool isAvailable, double value) =>
        isAvailable ? FormatBytesPerSecond(value) : Unavailable;

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

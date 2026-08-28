using System.Globalization;
using System.Net.NetworkInformation;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

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

    private static readonly Color CPUAccent = Color.FromRgb(0x32, 0xB5, 0xE5);
    private static readonly Color MemoryAccent = Color.FromRgb(0x58, 0x83, 0xD0);
    private static readonly Color GPUAccent = Color.FromRgb(0xA9, 0x4F, 0xC4);
    private static readonly Color NetworkAccent = Color.FromRgb(0xD0, 0x47, 0x80);
    private static readonly Color DiskAccent = Color.FromRgb(0x8B, 0xAD, 0x3C);

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
    public static Color GetAccent(PerformanceDeviceKind kind) => kind switch
    {
        PerformanceDeviceKind.CPU => CPUAccent,
        PerformanceDeviceKind.Memory => MemoryAccent,
        PerformanceDeviceKind.GPU => GPUAccent,
        PerformanceDeviceKind.Network => NetworkAccent,
        PerformanceDeviceKind.Disk => DiskAccent,
        _ => CPUAccent
    };

    /// <summary>Formats the configured graph duration for detail labels.</summary>
    public static string FormatHistoryWindow(int historyLengthMinutes)
    {
        int normalizedLength = PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
            historyLengthMinutes);
        string unit = normalizedLength == 1 ? "minute" : "minutes";
        return string.Concat(
            normalizedLength.ToString(CultureInfo.CurrentCulture),
            " ",
            unit);
    }

    /// <summary>Calculates the normalized network value used by cards and history graphs.</summary>
    public static bool TryGetNetworkUtilization(
        NetworkPerformanceSnapshot sample,
        out double utilizationPercent)
    {
        bool hasUtilization = sample.HasThroughputSample && sample.LinkSpeedBitsPerSecond > 0;
        utilizationPercent = hasUtilization
            ? Math.Clamp(
                Math.Max(sample.ReceiveBytesPerSecond, sample.SendBytesPerSecond)
                * 8.0
                / sample.LinkSpeedBitsPerSecond
                * 100.0,
                0,
                100)
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
        bool hasCurrentSpeed = sample.HasFrequencyData && sample.HighestCurrentSpeedHertz > 0;
        string speed = hasCurrentSpeed
            ? FormatHertz(sample.HighestCurrentSpeedHertz)
            : "Unavailable";
        string summary = hasCurrentSpeed
            ? string.Concat(utilization, "  ", speed)
            : utilization;
        PerformanceStatistic[] statistics =
        [
            new("Utilization", utilization),
            new("Speed", speed),
            new(
                "Highest logical processor",
                FormatPercent(sample.HasUtilizationSample, sample.HighestLogicalProcessorPercent)),
            new("Processes", sample.ProcessCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Threads", sample.ThreadCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Handles", sample.HandleCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Up time", FormatUptime(sample.Uptime)),
            new(
                "Highest recorded speed",
                sample.HighestRecordedSpeedHertz > 0
                    ? FormatHertz(sample.HighestRecordedSpeedHertz)
                    : "Unavailable"),
            new("Sockets", FormatCount(sample.SocketCount)),
            new("Physical cores", FormatCount(sample.CoreCount)),
            new("Logical processors", FormatCount(sample.LogicalProcessorCount)),
            new("Virtualization", sample.IsVirtualizationFirmwareEnabled ? "Enabled" : "Disabled"),
            new("L1 cache", FormatOptionalBytes(sample.L1CacheBytes)),
            new("L2 cache", FormatOptionalBytes(sample.L2CacheBytes)),
            new("L3 cache", FormatOptionalBytes(sample.L3CacheBytes)),
            new(
                "Base speed",
                sample.HasFrequencyData && sample.BaseSpeedHertz > 0
                    ? FormatHertz(sample.BaseSpeedHertz)
                    : "Unavailable")
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            "CPU",
            hardwareName,
            summary,
            hardwareName,
            string.Concat("% Utilization over ", graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            CPUAccent,
            statistics);
    }

    private static PerformanceDevicePresentation CreateMemory(
        MemoryPerformanceSnapshot sample,
        string graphWindow,
        PerformanceHardwareNameResolver hardwareNameResolver)
    {
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, "Physical memory");
        string used = sample.HasMemoryData ? FormatBytes(sample.UsedPhysicalBytes) : "Unavailable";
        string total = sample.HasMemoryData ? FormatBytes(sample.TotalPhysicalBytes) : "Unavailable";
        string inUse = sample.Composition.HasCompressionData
            ? string.Concat(
                used,
                " (",
                FormatBytes(sample.Composition.CompressedBytes),
                ")")
            : used;
        string summary = sample.HasMemoryData
            ? string.Concat(
                used,
                "/",
                total,
                " (",
                FormatPercent(true, sample.UtilizationPercent),
                ")")
            : "Unavailable";
        PerformanceStatistic[] statistics =
        [
            new("In use (Compressed)", inUse),
            new(
                "Available",
                sample.HasMemoryData ? FormatBytes(sample.AvailablePhysicalBytes) : "Unavailable"),
            new(
                "Committed",
                FormatOptionalBytePair(sample.CommittedBytes, sample.CommitLimitBytes)),
            new("Cached", FormatOptionalBytes(sample.CachedBytes)),
            new("Paged pool", FormatOptionalBytes(sample.PagedPoolBytes)),
            new("Non-paged pool", FormatOptionalBytes(sample.NonPagedPoolBytes)),
            new(
                "Speed",
                sample.Hardware.SpeedMegatransfersPerSecond > 0
                    ? string.Concat(
                        sample.Hardware.SpeedMegatransfersPerSecond.ToString(
                            "0",
                            CultureInfo.CurrentCulture),
                        " MT/s")
                    : "Unavailable"),
            new(
                "Slots used",
                sample.Hardware.UsedSlotCount > 0 && sample.Hardware.TotalSlotCount > 0
                    ? string.Concat(
                        sample.Hardware.UsedSlotCount.ToString(CultureInfo.CurrentCulture),
                        " of ",
                        sample.Hardware.TotalSlotCount.ToString(CultureInfo.CurrentCulture))
                    : "Unavailable"),
            new(
                "Form factor",
                string.Equals(sample.Hardware.FormFactor, "Unknown", StringComparison.Ordinal)
                    ? "Unavailable"
                    : sample.Hardware.FormFactor),
            new(
                "Hardware reserved",
                sample.InstalledPhysicalBytes > 0 && sample.HasMemoryData
                    ? FormatBytes(sample.HardwareReservedBytes)
                    : "Unavailable")
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            "Memory",
            total,
            summary,
            hardwareName,
            string.Concat("Memory use over ", graphWindow),
            sample.HasMemoryData,
            sample.UtilizationPercent,
            MemoryAccent,
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
            : "Unavailable";
        string sharedMemory = sample.HasSharedMemoryData
            ? FormatBytePair(sample.SharedMemoryBytes, sample.SharedMemoryCapacityBytes)
            : "Unavailable";
        bool hasGPUMemoryData = sample.HasDedicatedMemoryData && sample.HasSharedMemoryData;
        ulong totalGPUMemoryBytes = SaturatingAdd(
            sample.DedicatedMemoryBytes,
            sample.SharedMemoryBytes);
        ulong totalGPUMemoryCapacityBytes = SaturatingAdd(
            dedicatedMemoryCapacityBytes,
            sample.SharedMemoryCapacityBytes);
        string totalGPUMemory = hasGPUMemoryData && totalGPUMemoryCapacityBytes > 0
            ? FormatBytePair(totalGPUMemoryBytes, totalGPUMemoryCapacityBytes)
            : "Unavailable";
        string temperature = details?.HasTemperatureData == true
            ? string.Concat(
                details.TemperatureCelsius.ToString("N0", CultureInfo.CurrentCulture),
                " \u00B0C")
            : "Unavailable";
        string directXVersion = details != null
                                && !string.IsNullOrWhiteSpace(details.DirectXVersion)
            ? string.IsNullOrWhiteSpace(details.FeatureLevel)
                ? details.DirectXVersion
                : string.Concat(
                    details.DirectXVersion,
                    " (FL ",
                    details.FeatureLevel,
                    ")")
            : "Unavailable";
        PerformanceStatistic[] statistics =
        [
            new("Utilization", utilization),
            new("Dedicated GPU memory", dedicatedMemory),
            new("GPU Memory", totalGPUMemory),
            new("Shared GPU memory", sharedMemory),
            new("Temperature", temperature),
            new(
                "Driver version",
                details != null && !string.IsNullOrWhiteSpace(details.DriverVersion)
                    ? details.DriverVersion
                    : "Unavailable"),
            new(
                "Driver date",
                details?.DriverDate?.ToString("d", CultureInfo.CurrentCulture)
                ?? "Unavailable"),
            new("DirectX version", directXVersion),
            new(
                "Physical location",
                details != null && !string.IsNullOrWhiteSpace(details.PhysicalLocation)
                    ? details.PhysicalLocation
                    : "Unavailable"),
            new(
                "Hardware reserved memory",
                details?.HasHardwareReservedMemoryData == true
                    ? FormatBytes(details.HardwareReservedMemoryBytes)
                    : "Unavailable")
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            string.Concat("GPU ", sample.SortKey.ToString(CultureInfo.CurrentCulture)),
            hardwareName,
            utilization,
            hardwareName,
            string.Concat("% Utilization over ", graphWindow),
            sample.HasUtilizationSample,
            sample.UtilizationPercent,
            GPUAccent,
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
                "S: ",
                FormatBytesPerSecond(sample.SendBytesPerSecond),
                "  R: ",
                FormatBytesPerSecond(sample.ReceiveBytesPerSecond))
            : sample.IsOperational ? "Collecting throughput..." : "Disconnected";
        PerformanceStatistic[] statistics =
        [
            new("Send", FormatOptionalBytesPerSecond(sample.HasThroughputSample, sample.SendBytesPerSecond)),
            new(
                "Receive",
                FormatOptionalBytesPerSecond(sample.HasThroughputSample, sample.ReceiveBytesPerSecond)),
            new("Link speed", FormatBitRate(sample.LinkSpeedBitsPerSecond)),
            new("Status", sample.IsOperational ? "Connected" : "Disconnected"),
            new("Adapter type", sample.InterfaceType),
            new("Total sent", FormatSignedBytes(sample.TotalBytesSent)),
            new("Total received", FormatSignedBytes(sample.TotalBytesReceived))
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            FormatNetworkTitle(sample.InterfaceType),
            FormatNetworkSubtitle(sample.Name, hardwareName),
            summary,
            hardwareName,
            string.Concat("% Link utilization over ", graphWindow),
            hasNormalizedUtilization,
            utilizationPercent,
            NetworkAccent,
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
        string title = string.Concat(
            "Disk ",
            sample.SortKey.ToString(CultureInfo.CurrentCulture));
        if (!string.IsNullOrWhiteSpace(volumeNames))
            title = string.Concat(title, " (", volumeNames, ")");
        string summary = hasPerformanceSample
            ? utilization
            : "Collecting disk counters...";
        PerformanceStatistic[] statistics =
        [
            new("Active time", utilization),
            new(
                "Average response time",
                hasPerformanceSample
                    ? string.Concat(
                        averageResponseTimeMilliseconds.ToString("N1", CultureInfo.CurrentCulture),
                        " ms")
                    : "Unavailable"),
            new("Read speed", FormatOptionalBytesPerSecond(hasPerformanceSample, readBytesPerSecond)),
            new("Write speed", FormatOptionalBytesPerSecond(hasPerformanceSample, writeBytesPerSecond)),
            new("Capacity", FormatOptionalBytes(capacityBytes)),
            new("Formatted", FormatOptionalBytes(formattedCapacityBytes)),
            new(
                "System disk",
                details?.HasSystemDiskData == true
                    ? FormatBoolean(details.IsSystemDisk)
                    : "Unavailable"),
            new(
                "Page file",
                details?.HasPageFileData == true
                    ? FormatBoolean(details.HasPageFile)
                    : "Unavailable"),
            new("Type", string.IsNullOrWhiteSpace(deviceType) ? "Unavailable" : deviceType)
        ];
        return new PerformanceDevicePresentation(
            sample.DeviceID,
            sample.Kind,
            sample.SortKey,
            title,
            hardwareName,
            summary,
            hardwareName,
            string.Concat("% Active time over ", graphWindow),
            hasPerformanceSample,
            activeTimePercent,
            DiskAccent,
            statistics);
    }

    private static string FormatNetworkTitle(string interfaceType)
    {
        if (!Enum.TryParse(interfaceType, out NetworkInterfaceType parsedType))
            return "Network";

        return parsedType switch
        {
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ppp => "VPN",
            _ => "Network"
        };
    }

    private static string FormatNetworkSubtitle(string interfaceName, string hardwareName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName)) return hardwareName;
        if (string.IsNullOrWhiteSpace(hardwareName)
            || string.Equals(interfaceName, hardwareName, StringComparison.OrdinalIgnoreCase))
        {
            return interfaceName;
        }

        return string.Concat(interfaceName, NetworkDeviceNameSeparator, hardwareName);
    }

    internal static string FormatPercent(bool isAvailable, double value) =>
        isAvailable && double.IsFinite(value)
            ? string.Concat(
                Math.Clamp(value, 0, 100).ToString("N0", CultureInfo.CurrentCulture),
                "%")
            : "Unavailable";

    private static string FormatCount(int value) =>
        value > 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : "Unavailable";

    private static string FormatBoolean(bool value) => value ? "Yes" : "No";

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private static string FormatOptionalBytes(ulong value) =>
        value > 0 ? FormatBytes(value) : "Unavailable";

    private static string FormatOptionalBytePair(ulong value, ulong limit) =>
        value > 0 && limit > 0
            ? FormatBytePair(value, limit)
            : "Unavailable";

    private static string FormatBytePair(ulong value, ulong limit)
    {
        if (limit == 0) return "Unavailable";

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
        value >= 0 ? FormatBytes((ulong)value) : "Unavailable";

    private static string FormatOptionalBytesPerSecond(bool isAvailable, double value) =>
        isAvailable ? FormatBytesPerSecond(value) : "Unavailable";

    internal static string FormatBytesPerSecond(double value) =>
        double.IsFinite(value) && value >= 0
            ? string.Concat(FormatBytes(value), "/s")
            : "Unavailable";

    internal static string FormatBytes(ulong value) => FormatBytes((double)value);

    internal static string FormatBytes(double value)
    {
        if (!double.IsFinite(value) || value < 0) return "Unavailable";
        if (value >= BytesPerTebibyte)
            return FormatScaled(value / BytesPerTebibyte, "TB");
        if (value >= BytesPerGibibyte)
            return FormatScaled(value / BytesPerGibibyte, "GB");
        if (value >= BytesPerMebibyte)
            return FormatScaled(value / BytesPerMebibyte, "MB");
        if (value >= BytesPerKibibyte)
            return FormatScaled(value / BytesPerKibibyte, "KB");
        return string.Concat(value.ToString("N0", CultureInfo.CurrentCulture), " B");
    }

    private static string FormatScaled(double value, string suffix)
    {
        return string.Concat(FormatScaledNumber(value), " ", suffix);
    }

    private static string FormatScaledNumber(double value) =>
        value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture);

    private static string FormatHertz(ulong hertz) =>
        string.Concat(
            (hertz / HertzPerGigahertz).ToString("N2", CultureInfo.CurrentCulture),
            " GHz");

    private static string FormatBitRate(long bitsPerSecond) =>
        bitsPerSecond > 0
            ? string.Concat(
                (bitsPerSecond / BitsPerMegabit).ToString("N0", CultureInfo.CurrentCulture),
                " Mbps")
            : "Unavailable";

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero) return "Unavailable";
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{(int)uptime.TotalDays}:{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}");
    }
}

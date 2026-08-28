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
            new("In use", used),
            new(
                "Available",
                sample.HasMemoryData ? FormatBytes(sample.AvailablePhysicalBytes) : "Unavailable"),
            new("Committed", FormatOptionalBytes(sample.CommittedBytes)),
            new("Commit limit", FormatOptionalBytes(sample.CommitLimitBytes)),
            new("Cached", FormatOptionalBytes(sample.CachedBytes)),
            new("Paged pool", FormatOptionalBytes(sample.PagedPoolBytes)),
            new("Non-paged pool", FormatOptionalBytes(sample.NonPagedPoolBytes)),
            new("Installed", FormatOptionalBytes(sample.InstalledPhysicalBytes))
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
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Name);
        string utilization = FormatPercent(sample.HasUtilizationSample, sample.UtilizationPercent);
        string dedicatedMemory = sample.HasDedicatedMemoryData
            ? string.Concat(
                FormatBytes(sample.DedicatedMemoryBytes),
                "/",
                FormatBytes(sample.DedicatedMemoryCapacityBytes))
            : "Unavailable";
        string sharedMemory = sample.HasSharedMemoryData
            ? string.Concat(
                FormatBytes(sample.SharedMemoryBytes),
                "/",
                FormatBytes(sample.SharedMemoryCapacityBytes))
            : "Unavailable";
        GPUPerformanceEngineSnapshot? busiestEngine = FindBusiestEngine(sample.Engines.Span);
        PerformanceStatistic[] statistics =
        [
            new("Utilization", utilization),
            new("Dedicated GPU memory", dedicatedMemory),
            new("Shared GPU memory", sharedMemory),
            new("Busiest engine", busiestEngine?.Name ?? "Unavailable"),
            new(
                "Busiest engine use",
                busiestEngine.HasValue
                    ? FormatPercent(true, busiestEngine.Value.UtilizationPercent)
                    : "Unavailable"),
            new("Adapter LUID", string.Concat("0x", sample.AdapterLUID.ToString("X16")))
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
        string hardwareName = hardwareNameResolver.Resolve(sample.Kind, sample.Name);
        string utilization = FormatPercent(sample.HasPerformanceSample, sample.ActiveTimePercent);
        string title = string.Concat(
            "Disk ",
            sample.SortKey.ToString(CultureInfo.CurrentCulture));
        if (!string.IsNullOrWhiteSpace(sample.VolumeNames))
            title = string.Concat(title, " (", sample.VolumeNames, ")");
        string summary = sample.HasPerformanceSample
            ? utilization
            : "Collecting disk counters...";
        ulong availableBytes = Math.Min(sample.AvailableBytes, sample.FormattedCapacityBytes);
        ulong usedBytes = sample.FormattedCapacityBytes - availableBytes;
        PerformanceStatistic[] statistics =
        [
            new("Active time", utilization),
            new("Read speed", FormatOptionalBytesPerSecond(sample.HasPerformanceSample, sample.ReadBytesPerSecond)),
            new("Write speed", FormatOptionalBytesPerSecond(sample.HasPerformanceSample, sample.WriteBytesPerSecond)),
            new(
                "Average response time",
                sample.HasPerformanceSample
                    ? string.Concat(
                        sample.AverageResponseTimeMilliseconds.ToString("N1", CultureInfo.CurrentCulture),
                        " ms")
                    : "Unavailable"),
            new(
                "Queue depth",
                sample.HasPerformanceSample
                    ? sample.QueueDepth.ToString("N0", CultureInfo.CurrentCulture)
                    : "Unavailable"),
            new("Capacity", FormatOptionalBytes(sample.CapacityBytes)),
            new("Formatted", FormatOptionalBytes(sample.FormattedCapacityBytes)),
            new("Used space", sample.FormattedCapacityBytes > 0 ? FormatBytes(usedBytes) : "Unavailable"),
            new("Free space", sample.FormattedCapacityBytes > 0 ? FormatBytes(availableBytes) : "Unavailable")
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
            sample.HasPerformanceSample,
            sample.ActiveTimePercent,
            DiskAccent,
            statistics);
    }

    private static GPUPerformanceEngineSnapshot? FindBusiestEngine(
        ReadOnlySpan<GPUPerformanceEngineSnapshot> engines)
    {
        if (engines.Length == 0) return null;

        GPUPerformanceEngineSnapshot busiest = engines[0];
        for (int engineIndex = 1; engineIndex < engines.Length; engineIndex++)
        {
            if (engines[engineIndex].UtilizationPercent > busiest.UtilizationPercent)
                busiest = engines[engineIndex];
        }
        return busiest;
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

    private static string FormatPercent(bool isAvailable, double value) =>
        isAvailable && double.IsFinite(value)
            ? string.Concat(
                Math.Clamp(value, 0, 100).ToString("N0", CultureInfo.CurrentCulture),
                "%")
            : "Unavailable";

    private static string FormatCount(int value) =>
        value > 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : "Unavailable";

    private static string FormatOptionalBytes(ulong value) =>
        value > 0 ? FormatBytes(value) : "Unavailable";

    private static string FormatSignedBytes(long value) =>
        value >= 0 ? FormatBytes((ulong)value) : "Unavailable";

    private static string FormatOptionalBytesPerSecond(bool isAvailable, double value) =>
        isAvailable ? FormatBytesPerSecond(value) : "Unavailable";

    private static string FormatBytesPerSecond(double value) =>
        double.IsFinite(value) && value >= 0
            ? string.Concat(FormatBytes(value), "/s")
            : "Unavailable";

    private static string FormatBytes(ulong value) => FormatBytes((double)value);

    private static string FormatBytes(double value)
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
        string format = value >= 100 ? "N0" : "N1";
        return string.Concat(value.ToString(format, CultureInfo.CurrentCulture), " ", suffix);
    }

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

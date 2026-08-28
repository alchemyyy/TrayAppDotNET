namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One immutable, direct-OS snapshot for the Performance page.</summary>
internal sealed record PerformanceSnapshot(
    DateTimeOffset CapturedAt,
    long CapturedTimestamp,
    CPUPerformanceSnapshot CPU,
    MemoryPerformanceSnapshot Memory,
    ReadOnlyMemory<GPUPerformanceSnapshot> GPUs,
    ReadOnlyMemory<NetworkPerformanceSnapshot> Networks,
    ReadOnlyMemory<DiskPerformanceSnapshot> Disks)
{
    public static PerformanceSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        0,
        CPUPerformanceSnapshot.Empty,
        MemoryPerformanceSnapshot.Empty,
        ReadOnlyMemory<GPUPerformanceSnapshot>.Empty,
        ReadOnlyMemory<NetworkPerformanceSnapshot>.Empty,
        ReadOnlyMemory<DiskPerformanceSnapshot>.Empty);
}

/// <summary>Aggregate and per-logical-processor CPU values measured from Windows kernel counters.</summary>
internal sealed record CPUPerformanceSnapshot(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Name,
    bool HasUtilizationSample,
    double UtilizationPercent,
    double HighestLogicalProcessorPercent,
    ReadOnlyMemory<double> LogicalProcessorUtilizationPercents,
    bool HasFrequencyData,
    ulong HighestCurrentSpeedHertz,
    ulong BaseSpeedHertz,
    ulong HighestRecordedSpeedHertz,
    int SocketCount,
    int CoreCount,
    int LogicalProcessorCount,
    bool IsVirtualizationFirmwareEnabled,
    ulong L1CacheBytes,
    ulong L2CacheBytes,
    ulong L3CacheBytes,
    uint ProcessCount,
    uint ThreadCount,
    uint HandleCount,
    TimeSpan Uptime)
{
    public const string StableDeviceID = "cpu";

    public static CPUPerformanceSnapshot Empty { get; } = new(
        StableDeviceID,
        PerformanceDeviceKind.CPU,
        0,
        "CPU",
        false,
        0,
        0,
        ReadOnlyMemory<double>.Empty,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        TimeSpan.Zero);
}

/// <summary>Physical and committed-memory values measured from kernel memory APIs.</summary>
internal sealed record MemoryPerformanceSnapshot(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    bool HasMemoryData,
    double UtilizationPercent,
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    ulong UsedPhysicalBytes,
    ulong InstalledPhysicalBytes,
    ulong CommittedBytes,
    ulong CommitLimitBytes,
    ulong CachedBytes,
    ulong PagedPoolBytes,
    ulong NonPagedPoolBytes)
{
    public const string StableDeviceID = "memory";

    public static MemoryPerformanceSnapshot Empty { get; } = new(
        StableDeviceID,
        PerformanceDeviceKind.Memory,
        0,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}

/// <summary>One GPU engine's aggregate utilization across all owning processes.</summary>
internal readonly record struct GPUPerformanceEngineSnapshot(
    int EngineIndex,
    string Name,
    double UtilizationPercent);

/// <summary>One directly enumerated GPU adapter and its raw PDH engine data.</summary>
internal sealed record GPUPerformanceSnapshot(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Name,
    ulong AdapterLUID,
    int PhysicalAdapterIndex,
    bool HasUtilizationSample,
    double UtilizationPercent,
    ReadOnlyMemory<GPUPerformanceEngineSnapshot> Engines,
    bool HasDedicatedMemoryData,
    ulong DedicatedMemoryBytes,
    ulong DedicatedMemoryCapacityBytes,
    bool HasSharedMemoryData,
    ulong SharedMemoryBytes,
    ulong SharedMemoryCapacityBytes);

/// <summary>One network adapter sampled from cumulative interface byte counters.</summary>
internal sealed record NetworkPerformanceSnapshot(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Name,
    string Description,
    string InterfaceType,
    bool IsOperational,
    bool HasThroughputSample,
    double ReceiveBytesPerSecond,
    double SendBytesPerSecond,
    long LinkSpeedBitsPerSecond,
    long TotalBytesReceived,
    long TotalBytesSent);

/// <summary>One physical disk sampled through its kernel disk-performance counters.</summary>
internal sealed record DiskPerformanceSnapshot(
    string DeviceID,
    PerformanceDeviceKind Kind,
    int SortKey,
    string Name,
    string VolumeNames,
    string DeviceType,
    bool HasPerformanceSample,
    double ActiveTimePercent,
    double ReadBytesPerSecond,
    double WriteBytesPerSecond,
    double AverageResponseTimeMilliseconds,
    uint QueueDepth,
    ulong CapacityBytes,
    ulong FormattedCapacityBytes,
    ulong AvailableBytes);

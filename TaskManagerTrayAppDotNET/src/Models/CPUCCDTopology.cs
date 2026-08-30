namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies the exact source used to partition CPU cores into CCDs.</summary>
internal enum CPUCCDTopologySource
{
    None,
    WindowsProcessorDie,
    AMDExtendedCPUTopology
}

/// <summary>One active Windows logical processor and its system-wide index.</summary>
internal readonly record struct CPULogicalProcessor(
    int SystemIndex,
    ushort Group,
    byte Number);

/// <summary>One physical core and the logical processors scheduled on it.</summary>
internal sealed record CPUCoreTopologyEntry(
    int CoreIndex,
    int CCDIndex,
    ReadOnlyMemory<int> LogicalProcessorIndexes);

/// <summary>One CPU compute die and its physical-core and logical-processor membership.</summary>
internal sealed record CPUCCDTopologyEntry(
    int CCDIndex,
    uint? HardwareTopologyID,
    ReadOnlyMemory<int> CoreIndexes,
    ReadOnlyMemory<int> LogicalProcessorIndexes);

/// <summary>Immutable active CPU topology suitable for per-CCD metric aggregation.</summary>
internal sealed record CPUCCDTopology(
    CPUCCDTopologySource Source,
    ReadOnlyMemory<CPULogicalProcessor> LogicalProcessors,
    ReadOnlyMemory<CPUCoreTopologyEntry> Cores,
    ReadOnlyMemory<CPUCCDTopologyEntry> CCDs)
{
    public static CPUCCDTopology Empty { get; } = new(
        CPUCCDTopologySource.None,
        ReadOnlyMemory<CPULogicalProcessor>.Empty,
        ReadOnlyMemory<CPUCoreTopologyEntry>.Empty,
        ReadOnlyMemory<CPUCCDTopologyEntry>.Empty);

    public bool IsAvailable => Source != CPUCCDTopologySource.None && CCDs.Length > 0;
}

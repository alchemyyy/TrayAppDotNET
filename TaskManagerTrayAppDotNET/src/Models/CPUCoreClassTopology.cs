namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Physical cores that share one Windows efficiency class and level-3 cache placement.</summary>
/// <remarks>
/// <see cref="IsOutsideL3Cache"/> is true only for lower-class cores that Windows places in no level-3 cache
/// while other cores have one, which is how low power island efficiency cores appear.
/// </remarks>
internal sealed record CPUCoreClassEntry(
    byte EfficiencyClass,
    bool IsOutsideL3Cache,
    int CoreCount,
    ReadOnlyMemory<int> LogicalProcessorIndexes);

/// <summary>Windows core efficiency classes, ordered from the highest class to the lowest.</summary>
internal sealed record CPUCoreClassTopology(ReadOnlyMemory<CPUCoreClassEntry> Classes)
{
    public static CPUCoreClassTopology Empty { get; } = new(ReadOnlyMemory<CPUCoreClassEntry>.Empty);

    public bool IsAvailable => Classes.Length > 0;

    /// <summary>Gets whether Windows reports cores of more than one efficiency class.</summary>
    public bool IsHeterogeneous => Classes.Length > 1;
}

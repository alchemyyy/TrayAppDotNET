namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One normalized system-performance sample used by the tray graph.</summary>
internal readonly record struct SystemPerformanceSample(
    double CPUAveragePercent,
    double CPUHighestCorePercent,
    double MemoryPercent)
{
    public static SystemPerformanceSample Empty { get; } = new(0, 0, 0);

    /// <summary>Returns the percentage selected for the tray graph.</summary>
    public double Select(TrayGraphDataSource dataSource) =>
        dataSource switch
        {
            TrayGraphDataSource.CPUAverage => CPUAveragePercent,
            TrayGraphDataSource.CPUHighestCore => CPUHighestCorePercent,
            TrayGraphDataSource.Memory => MemoryPercent,
            _ => CPUAveragePercent
        };
}

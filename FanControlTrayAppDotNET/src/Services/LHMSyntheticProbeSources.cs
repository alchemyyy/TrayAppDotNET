namespace FanControlTrayAppDotNET.Services;

/// <summary>
/// Builds app-side synthetic probe sources from LibreHardwareMonitor readings.
/// </summary>
internal static class LHMSyntheticProbeSources
{
    public const string MaxClockDisplayName = "Cores (Max)";
    public const string MaxEffectiveClockDisplayName = "Cores (Max Effective)";

    private const string ClockFolderName = "Clock";
    private const string MaxClockLeaf = "Cores_(Max)";
    private const string MaxEffectiveClockLeaf = "Cores_(Max_Effective)";
    private const string EffectiveClockToken = "(Effective)";
    private const string AverageToken = "Average";
    private const string CoreToken = "Core";

    /// <summary>
    /// Builds the stable key for a CPU max-clock synthetic source.
    /// </summary>
    public static string BuildMaxClockKey(string hardwareName)
    {
        string controller = hardwareName.Replace(' ', '_');
        return $"{controller}.{ClockFolderName}.{MaxClockLeaf}";
    }

    /// <summary>
    /// Builds the stable key for a CPU max-effective-clock synthetic source.
    /// </summary>
    public static string BuildMaxEffectiveClockKey(string hardwareName)
    {
        string controller = hardwareName.Replace(' ', '_');
        return $"{controller}.{ClockFolderName}.{MaxEffectiveClockLeaf}";
    }

    /// <summary>
    /// Returns true for per-core clock sensors used by the max clock synthetic probe.
    /// </summary>
    public static bool IsCoreClockSensor(DataSourceTypeEnum sourceType, string sensorName)
    {
        if (sourceType != DataSourceTypeEnum.Clock) return false;
        if (sensorName.Contains(EffectiveClockToken, StringComparison.OrdinalIgnoreCase)) return false;
        if (sensorName.Contains(AverageToken, StringComparison.OrdinalIgnoreCase)) return false;

        return sensorName.Contains(CoreToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true for per-core effective clock sensors used by the max synthetic probe.
    /// </summary>
    public static bool IsCoreEffectiveClockSensor(DataSourceTypeEnum sourceType, string sensorName)
    {
        if (sourceType != DataSourceTypeEnum.Clock) return false;
        if (!sensorName.Contains(EffectiveClockToken, StringComparison.OrdinalIgnoreCase)) return false;
        if (sensorName.Contains(AverageToken, StringComparison.OrdinalIgnoreCase)) return false;

        return sensorName.Contains(CoreToken, StringComparison.OrdinalIgnoreCase);
    }
}

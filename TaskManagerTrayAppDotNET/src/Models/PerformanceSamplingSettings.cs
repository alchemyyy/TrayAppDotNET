namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Defines normalized Performance sampling and history-retention bounds.</summary>
internal static class PerformanceSamplingSettings
{
    public const int DefaultHistoryLengthMinutes = 1;
    public const int MinimumHistoryLengthMinutes = 1;
    public const int MaximumHistoryLengthMinutes = 60;
    public const int DefaultSampleIntervalMilliseconds = 1_000;
    public const int MinimumSampleIntervalMilliseconds = 1;
    public const int MaximumSampleIntervalMilliseconds = 60_000;

    private const int MillisecondsPerMinute = 60_000;

    /// <summary>Clamps a requested history length to the supported minute range.</summary>
    public static int NormalizeHistoryLengthMinutes(int value) =>
        Math.Clamp(value, MinimumHistoryLengthMinutes, MaximumHistoryLengthMinutes);

    /// <summary>Clamps a requested sampling interval to the supported millisecond range.</summary>
    public static int NormalizeSampleIntervalMilliseconds(int value) =>
        Math.Clamp(value, MinimumSampleIntervalMilliseconds, MaximumSampleIntervalMilliseconds);

    /// <summary>Calculates the bounded number of samples retained by one history.</summary>
    public static int CalculateMaximumHistoryCount(
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        int normalizedHistoryLength = NormalizeHistoryLengthMinutes(historyLengthMinutes);
        int normalizedSampleInterval = NormalizeSampleIntervalMilliseconds(
            sampleIntervalMilliseconds);
        int maximumHistoryCount = normalizedHistoryLength
                                  * MillisecondsPerMinute
                                  / normalizedSampleInterval;
        return Math.Max(1, maximumHistoryCount);
    }
}

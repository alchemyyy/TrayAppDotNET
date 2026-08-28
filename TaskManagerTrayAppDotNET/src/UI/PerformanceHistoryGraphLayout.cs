namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Identifies the history sample nearest a horizontal graph position.</summary>
internal readonly record struct PerformanceHistoryGraphHoverSample(
    int ChronologicalIndex,
    double Value,
    double PositionX);

/// <summary>Maps performance-history timestamps to graph hover positions.</summary>
internal static class PerformanceHistoryGraphLayout
{
    /// <summary>Finds the sample nearest the pointer on the graph's fixed-duration timeline.</summary>
    public static bool TryGetHoverSample(
        PerformanceHistory history,
        double pointerPositionX,
        double graphWidth,
        out PerformanceHistoryGraphHoverSample sample)
    {
        ArgumentNullException.ThrowIfNull(history);
        sample = default;
        if (history.Count == 0
            || !double.IsFinite(pointerPositionX)
            || !double.IsFinite(graphWidth)
            || graphWidth <= 0)
        {
            return false;
        }

        double windowStartTimestamp = history.CurrentTimestamp
                                      - (double)history.WindowDurationTicks;
        double clampedPointerPositionX = Math.Clamp(pointerPositionX, 0, graphWidth);
        double targetTimestamp = windowStartTimestamp
                                 + clampedPointerPositionX
                                 / graphWidth
                                 * history.WindowDurationTicks;
        int sampleIndex = FindNearestSampleIndex(history, targetTimestamp);
        long sampleTimestamp = history.GetTimestampChronological(sampleIndex);
        double elapsedWindowFraction = (sampleTimestamp - windowStartTimestamp)
                                       / history.WindowDurationTicks;
        double samplePositionX = Math.Clamp(elapsedWindowFraction, 0, 1) * graphWidth;
        sample = new PerformanceHistoryGraphHoverSample(
            sampleIndex,
            history.GetChronological(sampleIndex),
            samplePositionX);
        return true;
    }

    /// <summary>Uses binary search to select the nearest chronological sample.</summary>
    private static int FindNearestSampleIndex(
        PerformanceHistory history,
        double targetTimestamp)
    {
        int newestIndex = history.Count - 1;
        long oldestTimestamp = history.GetTimestampChronological(0);
        if (targetTimestamp <= oldestTimestamp) return 0;

        long newestTimestamp = history.GetTimestampChronological(newestIndex);
        if (targetTimestamp >= newestTimestamp) return newestIndex;

        int lowerBound = 1;
        int upperBound = newestIndex;
        while (lowerBound < upperBound)
        {
            int middleIndex = lowerBound + (upperBound - lowerBound) / 2;
            if (history.GetTimestampChronological(middleIndex) < targetTimestamp)
                lowerBound = middleIndex + 1;
            else
                upperBound = middleIndex;
        }

        int newerIndex = lowerBound;
        int olderIndex = newerIndex - 1;
        double olderDistance = targetTimestamp
                               - history.GetTimestampChronological(olderIndex);
        double newerDistance = history.GetTimestampChronological(newerIndex)
                               - targetTimestamp;
        return newerDistance <= olderDistance ? newerIndex : olderIndex;
    }
}

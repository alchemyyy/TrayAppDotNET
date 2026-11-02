namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Maps performance-history samples to graph hover positions.</summary>
/// <remarks>
/// The UI thread and the render-thread hover handler share these functions over one timeline snapshot, so the label
/// and the snapped line resolve the same sample for the same pointer position.
/// </remarks>
internal static class PerformanceHistoryGraphLayout
{
    /// <summary>
    /// Places each retained sample on the graph's fixed-duration timeline, oldest first, where 0 is the left edge and 1
    /// the right edge.
    /// </summary>
    public static double[] CreateSampleFractions(PerformanceHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        int sampleCount = history.Count;
        if (sampleCount == 0) return [];

        double windowStartTimestamp = history.CurrentTimestamp
                                      - (double)history.WindowDurationTicks;
        double[] sampleFractions = new double[sampleCount];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            sampleFractions[sampleIndex] = (history.GetTimestampChronological(sampleIndex) - windowStartTimestamp)
                                           / history.WindowDurationTicks;
        }

        return sampleFractions;
    }

    /// <summary>
    /// Finds the sample nearest the pointer, preferring the newer sample at an exact midpoint. A pointer beyond either
    /// graph edge resolves the nearest endpoint sample.
    /// </summary>
    public static bool TryGetHoverSample(
        ReadOnlySpan<double> sampleFractions,
        double pointerPositionX,
        double graphWidth,
        out int sampleIndex,
        out double samplePositionX)
    {
        sampleIndex = -1;
        samplePositionX = 0;
        if (sampleFractions.IsEmpty
            || !double.IsFinite(pointerPositionX)
            || !double.IsFinite(graphWidth)
            || graphWidth <= 0)
            return false;

        double targetFraction = Math.Clamp(pointerPositionX, min: 0, graphWidth) / graphWidth;
        sampleIndex = FindNearestSampleIndex(sampleFractions, targetFraction);
        samplePositionX = Math.Clamp(sampleFractions[sampleIndex], min: 0, max: 1) * graphWidth;
        return true;
    }

    /// <summary>Uses binary search to select the nearest chronological sample.</summary>
    private static int FindNearestSampleIndex(
        ReadOnlySpan<double> sampleFractions,
        double targetFraction)
    {
        int newestIndex = sampleFractions.Length - 1;
        if (targetFraction <= sampleFractions[0]) return 0;
        if (targetFraction >= sampleFractions[newestIndex]) return newestIndex;

        int lowerBound = 1;
        int upperBound = newestIndex;
        while (lowerBound < upperBound)
        {
            int middleIndex = lowerBound + (upperBound - lowerBound) / 2;
            if (sampleFractions[middleIndex] < targetFraction)
                lowerBound = middleIndex + 1;
            else
                upperBound = middleIndex;
        }

        int newerIndex = lowerBound;
        int olderIndex = newerIndex - 1;
        double olderDistance = targetFraction - sampleFractions[olderIndex];
        double newerDistance = sampleFractions[newerIndex] - targetFraction;
        return newerDistance <= olderDistance ? newerIndex : olderIndex;
    }
}

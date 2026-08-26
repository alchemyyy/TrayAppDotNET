using TrayAppDotNETCommon.UI.Controls.Curves;

namespace TaskManagerTrayAppDotNET.UI.Tray;

internal delegate double MarqueeSamplePlacementFunction(
    int currentSampleIndex,
    int numSamplesToDisplay);

/// <summary>Builds a recency-weighted, monotonic marquee curve for the tray graph.</summary>
internal static class TaskManagerTrayGraphSampler
{
    private const double EndpointTolerance = 0.000000001;

    /// <summary>Places samples with the default normalized quadratic polynomial.</summary>
    internal static double DefaultSamplePlacement(
        int currentSampleIndex,
        int numSamplesToDisplay)
    {
        if (numSamplesToDisplay <= 0)
            throw new ArgumentOutOfRangeException(nameof(numSamplesToDisplay));
        if ((uint)currentSampleIndex >= (uint)numSamplesToDisplay)
            throw new ArgumentOutOfRangeException(nameof(currentSampleIndex));
        if (numSamplesToDisplay == 1) return 1.0;

        double normalizedIndex = currentSampleIndex / (double)(numSamplesToDisplay - 1);
        return normalizedIndex * normalizedIndex;
    }

    /// <summary>Evaluates a caller-specified placement function for every displayed sample.</summary>
    internal static double[] CreateSamplePositions(
        int numSamplesToDisplay,
        MarqueeSamplePlacementFunction? placementFunction = null)
    {
        if (numSamplesToDisplay <= 0)
            throw new ArgumentOutOfRangeException(nameof(numSamplesToDisplay));

        MarqueeSamplePlacementFunction effectivePlacementFunction =
            placementFunction ?? DefaultSamplePlacement;
        double[] samplePositions = new double[numSamplesToDisplay];
        for (int currentSampleIndex = 0;
             currentSampleIndex < numSamplesToDisplay;
             currentSampleIndex++)
        {
            double samplePosition = effectivePlacementFunction(
                currentSampleIndex,
                numSamplesToDisplay);
            if (!double.IsFinite(samplePosition) || samplePosition is < 0 or > 1)
            {
                throw new InvalidOperationException(
                    "The marquee sample placement function must return a normalized value from 0 to 1.");
            }

            if (currentSampleIndex > 0
                && samplePosition <= samplePositions[currentSampleIndex - 1])
            {
                throw new InvalidOperationException(
                    "The marquee sample placement function must return strictly increasing positions.");
            }

            samplePositions[currentSampleIndex] = samplePosition;
        }

        if (numSamplesToDisplay == 1) return samplePositions;
        if (Math.Abs(samplePositions[0]) > EndpointTolerance
            || Math.Abs(samplePositions[^1] - 1.0) > EndpointTolerance)
        {
            throw new InvalidOperationException(
                "The marquee sample placement function must place the oldest sample at 0 and newest sample at 1.");
        }

        samplePositions[0] = 0;
        samplePositions[^1] = 1;
        return samplePositions;
    }

    /// <summary>Samples the marquee history through a monotonic cubic Hermite spline.</summary>
    internal static double[] SampleMarquee(
        IReadOnlyList<double> values,
        int sampleCount,
        MarqueeSamplePlacementFunction? placementFunction = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (sampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));

        double[] samples = new double[sampleCount];
        if (values.Count == 0) return samples;

        double currentPercent = NormalizePercent(values[^1]);
        if (values.Count == 1 || sampleCount == 1)
        {
            Array.Fill(samples, currentPercent);
            return samples;
        }

        double[] timePoints = CreateSamplePositions(values.Count, placementFunction);
        double[] valuePoints = new double[values.Count];
        for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
            valuePoints[valueIndex] = NormalizePercent(values[valueIndex]);

        double[] tangents = TimeCurveSampler.ComputeMonotonicTangents(timePoints, valuePoints);
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            double samplePosition = sampleIndex / (double)(sampleCount - 1);
            samples[sampleIndex] = Math.Clamp(
                TimeCurveSampler.InterpolateMonotonicCubic(
                    timePoints,
                    valuePoints,
                    tangents,
                    samplePosition),
                0,
                100);
        }

        return samples;
    }

    private static double NormalizePercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

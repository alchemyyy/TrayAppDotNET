using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceHistoryGraphLayoutTests
{
    [Fact]
    public void HoverSelectsTheNearestSampleOnTheFixedDurationTimeline()
    {
        const double GraphWidth = 200;
        PerformanceHistory history = CreateHistoryWithSamples(
            (0.25, 25),
            (0.50, 50),
            (0.75, 75));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            history,
            pointerPositionX: 108,
            GraphWidth,
            out PerformanceHistoryGraphHoverSample sample);

        Assert.True(wasFound);
        Assert.Equal(expected: 1, sample.ChronologicalIndex);
        Assert.Equal(history.GetTimestampChronological(1), sample.Timestamp);
        Assert.Equal(expected: 50, sample.Value);
        Assert.Equal(expected: 100, sample.PositionX, precision: 6);
    }

    [Theory]
    [InlineData(-20, 0, 25, 50)]
    [InlineData(400, 2, 75, 150)]
    public void HoverOutsideTheGraphClampsToTheNearestEndpointSample(
        double pointerPositionX,
        int expectedIndex,
        double expectedValue,
        double expectedPositionX)
    {
        const double GraphWidth = 200;
        PerformanceHistory history = CreateHistoryWithSamples(
            (0.25, 25),
            (0.50, 50),
            (0.75, 75));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            history,
            pointerPositionX,
            GraphWidth,
            out PerformanceHistoryGraphHoverSample sample);

        Assert.True(wasFound);
        Assert.Equal(expectedIndex, sample.ChronologicalIndex);
        Assert.Equal(expectedValue, sample.Value);
        Assert.Equal(expectedPositionX, sample.PositionX, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void HoverRejectsAnInvalidGraphWidth(double graphWidth)
    {
        PerformanceHistory history = CreateHistoryWithSamples((0.50, 50));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            history,
            pointerPositionX: 10,
            graphWidth,
            out PerformanceHistoryGraphHoverSample _);

        Assert.False(wasFound);
    }

    [Fact]
    public void HoverRejectsAnEmptyHistory()
    {
        PerformanceHistory history = new();

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            history,
            pointerPositionX: 10,
            graphWidth: 100,
            out PerformanceHistoryGraphHoverSample _);

        Assert.False(wasFound);
    }

    private static PerformanceHistory CreateHistoryWithSamples(
        params (double windowFraction, double value)[] samples)
    {
        PerformanceHistory history = new();
        long windowDuration = history.WindowDurationTicks;
        long currentTimestamp = windowDuration + Stopwatch.Frequency;
        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            (double windowFraction, double value) = samples[sampleIndex];
            long timestamp = currentTimestamp
                             - windowDuration
                             + (long)Math.Round(windowFraction * windowDuration);
            history.Add(timestamp, value);
        }

        history.AdvanceTo(currentTimestamp);
        return history;
    }
}

using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Performance;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceHistoryGraphLayoutTests
{
    [Fact]
    public void HoverSelectsTheNearestSampleOnTheFixedDurationTimeline()
    {
        const double GraphWidth = 200;
        double[] sampleFractions = PerformanceHistoryGraphLayout.CreateSampleFractions(
            CreateHistoryWithSamples(
                (0.25, 25),
                (0.50, 50),
                (0.75, 75)));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            sampleFractions,
            pointerPositionX: 108,
            GraphWidth,
            out int sampleIndex,
            out double samplePositionX);

        Assert.True(wasFound);
        Assert.Equal(expected: 1, sampleIndex);
        Assert.Equal(expected: 100, samplePositionX, precision: 6);
    }

    [Theory]
    [InlineData(-20, 0, 50)]
    [InlineData(400, 2, 150)]
    public void HoverOutsideTheGraphClampsToTheNearestEndpointSample(
        double pointerPositionX,
        int expectedIndex,
        double expectedPositionX)
    {
        const double GraphWidth = 200;
        double[] sampleFractions = PerformanceHistoryGraphLayout.CreateSampleFractions(
            CreateHistoryWithSamples(
                (0.25, 25),
                (0.50, 50),
                (0.75, 75)));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            sampleFractions,
            pointerPositionX,
            GraphWidth,
            out int sampleIndex,
            out double samplePositionX);

        Assert.True(wasFound);
        Assert.Equal(expectedIndex, sampleIndex);
        Assert.Equal(expectedPositionX, samplePositionX, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void HoverRejectsAnInvalidGraphWidth(double graphWidth)
    {
        double[] sampleFractions = PerformanceHistoryGraphLayout.CreateSampleFractions(
            CreateHistoryWithSamples((0.50, 50)));

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            sampleFractions,
            pointerPositionX: 10,
            graphWidth,
            out _,
            out _);

        Assert.False(wasFound);
    }

    [Fact]
    public void HoverRejectsAnEmptyHistory()
    {
        double[] sampleFractions = PerformanceHistoryGraphLayout.CreateSampleFractions(new PerformanceHistory());

        bool wasFound = PerformanceHistoryGraphLayout.TryGetHoverSample(
            sampleFractions,
            pointerPositionX: 10,
            graphWidth: 100,
            out _,
            out _);

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

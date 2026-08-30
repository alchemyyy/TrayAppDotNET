using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceHistoryTests
{
    [Fact]
    public void HistoryRetainsNewestSamplesInChronologicalOrder()
    {
        PerformanceHistory history = new(3);

        history.Add(10, 10);
        history.Add(20, 20);
        history.Add(30, 30);
        history.Add(40, 40);

        Assert.Equal(3, history.Count);
        Assert.Equal(20, history.GetChronological(0));
        Assert.Equal(30, history.GetChronological(1));
        Assert.Equal(40, history.GetChronological(2));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(125, 100)]
    public void HistoryNormalizesInvalidAndOutOfRangeSamples(double value, double expected)
    {
        PerformanceHistory history = new(1);

        history.Add(1, value);

        Assert.Equal(expected, history.GetChronological(0));
    }

    [Fact]
    public void ClearStartsANewTimeline()
    {
        PerformanceHistory history = new(3);
        history.Add(10, 10);
        history.Add(20, 20);

        history.Clear();
        history.Add(30, 30);

        Assert.Equal(1, history.Count);
        Assert.Equal(30, history.GetChronological(0));
    }

    [Fact]
    public void HistoryUsesASixtySecondWallClockWindow()
    {
        PerformanceHistory history = new();
        long firstTimestamp = 10;
        long lastTimestamp = firstTimestamp
                             + Stopwatch.Frequency
                             * PerformanceSamplingSettings.DefaultHistoryLengthMinutes
                             * 60
                             + 1;

        history.Add(firstTimestamp, 10);
        history.Add(lastTimestamp, 20);

        Assert.Equal(1, history.Count);
        Assert.Equal(lastTimestamp, history.GetTimestampChronological(0));
        Assert.Equal(20, history.GetChronological(0));
    }

    [Fact]
    public void UnavailableSamplesStillAdvanceAndExpireTheHistory()
    {
        PerformanceHistory history = new();
        long firstTimestamp = 10;
        long currentTimestamp = firstTimestamp
                                + Stopwatch.Frequency
                                * PerformanceSamplingSettings.DefaultHistoryLengthMinutes
                                * 60
                                + 1;
        history.Add(firstTimestamp, 25);

        history.AdvanceTo(currentTimestamp);

        Assert.Equal(0, history.Count);
        Assert.Equal(currentTimestamp, history.CurrentTimestamp);
    }

    [Fact]
    public void AdvancingTheWindowMovesExistingSamplesRelativeToNow()
    {
        PerformanceHistory history = new();
        history.Add(10, 25);

        history.AdvanceTo(20);

        Assert.Equal(1, history.Count);
        Assert.Equal(20, history.CurrentTimestamp);
        Assert.Equal(10, history.GetTimestampChronological(0));
    }

    [Fact]
    public void ConfiguredHistoryCapacityExactlyMatchesWindowDividedByInterval()
    {
        const int HistoryLengthMinutes = 5;
        const int SampleIntervalMilliseconds = 2_500;
        int expectedCapacity = PerformanceSamplingSettings.CalculateMaximumHistoryCount(
            HistoryLengthMinutes,
            SampleIntervalMilliseconds);
        PerformanceHistory history = new(HistoryLengthMinutes, SampleIntervalMilliseconds);

        Assert.Equal(120, expectedCapacity);
        Assert.Equal(expectedCapacity, history.Capacity);
        Assert.Equal(
            (long)Stopwatch.Frequency * HistoryLengthMinutes * 60,
            history.WindowDurationTicks);
    }

    [Fact]
    public void ConfiguredHistoryNeverRetainsMoreThanItsDerivedCapacity()
    {
        const int HistoryLengthMinutes = 1;
        const int SampleIntervalMilliseconds = 15_000;
        PerformanceHistory history = new(HistoryLengthMinutes, SampleIntervalMilliseconds);

        for (int sampleIndex = 0; sampleIndex < 5; sampleIndex++)
            history.Add(sampleIndex * Stopwatch.Frequency, sampleIndex);

        Assert.Equal(4, history.Capacity);
        Assert.Equal(4, history.Count);
        Assert.Equal(1, history.GetChronological(0));
        Assert.Equal(4, history.GetChronological(3));
    }

    [Fact]
    public void ExactLookupSupportsReplacementAndWrappedStorage()
    {
        PerformanceHistory history = new(2);
        history.Add(100, 10);
        history.Add(200, 20);
        history.Add(200, 25);
        history.Add(300, 30);

        Assert.False(history.TryGetExact(100, out double _));
        Assert.True(history.TryGetExact(200, out double replacedValue));
        Assert.Equal(25, replacedValue);
        Assert.True(history.TryGetExact(300, out double newestValue));
        Assert.Equal(30, newestValue);
        Assert.False(history.TryGetExact(250, out double _));
    }
}

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

        history.Add(timestamp: 10, value: 10);
        history.Add(timestamp: 20, value: 20);
        history.Add(timestamp: 30, value: 30);
        history.Add(timestamp: 40, value: 40);

        Assert.Equal(expected: 3, history.Count);
        Assert.Equal(expected: 20, history.GetChronological(0));
        Assert.Equal(expected: 30, history.GetChronological(1));
        Assert.Equal(expected: 40, history.GetChronological(2));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(125, 100)]
    public void HistoryNormalizesInvalidAndOutOfRangeSamples(double value, double expected)
    {
        PerformanceHistory history = new(1);

        history.Add(timestamp: 1, value);

        Assert.Equal(expected, history.GetChronological(0));
    }

    [Fact]
    public void ClearStartsANewTimeline()
    {
        PerformanceHistory history = new(3);
        history.Add(timestamp: 10, value: 10);
        history.Add(timestamp: 20, value: 20);

        history.Clear();
        history.Add(timestamp: 30, value: 30);

        Assert.Equal(expected: 1, history.Count);
        Assert.Equal(expected: 30, history.GetChronological(0));
    }

    [Fact]
    public void HistoryUsesASixtySecondWallClockWindow()
    {
        PerformanceHistory history = new();
        const long firstTimestamp = 10;
        long lastTimestamp = firstTimestamp
                             + Stopwatch.Frequency
                             * PerformanceSamplingSettings.DefaultHistoryLengthMinutes
                             * 60
                             + 1;

        history.Add(firstTimestamp, value: 10);
        history.Add(lastTimestamp, value: 20);

        Assert.Equal(expected: 1, history.Count);
        Assert.Equal(lastTimestamp, history.GetTimestampChronological(0));
        Assert.Equal(expected: 20, history.GetChronological(0));
    }

    [Fact]
    public void UnavailableSamplesStillAdvanceAndExpireTheHistory()
    {
        PerformanceHistory history = new();
        const long firstTimestamp = 10;
        long currentTimestamp = firstTimestamp
                                + Stopwatch.Frequency
                                * PerformanceSamplingSettings.DefaultHistoryLengthMinutes
                                * 60
                                + 1;
        history.Add(firstTimestamp, value: 25);

        history.AdvanceTo(currentTimestamp);

        Assert.Equal(expected: 0, history.Count);
        Assert.Equal(currentTimestamp, history.CurrentTimestamp);
    }

    [Fact]
    public void AdvancingTheWindowMovesExistingSamplesRelativeToNow()
    {
        PerformanceHistory history = new();
        history.Add(timestamp: 10, value: 25);

        history.AdvanceTo(20);

        Assert.Equal(expected: 1, history.Count);
        Assert.Equal(expected: 20, history.CurrentTimestamp);
        Assert.Equal(expected: 10, history.GetTimestampChronological(0));
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

        Assert.Equal(expected: 120, expectedCapacity);
        Assert.Equal(expectedCapacity, history.Capacity);
        Assert.Equal(
            Stopwatch.Frequency * HistoryLengthMinutes * 60,
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

        Assert.Equal(expected: 4, history.Capacity);
        Assert.Equal(expected: 4, history.Count);
        Assert.Equal(expected: 1, history.GetChronological(0));
        Assert.Equal(expected: 4, history.GetChronological(3));
    }

    [Fact]
    public void ExactLookupSupportsReplacementAndWrappedStorage()
    {
        PerformanceHistory history = new(2);
        history.Add(timestamp: 100, value: 10);
        history.Add(timestamp: 200, value: 20);
        history.Add(timestamp: 200, value: 25);
        history.Add(timestamp: 300, value: 30);

        Assert.False(history.TryGetExact(timestamp: 100, out double _));
        Assert.True(history.TryGetExact(timestamp: 200, out double replacedValue));
        Assert.Equal(expected: 25, replacedValue);
        Assert.True(history.TryGetExact(timestamp: 300, out double newestValue));
        Assert.Equal(expected: 30, newestValue);
        Assert.False(history.TryGetExact(timestamp: 250, out double _));
    }
}

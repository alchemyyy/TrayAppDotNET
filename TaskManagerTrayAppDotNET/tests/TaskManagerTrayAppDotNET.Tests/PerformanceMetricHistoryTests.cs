using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceMetricHistoryTests
{
    [Fact]
    public void RetainsRawValuesAbovePercentageRange()
    {
        PerformanceMetricHistory history = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);

        history.Add(timestamp: 100, value: 2_500_000_000);

        Assert.Equal(expected: 2_500_000_000, history.GetChronological(0));
    }

    [Fact]
    public void ReplacesSameTimestampAndFindsNearestSample()
    {
        PerformanceMetricHistory history = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        history.Add(timestamp: 100, value: 10);
        history.Add(timestamp: 200, value: 20);
        history.Add(timestamp: 200, value: 25);
        history.Add(timestamp: 400, value: 40);

        bool found = history.TryGetNearest(timestamp: 260, out double value);

        Assert.True(found);
        Assert.Equal(expected: 25, value);
        Assert.True(history.TryGetExact(timestamp: 200, out double exactValue));
        Assert.Equal(expected: 25, exactValue);
        Assert.False(history.TryGetExact(timestamp: 260, out double _));
        Assert.Equal(expected: 3, history.Count);
        Assert.Equal(expected: 40, history.GetMaximumValue());
    }

    [Fact]
    public void ExactLookupSupportsWrappedStorage()
    {
        PerformanceMetricHistory history = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 30_000);
        history.Add(timestamp: 100, value: 10);
        history.Add(timestamp: 200, value: 20);
        history.Add(timestamp: 300, value: 30);

        Assert.False(history.TryGetExact(timestamp: 100, out double _));
        Assert.True(history.TryGetExact(timestamp: 200, out double olderValue));
        Assert.Equal(expected: 20, olderValue);
        Assert.True(history.TryGetExact(timestamp: 300, out double newerValue));
        Assert.Equal(expected: 30, newerValue);
    }

    [Fact]
    public void AdvancingDropsSamplesOutsideConfiguredWindow()
    {
        PerformanceMetricHistory history = new(historyLengthMinutes: 1, sampleIntervalMilliseconds: 1_000);
        history.Add(timestamp: 1, value: 10);
        history.Add(Stopwatch.Frequency * 30, value: 20);

        history.AdvanceTo(Stopwatch.Frequency * 61);

        Assert.Equal(expected: 1, history.Count);
        Assert.Equal(expected: 20, history.GetChronological(0));
    }
}

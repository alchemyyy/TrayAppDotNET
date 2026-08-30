using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceMetricHistoryTests
{
    [Fact]
    public void RetainsRawValuesAbovePercentageRange()
    {
        PerformanceMetricHistory history = new(1, 1_000);

        history.Add(100, 2_500_000_000);

        Assert.Equal(2_500_000_000, history.GetChronological(0));
    }

    [Fact]
    public void ReplacesSameTimestampAndFindsNearestSample()
    {
        PerformanceMetricHistory history = new(1, 1_000);
        history.Add(100, 10);
        history.Add(200, 20);
        history.Add(200, 25);
        history.Add(400, 40);

        bool found = history.TryGetNearest(260, out double value);

        Assert.True(found);
        Assert.Equal(25, value);
        Assert.True(history.TryGetExact(200, out double exactValue));
        Assert.Equal(25, exactValue);
        Assert.False(history.TryGetExact(260, out double _));
        Assert.Equal(3, history.Count);
        Assert.Equal(40, history.GetMaximumValue());
    }

    [Fact]
    public void ExactLookupSupportsWrappedStorage()
    {
        PerformanceMetricHistory history = new(1, 30_000);
        history.Add(100, 10);
        history.Add(200, 20);
        history.Add(300, 30);

        Assert.False(history.TryGetExact(100, out double _));
        Assert.True(history.TryGetExact(200, out double olderValue));
        Assert.Equal(20, olderValue);
        Assert.True(history.TryGetExact(300, out double newerValue));
        Assert.Equal(30, newerValue);
    }

    [Fact]
    public void AdvancingDropsSamplesOutsideConfiguredWindow()
    {
        PerformanceMetricHistory history = new(1, 1_000);
        history.Add(1, 10);
        history.Add(Stopwatch.Frequency * 30, 20);

        history.AdvanceTo(Stopwatch.Frequency * 61);

        Assert.Equal(1, history.Count);
        Assert.Equal(20, history.GetChronological(0));
    }
}

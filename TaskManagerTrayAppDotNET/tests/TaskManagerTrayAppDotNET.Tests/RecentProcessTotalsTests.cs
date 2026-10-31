using System.Diagnostics;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class RecentProcessTotalsTests
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);

    [Fact]
    public void ReturnsPublishedTotalsWhileTheyAreRecent()
    {
        long now = 1_000;
        RecentProcessTotals source = new(() => now);
        SystemProcessTotals published = new(ProcessCount: 300, ThreadCount: 4_000, HandleCount: 120_000);

        source.Publish(published);
        now += Stopwatch.Frequency;

        Assert.True(source.TryGetRecent(MaxAge, out SystemProcessTotals totals));
        Assert.Equal(published, totals);
    }

    [Fact]
    public void RejectsTotalsOlderThanTheMaximumAge()
    {
        long now = 1_000;
        RecentProcessTotals source = new(() => now);
        source.Publish(new SystemProcessTotals(ProcessCount: 300, ThreadCount: 4_000, HandleCount: 120_000));

        now += 3 * Stopwatch.Frequency;

        Assert.False(source.TryGetRecent(MaxAge, out SystemProcessTotals totals));
        Assert.Equal(default, totals);
    }

    [Fact]
    public void ClearDropsTheLatestTotals()
    {
        RecentProcessTotals source = new(() => 1_000);
        source.Publish(new SystemProcessTotals(ProcessCount: 1, ThreadCount: 1, HandleCount: 1));

        source.Clear();

        Assert.False(source.TryGetRecent(MaxAge, out _));
    }

    [Fact]
    public void NothingPublishedIsNotRecent()
    {
        RecentProcessTotals source = new(() => 1_000);

        Assert.False(source.TryGetRecent(MaxAge, out _));
    }

    [Fact]
    public void SnapshotCaptureRecordsTotalsOverEveryProcessEntry()
    {
        using SystemProcessSnapshot snapshot = new();
        Dictionary<int, SystemProcessData> processes = [];

        Assert.True(snapshot.TryCapture(processes));
        SystemProcessTotals captured = snapshot.LastCaptureTotals;

        Assert.True(captured.ProcessCount >= processes.Count);
        Assert.True(captured.ThreadCount > 0);
        Assert.True(captured.HandleCount > 0);
    }
}

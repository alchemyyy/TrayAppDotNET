using Avalonia.Threading;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class FlyoutRebuildSchedulerTests
{
    [Fact]
    public void AFailedRebuildStaysPendingAndTheNextFlushRetriesItOnce() => AvaloniaTestHost.Run(() =>
    {
        Harness harness = new() { FailuresLeft = 1 };

        Assert.False(harness.Scheduler.RebuildNow());
        Assert.True(harness.Scheduler.IsPending);
        Assert.Equal(expected: 1, harness.Attempts);

        // The failure itself schedules nothing, so a rebuild that keeps failing cannot spin the dispatcher
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 1, harness.Attempts);

        harness.Scheduler.Flush();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 2, harness.Attempts);
        Assert.Equal(expected: 1, harness.Commits);
        Assert.False(harness.Scheduler.IsPending);
    });

    [Fact]
    public void AQueuedRebuildThatFailsStaysPending() => AvaloniaTestHost.Run(() =>
    {
        Harness harness = new() { FailuresLeft = 1 };

        harness.Scheduler.Queue();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expected: 1, harness.Attempts);
        Assert.Equal(expected: 0, harness.Commits);
        Assert.True(harness.Scheduler.IsPending);
    });

    [Fact]
    public void AHeldRebuildWaitsAndRunsOnTheFlushAfterTheHoldEnds() => AvaloniaTestHost.Run(() =>
    {
        Harness harness = new() { IsHeld = true };

        harness.Scheduler.Queue();
        Assert.False(harness.Scheduler.RebuildNow());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 0, harness.Attempts);
        Assert.True(harness.Scheduler.IsPending);

        // A flush while the interaction still holds the content keeps waiting
        harness.Scheduler.Flush();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 0, harness.Attempts);

        harness.IsHeld = false;
        harness.Scheduler.Flush();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 1, harness.Commits);
        Assert.False(harness.Scheduler.IsPending);
    });

    [Fact]
    public void AHiddenWindowOnlyMarksItsContentStale() => AvaloniaTestHost.Run(() =>
    {
        Harness harness = new() { CanShow = false };

        harness.Scheduler.Queue();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected: 0, harness.Attempts);
        Assert.True(harness.Scheduler.IsPending);

        harness.CanShow = true;
        Assert.True(harness.Scheduler.RebuildNow());
        Assert.Equal(expected: 1, harness.Commits);
    });

    [Fact]
    public void NothingRebuildsAfterTheWindowClosed() => AvaloniaTestHost.Run(() =>
    {
        Harness harness = new() { FailuresLeft = 1 };
        Assert.False(harness.Scheduler.RebuildNow());

        harness.Scheduler.Queue();
        harness.Scheduler.Dispose();
        harness.Scheduler.Flush();
        Assert.False(harness.Scheduler.RebuildNow());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expected: 1, harness.Attempts);
        Assert.False(harness.Scheduler.IsPending);
    });

    private sealed class Harness
    {
        public Harness() =>
            Scheduler = new FlyoutRebuildScheduler(
                nameof(FlyoutRebuildSchedulerTests),
                Rebuild,
                () => CanShow,
                () => IsHeld,
                () => Commits++);

        public FlyoutRebuildScheduler Scheduler { get; }
        public bool CanShow { get; set; } = true;
        public bool IsHeld { get; set; }
        public int FailuresLeft { get; set; }
        public int Attempts { get; private set; }
        public int Commits { get; private set; }

        private void Rebuild()
        {
            Attempts++;
            if (FailuresLeft <= 0) return;

            FailuresLeft--;
            throw new InvalidOperationException("Simulated build failure.");
        }
    }
}

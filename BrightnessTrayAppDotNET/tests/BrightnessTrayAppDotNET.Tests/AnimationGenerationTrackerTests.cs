using BrightnessTrayAppDotNET.UI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class AnimationGenerationTrackerTests
{
    [Fact]
    public void RestartedAnimationRejectsOldCallbackWithoutConsumingNewFrame()
    {
        AnimationGenerationTracker tracker = new();
        long firstGeneration = tracker.Start();
        Assert.True(tracker.TryQueue(firstGeneration));

        long secondGeneration = tracker.Start();
        Assert.True(tracker.TryQueue(secondGeneration));

        Assert.False(tracker.TryConsume(firstGeneration));
        Assert.True(tracker.TryConsume(secondGeneration));
    }

    [Fact]
    public void InvalidationRejectsQueuedCallback()
    {
        AnimationGenerationTracker tracker = new();
        long generation = tracker.Start();
        Assert.True(tracker.TryQueue(generation));

        tracker.Invalidate();

        Assert.False(tracker.TryConsume(generation));
        Assert.False(tracker.TryQueue(generation));
    }

    [Fact]
    public void CurrentGenerationAllowsOnlyOneOutstandingFrame()
    {
        AnimationGenerationTracker tracker = new();
        long generation = tracker.Start();

        Assert.True(tracker.TryQueue(generation));
        Assert.False(tracker.TryQueue(generation));
        Assert.True(tracker.TryConsume(generation));
        Assert.True(tracker.TryQueue(generation));
    }
}

using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class RetryableInitializationTests
{
    private const int RetryBaseMs = 1_000;
    private const int RetryMaxMs = 8_000;

    [Fact]
    public async Task CallersDuringInitializationShareTheRunningAttemptWithoutBlocking()
    {
        TaskCompletionSource<bool> helperReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        RetryableInitialization initialization = new(
            () =>
            {
                Interlocked.Increment(ref attempts);
                return helperReady.Task;
            },
            static () => 0,
            RetryBaseMs,
            RetryMaxMs,
            ownerName: "test");

        Task<bool>? first = initialization.EnsureStarted();
        Task<bool>? queuedWhileStarting = initialization.EnsureStarted();

        Assert.NotNull(first);
        Assert.Same(first, queuedWhileStarting);
        Assert.False(first.IsCompleted);

        helperReady.SetResult(true);
        Assert.True(await first);
        Assert.Same(first, initialization.EnsureStarted());
        Assert.Equal(expected: 1, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task FailedStartIsRetriedOnlyAfterTheBackoff()
    {
        long now = 0;
        int attempts = 0;
        RetryableInitialization initialization = new(
            () => Task.FromResult(Interlocked.Increment(ref attempts) > 1),
            () => Volatile.Read(ref now),
            RetryBaseMs,
            RetryMaxMs,
            ownerName: "test");

        Task<bool>? failed = initialization.EnsureStarted();
        Assert.NotNull(failed);
        Assert.False(await failed);

        Volatile.Write(ref now, RetryBaseMs - 1);
        Assert.Null(initialization.EnsureStarted());
        Assert.True(initialization.IsAvailable);

        Volatile.Write(ref now, RetryBaseMs);
        Task<bool>? retried = initialization.EnsureStarted();
        Assert.NotNull(retried);
        Assert.True(await retried);
        Assert.Equal(expected: 2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task PermanentFailureIsNeverRetried()
    {
        long now = 0;
        RetryableInitialization initialization = null!;
        initialization = new RetryableInitialization(
            () =>
            {
                initialization.MarkPermanentlyFailed();
                return Task.FromResult(false);
            },
            () => Volatile.Read(ref now),
            RetryBaseMs,
            RetryMaxMs,
            ownerName: "test");

        Task<bool>? unsupported = initialization.EnsureStarted();
        Assert.NotNull(unsupported);
        Assert.False(await unsupported);

        Volatile.Write(ref now, long.MaxValue / 2);
        Assert.Null(initialization.EnsureStarted());
        Assert.False(initialization.IsAvailable);
    }

    [Fact]
    public void ClosedInitializationStartsNothing()
    {
        RetryableInitialization initialization = new(
            static () => Task.FromResult(true),
            static () => 0,
            RetryBaseMs,
            RetryMaxMs,
            ownerName: "test");

        initialization.Close();

        Assert.Null(initialization.EnsureStarted());
        Assert.False(initialization.HasStarted);
        Assert.False(initialization.IsAvailable);
    }

    [Theory]
    [InlineData(1, 1_000)]
    [InlineData(2, 2_000)]
    [InlineData(4, 8_000)]
    [InlineData(5, 8_000)]
    [InlineData(64, 8_000)]
    public void BackoffDoublesUpToTheMaximum(int failedAttempts, int expectedDelayMs) =>
        Assert.Equal(
            expectedDelayMs,
            RetryableInitialization.GetRetryDelayMs(failedAttempts, RetryBaseMs, RetryMaxMs));
}

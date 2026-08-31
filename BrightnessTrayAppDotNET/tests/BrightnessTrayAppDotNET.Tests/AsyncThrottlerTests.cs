using TrayAppDotNETCommon.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class AsyncThrottlerTests
{
    [Fact]
    public async Task DrainAsyncPropagatesCancellationWhilePayloadIsRunning()
    {
        using AsyncThrottler<string> throttler = new(cooldownMs: 0, StringComparer.Ordinal, drainPollIntervalMs: 1);
        TaskCompletionSource payloadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releasePayload = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task payloadCompletion = throttler.RunAsync(key: "monitor", async context =>
        {
            payloadEntered.TrySetResult();
            await releasePayload.Task.ConfigureAwait(false);
        });
        await payloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            throttler.DrainAsync(cancellationTokenSource.Token));

        releasePayload.TrySetResult();
        // The test token is intentionally canceled before this cleanup wait
        // ReSharper disable once MethodSupportsCancellation
        await payloadCompletion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CooldownOverrideDelaysOnlyItsOwnKey()
    {
        using AsyncThrottler<string> throttler = new(cooldownMs: 0, StringComparer.Ordinal, drainPollIntervalMs: 1);
        Task firstSlowPayload = throttler.RunAsync(
            key: "HDMI",
            context => Task.CompletedTask,
            cooldownOverrideMs: 500);
        await firstSlowPayload.WaitAsync(TimeSpan.FromSeconds(1));

        bool secondSlowPayloadRan = false;
        Task secondSlowPayload = throttler.RunAsync(key: "HDMI", context =>
        {
            secondSlowPayloadRan = true;
            return Task.CompletedTask;
        });
        Task fastPayload = throttler.RunAsync(key: "DisplayPort", context => Task.CompletedTask);

        await fastPayload.WaitAsync(TimeSpan.FromMilliseconds(300));
        Assert.False(secondSlowPayloadRan);

        await secondSlowPayload.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(secondSlowPayloadRan);
    }
}

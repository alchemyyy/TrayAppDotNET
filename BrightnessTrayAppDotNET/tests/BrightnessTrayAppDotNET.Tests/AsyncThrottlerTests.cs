using TrayAppDotNETCommon.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class AsyncThrottlerTests
{
    [Fact]
    public async Task DrainAsyncPropagatesCancellationWhilePayloadIsRunning()
    {
        using AsyncThrottler<string> throttler = new(0, StringComparer.Ordinal, drainPollIntervalMs: 1);
        TaskCompletionSource payloadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releasePayload = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task payloadCompletion = throttler.RunAsync("monitor", async context =>
        {
            payloadEntered.TrySetResult();
            await releasePayload.Task.ConfigureAwait(false);
        });
        await payloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            throttler.DrainAsync(cancellationTokenSource.Token));

        releasePayload.TrySetResult();
        await payloadCompletion.WaitAsync(TimeSpan.FromSeconds(1));
    }
}

using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class AsyncThrottlerDisposeTests
{
    [Fact]
    public async Task EveryCallerCompletesWhenDisposeRacesTheDrivers()
    {
        for (int iteration = 0; iteration < 300; iteration++)
        {
            AsyncThrottler<int> throttler = new(cooldownMs: 0);
            using CancellationTokenSource external = new();
            using ManualResetEventSlim start = new();
            List<Task> callers = [];

            // A cancellable caller token makes the driver link it with the shutdown token after dequeuing
            Task producer = Task.Run(() =>
            {
                start.Wait();
                for (int key = 0; key < 8; key++)
                    callers.Add(throttler.RunAsync(key, static _ => Task.CompletedTask, external.Token));
            });
            Task disposer = Task.Run(() =>
            {
                start.Wait();
                throttler.Dispose();
            });

            start.Set();
            await Task.WhenAll(producer, disposer).WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task APayloadQueuedBeforeDisposeStillCompletesItsCaller()
    {
        AsyncThrottler<string> throttler = new(cooldownMs: 0);
        using SemaphoreSlim running = new(initialCount: 0);
        using SemaphoreSlim release = new(initialCount: 0);
        using CancellationTokenSource external = new();

        Task first = throttler.RunAsync(
            key: "a",
            async _ =>
            {
                running.Release();
                await release.WaitAsync();
            },
            external.Token);
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Task replacement = throttler.RunAsync(key: "a", static _ => Task.CompletedTask, external.Token);

        throttler.Dispose();
        release.Release();

        await Task.WhenAll(first, replacement).WaitAsync(TimeSpan.FromSeconds(10));
    }
}

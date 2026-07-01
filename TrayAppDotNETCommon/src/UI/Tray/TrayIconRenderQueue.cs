using Avalonia.Threading;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>
/// Runs tray icon rendering off the UI thread with latest-request-wins semantics.
/// </summary>
public sealed class TrayIconRenderQueue(Action<string>? log = null) : IDisposable
{
    private const string RenderKey = "tray-icon";

    private readonly AsyncThrottler<string> _throttler = new(cooldownMs: 0);
    private int _requestVersion;
    private volatile bool _disposed;

    /// <summary>
    /// Queues a render operation and applies the completed icon on the UI thread.
    /// </summary>
    public void Request(Func<NativeIcon?> render, Action<NativeIcon> apply)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(apply);
        if (_disposed) return;

        int requestVersion = Interlocked.Increment(ref _requestVersion);
        _ = _throttler.RunAsync(RenderKey, context => RunRenderAsync(requestVersion, render, apply, context));
    }

    private async Task RunRenderAsync(
        int requestVersion,
        Func<NativeIcon?> render,
        Action<NativeIcon> apply,
        ThrottlerContext context)
    {
        if (ShouldDrop(requestVersion, context)) return;

        NativeIcon? icon = null;
        try
        {
            icon = render();
        }
        catch (Exception ex)
        {
            log?.Invoke($"TrayIconRenderQueue.Render: {ex.Message}");
        }

        if (icon == null) return;

        if (ShouldDrop(requestVersion, context))
        {
            icon.Dispose();
            return;
        }

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (ShouldDrop(requestVersion, context))
                {
                    icon.Dispose();
                    return;
                }

                apply(icon);
            }
            catch (Exception ex)
            {
                icon.Dispose();
                log?.Invoke($"TrayIconRenderQueue.Apply: {ex.Message}");
            }
            finally
            {
                completionSource.TrySetResult();
            }
        }, DispatcherPriority.Background);

        await completionSource.Task.ConfigureAwait(false);
    }

    private bool ShouldDrop(int requestVersion, ThrottlerContext context) =>
        _disposed
        || context.CancellationToken.IsCancellationRequested
        || context.HasReplacement
        || Volatile.Read(ref _requestVersion) != requestVersion;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _requestVersion);
        _throttler.Drop(RenderKey);
        _throttler.Dispose();
    }
}

using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Coalesces content rebuilds of a flyout. A rebuild asked for while the window cannot show it (hidden and not
/// priming) only marks the content stale; one asked for while an interaction holds the content (a drag, an open
/// menu, an inline editor) or while a rebuild runs is kept pending until <see cref="Flush"/>. A rebuild that throws is
/// logged, leaves the previous content in place and stays pending; the next <see cref="Flush"/>, queue or show retries
/// it, never the failure itself, so a rebuild that keeps failing cannot spin the dispatcher.
/// </summary>
public sealed class FlyoutRebuildScheduler : IDisposable
{
    private readonly string _ownerName;
    private readonly Action _rebuild;
    private readonly Func<bool> _canShowRebuild;
    private readonly Func<bool> _isHeld;
    private readonly Action? _committed;
    private bool _pending;
    private bool _queued;
    private bool _disposed;

    /// <param name="ownerName">Name used in log lines.</param>
    /// <param name="rebuild">Builds and commits a replacement generation; throws to keep the current one.</param>
    /// <param name="canShowRebuild">Whether a rebuild can reach the screen now.</param>
    /// <param name="isHeld">Whether an interaction holds the current content.</param>
    /// <param name="committed">Runs after each successful rebuild.</param>
    public FlyoutRebuildScheduler(
        string ownerName,
        Action rebuild,
        Func<bool> canShowRebuild,
        Func<bool> isHeld,
        Action? committed = null)
    {
        _ownerName = ownerName ?? throw new ArgumentNullException(nameof(ownerName));
        _rebuild = rebuild ?? throw new ArgumentNullException(nameof(rebuild));
        _canShowRebuild = canShowRebuild ?? throw new ArgumentNullException(nameof(canShowRebuild));
        _isHeld = isHeld ?? throw new ArgumentNullException(nameof(isHeld));
        _committed = committed;
    }

    /// <summary>Gets whether a rebuild is running.</summary>
    public bool IsRebuilding { get; private set; }

    /// <summary>Gets whether a rebuild was asked for and has not run yet.</summary>
    public bool IsPending => _pending || _queued;

    /// <summary>Queues one coalesced rebuild; callable from any thread.</summary>
    public void Queue()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Queue, DispatcherPriority.Background);
            return;
        }

        if (!_canShowRebuild() || IsRebuilding || _isHeld())
        {
            _pending = true;
            return;
        }

        if (_queued) return;

        _queued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                // A synchronous rebuild since queueing, such as a show, already published this change
                if (_disposed || !_queued) return;

                _queued = false;
                RebuildNow();
            },
            DispatcherPriority.Background);
    }

    /// <summary>Rebuilds synchronously; returns whether a replacement was committed.</summary>
    public bool RebuildNow()
    {
        if (_disposed) return false;
        if (IsRebuilding || !_canShowRebuild() || _isHeld())
        {
            _pending = true;
            return false;
        }

        _queued = false;
        _pending = false;
        IsRebuilding = true;
        bool committed = false;
        try
        {
            _rebuild();
            committed = true;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{_ownerName}.Rebuild: {exception}");
        }
        finally
        {
            IsRebuilding = false;
        }

        if (!committed)
        {
            // The stale content stays on screen, so the request survives for the next flush, queue or show
            if (!_disposed) _pending = true;
            return false;
        }

        _committed?.Invoke();
        Flush();
        return true;
    }

    /// <summary>Queues the pending rebuild once nothing holds the content any more.</summary>
    public void Flush()
    {
        if (_disposed || !_pending || IsRebuilding || _isHeld() || !_canShowRebuild()) return;

        _pending = false;
        Queue();
    }

    public void Dispose()
    {
        _disposed = true;
        _pending = false;
        _queued = false;
    }
}

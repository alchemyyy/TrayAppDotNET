namespace TrayAppDotNETCommon.Services;

/// <summary>
/// Ends the app when its crash watcher process dies. Waits on the watcher's process handle through
/// <see cref="ProcessExitWatch"/>, so nothing wakes up until the watcher actually exits.
/// </summary>
public sealed class WatcherMonitor(
    int? watcherPid,
    Func<Action, Task> postToUiThread,
    Action onWatcherDied)
    : IDisposable
{
    private readonly Func<Action, Task> _postToUIThread =
        postToUiThread ?? throw new ArgumentNullException(nameof(postToUiThread));

    private readonly Action _onWatcherDied = onWatcherDied ?? throw new ArgumentNullException(nameof(onWatcherDied));

    private IDisposable? _registration;
    private bool _disposed;

    public bool IsRunning => Volatile.Read(ref _registration) != null;

    public void Start()
    {
        if (_disposed) return;
        if (watcherPid is not { } watcherPID) return;
        if (IsRunning) return;

        switch (ProcessExitWatch.TryRegister(watcherPID, expectedStartFileTime: null, OnWatcherExited, out IDisposable? registration))
        {
            case ProcessExitWatchResult.Watching:
                Volatile.Write(ref _registration, registration);
                break;
            case ProcessExitWatchResult.Exited:
                // Start runs on the UI thread during startup; the death callback must arrive as a later posted job,
                // after startup has finished, never inline in the middle of it
                _ = Task.Run(PostWatcherDiedAsync);
                break;
            default:
                TADNLog.Log($"WatcherMonitor: watcher PID {watcherPID} cannot be watched; the app keeps running without it.");
                break;
        }
    }

    public void Stop() => Interlocked.Exchange(ref _registration, value: null)?.Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void OnWatcherExited() => _ = PostWatcherDiedAsync();

    private async Task PostWatcherDiedAsync()
    {
        try
        {
            await _postToUIThread(_onWatcherDied).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"WatcherMonitor: watcher-death handling failed: {exception}");
        }
    }
}

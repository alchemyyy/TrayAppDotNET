using System.Diagnostics;

namespace TrayAppDotNETCommon.Services;

public sealed class WatcherMonitor(
    int? watcherPid,
    Func<Action, Task> postToUiThread,
    Action onWatcherDied,
    TimeSpan? pollInterval = null)
    : IDisposable
{
    private readonly Func<Action, Task> _postToUIThread =
        postToUiThread ?? throw new ArgumentNullException(nameof(postToUiThread));

    private readonly Action _onWatcherDied = onWatcherDied ?? throw new ArgumentNullException(nameof(onWatcherDied));
    private readonly TimeSpan _pollInterval =
        pollInterval ?? TimeSpan.FromMilliseconds(TimeConstants.WatcherLivenessPollIntervalMs);
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsRunning => _cts != null;

    public void Start()
    {
        if (_disposed) return;
        if (watcherPid is not { } watcherPID) return;
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                using Process watcherProcess = Process.GetProcessById(watcherPID);

                while (!token.IsCancellationRequested)
                {
                    if (watcherProcess.HasExited)
                    {
                        await _postToUIThread(_onWatcherDied).ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(_pollInterval, token).ConfigureAwait(false);
                }
            }
            catch (ArgumentException)
            {
                await _postToUIThread(_onWatcherDied).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TADNLog.Log($"WatcherMonitor poll loop: {ex.Message}");
            }
        }, token);
    }

    public void Stop()
    {
        CancellationTokenSource? cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null) return;

        try { cts.Cancel(); }
        catch (Exception ex) { TADNLog.Log($"WatcherMonitor.Stop: cancel: {ex.Message}"); }

        cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

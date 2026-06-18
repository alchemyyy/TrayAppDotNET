namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Event-triggered final fallback for DDC acquisition failures.
/// Healthy state is fully event-driven and no worker runs. When MonitorService reports a failed or
/// read-degraded known-DDC row, this service sets one global DDC recovery flag and starts a single
/// background loop. The loop performs targeted handle refresh/re-probe attempts every two seconds
/// until no stuck DDC candidates remain.
/// </summary>
public sealed class DDCRecoveryService(MonitorService monitorService) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Lock _candidateLogLock = new();
    private readonly HashSet<string> _lastCandidateSet = new(StringComparer.Ordinal);

    private CancellationTokenSource? _workerCts;
    private Task? _worker;
    private int _DDCRecoveryNeeded;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Starts listening for failed/read-degraded DDC rows.
    /// This does not start the retry worker unless candidates already exist.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed) return;

        _started = true;
        monitorService.MonitorsRefreshed += OnMonitorsRefreshed;

        if (GetDDCRecoveryCandidateIDs().Count > 0) SignalDDCRecoveryNeeded();
    }

    private void OnMonitorsRefreshed()
    {
        if (_disposed) return;

        List<string> candidates = GetDDCRecoveryCandidateIDs();
        LogCandidateTransitions(candidates);

        if (candidates.Count > 0)
            SignalDDCRecoveryNeeded();
        else
            ClearDDCRecoveryNeeded();
    }

    /// <summary>
    /// Signals that at least one DDC row needs acquisition retry and starts the single global worker
    /// if it is not already running.
    /// </summary>
    public void SignalDDCRecoveryNeeded()
    {
        if (_disposed) return;

        Interlocked.Exchange(ref _DDCRecoveryNeeded, 1);

        lock (_gate)
        {
            if (_disposed) return;
            if (_worker is { IsCompleted: false }) return;

            _workerCts?.Dispose();
            _workerCts = new CancellationTokenSource();
            _worker = Task.Run(() => RunDDCRecoveryWorkerAsync(_workerCts.Token));
        }
    }

    private void ClearDDCRecoveryNeeded() => Interlocked.Exchange(ref _DDCRecoveryNeeded, 0);

    private async Task RunDDCRecoveryWorkerAsync(CancellationToken token)
    {
        WPFLog.Log("DDCRecoveryService: fallback worker starting");

        try
        {
            while (!token.IsCancellationRequested && Volatile.Read(ref _DDCRecoveryNeeded) == 1)
            {
                await Task.Delay(TimeConstants.DDCRecoveryRetryIntervalMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) break;

                List<string> candidates = GetDDCRecoveryCandidateIDs();
                if (candidates.Count == 0)
                {
                    ClearDDCRecoveryNeeded();
                    break;
                }

                WPFLog.Log(
                    $"DDCRecoveryService: acquisition retry for {candidates.Count} candidate(s): "
                    + string.Join(", ", candidates));

                await RunDDCAcquisitionPassAsync(candidates, token).ConfigureAwait(false);

                if (GetDDCRecoveryCandidateIDs().Count == 0)
                {
                    ClearDDCRecoveryNeeded();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/dispose path.
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCRecoveryService.RunDDCRecoveryWorkerAsync: {ex.Message}");
            if (!_disposed && GetDDCRecoveryCandidateIDs().Count > 0)
                SignalDDCRecoveryNeeded();
        }
        finally
        {
            lock (_gate)
                _worker = null;

            WPFLog.Log("DDCRecoveryService: fallback worker stopped");

            if (!_disposed
                && Volatile.Read(ref _DDCRecoveryNeeded) == 1
                && GetDDCRecoveryCandidateIDs().Count > 0)
                SignalDDCRecoveryNeeded();
        }
    }

    private async Task RunDDCAcquisitionPassAsync(List<string> candidates, CancellationToken token)
    {
        foreach (string id in candidates)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                bool recovered = await Task.Run(
                        () => monitorService.TryRecoverMonitor(id, DDCRecoveryAction.RefreshHandle),
                        token)
                    .ConfigureAwait(false);
                WPFLog.Log($"DDCRecoveryService: targeted retry '{id}' result={recovered}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WPFLog.Log($"DDCRecoveryService: targeted retry '{id}' failed: {ex.Message}");
            }
        }
    }

    private List<string> GetDDCRecoveryCandidateIDs()
    {
        try { return monitorService.GetStuckRecoveryCandidateIDs(); }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCRecoveryService: candidate snapshot failed: {ex.Message}");
            return [];
        }
    }

    private void LogCandidateTransitions(List<string> currentIDs)
    {
        HashSet<string> currentSet = new(currentIDs, StringComparer.Ordinal);

        lock (_candidateLogLock)
        {
            foreach (string id in currentIDs)
            {
                if (!_lastCandidateSet.Contains(id))
                    WPFLog.Log($"DDCRecoveryService: candidate added '{id}'");
            }

            foreach (string id in _lastCandidateSet)
            {
                if (!currentSet.Contains(id))
                    WPFLog.Log($"DDCRecoveryService: candidate dropped '{id}'");
            }

            _lastCandidateSet.Clear();
            foreach (string id in currentIDs) _lastCandidateSet.Add(id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        ClearDDCRecoveryNeeded();

        if (_started) monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;

        lock (_gate)
        {
            _workerCts?.Cancel();
            _workerCts?.Dispose();
            _workerCts = null;
            _worker = null;
        }

        lock (_candidateLogLock)
            _lastCandidateSet.Clear();
    }
}

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Event-triggered final fallback for DDC acquisition failures.
/// Healthy state is fully event-driven and no worker runs. When MonitorService reports a failed or
/// read-degraded known-DDC row, this service sets one global DDC recovery flag and starts a single
/// background loop. The loop performs one immediate targeted pass, then fresh-enumeration/re-probe attempts every
/// two seconds while candidates remain. Independent monitor candidates run concurrently; healthy rows are never
/// swept as collateral recovery traffic.
/// </summary>
public sealed class DDCRecoveryService(
    MonitorService monitorService,
    int retryIntervalMs = TimeConstants.DDCRecoveryRetryIntervalMs) : IDisposable
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
        monitorService.DDCRecoveryRequested += OnDDCRecoveryRequested;
        WPFLog.Log("DDCRecoveryService: started");

        if (TryGetDDCRecoveryCandidateIDs(out List<string> candidates) && candidates.Count > 0)
            SignalDDCRecoveryNeeded();
    }

    private void OnMonitorsRefreshed()
    {
        if (_disposed) return;

        if (!TryGetDDCRecoveryCandidateIDs(out List<string> candidates)) return;
        LogCandidateTransitions(candidates);

        if (candidates.Count > 0)
            SignalDDCRecoveryNeeded();
        else if (ClearDDCRecoveryNeeded())
            WPFLog.Log("DDCRecoveryService: refresh found no eligible candidates; clearing recovery request");
    }

    private void OnDDCRecoveryRequested(string monitorID)
    {
        if (_disposed) return;

        WPFLog.Log($"DDCRecoveryService: direct recovery request '{monitorID}'");
        SignalDDCRecoveryNeeded();
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

    private bool ClearDDCRecoveryNeeded() => Interlocked.Exchange(ref _DDCRecoveryNeeded, 0) == 1;

    private async Task RunDDCRecoveryWorkerAsync(CancellationToken token)
    {
        WPFLog.Log("DDCRecoveryService: fallback worker starting");

        try
        {
            bool firstPass = true;
            while (!token.IsCancellationRequested && Volatile.Read(ref _DDCRecoveryNeeded) == 1)
            {
                if (!firstPass)
                {
                    await Task.Delay(Math.Max(1, retryIntervalMs), token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;
                }

                firstPass = false;

                if (!TryGetDDCRecoveryCandidateIDs(out List<string> candidates)) continue;
                if (candidates.Count == 0)
                {
                    WPFLog.Log("DDCRecoveryService: no eligible candidates; clearing recovery request");
                    ClearDDCRecoveryNeeded();
                    break;
                }

                WPFLog.Log(
                    $"DDCRecoveryService: acquisition retry for {candidates.Count} candidate(s): "
                    + string.Join(", ", candidates));

                await RunTargetedRecoveryPassAsync(candidates, token).ConfigureAwait(false);

                if (!TryGetDDCRecoveryCandidateIDs(out List<string> remainingCandidates)) continue;
                if (remainingCandidates.Count == 0)
                {
                    WPFLog.Log("DDCRecoveryService: all candidates recovered; clearing recovery request");
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
        }
        finally
        {
            lock (_gate)
                _worker = null;

            WPFLog.Log("DDCRecoveryService: fallback worker stopped");

            if (!_disposed
                && Volatile.Read(ref _DDCRecoveryNeeded) == 1)
                SignalDDCRecoveryNeeded();
        }
    }

    private async Task RunTargetedRecoveryPassAsync(List<string> candidates, CancellationToken token)
    {
        List<Task> recoveryTasks = [];
        foreach (string id in candidates.Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            recoveryTasks.Add(Task.Run(() => RecoverCandidate(id), token));
        }

        await Task.WhenAll(recoveryTasks).ConfigureAwait(false);

        return;

        void RecoverCandidate(string id)
        {
            try
            {
                bool recovered = monitorService.TryRecoverMonitor(id);
                WPFLog.Log($"DDCRecoveryService: targeted retry '{id}' result={recovered}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WPFLog.Log($"DDCRecoveryService: targeted retry '{id}' failed: {ex.Message}");
            }
        }
    }

    private bool TryGetDDCRecoveryCandidateIDs(out List<string> candidates)
    {
        try
        {
            candidates = monitorService.GetStuckRecoveryCandidateIDs();
            return true;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCRecoveryService: candidate snapshot failed: {ex.Message}");
            candidates = [];
            return false;
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

        if (_started)
        {
            monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
            monitorService.DDCRecoveryRequested -= OnDDCRecoveryRequested;
        }

        CancellationTokenSource? workerCts;
        Task? worker;
        lock (_gate)
        {
            workerCts = _workerCts;
            worker = _worker;
            workerCts?.Cancel();
        }

        DrainWorker(worker);

        lock (_gate)
        {
            if (ReferenceEquals(_workerCts, workerCts))
            {
                _workerCts = null;
                _worker = null;
            }
        }

        workerCts?.Dispose();

        lock (_candidateLogLock)
            _lastCandidateSet.Clear();
    }

    private static void DrainWorker(Task? worker)
    {
        if (worker == null) return;

        try
        {
            bool completed = worker.Wait(TimeSpan.FromMilliseconds(TimeConstants.DDCRecoveryShutdownDrainTimeoutMs));
            if (!completed)
                WPFLog.Log("DDCRecoveryService: fallback worker did not stop before shutdown drain timeout");
        }
        catch (AggregateException ex)
        {
            foreach (Exception inner in ex.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException) continue;

                WPFLog.Log($"DDCRecoveryService.DrainWorker: {inner.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/dispose path.
        }
    }
}

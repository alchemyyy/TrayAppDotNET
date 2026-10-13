namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Event-triggered final fallback for DDC acquisition failures.
/// Healthy state is fully event-driven and no worker runs. When MonitorService reports an unavailable or
/// read-degraded row, this service sets one global DDC recovery flag and starts a single background loop. The loop
/// performs one immediate targeted pass, then sleeps until the next candidate is due. Each candidate is probed at the
/// base interval for its first few failures, then its interval doubles up to a maximum, so a dark panel or one
/// without DDC/CI costs little bus time. A direct recovery request (a fresh failure, or the flyout opening) wakes the
/// loop and makes its candidate due at once while keeping its failure count. A row that fails again shortly after
/// recovering continues its streak instead, so a panel that answers reads but drops its writes cannot cycle through
/// demotion and recovery several times a second. Independent candidates run concurrently; healthy rows are never
/// swept as collateral recovery traffic.
/// </summary>
public sealed class DDCRecoveryService(
    MonitorService monitorService,
    int retryIntervalMs = TimeConstants.DDCRecoveryRetryIntervalMs,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Lock _candidateLogLock = new();
    private readonly HashSet<string> _lastCandidateSet = new(StringComparer.Ordinal);

    // Failed probes per candidate, how many times in a row it failed again right after recovering, and when it is
    // next due, guarded by _gate. Recovered candidates and rows that stop being candidates leave the map.
    private readonly Dictionary<string, (int Failures, int Relapses, DateTimeOffset NextAttempt)> _backoffs =
        new(StringComparer.Ordinal);

    // Candidates that recovered within the relapse window, with their relapse count, guarded by _gate
    private readonly Dictionary<string, (int Relapses, DateTimeOffset RecoveredAt)> _recentRecoveries =
        new(StringComparer.Ordinal);

    private CancellationTokenSource? _workerCts;

    // Cancels the worker's current sleep so a request is served at once; the flag covers a request that arrives
    // before the worker starts sleeping. Both guarded by _gate
    private CancellationTokenSource? _wakeCts;
    private bool _wakeRequested;
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
        TADNLog.Log("DDCRecoveryService: started");

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
            TADNLog.Log("DDCRecoveryService: refresh found no eligible candidates; clearing recovery request");
    }

    private void OnDDCRecoveryRequested(string monitorID)
    {
        if (_disposed) return;

        // Due now, but the failure count stands: opening the flyout must not restart a dark panel's backoff.
        // A row that just recovered and failed again waits out its next step instead
        lock (_gate)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_backoffs.TryGetValue(monitorID, out (int Failures, int Relapses, DateTimeOffset NextAttempt) backoff))
                _backoffs[monitorID] = (backoff.Failures, backoff.Relapses, now);
            else
                TryScheduleRelapseLocked(monitorID, now);
        }

        TADNLog.Log($"DDCRecoveryService: direct recovery request '{monitorID}'");
        SignalDDCRecoveryNeeded();
    }

    /// <summary>
    /// Signals that at least one DDC row needs acquisition retry. Starts the single global worker if it is not
    /// running, or wakes it from its sleep.
    /// </summary>
    public void SignalDDCRecoveryNeeded()
    {
        if (_disposed) return;

        Interlocked.Exchange(ref _DDCRecoveryNeeded, value: 1);

        CancellationTokenSource? sleepToEnd;
        lock (_gate)
        {
            if (_disposed) return;
            if (_worker is not { IsCompleted: false })
            {
                _workerCts?.Dispose();
                _workerCts = new CancellationTokenSource();
                _worker = Task.Run(() => RunDDCRecoveryWorkerAsync(_workerCts.Token));
                return;
            }

            _wakeRequested = true;
            sleepToEnd = _wakeCts;
        }

        // Outside the gate: the woken worker may continue inline on this thread and take the gate itself
        try { sleepToEnd?.Cancel(); }
        catch (ObjectDisposedException)
        {
            // The worker finished that sleep on its own and the wake flag still stands
        }
    }

    /// <summary>
    /// Sleeps until the earliest candidate backoff elapses, or one base interval when none is pending, and returns
    /// early when a request wakes the worker.
    /// </summary>
    private async Task SleepUntilNextDueAsync(CancellationToken token)
    {
        using CancellationTokenSource wake = CancellationTokenSource.CreateLinkedTokenSource(token);
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(val1: 1, retryIntervalMs));
        lock (_gate)
        {
            if (_wakeRequested)
            {
                _wakeRequested = false;
                return;
            }

            if (_backoffs.Count > 0)
            {
                TimeSpan untilEarliest = _backoffs.Values.Min(backoff => backoff.NextAttempt) - _timeProvider.GetUtcNow();
                delay = untilEarliest > TimeSpan.FromMilliseconds(1) ? untilEarliest : TimeSpan.FromMilliseconds(1);
            }

            _wakeCts = wake;
        }

        try
        {
            await Task.Delay(delay, _timeProvider, wake.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Woken by a recovery request
        }
        finally
        {
            lock (_gate)
            {
                _wakeRequested = false;
                if (ReferenceEquals(_wakeCts, wake)) _wakeCts = null;
            }
        }
    }

    private bool ClearDDCRecoveryNeeded() => Interlocked.Exchange(ref _DDCRecoveryNeeded, value: 0) == 1;

    private async Task RunDDCRecoveryWorkerAsync(CancellationToken token)
    {
        TADNLog.Log("DDCRecoveryService: fallback worker starting");

        try
        {
            bool firstPass = true;
            while (!token.IsCancellationRequested && Volatile.Read(ref _DDCRecoveryNeeded) == 1)
            {
                if (!firstPass)
                {
                    await SleepUntilNextDueAsync(token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;
                }

                firstPass = false;

                if (!TryGetDDCRecoveryCandidateIDs(out List<string> candidates)) continue;
                if (candidates.Count == 0)
                {
                    TADNLog.Log("DDCRecoveryService: no eligible candidates; clearing recovery request");
                    ClearDDCRecoveryNeeded();
                    break;
                }

                List<string> dueCandidates = TakeDueCandidates(candidates);
                if (dueCandidates.Count == 0) continue;

                TADNLog.Log(
                    $"DDCRecoveryService: acquisition retry for {dueCandidates.Count} of {candidates.Count} "
                    + "candidate(s): " + string.Join(separator: ", ", dueCandidates));

                await RunTargetedRecoveryPassAsync(dueCandidates, token).ConfigureAwait(false);

                if (!TryGetDDCRecoveryCandidateIDs(out List<string> remainingCandidates)) continue;
                if (remainingCandidates.Count == 0)
                {
                    TADNLog.Log("DDCRecoveryService: all candidates recovered; clearing recovery request");
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
            TADNLog.Log($"DDCRecoveryService.RunDDCRecoveryWorkerAsync: {ex.Message}");
        }
        finally
        {
            lock (_gate)
                _worker = null;

            TADNLog.Log("DDCRecoveryService: fallback worker stopped");

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
            bool recovered = false;
            try
            {
                recovered = monitorService.TryRecoverMonitor(id);
                TADNLog.Log($"DDCRecoveryService: targeted retry '{id}' result={recovered}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TADNLog.Log($"DDCRecoveryService: targeted retry '{id}' failed: {ex.Message}");
            }
            finally
            {
                RecordAttempt(id, recovered);
            }
        }
    }

    /// <summary>
    /// Returns the candidates whose backoff has elapsed, and forgets rows that are no longer candidates and recoveries
    /// older than the relapse window.
    /// </summary>
    private List<string> TakeDueCandidates(List<string> candidates)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        HashSet<string> current = new(candidates, StringComparer.Ordinal);
        List<string> due = [];
        lock (_gate)
        {
            foreach (string staleID in _backoffs.Keys.Where(id => !current.Contains(id)).ToList())
                _backoffs.Remove(staleID);

            foreach (string settledID in _recentRecoveries
                         .Where(pair => now - pair.Value.RecoveredAt >= RelapseWindow)
                         .Select(pair => pair.Key)
                         .ToList())
                _recentRecoveries.Remove(settledID);

            foreach (string id in current)
            {
                // A relapse whose failure raised no recovery request is caught here
                if (!_backoffs.ContainsKey(id)) TryScheduleRelapseLocked(id, now);

                if (_backoffs.TryGetValue(id, out (int Failures, int Relapses, DateTimeOffset NextAttempt) backoff)
                    && now < backoff.NextAttempt)
                    continue;

                due.Add(id);
            }
        }

        return due;
    }

    private void RecordAttempt(string id, bool recovered)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            bool known = _backoffs.TryGetValue(id, out (int Failures, int Relapses, DateTimeOffset NextAttempt) previous);
            if (recovered)
            {
                _backoffs.Remove(id);
                _recentRecoveries[id] = (known ? previous.Relapses : 0, now);
                return;
            }

            int failures = known ? previous.Failures + 1 : 1;
            _backoffs[id] = (failures, known ? previous.Relapses : 0, now + RetryDelayAfter(failures));
        }
    }

    private static TimeSpan RelapseWindow => TimeSpan.FromMilliseconds(TimeConstants.DDCRecoveryRelapseWindowMs);

    /// <summary>
    /// Treats a candidate that failed again within <see cref="TimeConstants.DDCRecoveryRelapseWindowMs"/> of
    /// recovering as continuing its failure streak: it waits out one backoff step per relapse in a row instead of
    /// being probed at once. Returns true when it scheduled such a wait.
    /// </summary>
    private bool TryScheduleRelapseLocked(string id, DateTimeOffset now)
    {
        if (!_recentRecoveries.Remove(id, out (int Relapses, DateTimeOffset RecoveredAt) recovery)) return false;
        if (now - recovery.RecoveredAt >= RelapseWindow) return false;

        int relapses = recovery.Relapses + 1;
        _backoffs[id] = (relapses, relapses, now + RetryDelayAfter(relapses));
        TADNLog.Log(
            $"DDCRecoveryService: '{id}' failed again {(now - recovery.RecoveredAt).TotalMilliseconds:F0} ms after "
            + $"recovering (relapse {relapses}); next probe in {RetryDelayAfter(relapses).TotalMilliseconds:F0} ms");
        return true;
    }

    /// <summary>
    /// Base interval for the first <see cref="TimeConstants.DDCRecoveryFullRateAttempts"/> failures, then doubling
    /// up to <see cref="TimeConstants.DDCRecoveryMaximumRetryIntervalMs"/>.
    /// With the default 2 s base: 2, 2, 2, 4, 8, 16, 30, 30... seconds.
    /// </summary>
    private TimeSpan RetryDelayAfter(int failures)
    {
        long baseMs = Math.Max(val1: 1, retryIntervalMs);
        int doublings = Math.Clamp(failures - TimeConstants.DDCRecoveryFullRateAttempts, min: 0, max: 16);
        long maximumMs = Math.Max(baseMs, TimeConstants.DDCRecoveryMaximumRetryIntervalMs);
        return TimeSpan.FromMilliseconds(Math.Min(baseMs << doublings, maximumMs));
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
            TADNLog.Log($"DDCRecoveryService: candidate snapshot failed: {ex.Message}");
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
                    TADNLog.Log($"DDCRecoveryService: candidate added '{id}'");
            }

            foreach (string id in _lastCandidateSet)
            {
                if (!currentSet.Contains(id))
                    TADNLog.Log($"DDCRecoveryService: candidate dropped '{id}'");
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

        lock (_gate)
        {
            _backoffs.Clear();
            _recentRecoveries.Clear();
        }

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
                TADNLog.Log("DDCRecoveryService: fallback worker did not stop before shutdown drain timeout");
        }
        catch (AggregateException ex)
        {
            foreach (Exception inner in ex.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException) continue;

                TADNLog.Log($"DDCRecoveryService.DrainWorker: {inner.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/dispose path.
        }
    }
}

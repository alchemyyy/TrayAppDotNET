namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// One initialization at a time that callers never block on. Concurrent callers share the running attempt and a
/// succeeded attempt is kept. A failed attempt may be retried once a backoff has elapsed; the backoff doubles per
/// consecutive failure. A permanent failure, or <see cref="Close"/>, ends all attempts.
/// </summary>
internal sealed class RetryableInitialization(
    Func<Task<bool>> initialize,
    Func<long> getTickCount64,
    int retryBaseDelayMs,
    int retryMaxDelayMs,
    string ownerName)
{
    private readonly Lock _gate = new();
    private Task<bool>? _current;
    private int _failedAttempts;
    private long _retryNotBeforeTick;
    private bool _permanentlyFailed;
    private bool _closed;

    public bool HasStarted
    {
        get
        {
            lock (_gate)
                return _current != null;
        }
    }

    /// <summary>False once closed or permanently failed; a retryable failure keeps it available.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
                return !_closed && !_permanentlyFailed;
        }
    }

    /// <summary>The latest attempt, running or completed; null before the first one.</summary>
    public Task<bool>? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>
    /// Returns the running or succeeded attempt, or starts a new one on the thread pool when none has run or the
    /// last one failed and its backoff has elapsed. Null while that backoff runs, once permanently failed, and
    /// after <see cref="Close"/>. The returned task never faults.
    /// </summary>
    public Task<bool>? EnsureStarted()
    {
        lock (_gate)
        {
            if (_closed || _permanentlyFailed) return null;

            Task<bool>? current = _current;
            if (current != null)
            {
                if (!current.IsCompleted || current.Result) return current;
                if (getTickCount64() < _retryNotBeforeTick) return null;
            }

            Task<bool> started = Task.Run(RunAttemptAsync);
            _current = started;
            return started;
        }
    }

    /// <summary>Stops all future attempts; the result can never change (for example, unsupported hardware).</summary>
    public void MarkPermanentlyFailed()
    {
        lock (_gate)
            _permanentlyFailed = true;
    }

    /// <summary>Stops all future attempts during shutdown.</summary>
    public void Close()
    {
        lock (_gate)
            _closed = true;
    }

    /// <summary>
    /// The wait after <paramref name="failedAttempts"/> consecutive failures: <paramref name="baseDelayMs"/>
    /// doubled per extra failure, capped at <paramref name="maxDelayMs"/>.
    /// </summary>
    internal static int GetRetryDelayMs(int failedAttempts, int baseDelayMs, int maxDelayMs)
    {
        int exponent = Math.Clamp(failedAttempts - 1, min: 0, max: 30);
        long delayMs = (long)Math.Max(baseDelayMs, val2: 0) << exponent;
        return (int)Math.Min(delayMs, maxDelayMs);
    }

    private async Task<bool> RunAttemptAsync()
    {
        bool succeeded;
        try
        {
            succeeded = await initialize().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ownerName}: initialization failed: {ex.Message}");
            succeeded = false;
        }

        lock (_gate)
        {
            if (succeeded)
            {
                _failedAttempts = 0;
                return true;
            }

            _failedAttempts++;
            int retryDelayMs = GetRetryDelayMs(_failedAttempts, retryBaseDelayMs, retryMaxDelayMs);
            _retryNotBeforeTick = getTickCount64() + retryDelayMs;
            if (!_closed && !_permanentlyFailed)
            {
                TADNLog.Log(
                    $"{ownerName}: initialization attempt {_failedAttempts} failed; "
                    + $"the next request may retry after {retryDelayMs} ms");
            }

            return false;
        }
    }
}

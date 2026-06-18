namespace TrayAppDotNETCommon.Services;

/// <summary>
/// Per-key "latest-pending-wins" payload scheduler.
/// </summary>
public sealed class AsyncThrottler<TKey>(
    int cooldownMs,
    IEqualityComparer<TKey>? comparer = null,
    int drainPollIntervalMs = TrayAppDotNETTimeConstants.DrainPollIntervalMs) : IDisposable
    where TKey : notnull
{
    private readonly Dictionary<TKey, Slot> _slots = new(comparer ?? EqualityComparer<TKey>.Default);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly int _drainPollIntervalMs = Math.Max(1, drainPollIntervalMs);
    private int _cooldownMs = Math.Max(0, cooldownMs);
    private bool _disposed;

    public int CooldownMs
    {
        get => _cooldownMs;
        set => _cooldownMs = Math.Max(0, value);
    }

    public Task RunAsync(TKey key, Func<ThrottlerContext, Task> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_disposed) return Task.CompletedTask;

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Slot slot;
        bool startDriver = false;

        lock (_gate)
        {
            if (_disposed)
            {
                completionSource.TrySetResult();
                return completionSource.Task;
            }

            if (!_slots.TryGetValue(key, out slot!))
            {
                slot = new Slot();
                _slots[key] = slot;
            }

            if (slot.NextPayload != null) slot.NextCompletionSource?.TrySetResult();

            slot.NextPayload = payload;
            slot.NextCancellationToken = cancellationToken;
            slot.NextCompletionSource = completionSource;
            slot.ReplacementSignal.HasReplacement = true;

            if (!slot.DriverRunning)
            {
                slot.DriverRunning = true;
                startDriver = true;
            }
        }

        if (startDriver)
            _ = DriveSlotAsync(key, slot);

        return completionSource.Task;
    }

    public void Drop(TKey key)
    {
        TaskCompletionSource? droppedCompletionSource;
        lock (_gate)
        {
            if (!_slots.TryGetValue(key, out Slot? slot)) return;

            droppedCompletionSource = slot.NextCompletionSource;
            slot.NextPayload = null;
            slot.NextCancellationToken = CancellationToken.None;
            slot.NextCompletionSource = null;
        }

        droppedCompletionSource?.TrySetResult();
    }

    public bool IsBusy(TKey key)
    {
        lock (_gate)
        {
            if (!_slots.TryGetValue(key, out Slot? slot)) return false;
            return slot.DriverRunning || slot.NextPayload != null;
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            int active;
            lock (_gate)
            {
                active = 0;
                foreach (Slot throttleSlot in _slots.Values)
                    if (throttleSlot.DriverRunning)
                        active++;
            }

            if (active == 0) return;
            try { await Task.Delay(_drainPollIntervalMs, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _shutdownTokenSource.Cancel(); }
        catch
        {
            /* best-effort during shutdown */
        }

        lock (_gate)
        {
            foreach (Slot throttleSlot in _slots.Values)
            {
                throttleSlot.NextCompletionSource?.TrySetResult();
                throttleSlot.NextPayload = null;
                throttleSlot.NextCompletionSource = null;
            }
        }

        try { _shutdownTokenSource.Dispose(); }
        catch
        {
            /* best-effort */
        }
    }

    private async Task DriveSlotAsync(TKey key, Slot slot)
    {
        while (true)
        {
            Func<ThrottlerContext, Task>? payload;
            CancellationToken externalCancellationToken;
            TaskCompletionSource? completionSource;

            lock (_gate)
            {
                if (_disposed || slot.NextPayload == null)
                {
                    slot.DriverRunning = false;
                    return;
                }

                payload = slot.NextPayload;
                externalCancellationToken = slot.NextCancellationToken;
                completionSource = slot.NextCompletionSource;
                slot.NextPayload = null;
                slot.NextCancellationToken = CancellationToken.None;
                slot.NextCompletionSource = null;
                slot.ReplacementSignal.HasReplacement = false;
            }

            CancellationTokenSource? linkedTokenSource = null;
            CancellationToken payloadCancellationToken = _shutdownTokenSource.Token;
            if (externalCancellationToken.CanBeCanceled)
            {
                linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    externalCancellationToken,
                    _shutdownTokenSource.Token);
                payloadCancellationToken = linkedTokenSource.Token;
            }

            ThrottlerContext throttlerContext = new(slot.ReplacementSignal, payloadCancellationToken);

            try
            {
                await payload(throttlerContext).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                /* expected on dispose / external cancel */
            }
            catch (Exception exception)
            {
                TADNLog.Log($"AsyncThrottler: payload for key '{key}' threw: {exception.Message}");
            }
            finally
            {
                linkedTokenSource?.Dispose();
                completionSource?.TrySetResult();
            }

            if (_disposed)
            {
                lock (_gate) slot.DriverRunning = false;
                return;
            }

            int cooldown = _cooldownMs;
            if (cooldown > 0)
            {
                try
                {
                    await Task.Delay(cooldown, _shutdownTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (_gate) slot.DriverRunning = false;
                    return;
                }
            }
        }
    }

    private sealed class Slot
    {
        public Func<ThrottlerContext, Task>? NextPayload;
        public CancellationToken NextCancellationToken;
        public TaskCompletionSource? NextCompletionSource;
        public bool DriverRunning;
        public readonly ReplacementSignal ReplacementSignal = new();
    }
}

internal sealed class ReplacementSignal
{
    private volatile bool _hasReplacement;

    public bool HasReplacement
    {
        get => _hasReplacement;
        internal set => _hasReplacement = value;
    }
}

public readonly struct ThrottlerContext
{
    private readonly ReplacementSignal? _signal;

    internal ThrottlerContext(ReplacementSignal signal, CancellationToken cancellationToken)
    {
        _signal = signal;
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }

    public bool HasReplacement => _signal?.HasReplacement == true;
}

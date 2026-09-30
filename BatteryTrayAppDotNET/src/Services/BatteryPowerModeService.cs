using BatteryTrayAppDotNET.Models;
using TrayAppDotNETCommon.Services;

namespace BatteryTrayAppDotNET.Services;

/// <summary>Keeps the latest requested power mode visible while serializing OS writes and guarded reads.</summary>
public sealed class BatteryPowerModeService : IDisposable
{
    private const int ApplyKey = 0;
    private readonly Lock _gate = new();
    private readonly AsyncThrottler<int> _applyQueue = new(cooldownMs: 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly Func<BatteryPowerMode, CancellationToken, Task<bool>> _apply;
    private readonly Func<CancellationToken, Task<BatteryPowerModeReadResult>> _read;
    private BatteryPowerMode? _observedMode;
    private BatteryPowerMode? _pendingMode;
    private bool _observedAvailable;
    private long _requestGeneration;
    private long _revision;
    private Task? _pendingTask;
    private Task? _refreshTask;
    private bool _disposed;

    public BatteryPowerModeService()
        : this(WindowsPowerModeBackend.ApplyAsync, WindowsPowerModeBackend.ReadAsync)
    {
    }

    public BatteryPowerModeService(
        Func<BatteryPowerMode, CancellationToken, Task<bool>> apply,
        Func<CancellationToken, Task<BatteryPowerModeReadResult>> read)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(read);
        _apply = apply;
        _read = read;
        _lifetimeToken = _lifetime.Token;
    }

    public BatteryPowerMode? DisplayMode
    {
        get { lock (_gate) return _pendingMode ?? _observedMode; }
    }

    public bool IsApplying
    {
        get { lock (_gate) return _pendingMode.HasValue; }
    }

    public bool IsStateAvailable
    {
        get { lock (_gate) return _pendingMode.HasValue || _observedAvailable; }
    }

    /// <summary>May run on a caller or worker thread. UI consumers must dispatch their own updates.</summary>
    public event Action? StateChanged;

    public Task RequestAsync(BatteryPowerMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        Task task;
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_pendingMode == mode) return _pendingTask ?? Task.CompletedTask;

            _pendingMode = mode;
            long generation = ++_requestGeneration;
            ++_revision;
            // Queue under the same lock as the desired state so concurrent callers cannot reverse their order.
            task = _applyQueue.RunAsync(ApplyKey,
                context => ApplyAsync(mode, generation, context.CancellationToken), _lifetimeToken);
            _pendingTask = task;
        }

        NotifyStateChanged();
        return task;
    }

    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed || _pendingMode.HasValue) return Task.CompletedTask;
            if (_refreshTask is { IsCompleted: false }) return _refreshTask;

            long revision = _revision;
            _refreshTask = Task.Run(() => RefreshCoreAsync(revision), _lifetimeToken);
            return _refreshTask;
        }
    }

    private async Task ApplyAsync(BatteryPowerMode mode, long generation, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed || generation != _requestGeneration) return;
        }

        bool success;
        try
        {
            success = await _apply(mode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryPowerModeService apply {mode}: {ex.Message}");
            success = false;
        }

        long completionRevision;
        lock (_gate)
        {
            if (_disposed || generation != _requestGeneration) return;
            completionRevision = ++_revision;
            if (success)
            {
                _observedMode = mode;
                _observedAvailable = true;
                _pendingMode = null;
                _pendingTask = null;
            }
        }

        if (success)
        {
            NotifyStateChanged();
            return;
        }

        // Keep the desired mode while obtaining actual state, then release pending even when readback fails.
        BatteryPowerModeReadResult actual = await ReadSafelyAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || generation != _requestGeneration || completionRevision != _revision) return;
            ++_revision;
            _observedMode = actual.Success ? actual.Mode : null;
            _observedAvailable = actual.Success;
            _pendingMode = null;
            _pendingTask = null;
        }

        TADNLog.Log($"BatteryPowerModeService apply {mode} failed; actual mode readback {(actual.Success ? "succeeded" : "unavailable")}");
        NotifyStateChanged();
    }

    private async Task RefreshCoreAsync(long revision)
    {
        BatteryPowerModeReadResult actual = await ReadSafelyAsync(_lifetimeToken).ConfigureAwait(false);
        bool changed;
        lock (_gate)
        {
            if (_disposed || _pendingMode.HasValue || revision != _revision) return;
            BatteryPowerMode? observed = actual.Success ? actual.Mode : null;
            changed = _observedMode != observed || _observedAvailable != actual.Success;
            _observedMode = observed;
            _observedAvailable = actual.Success;
            ++_revision;
        }

        if (changed) NotifyStateChanged();
    }

    private async Task<BatteryPowerModeReadResult> ReadSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _read(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BatteryPowerModeReadResult(Success: false, Mode: null);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryPowerModeService read: {ex.Message}");
            return new BatteryPowerModeReadResult(Success: false, Mode: null);
        }
    }

    private void NotifyStateChanged()
    {
        // Serialize notifications with disposal. Handlers can reenter state getters/request methods.
        lock (_gate)
        {
            if (_disposed) return;
            foreach (Delegate handler in StateChanged?.GetInvocationList() ?? [])
            {
                if (_disposed) return;
                try { ((Action)handler)(); }
                catch (Exception ex) { TADNLog.Log($"BatteryPowerModeService state notification: {ex.Message}"); }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ++_requestGeneration;
            ++_revision;
            _pendingMode = null;
            _pendingTask = null;
            StateChanged = null;
        }

        _lifetime.Cancel();
        _applyQueue.Drop(ApplyKey);
        _ = DisposeQueueWhenIdleAsync();
    }

    private async Task DisposeQueueWhenIdleAsync()
    {
        // Let the shared driver's token setup finish before disposing its token source.
        // Cancellation and dropping pending work happen synchronously in Dispose above.
        await _applyQueue.DrainAsync().ConfigureAwait(false);
        _applyQueue.Dispose();
        _lifetime.Dispose();
    }
}

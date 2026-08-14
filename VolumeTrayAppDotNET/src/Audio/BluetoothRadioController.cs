using Avalonia.Threading;
using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

internal enum BluetoothRadioPowerState
{
    Unknown,
    Unavailable,
    Off,
    On
}

/// <summary>Reads and changes the Windows Bluetooth radio state through RadioMgr.h.</summary>
internal static class BluetoothRadioPower
{
    /// <summary>Returns the aggregate state of every Bluetooth radio exposed by Windows.</summary>
    public static BluetoothRadioPowerState Query() => Execute(null);

    /// <summary>Changes every controllable Bluetooth radio and returns the resulting state.</summary>
    public static BluetoothRadioPowerState SetEnabled(bool isEnabled) => Execute(isEnabled);

    internal static BluetoothRadioPowerState ResolveState(IEnumerable<DeviceRadioState> states)
    {
        bool foundOffRadio = false;
        foreach (DeviceRadioState state in states)
        {
            switch (state)
            {
                case DeviceRadioState.RadioOn:
                case DeviceRadioState.HardwareRadioOnUncontrollable:
                    return BluetoothRadioPowerState.On;
                case DeviceRadioState.SoftwareRadioOff:
                case DeviceRadioState.HardwareRadioOff:
                case DeviceRadioState.SoftwareAndHardwareRadioOff:
                case DeviceRadioState.HardwareRadioOffUncontrollable:
                    foundOffRadio = true;
                    break;
                case DeviceRadioState.Invalid:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        return foundOffRadio ? BluetoothRadioPowerState.Off : BluetoothRadioPowerState.Unavailable;
    }

    internal static bool NeedsStateChange(DeviceRadioState state, bool isEnabled) => state switch
    {
        DeviceRadioState.RadioOn => !isEnabled,
        DeviceRadioState.SoftwareRadioOff => isEnabled,
        DeviceRadioState.HardwareRadioOff => !isEnabled,
        DeviceRadioState.SoftwareAndHardwareRadioOff => isEnabled,
        DeviceRadioState.HardwareRadioOnUncontrollable => false,
        DeviceRadioState.Invalid => false,
        DeviceRadioState.HardwareRadioOffUncontrollable => false,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static BluetoothRadioPowerState Execute(bool? requestedEnabled)
    {
        IMediaRadioManager? manager = null;
        IRadioInstanceCollection? collection = null;
        List<DeviceRadioState> states = [];

        try
        {
            manager = BluetoothRadioManagerFactory.Create();
            int collectionResult = manager.GetRadioInstances(out collection);
            if (collectionResult < 0 || collection == null)
            {
                LogFailure(requestedEnabled, "enumeration", collectionResult);
                return BluetoothRadioPowerState.Unavailable;
            }

            int countResult = collection.GetCount(out uint radioCount);
            if (countResult < 0)
            {
                LogFailure(requestedEnabled, "count query", countResult);
                return BluetoothRadioPowerState.Unavailable;
            }

            for (uint radioIndex = 0; radioIndex < radioCount; radioIndex++)
            {
                IRadioInstance? radio = null;
                try
                {
                    int instanceResult = collection.GetAt(radioIndex, out radio);
                    if (instanceResult < 0 || radio == null)
                    {
                        LogFailure(requestedEnabled, $"radio {radioIndex} lookup", instanceResult);
                        continue;
                    }

                    int stateResult = radio.GetRadioState(out DeviceRadioState state);
                    if (stateResult < 0)
                    {
                        LogFailure(requestedEnabled, $"radio {radioIndex} state query", stateResult);
                        continue;
                    }

                    if (requestedEnabled is bool isEnabled && NeedsStateChange(state, isEnabled))
                    {
                        DeviceRadioState targetState = isEnabled
                            ? DeviceRadioState.RadioOn
                            : DeviceRadioState.SoftwareRadioOff;
                        int setResult = radio.SetRadioState(
                            targetState,
                            TimeConstants.BluetoothRadioStateChangeTimeoutSeconds);
                        if (setResult < 0)
                        {
                            LogFailure(requestedEnabled, $"radio {radioIndex} state change", setResult);
                        }
                        else
                        {
                            int refreshedStateResult = radio.GetRadioState(out DeviceRadioState refreshedState);
                            if (refreshedStateResult >= 0) state = refreshedState;
                            else LogFailure(requestedEnabled, $"radio {radioIndex} refresh", refreshedStateResult);
                        }
                    }

                    states.Add(state);
                }
                finally
                {
                    Safe.Release(radio);
                }
            }
        }
        catch (Exception exception)
        {
            if (requestedEnabled.HasValue)
                TADNLog.Log($"Bluetooth radio toggle failed: {exception.GetType().Name}: {exception.Message}");
            return BluetoothRadioPowerState.Unavailable;
        }
        finally
        {
            Safe.Release(collection);
            Safe.Release(manager);
        }

        return ResolveState(states);
    }

    private static void LogFailure(bool? requestedEnabled, string operation, int result)
    {
        if (!requestedEnabled.HasValue) return;
        TADNLog.Log($"Bluetooth radio {operation} failed: HRESULT=0x{result:X8}");
    }
}

/// <summary>Keeps the flyout's Bluetooth radio button synchronized without blocking the UI.</summary>
internal sealed class BluetoothRadioController(Dispatcher dispatcher) : IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private DispatcherTimer? _pollTimer;
    private long _pollGeneration;
    private bool _disposed;

    public event Action? StateChanged;

    public BluetoothRadioPowerState State { get; private set; } = BluetoothRadioPowerState.Unknown;

    /// <summary>Requests one non-blocking state refresh without changing the polling lifetime.</summary>
    public void Refresh() => RequestRefresh(Volatile.Read(ref _pollGeneration));

    /// <summary>Starts flyout-scoped state polling and performs an immediate refresh.</summary>
    public void StartPolling()
    {
        if (_disposed || _pollTimer != null) return;

        long generation = Interlocked.Increment(ref _pollGeneration);
        DispatcherTimer pollTimer = new(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.BluetoothRadioStatePollIntervalMs)
        };
        _pollTimer = pollTimer;
        pollTimer.Tick += OnPollTick;
        pollTimer.Start();
        RequestRefresh(generation);
    }

    /// <summary>Stops polling while retaining the last state for the next flyout opening.</summary>
    public void StopPolling()
    {
        Interlocked.Increment(ref _pollGeneration);
        DispatcherTimer? pollTimer = _pollTimer;
        _pollTimer = null;
        if (pollTimer == null) return;

        pollTimer.Stop();
        pollTimer.Tick -= OnPollTick;
    }

    /// <summary>Toggles Bluetooth based on a fresh radio query.</summary>
    public async Task ToggleAsync()
    {
        if (_disposed) return;

        await _operationGate.WaitAsync();
        try
        {
            if (_disposed) return;

            BluetoothRadioPowerState currentState = await Task.Run(BluetoothRadioPower.Query);
            await ApplyStateAsync(currentState);
            if (currentState is BluetoothRadioPowerState.Unknown or BluetoothRadioPowerState.Unavailable)
                return;

            bool enableRadio = currentState != BluetoothRadioPowerState.On;
            BluetoothRadioPowerState resultingState = await Task.Run(
                () => BluetoothRadioPower.SetEnabled(enableRadio));
            await ApplyStateAsync(resultingState);
        }
        catch (Exception exception)
        {
            if (!_disposed)
                TADNLog.Log($"BluetoothRadioController.ToggleAsync failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        StateChanged = null;
    }

    private void OnPollTick(object? sender, EventArgs eventArgs) =>
        RequestRefresh(Volatile.Read(ref _pollGeneration));

    private void RequestRefresh(long generation) => _ = RefreshAsync(generation);

    private async Task RefreshAsync(long generation)
    {
        if (_disposed || generation != Volatile.Read(ref _pollGeneration)) return;
        bool enteredGate = await _operationGate.WaitAsync(0);
        if (!enteredGate) return;

        try
        {
            BluetoothRadioPowerState state = await Task.Run(BluetoothRadioPower.Query);
            if (_disposed || generation != Volatile.Read(ref _pollGeneration)) return;
            await ApplyStateAsync(state);
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == Volatile.Read(ref _pollGeneration))
                TADNLog.Log($"BluetoothRadioController.RefreshAsync failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ApplyStateAsync(BluetoothRadioPowerState state)
    {
        await dispatcher.InvokeAsync(() =>
        {
            if (_disposed || State == state) return;
            State = state;
            StateChanged?.Invoke();
        }, DispatcherPriority.Background);
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Reports Bluetooth audio device container and battery state from Configuration Manager.
/// <para/>
/// The app used to split this between a WinRT PnP watcher for Bluetooth container discovery and
/// cfgmgr32 polling for <c>DEVPKEY_Bluetooth_Battery</c>. The value always lived in cfgmgr32, and
/// the WinRT watcher requires runtime COM/WinRT marshalling that is brittle in constrained publish
/// modes. This class uses cfgmgr32 for container identity and battery data, then the cached Classic
/// Bluetooth device record's <c>fConnected</c> flag for actual physical connection state.
/// </summary>
internal sealed class BluetoothBatteryMonitor(Dispatcher dispatcher) : INotifyPropertyChanged, IDisposable
{
    private static readonly Guid BluetoothClassGuid = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
    private const string RefreshThrottleKey = "bluetooth-battery-refresh";
    private const string PollRefreshThrottleKey = "bluetooth-battery-poll-refresh";
    private const string ConnectionRefreshThrottleKey = "bluetooth-connection-refresh";
    private const string BluetoothDeviceInstancePrefix = "BTH";
    private const int CMNotifyEventDataHeaderSize = 8;

    // Present devnodes that classify a container as Bluetooth, keyed by PnP instance id. This
    // includes both Bluetooth-class devnodes and battery-bearing devnodes that carry
    // DEVPKEY_Bluetooth_Battery.
    private readonly Dictionary<string, Guid> _idToContainer = new(StringComparer.Ordinal);

    // Current battery cache by physical-device container id.
    private readonly Dictionary<Guid, int> _batteries = [];

    // Paired/present PnP containers identify Bluetooth devices but do not imply a live radio link.
    // Windows keeps BTHENUM devnodes started after a headset disconnects.
    private readonly HashSet<Guid> _knownBluetoothContainers = [];

    // Containers whose cached Classic Bluetooth record currently has fConnected set. This is the
    // physical-link signal used independently of Core Audio endpoint activation.
    private readonly HashSet<Guid> _connectedBluetoothContainers = [];
    private readonly AsyncThrottler<string> _refreshThrottler = new(cooldownMs: 0, StringComparer.Ordinal);

    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _connectionPollTimer;
    private long _pollGeneration;
    private long _activePollGeneration;
    private long _refreshRequestGeneration;
    private long _lastAppliedRefreshGeneration;
    private long _connectionRefreshRequestGeneration;
    private long _lastAppliedConnectionRefreshGeneration;
    private CfgMgr32.CMNotifyCallback? _deviceNotificationCallback;
    private IntPtr _deviceNotificationHandle;
    private bool _isRunning;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires on the dispatcher whenever a container's battery percentage transitions, including
    /// to null when the container is no longer present or no longer reports a battery.
    /// </summary>
    public event Action<Guid, int?>? BatteryChanged;

    /// <summary>
    /// Fires on the dispatcher whenever the Classic Bluetooth API reports that a container's
    /// physical connection flag changed. This is independent of Core Audio endpoint activation.
    /// </summary>
    public event Action<Guid, bool>? BluetoothContainerConnectionChanged;

    /// <summary>True once cfgmgr32 reconciliation is active.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Last known battery percentage (0-100) for the given container id, or null when unknown.
    /// </summary>
    public int? TryGet(Guid containerId) => _batteries.TryGetValue(containerId, out int v) ? v : null;

    /// <summary>True when the latest reconciliation pass classified this container as Bluetooth.</summary>
    public bool IsBluetoothContainer(Guid containerId) => _knownBluetoothContainers.Contains(containerId);

    /// <summary>True when Windows currently reports a live Classic Bluetooth connection.</summary>
    public bool IsBluetoothContainerConnected(Guid containerId) =>
        _connectedBluetoothContainers.Contains(containerId);

    /// <summary>
    /// Resolves a physical Bluetooth container to the remote address embedded in its present
    /// BTHENUM/BTHLE devnode id. The address is consumed by IOCTL_BTH_DISCONNECT_DEVICE.
    /// </summary>
    public bool TryGetBluetoothAddress(Guid containerId, out ulong address)
    {
        // Classic audio endpoints are backed by BTHENUM. Prefer that address when a dual-mode
        // headset also exposes a BTHLE devnode in the same physical container.
        const string classicDevicePrefix = "BTHENUM\\DEV_";
        foreach (KeyValuePair<string, Guid> entry in _idToContainer)
        {
            if (entry.Value == containerId
                && entry.Key.StartsWith(classicDevicePrefix, StringComparison.OrdinalIgnoreCase)
                && BluetoothDeviceDisconnector.TryParseAddress(entry.Key, out address))
                return true;
        }

        foreach (KeyValuePair<string, Guid> entry in _idToContainer)
        {
            if (entry.Value == containerId
                && BluetoothDeviceDisconnector.TryParseAddress(entry.Key, out address))
                return true;
        }

        address = 0;
        return false;
    }

    /// <summary>
    /// Starts cfgmgr32 reconciliation. Idempotent and non-throwing.
    /// </summary>
    public void Start()
    {
        if (_disposed || _isRunning) return;

        IsRunning = true;
        RegisterDeviceNotifications();
        Refresh();
        TADNLog.LogDebug("BluetoothBatteryMonitor.Start: cfgmgr32 reconciliation started.");
    }

    private void RegisterDeviceNotifications()
    {
        if (_disposed || _deviceNotificationHandle != IntPtr.Zero) return;

        _deviceNotificationCallback = OnDeviceNotification;
        CfgMgr32.CMNotifyFilter filter = new()
        {
            Size = (uint)Marshal.SizeOf<CfgMgr32.CMNotifyFilter>(),
            Flags = CfgMgr32.CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES,
            FilterType = CfgMgr32.CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE
        };

        int result = CfgMgr32.CM_Register_Notification(
            ref filter,
            IntPtr.Zero,
            _deviceNotificationCallback,
            out _deviceNotificationHandle);
        if (result == CfgMgr32.CR_SUCCESS && _deviceNotificationHandle != IntPtr.Zero) return;

        _deviceNotificationHandle = IntPtr.Zero;
        _deviceNotificationCallback = null;
        TADNLog.Log(
            $"BluetoothBatteryMonitor: CM_Register_Notification failed, cr=0x{result:X8}; " +
            "connection presence will use flyout polling");
    }

    private uint OnDeviceNotification(
        IntPtr notification,
        IntPtr context,
        uint action,
        IntPtr eventData,
        uint eventDataSize)
    {
        try
        {
            if (_disposed || eventData == IntPtr.Zero || eventDataSize <= CMNotifyEventDataHeaderSize)
                return 0;
            if (action is not CfgMgr32.CM_NOTIFY_ACTION_DEVICEINSTANCEENUMERATED
                and not CfgMgr32.CM_NOTIFY_ACTION_DEVICEINSTANCESTARTED
                and not CfgMgr32.CM_NOTIFY_ACTION_DEVICEINSTANCEREMOVED)
                return 0;

            string? instanceID = Marshal.PtrToStringUni(IntPtr.Add(eventData, CMNotifyEventDataHeaderSize));
            if (instanceID?.StartsWith(BluetoothDeviceInstancePrefix, StringComparison.OrdinalIgnoreCase) != true)
                return 0;

            Refresh();
        }
        catch (Exception exception)
        {
            if (!_disposed)
                TADNLog.Log($"BluetoothBatteryMonitor device notification failed: {exception.Message}");
        }

        return 0;
    }

    /// <summary>
    /// Runs one immediate cfgmgr32 reconciliation pass. Callers use this before interpreting a
    /// newly-added audio endpoint, and the flyout polling timer uses it for battery deltas.
    /// </summary>
    public void Refresh() => RefreshCore(null);

    private void RefreshCore(PollGenerationLease? pollLease)
    {
        if (_disposed || (pollLease.HasValue && !IsPollGenerationCurrent(pollLease.Value))) return;
        long refreshGeneration = Interlocked.Increment(ref _refreshRequestGeneration);
        long connectionRefreshGeneration = Interlocked.Increment(ref _connectionRefreshRequestGeneration);
        string throttleKey = pollLease.HasValue ? PollRefreshThrottleKey : RefreshThrottleKey;
        _ = _refreshThrottler.RunAsync(throttleKey, async context =>
        {
            if (_disposed || (pollLease.HasValue && !IsPollGenerationCurrent(pollLease.Value))) return;
            ReconciliationResult result = await Task.Run(BuildCurrentState, context.CancellationToken)
                .ConfigureAwait(false);
            if (_disposed || context.HasReplacement
                          || (pollLease.HasValue && !IsPollGenerationCurrent(pollLease.Value)))
                return;

            if (!result.HasData)
            {
                TADNLog.LogDebug(
                    "BluetoothBatteryMonitor.Reconcile: cfgmgr32 returned no present devnodes; keeping previous state.");
                return;
            }

            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || (pollLease.HasValue && !IsPollGenerationCurrent(pollLease.Value))) return;
                    if (refreshGeneration <= Volatile.Read(ref _lastAppliedRefreshGeneration)) return;
                    Volatile.Write(ref _lastAppliedRefreshGeneration, refreshGeneration);
                    ApplyCurrentState(
                        result.CurrentIds,
                        result.CurrentContainers,
                        result.CurrentBatteries,
                        result.ConnectedContainers,
                        result.HasConnectionData,
                        connectionRefreshGeneration);
                    TADNLog.LogDebug(
                        $"BluetoothBatteryMonitor.Reconcile: scanned={result.ScannedCount} " +
                        $"bluetoothClass={result.BluetoothClassMatches} battery={result.BatteryMatches} " +
                        $"knownContainers={_knownBluetoothContainers.Count} " +
                        $"connectedContainers={_connectedBluetoothContainers.Count}");
                }, DispatcherPriority.Background);
            }
            catch (Exception exception)
            {
                if (!_disposed && (!pollLease.HasValue || IsPollGenerationCurrent(pollLease.Value)))
                    TADNLog.Log($"BluetoothBatteryMonitor dispatcher application failed: {exception.Message}");
            }
        });
    }

    /// <summary>
    /// Begins periodic active reconciliation while the flyout is visible. Battery properties use
    /// their low-frequency timer; the inexpensive cached Classic Bluetooth connection query uses
    /// a separate fast timer so physical-link changes precede delayed Core Audio activation.
    /// </summary>
    public void StartPolling()
    {
        if (_disposed || _pollTimer != null || _connectionPollTimer != null) return;
        TADNLog.LogDebug($"BluetoothBatteryMonitor.StartPolling: tracking {_idToContainer.Count} devnodes");
        long generation = Interlocked.Increment(ref _pollGeneration);
        DispatcherTimer pollTimer = new(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.BluetoothBatteryPollIntervalMs)
        };
        DispatcherTimer connectionPollTimer = new(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.BluetoothConnectionStatePollIntervalMs)
        };
        _pollTimer = pollTimer;
        _connectionPollTimer = connectionPollTimer;
        Volatile.Write(ref _activePollGeneration, generation);
        pollTimer.Tick += OnPollTick;
        connectionPollTimer.Tick += OnConnectionPollTick;
        try
        {
            pollTimer.Start();
            connectionPollTimer.Start();
        }
        catch
        {
            StopPolling();
            throw;
        }

        RefreshCore(new PollGenerationLease(pollTimer, generation));
    }

    /// <summary>
    /// Stops the flyout-scoped reconciliation timer. The latest classification and battery cache
    /// remain available until the next explicit refresh or start.
    /// </summary>
    public void StopPolling()
    {
        Volatile.Write(ref _activePollGeneration, value: 0);
        DispatcherTimer? pollTimer = _pollTimer;
        DispatcherTimer? connectionPollTimer = _connectionPollTimer;
        _pollTimer = null;
        _connectionPollTimer = null;

        if (pollTimer != null)
        {
            try { pollTimer.Stop(); }
            catch (Exception exception)
            {
                TADNLog.Log($"BluetoothBatteryMonitor battery polling stop failed: {exception.Message}");
            }
            finally
            {
                pollTimer.Tick -= OnPollTick;
            }
        }

        if (connectionPollTimer != null)
        {
            try { connectionPollTimer.Stop(); }
            catch (Exception exception)
            {
                TADNLog.Log($"BluetoothBatteryMonitor connection polling stop failed: {exception.Message}");
            }
            finally
            {
                connectionPollTimer.Tick -= OnConnectionPollTick;
            }
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer pollTimer) return;

        long generation = Volatile.Read(ref _activePollGeneration);
        PollGenerationLease pollLease = new(pollTimer, generation);
        if (!IsPollGenerationCurrent(pollLease)) return;
        RefreshCore(pollLease);
    }

    private void OnConnectionPollTick(object? sender, EventArgs eventArgs)
    {
        if (_disposed || !ReferenceEquals(sender, _connectionPollTimer)) return;
        if (Volatile.Read(ref _activePollGeneration) == 0) return;
        RefreshConnectionState();
    }

    private bool IsPollGenerationCurrent(PollGenerationLease pollLease) =>
        pollLease.Generation != 0
        && !_disposed
        && ReferenceEquals(_pollTimer, pollLease.Timer)
        && Volatile.Read(ref _activePollGeneration) == pollLease.Generation;

    private void RefreshConnectionState()
    {
        if (_disposed) return;

        Dictionary<string, Guid> currentIds = new(_idToContainer, StringComparer.Ordinal);
        if (currentIds.Count == 0) return;

        long refreshGeneration = Interlocked.Increment(ref _connectionRefreshRequestGeneration);
        _ = _refreshThrottler.RunAsync(ConnectionRefreshThrottleKey, async context =>
        {
            if (_disposed) return;

            BluetoothConnectionQuery query = await Task.Run(
                    QueryConnectedClassicAddresses,
                    context.CancellationToken)
                .ConfigureAwait(false);
            if (_disposed || context.HasReplacement || !query.HasData) return;

            HashSet<Guid> connectedContainers = ResolveConnectedContainers(
                currentIds,
                query.ConnectedAddresses);
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    if (refreshGeneration <= Volatile.Read(ref _lastAppliedConnectionRefreshGeneration))
                        return;

                    Volatile.Write(ref _lastAppliedConnectionRefreshGeneration, refreshGeneration);
                    ApplyConnectedState(connectedContainers);
                }, DispatcherPriority.Background);
            }
            catch (Exception exception)
            {
                if (!_disposed)
                    TADNLog.Log($"BluetoothBatteryMonitor connection refresh failed: {exception.Message}");
            }
        });
    }

    private static ReconciliationResult BuildCurrentState()
    {
        List<string> ids = EnumeratePresentDevnodeIds();
        if (ids.Count == 0)
            return ReconciliationResult.Empty;

        Dictionary<string, Guid> currentIds = new(StringComparer.Ordinal);
        Dictionary<Guid, int> currentBatteries = [];
        HashSet<Guid> currentContainers = [];
        int bluetoothClassMatches = 0;
        int batteryMatches = 0;

        for (int i = 0; i < ids.Count; i++)
        {
            string deviceId = ids[i];
            int cr = CfgMgr32.CM_Locate_DevNodeW(out uint devInst, deviceId, CfgMgr32.CM_LOCATE_DEVNODE_NORMAL);
            if (cr != CfgMgr32.CR_SUCCESS) continue;

            Guid? container = TryReadGuidProperty(devInst, CfgMgr32.DEVPKEY_Device_ContainerId);
            if (!container.HasValue || !IsRealContainer(container.Value)) continue;

            Guid? classGuid = TryReadGuidProperty(devInst, CfgMgr32.DEVPKEY_Device_ClassGuid);
            if (classGuid == BluetoothClassGuid)
            {
                currentIds[deviceId] = container.Value;
                currentContainers.Add(container.Value);
                bluetoothClassMatches++;
            }

            int? battery = TryReadByteProperty(devInst, CfgMgr32.DEVPKEY_Bluetooth_Battery);
            if (!battery.HasValue) continue;

            currentIds[deviceId] = container.Value;
            currentContainers.Add(container.Value);
            currentBatteries[container.Value] = battery.Value;
            batteryMatches++;
        }

        BluetoothConnectionQuery connectionQuery = QueryConnectedClassicAddresses();
        HashSet<Guid> connectedContainers = connectionQuery.HasData
            ? ResolveConnectedContainers(currentIds, connectionQuery.ConnectedAddresses)
            : [];

        return new ReconciliationResult(
            currentIds,
            currentContainers,
            currentBatteries,
            connectedContainers,
            ids.Count,
            bluetoothClassMatches,
            batteryMatches,
            HasData: true,
            connectionQuery.HasData);
    }

    private void ApplyCurrentState(
        Dictionary<string, Guid> currentIds,
        HashSet<Guid> currentContainers,
        Dictionary<Guid, int> currentBatteries,
        HashSet<Guid> connectedContainers,
        bool hasConnectionData,
        long connectionRefreshGeneration)
    {
        List<Guid> newlyKnownContainers = [];
        foreach (Guid container in currentContainers)
        {
            if (!_knownBluetoothContainers.Contains(container))
                newlyKnownContainers.Add(container);
        }

        _idToContainer.Clear();
        foreach (KeyValuePair<string, Guid> kv in currentIds) _idToContainer[kv.Key] = kv.Value;

        _knownBluetoothContainers.Clear();
        foreach (Guid container in currentContainers) _knownBluetoothContainers.Add(container);

        if (hasConnectionData
            && connectionRefreshGeneration > Volatile.Read(ref _lastAppliedConnectionRefreshGeneration))
        {
            Volatile.Write(ref _lastAppliedConnectionRefreshGeneration, connectionRefreshGeneration);
            ApplyConnectedState(connectedContainers);
        }
        else
            RemoveConnectionsForUnknownContainers();

        if (hasConnectionData)
        {
            for (int containerIndex = 0; containerIndex < newlyKnownContainers.Count; containerIndex++)
            {
                Guid container = newlyKnownContainers[containerIndex];
                if (!_connectedBluetoothContainers.Contains(container))
                    RaiseBluetoothContainerConnectionChanged(container, isConnected: false);
            }
        }

        List<Guid> staleBatteryContainers = [];
        foreach (Guid container in _batteries.Keys)
        {
            if (!currentBatteries.ContainsKey(container))
                staleBatteryContainers.Add(container);
        }

        for (int i = 0; i < staleBatteryContainers.Count; i++)
            ApplyBattery(staleBatteryContainers[i], newValue: null);

        foreach (KeyValuePair<Guid, int> kv in currentBatteries)
            ApplyBattery(kv.Key, kv.Value);
    }

    private void ApplyConnectedState(HashSet<Guid> connectedContainers)
    {
        List<Guid> disconnectedContainers = [];
        foreach (Guid container in _connectedBluetoothContainers)
        {
            if (!_knownBluetoothContainers.Contains(container) || !connectedContainers.Contains(container))
                disconnectedContainers.Add(container);
        }

        List<Guid> newlyConnectedContainers = [];
        foreach (Guid container in connectedContainers)
        {
            if (_knownBluetoothContainers.Contains(container)
                && !_connectedBluetoothContainers.Contains(container))
                newlyConnectedContainers.Add(container);
        }

        for (int containerIndex = 0; containerIndex < disconnectedContainers.Count; containerIndex++)
        {
            Guid container = disconnectedContainers[containerIndex];
            _connectedBluetoothContainers.Remove(container);
            RaiseBluetoothContainerConnectionChanged(container, isConnected: false);
        }

        for (int containerIndex = 0; containerIndex < newlyConnectedContainers.Count; containerIndex++)
        {
            Guid container = newlyConnectedContainers[containerIndex];
            _connectedBluetoothContainers.Add(container);
            RaiseBluetoothContainerConnectionChanged(container, isConnected: true);
        }
    }

    private void RemoveConnectionsForUnknownContainers()
    {
        List<Guid> unknownContainers = [];
        foreach (Guid container in _connectedBluetoothContainers)
        {
            if (!_knownBluetoothContainers.Contains(container))
                unknownContainers.Add(container);
        }

        for (int containerIndex = 0; containerIndex < unknownContainers.Count; containerIndex++)
        {
            Guid container = unknownContainers[containerIndex];
            _connectedBluetoothContainers.Remove(container);
            RaiseBluetoothContainerConnectionChanged(container, isConnected: false);
        }
    }

    internal static HashSet<Guid> ResolveConnectedContainers(
        IReadOnlyDictionary<string, Guid> idToContainer,
        IReadOnlySet<ulong> connectedAddresses)
    {
        HashSet<Guid> connectedContainers = [];
        foreach (KeyValuePair<string, Guid> entry in idToContainer)
        {
            if (BluetoothDeviceDisconnector.TryParseAddress(entry.Key, out ulong address)
                && connectedAddresses.Contains(address))
                connectedContainers.Add(entry.Value);
        }

        return connectedContainers;
    }

    private static BluetoothConnectionQuery QueryConnectedClassicAddresses()
    {
        HashSet<ulong> connectedAddresses = [];
        BluetoothApis.BLUETOOTH_DEVICE_SEARCH_PARAMS searchParameters = new()
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnUnknown = 1,
            fReturnConnected = 1,
            fIssueInquiry = 0,
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero
        };
        BluetoothApis.BLUETOOTH_DEVICE_INFO deviceInfo = NewBluetoothDeviceInfo();
        IntPtr findHandle = IntPtr.Zero;

        try
        {
            findHandle = BluetoothApis.BluetoothFindFirstDevice(ref searchParameters, ref deviceInfo);
            if (findHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                bool emptyResult = error is BluetoothApis.ERROR_SUCCESS
                    or BluetoothApis.ERROR_NO_MORE_ITEMS
                    or BluetoothApis.ERROR_NOT_FOUND;
                if (!emptyResult)
                {
                    TADNLog.LogDebug(
                        $"BluetoothBatteryMonitor: BluetoothFindFirstDevice failed; error={error}");
                }

                return new BluetoothConnectionQuery(connectedAddresses, emptyResult);
            }

            while (true)
            {
                if (deviceInfo.Address != 0 && deviceInfo.fConnected != 0)
                    connectedAddresses.Add(deviceInfo.Address);
                deviceInfo = NewBluetoothDeviceInfo();

                if (BluetoothApis.BluetoothFindNextDevice(findHandle, ref deviceInfo)) continue;

                int error = Marshal.GetLastWin32Error();
                if (error is not BluetoothApis.ERROR_SUCCESS and not BluetoothApis.ERROR_NO_MORE_ITEMS)
                {
                    TADNLog.LogDebug(
                        $"BluetoothBatteryMonitor: BluetoothFindNextDevice failed; error={error}");
                    return new BluetoothConnectionQuery(connectedAddresses, HasData: false);
                }

                break;
            }

            return new BluetoothConnectionQuery(connectedAddresses, HasData: true);
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug(
                $"BluetoothBatteryMonitor: Classic connection query failed: {exception.Message}");
            return new BluetoothConnectionQuery(connectedAddresses, HasData: false);
        }
        finally
        {
            if (findHandle != IntPtr.Zero) BluetoothApis.BluetoothFindDeviceClose(findHandle);
        }
    }

    private static BluetoothApis.BLUETOOTH_DEVICE_INFO NewBluetoothDeviceInfo() => new()
    {
        dwSize = (uint)Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_INFO>()
    };

    private void RaiseBluetoothContainerConnectionChanged(Guid containerID, bool isConnected)
    {
        TADNLog.LogDebug(
            $"BluetoothBatteryMonitor: BT container={containerID} connected={isConnected}");
        try { BluetoothContainerConnectionChanged?.Invoke(containerID, isConnected); }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"BluetoothBatteryMonitor: connection-state subscriber threw: {exception.Message}");
        }
    }

    private void ApplyBattery(Guid containerId, int? newValue)
    {
        bool changed;
        if (newValue is { } v)
        {
            changed = !_batteries.TryGetValue(containerId, out int existing) || existing != v;
            _batteries[containerId] = v;
        }
        else
            changed = _batteries.Remove(containerId);

        if (!changed) return;

        TADNLog.LogDebug(
            $"BluetoothBatteryMonitor: container={containerId} battery={newValue?.ToString() ?? "<null>"}");

        try { BatteryChanged?.Invoke(containerId, newValue); }
        catch (Exception ex) { TADNLog.Log($"BluetoothBatteryMonitor: subscriber threw: {ex.Message}"); }
    }

    private static bool IsRealContainer(Guid g) => g != Guid.Empty && g != NoContainerSentinel;

    // Windows assigns this GUID to devnodes that do not belong to a real physical-device
    // container. Treating it as Bluetooth would promote unrelated built-in audio endpoints.
    private static readonly Guid NoContainerSentinel = new("00000000-0000-0000-ffff-ffffffffffff");

    // CM_Get_DevNode_Property: read a single byte property (DEVPROP_TYPE_BYTE) off a located
    // devnode handle. Returns null on any CR_* failure / type mismatch / out-of-range value.
    private static int? TryReadByteProperty(uint devInst, CfgMgr32.DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, propertyBuffer: null, ref size,
            ulFlags: 0);
        if (cr is not CfgMgr32.CR_BUFFER_SMALL and not CfgMgr32.CR_SUCCESS) return null;
        if (propType != CfgMgr32.DEVPROP_TYPE_BYTE || size < 1) return null;

        byte[] buf = new byte[size];
        cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, ulFlags: 0);
        if (cr != CfgMgr32.CR_SUCCESS) return null;

        int level = buf[0];
        return level is >= 0 and <= 100 ? level : null;
    }

    // CM_Get_DevNode_Property: read a 16-byte GUID property (DEVPROP_TYPE_GUID).
    private static Guid? TryReadGuidProperty(uint devInst, CfgMgr32.DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, propertyBuffer: null, ref size,
            ulFlags: 0);
        if (cr is not CfgMgr32.CR_BUFFER_SMALL and not CfgMgr32.CR_SUCCESS) return null;
        if (propType != CfgMgr32.DEVPROP_TYPE_GUID || size != 16) return null;

        byte[] buf = new byte[16];
        cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, ulFlags: 0);
        if (cr != CfgMgr32.CR_SUCCESS) return null;

        return new Guid(buf);
    }

    // CM_Get_Device_ID_List(null, PRESENT): every PnP devnode currently present on the system,
    // as a double-null-terminated multi-string.
    private static List<string> EnumeratePresentDevnodeIds()
    {
        List<string> ids = new(512);

        int cr = CfgMgr32.CM_Get_Device_ID_List_SizeW(out uint size, pszFilter: null,
            CfgMgr32.CM_GETIDLIST_FILTER_PRESENT);
        if (cr != CfgMgr32.CR_SUCCESS || size == 0) return ids;

        char[] buffer = new char[size];
        cr = CfgMgr32.CM_Get_Device_ID_ListW(pszFilter: null, buffer, size, CfgMgr32.CM_GETIDLIST_FILTER_PRESENT);
        if (cr != CfgMgr32.CR_SUCCESS) return ids;

        int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0') continue;
            if (i == start) break;
            ids.Add(new string(buffer, start, i - start));
            start = i + 1;
        }

        return ids;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        IntPtr deviceNotificationHandle = _deviceNotificationHandle;
        _deviceNotificationHandle = IntPtr.Zero;
        if (deviceNotificationHandle != IntPtr.Zero)
        {
            int result = CfgMgr32.CM_Unregister_Notification(deviceNotificationHandle);
            if (result != CfgMgr32.CR_SUCCESS)
            {
                TADNLog.Log(
                    $"BluetoothBatteryMonitor: CM_Unregister_Notification failed, cr=0x{result:X8}");
            }
        }

        _deviceNotificationCallback = null;
        try { IsRunning = false; }
        catch (Exception exception)
        {
            TADNLog.Log($"BluetoothBatteryMonitor running-state notification failed: {exception.Message}");
        }

        Safe.Dispose(_refreshThrottler);
        _idToContainer.Clear();
        _batteries.Clear();
        _knownBluetoothContainers.Clear();
        _connectedBluetoothContainers.Clear();
        PropertyChanged = null;
        BatteryChanged = null;
        BluetoothContainerConnectionChanged = null;
    }

    private readonly record struct PollGenerationLease(DispatcherTimer Timer, long Generation);

    private readonly record struct BluetoothConnectionQuery(
        HashSet<ulong> ConnectedAddresses,
        bool HasData);

    private sealed record ReconciliationResult(
        Dictionary<string, Guid> CurrentIds,
        HashSet<Guid> CurrentContainers,
        Dictionary<Guid, int> CurrentBatteries,
        HashSet<Guid> ConnectedContainers,
        int ScannedCount,
        int BluetoothClassMatches,
        int BatteryMatches,
        bool HasData,
        bool HasConnectionData)
    {
        public static ReconciliationResult Empty { get; } =
            new(
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                [],
                [],
                [],
                ScannedCount: 0,
                BluetoothClassMatches: 0,
                BatteryMatches: 0,
                HasData: false,
                HasConnectionData: false);
    }
}

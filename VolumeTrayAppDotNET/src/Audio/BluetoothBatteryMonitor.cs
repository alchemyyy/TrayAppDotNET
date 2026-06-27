using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using VolumeTrayAppDotNET.Interop;


namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Reports Bluetooth audio device container and battery state from Configuration Manager.
/// <para/>
/// The app used to split this between a WinRT PnP watcher for Bluetooth container discovery and
/// cfgmgr32 polling for <c>DEVPKEY_Bluetooth_Battery</c>. The value always lived in cfgmgr32, and
/// the WinRT watcher requires runtime COM/WinRT marshalling that is brittle in constrained publish modes, so
/// this class now uses one cfgmgr32 reconciliation pass for both responsibilities.
/// </summary>
internal sealed class BluetoothBatteryMonitor(Dispatcher dispatcher) : INotifyPropertyChanged, IDisposable
{
    private static readonly Guid BluetoothClassGuid = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");

    // Present devnodes that classify a container as Bluetooth, keyed by PnP instance id. This
    // includes both Bluetooth-class devnodes and battery-bearing devnodes that carry
    // DEVPKEY_Bluetooth_Battery.
    private readonly Dictionary<string, Guid> _idToContainer = new(StringComparer.Ordinal);

    // Current battery cache by physical-device container id.
    private readonly Dictionary<Guid, int> _batteries = [];

    // Current, present Bluetooth containers from the latest successful reconciliation pass.
    private readonly HashSet<Guid> _activeBluetoothContainers = [];

    private DispatcherTimer? _pollTimer;
    private bool _isRunning;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires on the dispatcher whenever a container's battery percentage transitions, including
    /// to null when the container is no longer present or no longer reports a battery.
    /// </summary>
    public event Action<Guid, int?>? BatteryChanged;

    /// <summary>
    /// Fires on the dispatcher whenever a Bluetooth container becomes active in a reconciliation
    /// pass. Audio code uses this to promote already-wrapped endpoints sharing that container.
    /// </summary>
    public event Action<Guid>? BluetoothContainerSeen;

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
    public bool IsBluetoothContainer(Guid containerId) => _activeBluetoothContainers.Contains(containerId);

    /// <summary>
    /// Starts cfgmgr32 reconciliation. Idempotent and non-throwing.
    /// </summary>
    public void Start()
    {
        if (_disposed || _isRunning) return;

        IsRunning = true;
        Refresh();
        TADNLog.LogDebug("BluetoothBatteryMonitor.Start: cfgmgr32 reconciliation started.");
    }

    /// <summary>
    /// Runs one immediate cfgmgr32 reconciliation pass. Callers use this before interpreting a
    /// newly-added audio endpoint, and the flyout polling timer uses it for battery deltas.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        Reconcile();
    }

    /// <summary>
    /// Begins periodic active reconciliation while the flyout is visible. This keeps battery
    /// percentages and container presence fresh without a process-lifetime WinRT watcher.
    /// </summary>
    public void StartPolling()
    {
        if (_disposed || _pollTimer != null) return;
        TADNLog.LogDebug($"BluetoothBatteryMonitor.StartPolling: tracking {_idToContainer.Count} devnodes");
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.BluetoothBatteryPollIntervalMs),
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
        Refresh();
    }

    /// <summary>
    /// Stops the flyout-scoped reconciliation timer. The latest classification and battery cache
    /// remain available until the next explicit refresh or start.
    /// </summary>
    public void StopPolling()
    {
        if (_pollTimer == null) return;
        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTick;
        _pollTimer = null;
    }

    private void OnPollTick(object? sender, EventArgs e) => Refresh();

    private void Reconcile()
    {
        if (_disposed) return;

        List<string> ids = EnumeratePresentDevnodeIds();
        if (ids.Count == 0)
        {
            TADNLog.LogDebug(
                "BluetoothBatteryMonitor.Reconcile: cfgmgr32 returned no present devnodes; keeping previous state.");
            return;
        }

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

        ApplyCurrentState(currentIds, currentContainers, currentBatteries);

        TADNLog.LogDebug(
            $"BluetoothBatteryMonitor.Reconcile: scanned={ids.Count} bluetoothClass={bluetoothClassMatches} battery={batteryMatches} activeContainers={_activeBluetoothContainers.Count}");
    }

    private void ApplyCurrentState(
        Dictionary<string, Guid> currentIds,
        HashSet<Guid> currentContainers,
        Dictionary<Guid, int> currentBatteries)
    {
        List<Guid> containersBecameActive = [];
        List<Guid> containersBecameInactive = [];

        foreach (Guid container in _activeBluetoothContainers)
            if (!currentContainers.Contains(container))
                containersBecameInactive.Add(container);

        foreach (Guid container in currentContainers)
            if (!_activeBluetoothContainers.Contains(container))
                containersBecameActive.Add(container);

        _idToContainer.Clear();
        foreach (KeyValuePair<string, Guid> kv in currentIds) _idToContainer[kv.Key] = kv.Value;

        foreach (Guid container in containersBecameInactive)
        {
            _activeBluetoothContainers.Remove(container);
            ApplyBattery(container, null);
        }

        foreach (Guid container in containersBecameActive)
        {
            _activeBluetoothContainers.Add(container);
            RaiseBluetoothContainerSeen(container);
        }

        List<Guid> staleBatteryContainers = [];
        foreach (Guid container in _batteries.Keys)
            if (!currentBatteries.ContainsKey(container))
                staleBatteryContainers.Add(container);

        for (int i = 0; i < staleBatteryContainers.Count; i++)
            ApplyBattery(staleBatteryContainers[i], null);

        foreach (KeyValuePair<Guid, int> kv in currentBatteries)
            ApplyBattery(kv.Key, kv.Value);
    }

    private void RaiseBluetoothContainerSeen(Guid containerId)
    {
        TADNLog.LogDebug($"BluetoothBatteryMonitor: active BT container={containerId}");
        try { BluetoothContainerSeen?.Invoke(containerId); }
        catch (Exception ex) { TADNLog.Log($"BluetoothBatteryMonitor: container-seen subscriber threw: {ex.Message}"); }
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
            $"BluetoothBatteryMonitor: container={containerId} battery={(newValue?.ToString() ?? "<null>")}");

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
        int cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, null, ref size, 0);
        if (cr is not CfgMgr32.CR_BUFFER_SMALL and not CfgMgr32.CR_SUCCESS) return null;
        if (propType != CfgMgr32.DEVPROP_TYPE_BYTE || size < 1) return null;

        byte[] buf = new byte[size];
        cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, 0);
        if (cr != CfgMgr32.CR_SUCCESS) return null;

        int level = buf[0];
        return level is >= 0 and <= 100 ? level : null;
    }

    // CM_Get_DevNode_Property: read a 16-byte GUID property (DEVPROP_TYPE_GUID).
    private static Guid? TryReadGuidProperty(uint devInst, CfgMgr32.DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, null, ref size, 0);
        if (cr is not CfgMgr32.CR_BUFFER_SMALL and not CfgMgr32.CR_SUCCESS) return null;
        if (propType != CfgMgr32.DEVPROP_TYPE_GUID || size != 16) return null;

        byte[] buf = new byte[16];
        cr = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, 0);
        if (cr != CfgMgr32.CR_SUCCESS) return null;

        return new Guid(buf);
    }

    // CM_Get_Device_ID_List(null, PRESENT): every PnP devnode currently present on the system,
    // as a double-null-terminated multi-string.
    private static List<string> EnumeratePresentDevnodeIds()
    {
        List<string> ids = new(capacity: 512);

        int cr = CfgMgr32.CM_Get_Device_ID_List_SizeW(out uint size, null, CfgMgr32.CM_GETIDLIST_FILTER_PRESENT);
        if (cr != CfgMgr32.CR_SUCCESS || size == 0) return ids;

        char[] buffer = new char[size];
        cr = CfgMgr32.CM_Get_Device_ID_ListW(null, buffer, size, CfgMgr32.CM_GETIDLIST_FILTER_PRESENT);
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
        IsRunning = false;
        _pollTimer?.Stop();
        if (_pollTimer != null) _pollTimer.Tick -= OnPollTick;
        _pollTimer = null;
        _idToContainer.Clear();
        _batteries.Clear();
        _activeBluetoothContainers.Clear();
    }
}

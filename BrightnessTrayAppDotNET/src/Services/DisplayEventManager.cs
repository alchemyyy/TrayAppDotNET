using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Microsoft.Win32;
using TrayAppDotNETCommon.Serialization;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Single owner of the WM_DEVICECHANGE / SystemEvents pipeline that signals "display topology might have
/// changed". Two parallel responses share one HWND:
/// <list type="bullet">
/// <item>
/// <b>Fast path</b> - a 250 ms <see cref="DispatcherTimer"/> debounce that collapses bursts of WM_DEVICECHANGE
/// / DisplaySettingsChanged / PowerModeChanged.Resume / SessionSwitch.Unlock into one
/// <see cref="DisplayTopologyChanged"/> tick on the UI thread.
/// </item>
/// <item>
/// <b>Slow path</b> - a DDC/CI-free <see cref="System.Threading.Timer"/> burst (1 s x up to 10 ticks) kicked by
/// monitor hot-plug/unplug WM_DEVICECHANGE events. Each tick consults SetupAPI using the monitor setup-class GUID
/// and, if Device Manager reports a devnode the primary pipeline hasn't picked up, calls
/// <see cref="MonitorService.Refresh"/>. Short-circuits the moment every monitor required by the currently
/// selected profile is loaded, so a quiet bus exits the burst immediately.
/// </item>
/// </list>
/// The <see cref="DisplayTopologyChanged"/> event is the merged "fast-path" signal. The slow-path scanner does
/// not raise it directly - it nudges <see cref="MonitorService.Refresh"/> itself, which causes downstream
/// collection updates exactly as before.
/// </summary>
public sealed class DisplayEventManager(MonitorService monitorService, string profilesPath) : IDisposable
{
    private const int BurstMaxTicks = 10;

    private readonly Win32Window _window = new();
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly Lock _burstGate = new();
    private DispatcherTimer? _coalesce;
    private EventHandler? _coalesceTickHandler;
    private long _coalesceGeneration;
    private IntPtr _devNotify;

    private Timer? _burstTimer;
    private int _burstTickCount;
    private volatile bool _burstActive;
    private long _burstGeneration;
    private int _scanInProgress;

    // Signature of the gap the last scan saw; only one scan runs at a time
    private volatile string? _lastRefreshedGap;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Fires on the UI dispatcher after the debounce window expires.
    /// </summary>
    public event Action? DisplayTopologyChanged;

    /// <summary>
    /// Registers the single <c>WM_DEVICECHANGE</c> sink and subscribes to <see cref="SystemEvents"/>. Must be
    /// called from the UI thread so the Win32 window handle attaches to that thread's message pump (matching
    /// the original scanner's contract - RegisterHotKey-style thread affinity).
    /// </summary>
    public void Start()
    {
        if (_started || _disposed) return;

        _started = true;

        _window.Initialize(
            typeof(DisplayEventManager).FullName ?? nameof(DisplayEventManager),
            OnMessage,
            User32.HWND_MESSAGE);

        _devNotify = DeviceNotification.RegisterForDeviceInterface(
            _window.Handle,
            new Guid(Constants.MonitorDeviceInterfaceGUID),
            ownerLabel: "DisplayEventManager",
            failureModeSuffix: "hot-plug events will fall back to WM_DISPLAYCHANGE only.");

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
    }

    /// <summary>
    /// Trigger B for the slow path: invoked from <c>BrightnessFlyout.Show()</c>.
    /// Asks recovery to probe unavailable rows now, since a panel that powered on without a Windows event is
    /// only found by probing. Then runs the short-circuit gate; if it passes, performs exactly one scan off the UI
    /// thread.
    /// </summary>
    public void RunSingleGatedScan()
    {
        if (_disposed) return;

        monitorService.RequestRecoveryOfUnavailableMonitors();

        if (AllProfileMonitorsLoaded())
        {
            TADNLog.Log("DisplayEventManager: short-circuit (flyout)");
            return;
        }

        Task.Run(ScanAndReconcile);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != DeviceNotification.WM_DEVICECHANGE) return IntPtr.Zero;

        int deviceEvent = wParam.ToInt32();
        if (deviceEvent is not DeviceNotification.DBT_DEVICEARRIVAL and
            not DeviceNotification.DBT_DEVICEREMOVECOMPLETE)
            return IntPtr.Zero;

        // We registered only for the monitor device-interface GUID, so every notification we receive here is already
        // scoped to monitors - no need to marshal the DEV_BROADCAST_HDR from LParam and re-check the class GUID.
        Kick();
        OnMonitorHardwareEvent();
        handled = true;
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Kick();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Kick();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // Workstation unlock and "console connect" (RDP-to-physical handoff) can reset gamma LUTs and clear DDC
        // caches on some drivers. We don't react to SessionLock - there's no point applying anything to a
        // locked screen.
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect) Kick();
    }

    /// <summary>
    /// Restart the debounce window. Each incoming signal pushes the fire time out another 250 ms, so a burst of
    /// events produces exactly one tick.
    /// </summary>
    private void Kick()
    {
        if (_disposed) return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(Kick);
            return;
        }

        StopCoalesceTimer();
        DispatcherTimer timer = new(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.DisplayEventDebounceIntervalMs)
        };
        long generation = Interlocked.Increment(ref _coalesceGeneration);
        EventHandler tickHandler = (sender, e) => OnCoalesceTick(sender, e, generation);
        _coalesce = timer;
        _coalesceTickHandler = tickHandler;
        timer.Tick += tickHandler;
        try { timer.Start(); }
        catch
        {
            StopCoalesceTimer();
            throw;
        }
    }

    private void OnCoalesceTick(object? sender, EventArgs e, long generation)
    {
        // A stopped debounce timer can still dispatch a queued tick after its replacement starts
        if (_disposed || generation != Volatile.Read(ref _coalesceGeneration)) return;
        if (!ReferenceEquals(sender, _coalesce)) return;
        StopCoalesceTimer();

        try
        {
            DisplayTopologyChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayEventManager.OnCoalesceTick: {ex.Message}");
        }
    }

    private void StopCoalesceTimer()
    {
        Interlocked.Increment(ref _coalesceGeneration);
        DispatcherTimer? timer = _coalesce;
        EventHandler? tickHandler = _coalesceTickHandler;
        _coalesce = null;
        _coalesceTickHandler = null;
        if (timer == null) return;

        try { timer.Stop(); }
        catch (Exception exception)
        {
            TADNLog.Log($"DisplayEventManager debounce stop failed: {exception.Message}");
        }

        if (tickHandler != null)
            timer.Tick -= tickHandler;
    }

    private void OnMonitorHardwareEvent()
    {
        if (_disposed) return;

        // Idempotent restart: reset the tick counter so a second hot-plug mid-burst gets a full 10-second
        // window, but don't spawn a second timer. A new hot-plug may resolve a gap an earlier Refresh could not.
        _lastRefreshedGap = null;
        lock (_burstGate)
        {
            _burstTickCount = 0;
            if (_burstActive) return;

            _burstActive = true;
            long generation = ++_burstGeneration;
            try
            {
                _burstTimer = new Timer(OnBurstTick, generation, dueTime: 0, TimeConstants.DisplayEventBurstIntervalMs);
            }
            catch
            {
                _burstActive = false;
                _burstGeneration++;
                throw;
            }
        }

        TADNLog.Log("DisplayEventManager: burst start");
    }

    private void OnBurstTick(object? state)
    {
        // Threading.Timer callbacks run on the threadpool with no DispatcherUnhandledException net - an
        // unhandled throw here would tear down the process. Belt-and-braces: inner calls are already
        // try/catch'd, but we wrap the dispatch loop too.
        try
        {
            if (state is not long generation) return;
            int tickCount;
            lock (_burstGate)
            {
                if (_disposed || !_burstActive || generation != _burstGeneration) return;
                tickCount = ++_burstTickCount;
            }

            if (AllProfileMonitorsLoaded())
            {
                TADNLog.Log("DisplayEventManager: short-circuit (burst)");
                StopBurst(generation);
                return;
            }

            TADNLog.Log($"DisplayEventManager: tick {tickCount}");
            ScanAndReconcile();

            bool timedOut;
            lock (_burstGate)
            {
                if (_disposed || !_burstActive || generation != _burstGeneration) return;
                timedOut = _burstTickCount >= BurstMaxTicks;
            }

            if (timedOut)
            {
                TADNLog.Log("DisplayEventManager: burst timed out");
                StopBurst(generation);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayEventManager.OnBurstTick: {ex.Message}");
        }
    }

    private void OnMonitorsRefreshed()
    {
        if (_burstActive && AllProfileMonitorsLoaded())
        {
            TADNLog.Log("DisplayEventManager: short-circuit on refresh");
            StopBurst();
        }
    }

    private void StopBurst(long? expectedGeneration = null)
    {
        Timer? timer;
        lock (_burstGate)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != _burstGeneration) return;

            _burstGeneration++;
            _burstActive = false;
            timer = _burstTimer;
            _burstTimer = null;
        }

        timer?.Dispose();
    }

    private void ScanAndReconcile()
    {
        if (Interlocked.Exchange(ref _scanInProgress, value: 1) == 1) return;

        try
        {
            HashSet<string> reportedInstancePaths = EnumerateDeviceManagerMonitors();
            string? gap = DescribeMonitorGap(reportedInstancePaths, monitorService.CaptureMonitorPresence());

            // Device Manager also lists connected monitors outside the desktop (lid closed while docked, Win+P,
            // "Disconnect this display"). No row can account for them, so an unchanged gap gets one Refresh, not one
            // per burst tick and flyout open.
            string? refreshedGap = _lastRefreshedGap;
            _lastRefreshedGap = gap;
            if (gap != null && string.Equals(gap, refreshedGap, StringComparison.Ordinal))
            {
                TADNLog.Log("DisplayEventManager: gap unchanged since its Refresh");
                return;
            }

            if (gap != null)
            {
                TADNLog.Log("DisplayEventManager: gap detected, calling Refresh");
                // Mark this as a topology-event-driven Refresh so MonitorService applies the
                // post-detection settle to Phase B. Cold-start / sweep / recovery Refreshes do not
                // call this and Phase B runs synchronously, keeping the launch path responsive.
                monitorService.NotifyTopologyEvent();
                monitorService.Refresh();
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayEventManager: scan failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, value: 0);
        }
    }

    /// <summary>
    /// Slow-path decision: Device Manager reports more monitor devnodes than the live rows account for.
    /// Devnodes are compared by exact instance path. Rows without a path on record may each account for one
    /// unmatched devnode. The old comparison used model hardware IDs read out of row IDs; <c>num:</c> and
    /// <c>edid:</c> IDs carry none, so every burst tick saw a gap and restarted the post-detection settle.
    /// </summary>
    internal static bool HasMonitorGap(
        IReadOnlySet<string> reportedInstancePaths,
        MonitorService.MonitorPresence presence) =>
        DescribeMonitorGap(reportedInstancePaths, presence) != null;

    /// <summary>
    /// The unaccounted devnodes of a gap as a stable signature, or null when the rows account for every one.
    /// The comparison is case-insensitive: SetupAPI and the interface path disagree on instance-ID casing.
    /// </summary>
    internal static string? DescribeMonitorGap(
        IReadOnlySet<string> reportedInstancePaths,
        MonitorService.MonitorPresence presence)
    {
        List<string> unaccounted =
        [
            .. reportedInstancePaths
                .Where(path => !presence.DisplayInstancePaths.Contains(path))
                .Select(static path => path.ToUpperInvariant())
                .Order(StringComparer.Ordinal)
        ];
        return unaccounted.Count > presence.RowsWithoutInstancePath ? string.Join('|', unaccounted) : null;
    }

    private bool AllProfileMonitorsLoaded()
    {
        List<(string ID, string EDIDKey)>? expected = ReadSelectedProfileIdentities();
        try
        {
            return AreProfileMonitorsLoaded(expected, monitorService.Monitors);
        }
        catch
        {
            // Observable collection mutated during iteration; report "not loaded" so the scan runs
            return false;
        }
    }

    /// <summary>
    /// Burst and flyout short-circuit gate: true when every monitor in the selected profile has a live row.
    /// </summary>
    internal static bool AreProfileMonitorsLoaded(
        IReadOnlyList<(string ID, string EDIDKey)>? expected,
        IEnumerable<MonitorInfo> monitors)
    {
        if (expected == null || expected.Count == 0) return true;

        // Snapshot both keys per live monitor. A saved entry counts as "loaded" if either:
        //   * its ID matches a live ID (legacy behaviour, plus the post-fix steady state), OR
        //   * its EDIDKey matches a live EDIDKey (survives display-number drift).
        // Without the EDID arm, a power-cycle that drifted the display number would keep the scanner thinking
        // monitors are missing and burst-refresh forever.
        HashSet<string> loadedIDs = new(StringComparer.Ordinal);
        HashSet<string> loadedEDIDKeys = new(StringComparer.Ordinal);
        foreach (MonitorInfo monitor in monitors)
        {
            if (!string.IsNullOrEmpty(monitor.ID)) loadedIDs.Add(monitor.ID);
            if (!string.IsNullOrEmpty(monitor.EDIDKey)) loadedEDIDKeys.Add(monitor.EDIDKey);
        }

        foreach ((string id, string EDIDKey) in expected)
        {
            bool found = (!string.IsNullOrEmpty(EDIDKey) && loadedEDIDKeys.Contains(EDIDKey))
                         || (!string.IsNullOrEmpty(id) && loadedIDs.Contains(id));
            if (!found) return false;
        }

        return true;
    }

    private List<(string ID, string EDIDKey)>? ReadSelectedProfileIdentities()
    {
        // The scanner deliberately re-reads profiles.xml per check rather than holding a ProfileManager
        // instance: ProfileManager.SelectedIndex has a private setter, so a scanner-owned copy would freeze at
        // startup and miss user-driven profile switches. The file is small and the cost is bounded (at most
        // once per tick, i.e. once per second during a burst).
        try
        {
            if (!TrayXmlSerializer.TryReadFile(
                    profilesPath,
                    out ProfileCollection? collection,
                    ex => TADNLog.Log($"DisplayEventManager: failed to read profiles.xml: {ex.Message}")))
                return null;

            int idx = collection.LastSelectedIndex;
            if (idx < 0 || idx >= collection.Profiles.Count) return null;

            // Reads the obsolete MonitorState.ID alongside EDIDKey: the scanner's match-set has to tolerate
            // both shapes until the lazy migration in ProfileManager has had a chance to upgrade every entry.
            // Suppress the obsolete warning at the read sites only.
#pragma warning disable CS0618
            return
            [
                .. collection.Profiles[idx].MonitorStates
                    .Where(s => !string.IsNullOrEmpty(s.ID) || !string.IsNullOrEmpty(s.EDIDKey))
                    .Select(s => (s.ID, s.EDIDKey))
            ];
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayEventManager: failed to read profiles.xml: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Instance paths of every present monitor devnode, in the <c>DISPLAY\hwid\instance</c> form that
    /// <see cref="DDCCI.DDCMonitor.DisplayInstancePath"/> also uses.
    /// </summary>
    private static HashSet<string> EnumerateDeviceManagerMonitors()
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        Guid classGuid = new(Constants.MonitorSetupClassGUID);
        IntPtr hDevInfo = SetupAPI.SetupDiGetClassDevs(
            ref classGuid, IntPtr.Zero, IntPtr.Zero, SetupAPI.DIGCF_PRESENT);
        if (hDevInfo == SetupAPI.INVALID_HANDLE_VALUE)
        {
            TADNLog.Log(
                $"DisplayEventManager: SetupDiGetClassDevs failed ({Marshal.GetLastWin32Error()})");
            return result;
        }

        try
        {
            SetupAPI.SP_DEVINFO_DATA devInfo = new() { cbSize = Marshal.SizeOf<SetupAPI.SP_DEVINFO_DATA>() };

            int index = 0;
            while (SetupAPI.SetupDiEnumDeviceInfo(hDevInfo, index, ref devInfo))
            {
                StringBuilder instanceIDBuilder = new(256);
                if (SetupAPI.SetupDiGetDeviceInstanceId(
                        hDevInfo, ref devInfo, instanceIDBuilder, instanceIDBuilder.Capacity, out _))
                    result.Add(instanceIDBuilder.ToString());

                index++;
            }
        }
        finally
        {
            SetupAPI.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        StopCoalesceTimer();

        if (_started)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
        }

        StopBurst();

        if (_devNotify != IntPtr.Zero)
        {
            DeviceNotification.UnregisterDeviceNotification(_devNotify);
            _devNotify = IntPtr.Zero;
        }

        _window.Dispose();
    }
}

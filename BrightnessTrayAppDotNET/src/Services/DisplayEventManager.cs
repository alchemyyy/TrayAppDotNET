using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Microsoft.Win32;

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
public sealed class DisplayEventManager : IDisposable
{
    private const int BurstMaxTicks = 10;

    private readonly MonitorService _monitorService;
    private readonly string _profilesPath;

    private readonly Win32Window _window;
    private readonly DispatcherTimer _coalesce;
    private readonly Dispatcher _dispatcher;
    private IntPtr _devNotify;

    private Timer? _burstTimer;
    private int _burstTickCount;
    private volatile bool _burstActive;
    private int _scanInProgress;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Fires on the UI dispatcher after the debounce window expires.
    /// </summary>
    public event Action? DisplayTopologyChanged;

    public DisplayEventManager(MonitorService monitorService, string profilesPath)
    {
        _monitorService = monitorService;
        _profilesPath = profilesPath;
        _dispatcher = Dispatcher.UIThread;

        _window = new Win32Window();

        _coalesce = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.DisplayEventDebounceIntervalMs),
        };
        _coalesce.Tick += OnCoalesceTick;
    }

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
            "DisplayEventManager",
            "hot-plug events will fall back to WM_DISPLAYCHANGE only.");

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
    }

    /// <summary>
    /// Trigger B for the slow path: invoked from <c>BrightnessFlyout.Show()</c>.
    /// Runs the short-circuit gate; if it passes, performs exactly one scan off the UI thread.
    /// </summary>
    public void RunSingleGatedScan()
    {
        if (_disposed) return;

        if (AllProfileMonitorsLoaded())
        {
            WPFLog.Log("DisplayEventManager: short-circuit (flyout)");
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

        _coalesce.Stop();
        _coalesce.Start();
    }

    private void OnCoalesceTick(object? sender, EventArgs e)
    {
        _coalesce.Stop();
        if (_disposed) return;

        try
        {
            DisplayTopologyChanged?.Invoke();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DisplayEventManager.OnCoalesceTick: {ex.Message}");
        }
    }

    private void OnMonitorHardwareEvent()
    {
        if (_disposed) return;

        // Idempotent restart: reset the tick counter so a second hot-plug mid-burst gets a full 10-second
        // window, but don't spawn a second timer.
        _burstTickCount = 0;
        if (_burstActive) return;

        _burstActive = true;
        WPFLog.Log("DisplayEventManager: burst start");
        _burstTimer = new Timer(OnBurstTick, null, 0, TimeConstants.DisplayEventBurstIntervalMs);
    }

    private void OnBurstTick(object? _)
    {
        // Threading.Timer callbacks run on the threadpool with no DispatcherUnhandledException net - an
        // unhandled throw here would tear down the process. Belt-and-braces: inner calls are already
        // try/catch'd, but we wrap the dispatch loop too.
        try
        {
            if (_disposed || !_burstActive) return;

            if (AllProfileMonitorsLoaded())
            {
                WPFLog.Log("DisplayEventManager: short-circuit (burst)");
                StopBurst();
                return;
            }

            _burstTickCount++;
            WPFLog.Log($"DisplayEventManager: tick {_burstTickCount}");
            ScanAndReconcile();

            if (_burstTickCount >= BurstMaxTicks)
            {
                WPFLog.Log("DisplayEventManager: burst timed out");
                StopBurst();
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DisplayEventManager.OnBurstTick: {ex.Message}");
        }
    }

    private void OnMonitorsRefreshed()
    {
        if (_burstActive && AllProfileMonitorsLoaded())
        {
            WPFLog.Log("DisplayEventManager: short-circuit on refresh");
            StopBurst();
        }
    }

    private void StopBurst()
    {
        _burstActive = false;
        _burstTimer?.Dispose();
        _burstTimer = null;
    }

    private void ScanAndReconcile()
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1) return;

        try
        {
            HashSet<string> reportedHwids = EnumerateDeviceManagerMonitors();
            HashSet<string>? knownHwids = SafeExtractKnownHwids();
            if (knownHwids == null) return;

            int monitorsCount;
            try { monitorsCount = _monitorService.Monitors.Count; }
            catch { return; }

            bool hwidGap = reportedHwids.Except(knownHwids).Any();
            bool countGap = reportedHwids.Count > monitorsCount;

            if (hwidGap || countGap)
            {
                WPFLog.Log("DisplayEventManager: gap detected, calling Refresh");
                // Mark this as a topology-event-driven Refresh so MonitorService applies the
                // post-detection settle to Phase B. Cold-start / sweep / recovery Refreshes do not
                // call this and Phase B runs synchronously, keeping the launch path responsive.
                _monitorService.NotifyTopologyEvent();
                _monitorService.Refresh();
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DisplayEventManager: scan failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private HashSet<string>? SafeExtractKnownHwids()
    {
        try
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (MonitorInfo monitor in _monitorService.Monitors)
            {
                string? hwid = ExtractHwid(monitor.ID);
                if (hwid != null) result.Add(hwid);
            }

            return result;
        }
        catch
        {
            // Observable collection mutated during iteration. The scan will re-run on the next tick (and
            // MonitorsRefreshed will re-evaluate the short-circuit gate), so skipping here is safe.
            return null;
        }
    }

    private bool AllProfileMonitorsLoaded()
    {
        List<(string ID, string EDIDKey)>? expected = ReadSelectedProfileIdentities();
        if (expected == null || expected.Count == 0) return true;

        // Snapshot both keys per live monitor. A saved entry counts as "loaded" if either:
        //   * its ID matches a live ID (legacy behaviour, plus the post-fix steady state), OR
        //   * its EDIDKey matches a live EDIDKey (survives display-number drift).
        // Without the EDID arm, a power-cycle that drifted the display number would keep the scanner thinking
        // monitors are missing and burst-refresh forever.
        HashSet<string> loadedIDs;
        HashSet<string> loadedEDIDKeys;
        try
        {
            loadedIDs = new HashSet<string>(StringComparer.Ordinal);
            loadedEDIDKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (MonitorInfo monitor in _monitorService.Monitors)
            {
                if (!string.IsNullOrEmpty(monitor.ID)) loadedIDs.Add(monitor.ID);
                if (!string.IsNullOrEmpty(monitor.EDIDKey)) loadedEDIDKeys.Add(monitor.EDIDKey);
            }
        }
        catch
        {
            return false;
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
            if (!File.Exists(_profilesPath)) return null;

            using FileStream fs = File.OpenRead(_profilesPath);
            ProfileCollection collection = ProfileXml.Load(fs);

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
            WPFLog.Log($"DisplayEventManager: failed to read profiles.xml: {ex.Message}");
            return null;
        }
    }

    private static HashSet<string> EnumerateDeviceManagerMonitors()
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        Guid classGuid = new(Constants.MonitorSetupClassGUID);
        IntPtr hDevInfo = SetupAPI.SetupDiGetClassDevs(
            ref classGuid, IntPtr.Zero, IntPtr.Zero, SetupAPI.DIGCF_PRESENT);
        if (hDevInfo == SetupAPI.INVALID_HANDLE_VALUE)
        {
            WPFLog.Log(
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
                {
                    string? hwid = ExtractHwid(instanceIDBuilder.ToString());
                    if (hwid != null) result.Add(hwid);
                }

                index++;
            }
        }
        finally
        {
            SetupAPI.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return result;
    }

    /// <summary>
    /// Extracts the hardware-ID segment shared by both SetupAPI and <c>EnumDisplayDevices</c> forms of a monitor
    /// instance path:
    /// <list type="bullet">
    /// <item><c>DISPLAY\LGE1234\5&amp;abc&amp;0&amp;UID123</c>   -> <c>LGE1234</c></item>
    /// <item><c>MONITOR\LGE1234\{4d36e96e-...}\0001</c>          -> <c>LGE1234</c></item>
    /// <item><c>port:MONITOR\LGE1234\{...}\0001</c>              -> <c>LGE1234</c></item>
    /// <item><c>edid:...</c>, <c>num:...</c>, anything else      -> null</item>
    /// </list>
    /// Returning null signals "can't be matched this way" so the caller falls back to the count-based gap check.
    /// </summary>
    private static string? ExtractHwid(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;

        if (s.StartsWith("port:", StringComparison.Ordinal)) s = s[5..];

        if (!s.StartsWith("DISPLAY\\", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("MONITOR\\", StringComparison.OrdinalIgnoreCase))
            return null;

        string[] parts = s.Split('\\');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1])) return null;

        return parts[1].ToUpperInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        _coalesce.Stop();
        _coalesce.Tick -= OnCoalesceTick;

        if (_started)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            _monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
        }

        _burstActive = false;
        _burstTimer?.Dispose();
        _burstTimer = null;

        if (_devNotify != IntPtr.Zero)
        {
            DeviceNotification.UnregisterDeviceNotification(_devNotify);
            _devNotify = IntPtr.Zero;
        }

        _window.Dispose();
    }
}

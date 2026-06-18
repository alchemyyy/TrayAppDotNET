using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Timers;
using Avalonia.Threading;
using Microsoft.Win32;
using VolumeTrayAppDotNET.Interop;

using IMMDevice = VolumeTrayAppDotNET.Interop.IMMDevice;
using IMMDeviceCollection = VolumeTrayAppDotNET.Interop.IMMDeviceCollection;
using IMMDeviceEnumerator = VolumeTrayAppDotNET.Interop.IMMDeviceEnumerator;
using IMMEndpoint = VolumeTrayAppDotNET.Interop.IMMEndpoint;
using IMMNotificationClient = VolumeTrayAppDotNET.Interop.IMMNotificationClient;
using MMDeviceEnumeratorFactory = VolumeTrayAppDotNET.Interop.MMDeviceEnumeratorFactory;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Top-level audio service. Owns the IMMDeviceEnumerator, tracks the live default render endpoint,
/// and exposes the live device list. Implements <see cref="IMMNotificationClient"/> so device add /
/// remove / default-change notifications keep the wrapper state in sync without polling.
/// All observable state mutations are marshaled onto the UI dispatcher captured at construction.
/// Peak metering for all visible sessions is driven by a single dispatcher timer that the host can
/// start / stop based on flyout visibility.
/// </summary>
internal sealed partial class AudioDeviceManager : INotifyPropertyChanged, IDisposable
{
    // Reassigned only on dispatcher-thread TryRecreateEnumerator; threadpool reads grab the
    // field reference once into a local and operate on a single coherent reference per call.
    private IMMDeviceEnumerator _enumerator;
    private readonly NotificationBridge _bridge;

    private readonly Dispatcher _dispatcher;

    // Threadpool-fired timers. Sample timer reads the COM peak off the UI thread; render timer
    // BeginInvokes the lerp advancement onto the dispatcher. Mirrors EarTrumpet exactly:
    // running the render timer at FPS > SampleRate is what keeps the lerp visiting intermediate
    // step-counter values between samples - dispatcher updates 180/s, screen vsyncs at the monitor
    // rate, so each painted frame catches a smoothly-stepped value instead of a snap-to-sample.
    private readonly System.Timers.Timer _peakSampleTimer;
    private readonly System.Timers.Timer _peakRenderTimer;

    private readonly ObservableCollection<AudioDevice> _devices = [];

    // Bg-thread-readable snapshot of _devices. UI-thread Add/Remove rebuilds this on every
    // CollectionChanged so the sample tick reads the field once without allocating per tick.
    // Volatile gives the bg thread the latest-published reference; AudioDevice[] is immutable
    // once written so a torn enumeration is impossible.
    private volatile AudioDevice[] _devicesSnapshot = [];
    private readonly AppSettings? _settings;

    // Shared rate-limiter for SetMasterVolume(Level)Scalar writes. Sliders update _volume + raise
    // PropertyChanged synchronously so the UI stays responsive, but the underlying COM write is
    // queued through this throttler with latest-pending-wins semantics. One throttler is enough -
    // it keys per device/session id internally so independent sliders don't block each other.
    private readonly AsyncThrottler<string> _volumeThrottler;

    // Watches every audio session's PID; on exit fires a callback the session uses to mark itself
    // disconnected. Closes the gap where the audio engine leaves a session as Inactive long after
    // the owning process is force-killed instead of firing Expired promptly.
    private readonly ProcessExitMonitor _processExitMonitor;

    // Coalesces the notification storm that follows a single device disable / default-change.
    // Up to four IMMNotificationClient callbacks (Console / Multimedia / Communications role
    // transitions plus the state change itself) marshal onto the dispatcher in quick succession;
    // ScheduleUpdateAllDefaults dwells inside the throttler payload and uses HasReplacement to
    // collapse the burst into a single UpdateAllDefaults pass on the UI thread.
    private readonly AsyncThrottler<string> _defaultsRefreshThrottler;
    private const string DefaultsRefreshKey = "defaults";

    // One-shot recovery path for power-resume / stale-CoreAudio-notification cases. Normal endpoint
    // add / remove / state callbacks stay incremental; this full rebuild is reserved for cases where
    // the default render endpoint disappears or the tray is forced to paint the no-device fallback.
    private readonly AsyncThrottler<string> _deviceListRefreshThrottler;
    private const string MissingDefaultDeviceListRefreshKey = "missing-default-device-list-refresh";
    private const string ResumeDeviceListRefreshKey = "resume-device-list-refresh";
    private bool _missingDefaultRecoveryArmed;

    // Realtime listener for the system A2DP codec. The ETW event the monitor consumes is
    // system-wide (one A2DP stream at a time), so we propagate the codec to every Active BT
    // render device on each change. Non-admin runs leave the monitor inert and CurrentCodec
    // stays null on every device.
    private readonly BluetoothCodecMonitor _codecMonitor;

    // CfgMgr32-backed battery monitor keyed by PnP container id. Reads Windows' aggregated
    // DEVPKEY_Bluetooth_Battery (covers BLE GATT, HFP IPHONEACCEV, HID battery reports),
    // classifies Bluetooth containers, and fans changes out to every AudioDevice sharing that
    // container.
    private readonly BluetoothBatteryMonitor _batteryMonitor;

    private string? _notifiedRenderDefaultDeviceID;
    private string? _notifiedRenderCommunicationsDeviceID;
    private string? _notifiedCaptureDefaultDeviceID;
    private string? _notifiedCaptureCommunicationsDeviceID;
    private bool _disposed;

    public ReadOnlyObservableCollection<AudioDevice> Devices { get; }

    /// <summary>
    /// The current default render endpoint (multimedia role). May be null briefly during device
    /// transitions (e.g. between an unplug and the OS picking a successor).
    /// </summary>
    public AudioDevice? DefaultDevice
    {
        get;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The current communications-role default render endpoint, or null when unset.</summary>
    public AudioDevice? DefaultCommunicationsDevice
    {
        get;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The current default capture endpoint (multimedia role).</summary>
    public AudioDevice? DefaultCaptureDevice
    {
        get;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The current communications-role default capture endpoint.</summary>
    public AudioDevice? DefaultCommunicationsCaptureDevice
    {
        get;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Bluetooth A2DP codec monitor. Exposed so UI can bind to its IsRunning / RequiresElevation
    /// flags (e.g. to show a "codec unavailable - needs admin" hint). Per-device codec state is
    /// already surfaced as <see cref="AudioDevice.CurrentCodec"/> - this property is for the
    /// service-level flags, not for codec readout.
    /// </summary>
    public BluetoothCodecMonitor CodecMonitor => _codecMonitor;

    /// <summary>
    /// Bluetooth battery monitor. Exposed so UI can bind to its <see cref="BluetoothBatteryMonitor.IsRunning"/>
    /// flag for diagnostics. Per-device battery readout is already surfaced as
    /// <see cref="AudioDevice.BatteryLevel"/>.
    /// </summary>
    public BluetoothBatteryMonitor BatteryMonitor => _batteryMonitor;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raw IMMNotificationClient::OnDefaultDeviceChanged pass-through, fired synchronously on the
    /// COM worker thread BEFORE the dispatcher-side coalesced refresh. Subscribers that need to
    /// synchronize against the audio service's async fanout - default-device preservation across
    /// an endpoint cycle is the main use case - signal a wait primitive directly from the handler
    /// without round-tripping through the UI dispatcher; that would deadlock a thread already
    /// blocking on the wait. Handlers must filter by flow + role themselves.
    /// </summary>
    public static event Action<EDataFlow, ERole, string?>? DefaultDeviceChangedRaw;

    public AudioDeviceManager(Dispatcher dispatcher, AppSettings? settings = null)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        Devices = new ReadOnlyObservableCollection<AudioDevice>(_devices);
        // Republish snapshot on every UI-thread mutation. Cheap (~N AudioDevice refs copied) and
        // happens at the rate the user is plugging / unplugging devices, not at sample-tick rate.
        _devices.CollectionChanged += (_, _) => _devicesSnapshot = [.. _devices];

        _volumeThrottler = new AsyncThrottler<string>(
            TimeConstants.VolumeWriteRateDefaultMs,
            StringComparer.Ordinal);
        _processExitMonitor = new ProcessExitMonitor();

        // Cooldown 0; the dwell-then-bail pattern in ScheduleUpdateAllDefaults's payload provides
        // the coalescing instead. Cooldown would only space out successive runs, which we don't
        // need - one final UpdateAllDefaults per quiet window is the goal.
        _defaultsRefreshThrottler = new AsyncThrottler<string>(
            0,
            drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);
        _deviceListRefreshThrottler = new AsyncThrottler<string>(
            0,
            drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

        _enumerator = CreateDeviceEnumerator();

        // Sample timer's Elapsed fires on the threadpool and does the COM peak read off the UI
        // thread; render timer's Elapsed BeginInvokes the lerp advancement onto the dispatcher.
        // SynchronizingObject left null on purpose so Elapsed runs on the threadpool rather than
        // any captured sync context.
        _peakSampleTimer = new System.Timers.Timer(ResolveSampleIntervalMs()) { AutoReset = true, };
        _peakSampleTimer.Elapsed += OnPeakSampleElapsed;

        _peakRenderTimer = new System.Timers.Timer(ResolveRenderIntervalMs()) { AutoReset = true, };
        _peakRenderTimer.Elapsed += OnPeakRenderElapsed;

        // Retune timers immediately when the user changes the rates from the settings page.
        // System.Timers.Timer.Interval is thread-safe and reschedules on the next tick.
        if (_settings != null)
        {
            _settings.MeterPeakFpsChanged += OnMeterPeakFpsChanged;
            _settings.MeterPeakSampleRateChanged += OnMeterPeakSampleRateChanged;
        }

        _bridge = new NotificationBridge(this);
        _enumerator.RegisterEndpointNotificationCallback(_bridge);

        RebuildDeviceList();

        // Bluetooth codec monitor. Start fires up an ETW worker thread if elevated; on non-admin
        // runs it stays inert and exposes RequiresElevation = true. CodecChanged marshals onto
        // the dispatcher inside the monitor, so the propagation handler runs on the UI thread.
        _codecMonitor = new BluetoothCodecMonitor(dispatcher);
        _codecMonitor.CodecChanged += OnBluetoothCodecChanged;
        _codecMonitor.Start();
        PropagateCodecToBluetoothDevices(_codecMonitor.CurrentCodec);

        // Bluetooth battery monitor. No elevation requirement; a cfgmgr32 reconciliation pass
        // classifies Bluetooth containers and reads DEVPKEY_Bluetooth_Battery. The first pass
        // retroactively seeds BatteryLevel on wrappers whose container id matches a paired BT
        // device and fires BluetoothContainerSeen so we can promote endpoints whose property
        // store did not surface BTHENUM.
        _batteryMonitor = new BluetoothBatteryMonitor(dispatcher);
        _batteryMonitor.BatteryChanged += OnBluetoothBatteryChanged;
        _batteryMonitor.BluetoothContainerSeen += OnBluetoothContainerSeen;
        _batteryMonitor.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private double ResolveRenderIntervalMs()
    {
        int fps = _settings?.MeterPeakFps ?? AppSettings.MeterPeakFpsDefault;
        if (fps < 1) fps = 1;
        return 1000.0 / fps;
    }

    private double ResolveSampleIntervalMs()
    {
        int rate = _settings?.MeterPeakSampleRate ?? AppSettings.MeterPeakSampleRateDefault;
        if (rate < 1) rate = 1;
        return 1000.0 / rate;
    }

    /// <summary>
    /// How many render frames span one sample interval. Drives the lerp denominator: at FPS=180
    /// SampleRate=90 the result is 2, so each sample is rendered over 2 frames - first the midpoint,
    /// then snap-to-target. Clamped to at least 1 so FPS &lt; SampleRate degrades to "snap on every
    /// render" rather than dividing by zero.
    /// </summary>
    private int ResolveInterpolationSteps()
    {
        int fps = _settings?.MeterPeakFps ?? AppSettings.MeterPeakFpsDefault;
        int rate = _settings?.MeterPeakSampleRate ?? AppSettings.MeterPeakSampleRateDefault;
        if (rate < 1) rate = 1;
        return Math.Max(1, fps / rate);
    }

    private void OnMeterPeakFpsChanged() => _peakRenderTimer.Interval = ResolveRenderIntervalMs();

    private void OnMeterPeakSampleRateChanged() => _peakSampleTimer.Interval = ResolveSampleIntervalMs();

    /// <summary>Starts the peak-meter polling + render timers, and the Bluetooth battery active-poll
    /// timer. Called when the flyout becomes visible.</summary>
    public void StartMetering()
    {
        _peakSampleTimer.Start();
        _peakRenderTimer.Start();
        _batteryMonitor.StartPolling();
    }

    /// <summary>Stops the peak-meter timers and the BT battery active-poll timer. Called when the
    /// flyout hides so the app stays idle.</summary>
    public void StopMetering()
    {
        _peakSampleTimer.Stop();
        _peakRenderTimer.Stop();
        _batteryMonitor.StopPolling();
    }

    /// <summary>
    /// Bg-thread sample tick. Reads the published <see cref="_devicesSnapshot"/> once per tick
    /// (no per-tick alloc; UI thread rebuilds the snapshot on CollectionChanged), then dispatches
    /// the UI-thread lerp arming through <see cref="OnNewSample"/>. The dispatcher only sees the
    /// arming work, never the COM call.
    /// </summary>
    private void OnPeakSampleElapsed(object? sender, ElapsedEventArgs e)
    {
        int steps = ResolveInterpolationSteps();
        // Snapshot the unified-meter config once per tick so every device/session this sample
        // touches sees a coherent (unified, bias) pair even if the user flips the toggle mid-tick.
        bool unified = _settings?.UnifiedPeakMeter ?? false;
        int biasMultiplier = _settings?.UnifiedMeterLowChannelBiasMultiplier
                             ?? AppSettings.UnifiedMeterLowChannelBiasMultiplierDefault;

        AudioDevice[] devices = _devicesSnapshot;

        for (int i = 0; i < devices.Length; i++)
        {
            try { devices[i].UpdatePeakValueBackground(unified, biasMultiplier); }
            catch
            {
                /* device may have died between callbacks */
            }
        }

        _dispatcher.InvokeAsync(() =>
        {
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                try { _devices[i].OnNewSample(steps); }
                catch
                {
                    /* device may have died between callbacks */
                }
            }
        });
    }

    /// <summary>
    /// Bg-thread render tick. Marshals the per-frame lerp advancement onto the dispatcher; the
    /// device.OnRenderTick walk and the PropertyChanged that drives the bound MeterPeak overlay
    /// both run on the UI thread. Running this faster than SampleRate is what gives the meter
    /// stepwise intermediate values between samples - the screen at vsync catches a smoothly
    /// stepped sequence rather than a snap-to-latest-sample sequence.
    /// </summary>
    private void OnPeakRenderElapsed(object? sender, ElapsedEventArgs e)
    {
        // Snapshot the user's MeterPeakChangeCeiling once per tick so every device/session this
        // frame paints sees a coherent value even if the user flips the spinner mid-tick. The
        // ceiling lives in the lerp's render-tick (not in VolumeSlider) because a rate limit
        // needs a periodic clock - PropertyChanged is event-driven and stops once display
        // converges, so a downstream rate limiter would get stuck arbitrarily far from target.
        int percent = _settings?.MeterPeakChangeCeiling ?? AppSettings.MeterPeakChangeCeilingDefault;
        float maxStep = percent / 100f;

        _dispatcher.InvokeAsync(() =>
        {
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                try { _devices[i].OnRenderTick(maxStep); }
                catch
                {
                    /* device may have died between callbacks */
                }
            }
        });
    }

    /// <summary>
    /// One-shot full enumeration. Used at startup; runtime device events take the incremental
    /// add / remove paths so unrelated devices don't lose their session state on every plug event.
    /// Enumerates both render and capture endpoints across every device state (Active | Disabled |
    /// NotPresent | Unplugged) - the visibility filters in consumers decide what's surfaced to
    /// the user; the manager keeps the full set so clicks through the tray menu can re-enable a
    /// disabled device without the wrapper having to be re-built first.
    /// </summary>
    private bool RebuildDeviceList()
    {
        List<AudioDevice> rebuilt = [];
        bool committed = false;
        try
        {
            if (!TryBuildDeviceList(rebuilt, out Exception? firstFailure))
            {
                DisposeTemporaryDevices(rebuilt);
                rebuilt.Clear();

                if (!TryRecreateEnumerator("rebuild-enumeration-failed", firstFailure)) return false;
                if (!TryBuildDeviceList(rebuilt, out _)) return false;
            }

            if (_devices.Count > 0 && rebuilt.Count == 0)
            {
                TADNLog.Log("AudioDeviceManager.RebuildDeviceList: empty runtime snapshot; recreating enumerator");
                DisposeTemporaryDevices(rebuilt);
                rebuilt.Clear();

                if (!TryRecreateEnumerator("empty-runtime-snapshot", null)) return false;
                if (!TryBuildDeviceList(rebuilt, out _)) return false;
                if (rebuilt.Count == 0) return false;
            }

            ClearRememberedDefaultNotifications();

            foreach (AudioDevice d in _devices.ToArray()) d.Dispose();
            _devices.Clear();

            foreach (AudioDevice d in rebuilt) _devices.Add(d);
            committed = true;
        }
        finally
        {
            if (!committed) DisposeTemporaryDevices(rebuilt);
        }

        UpdateAllDefaults();
        // UpdateAllDefaults already calls UpdateListenTargetActiveness, but only after the default
        // resolution lands - so the seed is good on first paint without an extra call here.
        return true;
    }

    private bool TryBuildDeviceList(List<AudioDevice> target, out Exception? failure)
    {
        failure = null;
        if (!TryEnumerateAndWrap(EDataFlow.eRender, target, out failure)) return false;
        if (!TryEnumerateAndWrap(EDataFlow.eCapture, target, out failure)) return false;
        return true;
    }

    private bool TryEnumerateAndWrap(EDataFlow flow, List<AudioDevice> target, out Exception? failure)
    {
        failure = null;
        IMMDeviceCollection? collection = null;
        try
        {
            _enumerator.EnumAudioEndpoints(flow, DeviceState.All, out collection);
            collection.GetCount(out uint count);

            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out IMMDevice device);
                AudioDevice? wrapped = WrapOrRelease(device, flow);
                if (wrapped != null) target.Add(wrapped);
            }

            return true;
        }
        catch (Exception ex)
        {
            failure = ex;
            TADNLog.Log($"AudioDeviceManager.RebuildDeviceList: enumerate {flow} failed: {FormatExceptionForLog(ex)}");
            return false;
        }
        finally
        {
            Safe.Release(collection);
        }
    }

    private static void DisposeTemporaryDevices(List<AudioDevice> devices)
    {
        foreach (AudioDevice d in devices) Safe.Dispose(d);
    }

    /// <summary>
    /// Add a device by ID if we don't already track it. Wraps it for any state and either flow
    /// (render or capture) so visibility-filter consumers can decide what to surface. Used for
    /// OnDeviceAdded paths.
    /// </summary>
    private void AddDeviceByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (FindDeviceByID(id) != null) return;

        IMMDevice? device = null;
        try
        {
            _enumerator.GetDevice(id, out device);

            EDataFlow flow = ResolveDataFlow(device);
            // Sanity check: ResolveDataFlow returns eAll on failure - drop those rather than wrap a
            // device with no usable flow.
            if (flow is not EDataFlow.eRender and not EDataFlow.eCapture)
            {
                Safe.Release(device);
                return;
            }

            AudioDevice? wrapped = WrapOrRelease(device, flow);
            if (wrapped == null) return;

            _devices.Add(wrapped);
            NoteExternalDeviceTopologyChanged();
            ScheduleUpdateAllDefaults();
            // Refresh and promote synchronously for runtime device-add events. The
            // property-store EnumeratorName check at construction misses some Win11 drivers,
            // so without this pass the codec strip and battery row can stay collapsed on a
            // newly-added Bluetooth row until the flyout polling timer runs.
            _batteryMonitor.Refresh();
            PromoteBluetoothIfClassified(wrapped);
            // Newly-added BT render endpoint inherits whatever codec the monitor last saw
            // so it doesn't paint blank until the next ETW event fires.
            if (wrapped is { IsBluetooth: true, DataFlow: EDataFlow.eRender })
                wrapped.CurrentCodec = _codecMonitor.CurrentCodec;
        }
        catch
        {
            // Device gone between notification and our query; nothing to add.
            Safe.Release(device);
        }
    }

    /// <summary>
    /// Remove a single device wrapper by ID, disposing its sessions and releasing its COM proxies.
    /// Used for OnDeviceRemoved.
    /// </summary>
    private void RemoveDeviceByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AudioDevice? match = FindDeviceByID(id);
        if (match == null) return;

        bool wasBluetoothRender = match is { IsBluetooth: true, DataFlow: EDataFlow.eRender };
        _devices.Remove(match);
        Safe.Dispose(match);
        NoteExternalDeviceTopologyChanged();
        ScheduleUpdateAllDefaults();

        // Last BT render endpoint just got removed - drop the cached codec.
        if (wasBluetoothRender && !HasActiveBluetoothRenderDevice()) _codecMonitor.Reset();
    }

    /// <summary>
    /// OnDeviceStateChanged: refresh the existing wrapper's State and, when transitioning to Active,
    /// upgrade its endpoint proxies so volume / metering wakes back up. When leaving Active, drop
    /// those proxies so a later Active transition re-subscribes instead of keeping invalidated COM
    /// callbacks. Default-device evaluation runs at the end so a state flip on a default-eligible
    /// device propagates immediately.
    /// </summary>
    private void HandleDeviceStateChanged(string id, uint newState)
    {
        if (string.IsNullOrEmpty(id)) return;

        AudioDevice? match = FindDeviceByID(id);
        if (match == null)
        {
            // First time we've seen this device id - run the add path (e.g. user re-enabled a device
            // we never wrapped; we always wrap on first sight).
            AddDeviceByID(id);
            return;
        }

        bool isActive = (newState & (uint)DeviceState.Active) != 0;
        match.State = (DeviceState)newState;
        if (isActive)
        {
            match.UpgradeFromActiveState();
            _batteryMonitor.Refresh();
            PromoteBluetoothIfClassified(match);
        }
        else match.DowngradeFromActiveState();

        NoteExternalDeviceTopologyChanged();
        ScheduleUpdateAllDefaults();
        // Any render endpoint going active / inactive can change the dim state of every capture
        // row's listen button (target-active is a cross-device derived flag), so recompute here.

        UpdateListenTargetActiveness();

        // BT state transition: in-memory codec cache survives the active <-> inactive flicker
        // a paired headset goes through on power-off, range loss, sleep / resume, etc. The ETW
        // provider only emits on AVDTP renegotiation, so wiping the cached codec on every
        // disconnect leaves the strip blank for the rest of the session until audio plays
        // again. We trust that if the codec actually changes mid-session (the stack picked a
        // different one due to link quality) the next ETW event overwrites the cache; until
        // then the last-observed value is the best display we have. RemoveDeviceByID still
        // calls Reset() when the wrapper is actually disposed (true unpair / Windows-level
        // removal), so the cache doesn't survive a real disconnect-for-good.
        if (match is { IsBluetooth: true, DataFlow: EDataFlow.eRender } && HasActiveBluetoothRenderDevice())
        {
            // Newly-Active BT render endpoint inherits the cached codec so it doesn't paint
            // blank until the next ETW event - including the case where this transition was
            // Inactive -> Active for the only BT render device on the system.
            PropagateCodecToBluetoothDevices(_codecMonitor.CurrentCodec);
        }
    }

    private void HandleDefaultDeviceChanged(EDataFlow flow, ERole role, string? id)
    {
        NoteExternalDeviceTopologyChanged();
        _batteryMonitor.Refresh();
        RememberDefaultNotification(flow, role, id);
        ScheduleUpdateAllDefaults();
    }

    private void ClearRememberedDefaultNotifications()
    {
        _notifiedRenderDefaultDeviceID = null;
        _notifiedRenderCommunicationsDeviceID = null;
        _notifiedCaptureDefaultDeviceID = null;
        _notifiedCaptureCommunicationsDeviceID = null;
    }

    private void RememberDefaultNotification(EDataFlow flow, ERole role, string? id)
    {
        if (role == ERole.eMultimedia)
        {
            if (flow == EDataFlow.eRender) _notifiedRenderDefaultDeviceID = id;
            else if (flow == EDataFlow.eCapture) _notifiedCaptureDefaultDeviceID = id;
        }
        else if (role == ERole.eCommunications)
        {
            if (flow == EDataFlow.eRender) _notifiedRenderCommunicationsDeviceID = id;
            else if (flow == EDataFlow.eCapture) _notifiedCaptureCommunicationsDeviceID = id;
        }
    }

    /// <summary>
    /// Update one device's friendly name in place when the OS reports PKEY_Device_FriendlyName changed.
    /// Avoids the full RebuildDeviceList that would otherwise drop every other device's session list.
    /// </summary>
    private void RefreshDeviceFriendlyName(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AudioDevice? match = FindDeviceByID(id);
        match?.RefreshFriendlyNameFromStore();
    }

    /// <summary>
    /// Update one device's PKEY_AudioEngine_DeviceFormat readout in place when the OS reports the
    /// format pid changed - typically the user clicking Apply on the Sound Control Panel's
    /// Advanced tab.
    /// </summary>
    private void RefreshDeviceDefaultFormat(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AudioDevice? match = FindDeviceByID(id);
        match?.RefreshDefaultFormatFromStore();
    }

    /// <summary>
    /// Refresh one capture device's Listen-feature state (enable bit + target endpoint id) when the
    /// OS reports a change to either listen-fmtid pid. Capture-only; render endpoints have no
    /// listen state. The target-active dim flag is recomputed off the new target id.
    /// </summary>
    private void RefreshDeviceListenState(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AudioDevice? match = FindDeviceByID(id);
        if (match == null) return;
        match.RefreshListenStateFromStore();
        UpdateListenTargetActiveness();
    }

    private void RefreshDeviceAllowExclusiveControl(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AudioDevice? match = FindDeviceByID(id);
        match?.RefreshAllowExclusiveControlFromStore();
    }

    /// <summary>
    /// Recomputes <see cref="AudioDevice.IsListenTargetActive"/> on every capture endpoint. The
    /// target is either a specific render endpoint id (pid 0 of the listen fmtid) or null meaning
    /// follow the system default playback device. Called from any path that could change the
    /// answer: device state flips, default-device changes, listen-target writes.
    /// </summary>
    private void UpdateListenTargetActiveness()
    {
        AudioDevice? defaultRender = null;
        foreach (AudioDevice d in _devices)
            if (d is { DataFlow: EDataFlow.eRender, IsDefault: true })
            {
                defaultRender = d;
                break;
            }

        foreach (AudioDevice d in _devices)
        {
            if (d.DataFlow != EDataFlow.eCapture) continue;
            string? targetID = d.ListenTargetDeviceID;
            AudioDevice? target = targetID == null ? defaultRender : FindDeviceByID(targetID);
            d.IsListenTargetActive = target is { IsActive: true };
        }
    }

    private AudioDevice? FindDeviceByID(string id)
    {
        foreach (AudioDevice d in _devices)
            if (d.Id == id)
                return d;
        return null;
    }

    private static EDataFlow ResolveDataFlow(IMMDevice device)
    {
        try
        {
            if (device is not IMMEndpoint endpoint) return EDataFlow.eAll;
            endpoint.GetDataFlow(out EDataFlow flow);
            return flow;
        }
        catch { return EDataFlow.eAll; }
    }

    private AudioDevice? WrapOrRelease(IMMDevice device, EDataFlow flow)
    {
        try { return new AudioDevice(device, flow, _dispatcher, _volumeThrottler, _processExitMonitor); }
        catch
        {
            Safe.Release(device);
            return null;
        }
    }

    private static IMMDeviceEnumerator CreateDeviceEnumerator() => MMDeviceEnumeratorFactory.Create();

    private bool TryRecreateEnumerator(string reason, Exception? cause)
    {
        IMMDeviceEnumerator? replacement = null;
        try
        {
            replacement = CreateDeviceEnumerator();
            replacement.RegisterEndpointNotificationCallback(_bridge);
        }
        catch (Exception ex)
        {
            Safe.Release(replacement);
            TADNLog.Log($"AudioDeviceManager.TryRecreateEnumerator({reason}): {FormatExceptionForLog(ex)}");
            return false;
        }

        try { _enumerator.UnregisterEndpointNotificationCallback(_bridge); }
        catch { }

        IMMDeviceEnumerator old = _enumerator;
        _enumerator = replacement;
        Safe.Release(old);

        string causeText = cause == null ? "no exception" : FormatExceptionForLog(cause);
        TADNLog.Log($"AudioDeviceManager.TryRecreateEnumerator({reason}): recreated after {causeText}");
        return true;
    }

    private static string FormatExceptionForLog(Exception ex)
    {
        string hr = ex is COMException ? $" hr=0x{ex.HResult:X8}" : string.Empty;
        return $"{ex.GetType().Name}{hr}: {ex.Message}";
    }

    /// <summary>
    /// Schedules a coalesced UpdateAllDefaults run. Notification handlers fire this instead of
    /// calling UpdateAllDefaults directly so a burst of role-change notifications (common when a
    /// default device is disabled - up to three role transitions plus a state change all marshal
    /// onto the dispatcher in succession) collapses into one refresh after a brief quiet window.
    /// The payload itself dispatches back to the UI thread for the actual flag flips.
    /// </summary>
    private void ScheduleUpdateAllDefaults()
    {
        _ = _defaultsRefreshThrottler.RunAsync(DefaultsRefreshKey, async ctx =>
        {
            // Dwell before doing the work, then bail if a fresher schedule landed during the dwell -
            // the replacement payload will handle the refresh itself, no need to do it twice.
            try
            {
                await Task.Delay(TimeConstants.DefaultsRefreshCoalesceDwellMs, ctx.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (ctx.HasReplacement) return;

            try { await _dispatcher.InvokeAsync(UpdateAllDefaults); }
            catch
            {
                /* dispatcher shut down - nothing to refresh */
            }
        });
    }

    internal void RequestMissingDefaultRecovery(string reason)
    {
        if (!_dispatcher.CheckAccess())
        {
            try { _ = _dispatcher.InvokeAsync(() => RequestMissingDefaultRecovery(reason)); }
            catch
            {
                /* dispatcher shut down - nothing to recover */
            }

            return;
        }

        if (_disposed || _missingDefaultRecoveryArmed) return;

        _missingDefaultRecoveryArmed = true;
        ScheduleDeviceListRefresh(reason, TimeConstants.DeviceListRefreshAfterMissingDefaultMs);
    }

    private void UpdateMissingDefaultRecovery(AudioDevice? renderDefault)
    {
        if (renderDefault != null)
        {
            _missingDefaultRecoveryArmed = false;
            return;
        }

        RequestMissingDefaultRecovery("missing-render-default");
    }

    private void NoteExternalDeviceTopologyChanged() => _missingDefaultRecoveryArmed = false;

    private void ScheduleDeviceListRefresh(string reason, int delayMs)
    {
        int dwellMs = Math.Max(0, delayMs);
        string key = reason == "power-resume"
            ? ResumeDeviceListRefreshKey
            : MissingDefaultDeviceListRefreshKey;
        _ = _deviceListRefreshThrottler.RunAsync(key, async ctx =>
        {
            try { await Task.Delay(dwellMs, ctx.CancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            if (ctx.HasReplacement) return;

            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    if (DefaultDevice != null)
                    {
                        _missingDefaultRecoveryArmed = false;
                        return;
                    }

                    TADNLog.Log($"AudioDeviceManager.RebuildDeviceList: reason={reason}");
                    if (!RebuildDeviceList()) _missingDefaultRecoveryArmed = false;
                });
            }
            catch
            {
                /* dispatcher shut down - nothing to refresh */
            }
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        TADNLog.Log("AudioDeviceManager.PowerModeChanged: resume");
        try
        {
            _ = _dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                _missingDefaultRecoveryArmed = false;
                ScheduleDeviceListRefresh("power-resume", TimeConstants.DeviceListRefreshAfterResumeMs);
            });
        }
        catch
        {
            /* dispatcher shut down - nothing to recover */
        }
    }

    /// <summary>
    /// Re-evaluates every default-role binding (render multimedia + render comms + capture
    /// multimedia + capture comms) against the current device list. Cheap to call - four
    /// GetDefaultAudioEndpoint COM hits and a flag-by-flag toggle on each wrapper.
    /// Also persists the active default id per role / flow into AppSettings, and falls back
    /// onto the persisted id when GetDefaultAudioEndpoint returns null. A null result means
    /// no active device of that role / flow exists - typically the user just disabled the
    /// default and there's no replacement to promote. Treating the persisted id as the
    /// logical default in that window is what gives the ShowDefault*EvenIfDisabled toggles
    /// a target to act on; without it, IsDefault flips off the disabled wrapper before the
    /// visibility filter ever sees it.
    /// Manager-level Default* properties stay strictly the active default - hotkeys, the
    /// tray icon attach, and the flyout's primary-device binding shouldn't track a disabled
    /// fallback.
    /// </summary>
    private void UpdateAllDefaults()
    {
        AudioDevice? renderDefault = LookupDefault(EDataFlow.eRender, ERole.eMultimedia);
        AudioDevice? renderComms = LookupDefault(EDataFlow.eRender, ERole.eCommunications);
        AudioDevice? captureDefault = LookupDefault(EDataFlow.eCapture, ERole.eMultimedia);
        AudioDevice? captureComms = LookupDefault(EDataFlow.eCapture, ERole.eCommunications);

        PersistLastKnownDefaults(renderDefault, renderComms, captureDefault, captureComms);

        // Effective defaults: the active result when present, otherwise the persisted-id wrapper.
        // Returns null when the persisted id is empty or points to a device that's been removed.
        AudioDevice? renderEffective =
            renderDefault ?? FindFallbackDefault(_settings?.LastKnownDefaultPlaybackDeviceID);
        AudioDevice? renderCommsEffective =
            renderComms ?? FindFallbackDefault(_settings?.LastKnownDefaultCommsPlaybackDeviceID);
        AudioDevice? captureEffective =
            captureDefault ?? FindFallbackDefault(_settings?.LastKnownDefaultRecordingDeviceID);
        AudioDevice? captureCommsEffective =
            captureComms ?? FindFallbackDefault(_settings?.LastKnownDefaultCommsRecordingDeviceID);

        // IsDefault tracks the multimedia-role default for the device's flow. IsDefaultCommunications
        // tracks the comms-role default. Both honor the fallback so a disabled last-known-default
        // retains the logical-default status the visibility filter checks.
        foreach (AudioDevice d in _devices)
        {
            bool flowDefault = d.DataFlow == EDataFlow.eRender
                ? ReferenceEquals(d, renderEffective)
                : ReferenceEquals(d, captureEffective);
            bool flowComms = d.DataFlow == EDataFlow.eRender
                ? ReferenceEquals(d, renderCommsEffective)
                : ReferenceEquals(d, captureCommsEffective);
            d.IsDefault = flowDefault;
            d.IsDefaultCommunications = flowComms;
        }

        DefaultDevice = renderDefault;
        DefaultCommunicationsDevice = renderComms;
        DefaultCaptureDevice = captureDefault;
        DefaultCommunicationsCaptureDevice = captureComms;
        UpdateMissingDefaultRecovery(renderDefault);

        // Capture rows that follow the default playback device (null target id) reread their dim
        // state from whichever render endpoint is now the default.
        UpdateListenTargetActiveness();
    }

    /// <summary>
    /// Writes the active-default id per role / flow into AppSettings whenever the lookup
    /// returned a real device. A null result is intentionally ignored so a transient
    /// "no default" window during a disable doesn't wipe the memory the fallback path
    /// depends on. Saves to disk only when at least one id actually changed.
    /// </summary>
    private void PersistLastKnownDefaults(AudioDevice? renderDefault, AudioDevice? renderComms,
        AudioDevice? captureDefault, AudioDevice? captureComms)
    {
        if (_settings == null) return;

        bool dirty = false;
        if (renderDefault != null && _settings.LastKnownDefaultPlaybackDeviceID != renderDefault.Id)
        {
            _settings.LastKnownDefaultPlaybackDeviceID = renderDefault.Id;
            dirty = true;
        }

        if (renderComms != null && _settings.LastKnownDefaultCommsPlaybackDeviceID != renderComms.Id)
        {
            _settings.LastKnownDefaultCommsPlaybackDeviceID = renderComms.Id;
            dirty = true;
        }

        if (captureDefault != null && _settings.LastKnownDefaultRecordingDeviceID != captureDefault.Id)
        {
            _settings.LastKnownDefaultRecordingDeviceID = captureDefault.Id;
            dirty = true;
        }

        if (captureComms != null && _settings.LastKnownDefaultCommsRecordingDeviceID != captureComms.Id)
        {
            _settings.LastKnownDefaultCommsRecordingDeviceID = captureComms.Id;
            dirty = true;
        }

        if (dirty) _settings.Save();
    }

    private AudioDevice? FindFallbackDefault(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return FindDeviceByID(id);
    }

    private AudioDevice? LookupDefault(EDataFlow flow, ERole role)
    {
        AudioDevice? notifiedDefault = FindNotifiedDefault(flow, role);
        if (notifiedDefault != null) return notifiedDefault;

        IMMDevice? defaultDevice = null;
        try
        {
            // GetDefaultAudioEndpoint returns E_NOTFOUND (0x80070490) when no endpoint of that flow
            // exists; treat as "no default" instead of throwing.
            int hr = _enumerator.GetDefaultAudioEndpoint(flow, role, out defaultDevice);
            if (hr < 0) return null;
            defaultDevice.GetId(out string defaultID);
            if (string.IsNullOrEmpty(defaultID)) return null;

            // Foreach must stay inside the try: RefreshDeviceStateFromDefaultEndpoint calls
            // defaultDevice.GetState, so the COM ref has to outlive the release in the finally.
            foreach (AudioDevice d in _devices)
            {
                if (d.Id != defaultID) continue;
                if (d.DataFlow != flow) return null;
                if (!RefreshDeviceStateFromDefaultEndpoint(d, defaultDevice)) return null;
                return d;
            }

            return null;
        }
        catch { return null; }
        finally
        {
            Safe.Release(defaultDevice);
        }
    }

    private static bool RefreshDeviceStateFromDefaultEndpoint(AudioDevice device, IMMDevice? defaultDevice)
    {
        if (defaultDevice == null) return false;

        uint stateRaw;
        try { defaultDevice.GetState(out stateRaw); }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"AudioDeviceManager.LookupDefault({device.FriendlyName}): GetState failed: {FormatExceptionForLog(ex)}");
            return false;
        }

        DeviceState state = (DeviceState)stateRaw;
        bool isActive = (state & DeviceState.Active) != 0;

        if (device.State != state)
        {
            device.State = state;
            if (isActive) device.UpgradeFromActiveState();
            else device.DowngradeFromActiveState();
        }

        return isActive;
    }

    private AudioDevice? FindNotifiedDefault(EDataFlow flow, ERole role)
    {
        string? id = role switch
        {
            ERole.eMultimedia when flow == EDataFlow.eRender => _notifiedRenderDefaultDeviceID,
            ERole.eCommunications when flow == EDataFlow.eRender => _notifiedRenderCommunicationsDeviceID,
            ERole.eMultimedia when flow == EDataFlow.eCapture => _notifiedCaptureDefaultDeviceID,
            ERole.eCommunications when flow == EDataFlow.eCapture => _notifiedCaptureCommunicationsDeviceID,
            _ => null
        };

        if (string.IsNullOrEmpty(id)) return null;

        AudioDevice? device = FindDeviceByID(id);
        if (device == null) return null;
        if (device.DataFlow != flow || !device.IsActive) return null;
        return device;
    }

    // CodecMonitor fires this on the dispatcher whenever a new A2DP SET_CONFIGURATION /
    // RECONFIGURE event lands. Push the new codec onto every Active BT render endpoint so the
    // bound UI reflects it. We don't try to attribute the codec to one specific device when
    // multiple BT endpoints are Active - the ETW event is system-wide (one A2DP stream at a
    // time on Windows) and doesn't carry the remote BDADDR in a field we can rely on; in the
    // overwhelmingly common single-headset case this is correct.
    private void OnBluetoothCodecChanged(BluetoothCodec? codec) => PropagateCodecToBluetoothDevices(codec);

    private void PropagateCodecToBluetoothDevices(BluetoothCodec? codec)
    {
        foreach (AudioDevice d in _devices)
        {
            if (!d.IsBluetooth) continue;
            // Render-only: the ETW event covers A2DP playback, not HFP capture. Pushing it on a
            // BT capture endpoint would surface "Sony LDAC" next to the mic, which is wrong.
            if (d.DataFlow != EDataFlow.eRender) continue;
            d.CurrentCodec = codec;
        }
    }

    private void PromoteBluetoothIfClassified(AudioDevice device)
    {
        if (device.ContainerId is not { } container) return;
        if (!_batteryMonitor.IsBluetoothContainer(container)) return;
        if (!CanPromoteToBluetooth(device)) return;

        if (!device.IsBluetooth) device.IsBluetooth = true;
        device.BatteryLevel = _batteryMonitor.TryGet(container);
    }

    // BatteryMonitor fires this on the dispatcher whenever a tracked container's battery
    // transitions. Fan out to every wrapper (render and capture - a single BT headset typically
    // exposes both an A2DP render endpoint and a HFP capture endpoint that share the same
    // container, and both rows should show the same level). Matching on ContainerId alone is
    // sufficient: the monitor only fires for containers it classified through cfgmgr32 as
    // Bluetooth-backed.
    private void OnBluetoothBatteryChanged(Guid containerId, int? batteryPercent)
    {
        foreach (AudioDevice d in _devices)
        {
            if (d.ContainerId != containerId) continue;
            d.BatteryLevel = batteryPercent;
        }
    }

    // BatteryMonitor fires this when a BT container becomes active in a cfgmgr32 reconciliation.
    // Promote IsBluetooth on every wrapped endpoint sharing that container - the property-store
    // EnumeratorName check at construction misses devices where the audio endpoint doesn't
    // inherit BTHENUM (common on Win11 with some drivers), so the codec strip and battery row
    // both stay collapsed until we upgrade the flag here. Once promoted, fan in the cached codec
    // (render endpoints only) and cached battery so the UI catches up immediately.
    private void OnBluetoothContainerSeen(Guid containerId)
    {
        foreach (AudioDevice d in _devices)
        {
            if (d.ContainerId != containerId) continue;
            if (!CanPromoteToBluetooth(d)) continue;
            if (!d.IsBluetooth) d.IsBluetooth = true;
            if (d.DataFlow == EDataFlow.eRender) d.CurrentCodec = _codecMonitor.CurrentCodec;
            d.BatteryLevel = _batteryMonitor.TryGet(containerId);
        }
    }

    // Defense in depth against a non-BT endpoint getting promoted via a stray container match.
    // The promotion path was designed for endpoints whose property store didn't surface the BT
    // bus enumerator at construction time - empty / unknown enumerator. If the endpoint's own
    // PnP bus identity is something else (HDAUDIO, USB, ROOT, ...) the container match is
    // suspect and we refuse to override the original IsBluetooth=false. Bluetooth bus names all
    // start with "BTH"; an empty enumerator means we genuinely don't know, in which case the
    // container claim is the better signal.
    private static bool CanPromoteToBluetooth(AudioDevice d)
    {
        string enumerator = d.EnumeratorName;
        if (enumerator.Length == 0) return true;
        return enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
    }

    // True when at least one Bluetooth render endpoint is currently Active. Drives the codec
    // reset path: when the last BT device disconnects the cached codec from the monitor is
    // stale (the ETW provider only emits on stream start / reconfigure, never on stream stop),
    // so we explicitly clear it.
    private bool HasActiveBluetoothRenderDevice()
    {
        foreach (AudioDevice d in _devices)
            if (d is { IsBluetooth: true, DataFlow: EDataFlow.eRender, IsActive: true })
                return true;
        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _peakSampleTimer.Stop();
        _peakRenderTimer.Stop();
        _peakSampleTimer.Elapsed -= OnPeakSampleElapsed;
        _peakRenderTimer.Elapsed -= OnPeakRenderElapsed;
        Safe.Dispose(_peakSampleTimer);
        Safe.Dispose(_peakRenderTimer);
        if (_settings != null)
        {
            _settings.MeterPeakFpsChanged -= OnMeterPeakFpsChanged;
            _settings.MeterPeakSampleRateChanged -= OnMeterPeakSampleRateChanged;
        }

        _codecMonitor.CodecChanged -= OnBluetoothCodecChanged;
        Safe.Dispose(_codecMonitor);

        _batteryMonitor.BatteryChanged -= OnBluetoothBatteryChanged;
        _batteryMonitor.BluetoothContainerSeen -= OnBluetoothContainerSeen;
        Safe.Dispose(_batteryMonitor);

        try { _enumerator.UnregisterEndpointNotificationCallback(_bridge); }
        catch { }

        foreach (AudioDevice d in _devices.ToArray()) Safe.Dispose(d);
        _devices.Clear();

        // Dispose the throttler last - any payload still in flight will see _disposed on the
        // RCW it captured and bail out via its inner try/catch. Letting it run to completion
        // is preferable to forcibly cancelling, which can race with finalization.
        Safe.Dispose(_volumeThrottler);
        Safe.Dispose(_defaultsRefreshThrottler);
        Safe.Dispose(_deviceListRefreshThrottler);

        // Tear the watcher thread down after every device (and so every session) is disposed -
        // sessions Unwatch on Dispose, so by the time we get here the watch set is empty and the
        // monitor's worker thread is just blocked on the wake event.
        Safe.Dispose(_processExitMonitor);

        Safe.Release(_enumerator);
    }

    // Notification callbacks fire on COM worker threads; everything is marshaled to the dispatcher
    // before mutating observable state. Each callback emits one diagnostic log line so an external
    // state change (mmsys.cpl enable / disable, default-device change from another app) leaves a
    // trail that proves the CCW is actually wired - a missing line in the active.log when the user
    // toggles a device from Windows is a clear signal the registration didn't take.
    [GeneratedComClass]
    private sealed partial class NotificationBridge(AudioDeviceManager owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
        {
            // Active <-> Disabled / Unplugged transitions add or remove a single device. Incremental
            // path preserves session state on every other device, which a full rebuild would discard.
            string id = pwstrDeviceId;
            uint state = dwNewState;
            TADNLog.Log($"AudioDeviceManager.OnDeviceStateChanged: id={id} newState=0x{state:X}");
            owner._dispatcher.InvokeAsync(() => owner.HandleDeviceStateChanged(id, state));
            return 0;
        }

        public int OnDeviceAdded(string pwstrDeviceId)
        {
            string id = pwstrDeviceId;
            TADNLog.Log($"AudioDeviceManager.OnDeviceAdded: id={id}");
            owner._dispatcher.InvokeAsync(() => owner.AddDeviceByID(id));
            return 0;
        }

        public int OnDeviceRemoved(string pwstrDeviceId)
        {
            string id = pwstrDeviceId;
            TADNLog.Log($"AudioDeviceManager.OnDeviceRemoved: id={id}");
            owner._dispatcher.InvokeAsync(() => owner.RemoveDeviceByID(id));
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? pwstrDefaultDeviceId)
        {
            // Refresh every role / flow because the manager exposes all four. ScheduleUpdateAllDefaults
            // coalesces the burst (a single device disable typically fires this 3+ times in quick
            // succession - one per role transition) into a single UpdateAllDefaults pass; the throttler
            // payload itself dispatches onto the UI thread for the actual flag flips.
            TADNLog.Log(
                $"AudioDeviceManager.OnDefaultDeviceChanged: flow={flow} role={role} id={pwstrDefaultDeviceId ?? "<null>"}");

            // Fire the raw pass-through synchronously on this COM thread BEFORE the dispatcher
            // refresh, so subscribers waiting for fanout completion (force-cycle preservation)
            // unblock without a UI-thread hop.
            try { DefaultDeviceChangedRaw?.Invoke(flow, role, pwstrDefaultDeviceId); }
            catch (Exception ex)
            {
                TADNLog.Log($"AudioDeviceManager.DefaultDeviceChangedRaw subscriber threw: {ex.Message}");
            }

            owner._dispatcher.InvokeAsync(() => owner.HandleDefaultDeviceChanged(flow, role, pwstrDefaultDeviceId));
            return 0;
        }

        public int OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key)
        {
            // Friendly-name renames from the OS Settings page arrive here. Refresh that one device's
            // name in place rather than rebuilding the world.
            if (key.fmtid == PropertyKeys.PKEY_Device_FriendlyName.fmtid &&
                key.pid == PropertyKeys.PKEY_Device_FriendlyName.pid)
            {
                string id = pwstrDeviceId;
                owner._dispatcher.InvokeAsync(() => owner.RefreshDeviceFriendlyName(id));
            }
            // Listen-feature changes from mmsys.cpl land here for the affected capture endpoint.
            // The same fmtid covers pid 1 (enable bool) and pid 0 (target endpoint id) - we
            // recheck both whenever either fires so a target change without an enable change
            // still updates the dim state, and vice versa.
            else if (key.fmtid == PropertyKeys.PKEY_AudioEndpoint_ListenToThisDevice.fmtid &&
                     (key.pid == PropertyKeys.PKEY_AudioEndpoint_ListenToThisDevice.pid ||
                      key.pid == PropertyKeys.PKEY_AudioEndpoint_ListenTargetDeviceID.pid))
            {
                string id = pwstrDeviceId;
                owner._dispatcher.InvokeAsync(() => owner.RefreshDeviceListenState(id));
            }
            // Sound Control Panel's Advanced > Default Format change lands here as a write to the
            // engine-format pid. Refresh the cached readout so the flyout's compact format label
            // doesn't show a stale rate / bit depth.
            else if (key.fmtid == PropertyKeys.PKEY_AudioEngine_DeviceFormat.fmtid &&
                     key.pid == PropertyKeys.PKEY_AudioEngine_DeviceFormat.pid)
            {
                string id = pwstrDeviceId;
                owner._dispatcher.InvokeAsync(() => owner.RefreshDeviceDefaultFormat(id));
            }
            // mmsys.cpl Advanced > Exclusive Mode > "Allow applications to take exclusive control"
            // toggles land here. Refresh our cached flag so the flyout's exclusive-mode button
            // tracks external edits as well as our own ToggleAllowExclusiveControl writes.
            else if (key.fmtid == PropertyKeys.PKEY_AudioEndpoint_AllowExclusiveControl.fmtid &&
                     key.pid == PropertyKeys.PKEY_AudioEndpoint_AllowExclusiveControl.pid)
            {
                string id = pwstrDeviceId;
                owner._dispatcher.InvokeAsync(() => owner.RefreshDeviceAllowExclusiveControl(id));
            }

            return 0;
        }
    }
}

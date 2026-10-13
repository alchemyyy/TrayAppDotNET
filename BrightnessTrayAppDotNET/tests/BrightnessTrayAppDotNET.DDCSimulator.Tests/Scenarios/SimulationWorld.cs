using System.Diagnostics;
using System.Globalization;
using System.Text;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using BrightnessTrayAppDotNET.Utils;
using TrayAppDotNETCommon;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>Outcome of one scenario run.</summary>
internal sealed class ScenarioResult
{
    public required string Name { get; init; }
    public required int Seed { get; init; }
    public required IReadOnlyList<Violation> Violations { get; init; }
    public required IReadOnlyList<string> Observations { get; init; }
    public required string Report { get; init; }
    public required string TracePath { get; init; }

    public bool Passed => Violations.Count == 0;
}

/// <summary>
/// One simulated machine running the real <see cref="MonitorService"/> and <see cref="DDCRecoveryService"/> on virtual
/// time: monitors, connectors and the global DDC path, the Windows display stack, the helper processes, the event
/// bridge and the curve driver, all observed by the oracle.
/// </summary>
internal sealed class SimulationWorld : IDisposable
{
    private static readonly string[] AppExceptionMarkers = ["threw unexpectedly", "unexpected target failure", "threw:"];

    // VCP 0xD6 value for a powered-on panel
    private const uint PowerModeOnValue = 1;

    private readonly List<SimulatedMonitor> _monitors = [];

    // Whether the last power SET submitted to each panel turned it off, guarded by _powerWritesGate
    private readonly Dictionary<SimulatedMonitor, bool> _sentPowerOff = [];
    private readonly Lock _powerWritesGate = new();
    private readonly List<SimulatedDisplayService> _displayServices = [];
    private readonly string _directory;
    private int _appStarts;
    private readonly Dictionary<SimulatedMonitor, long> _lastActiveTicks = [];
    private readonly Dictionary<SimulatedMonitor, int> _originalConnectorIndex = [];
    private readonly Lock _profileGate = new();
    private readonly Stopwatch _realTime = Stopwatch.StartNew();
    private List<(string ID, string EDIDKey)>? _profileIdentities;
    private AppInstance? _app;
    private ITimer? _continuousCheckTimer;
    private volatile List<MonitorInfo> _rowsSnapshot = [];
    private long _lastWakeTicks;
    private long _totalSleptTicks;

    /// <summary>Helper processes started per hour the machine was awake, across every app instance.</summary>
    public double HelperSpawnsPerAwakeHour
    {
        get
        {
            double awakeHours = (Clock.ElapsedTicks - Interlocked.Read(ref _totalSleptTicks)) / (double)TimeSpan.TicksPerHour;
            return awakeHours <= 0 ? 0 : _displayServices.Sum(display => display.HelperSpawns) / awakeHours;
        }
    }
    private bool _hung;
    private bool _disposed;

    public SimulationWorld(WorldConfiguration configuration)
    {
        Configuration = configuration;
        Clock = new SimulationClock(configuration.Epoch, configuration.Quiescence);
        Trace = new SimulationTrace(Clock);
        Oracle = new SimulationOracle(Clock, Trace);
        Dispatcher = new SimulationDispatcher(Clock, exception => Oracle.Report(
            ViolationKind.UnhandledException,
            "ui:" + exception.GetType().Name + ":" + exception.Message,
            "exception escaped to the UI thread: " + exception));
        Clock.Dispatcher = Dispatcher;
        Clock.UnhandledCallbackException = exception => Oracle.Report(
            ViolationKind.UnhandledException,
            "timer:" + exception.GetType().Name + ":" + exception.Message,
            "exception escaped a timer callback: " + exception);
        Clock.CalibrateBackgroundThreads();

        int seed = configuration.Seed;
        Host = new HostDisplayStack(Clock, Trace, configuration.Host, unchecked(seed * 7 + 11));
        Controller = new GPUDDCController(Clock, Trace, unchecked(seed * 7 + 13));
        for (int index = 0; index < configuration.Monitors.Count; index++)
        {
            MonitorSetup setup = configuration.Monitors[index];
            LinkProfile link = LinkProfile.Create(setup.Link, configuration.Host.Visibility);
            link.BitErrorRate = setup.BitErrorRate;
            if (!setup.GarblesWhileOff) link.GarblesWhileSinkSilent = false;

            GPUConnector connector = Host.AddConnector(link);
            SimulatedMonitor monitor = new(
                setup.PhysicalID,
                setup.Profile.Clone(),
                setup.SerialDescriptor,
                setup.NumericSerial,
                Clock,
                Trace,
                unchecked(seed * 31 + index),
                setup.InitialBrightness);
            if (setup.StartsOff) monitor.StartPoweredOff();
            monitor.PowerStateChanged += Host.OnMonitorPowerChanged;
            Host.Cable(connector, monitor);
            _monitors.Add(monitor);
            _originalConnectorIndex[monitor] = connector.Index;
        }

        foreach (LinkKind spare in configuration.SpareConnectors)
            Host.AddConnector(LinkProfile.Create(spare, configuration.Host.Visibility));

        Ownership = new OwnershipResolver(_monitors, Host);
        _directory = TestEnvironment.CreateWorldDirectory(configuration.Name + "-" + seed.ToString(CultureInfo.InvariantCulture));
        TADNLog.MessageObserver = OnAppLog;
        Host.Start();
        foreach (SimulatedMonitor monitor in _monitors) _lastActiveTicks[monitor] = 0;
    }

    public WorldConfiguration Configuration { get; }
    public SimulationClock Clock { get; }
    public SimulationTrace Trace { get; }
    public SimulationOracle Oracle { get; }
    public SimulationDispatcher Dispatcher { get; }
    public HostDisplayStack Host { get; }
    public GPUDDCController Controller { get; }
    public OwnershipResolver Ownership { get; }

    /// <summary>The running app's display service. Each app start gets a new one, as each process does.</summary>
    public SimulatedDisplayService? Display => _app?.Display;
    public IReadOnlyList<SimulatedMonitor> Monitors => _monitors;

    public MonitorService? Service => _app?.Service;
    public CurveDriver? Curve => _app?.Curve;
    public AppSettings? Settings => _app?.Settings;
    public AppEventBridge? Bridge => _app?.Bridge;

    public SimulatedMonitor Monitor(string physicalID) =>
        _monitors.Single(monitor => monitor.PhysicalID == physicalID);

    /// <summary>Runs a scenario from a cold app start to the end of its tail and returns the oracle's verdict.</summary>
    public ScenarioResult Run(ScenarioScript script)
    {
        Trace.Add("scenario", script.Describe().TrimEnd());
        StartApp();
        foreach (ScenarioStep step in script.Steps)
        {
            ScenarioStep scheduled = step;
            Clock.ScheduleAt(scheduled.At.Ticks, () =>
            {
                Trace.Add("scenario", scheduled.Description);
                scheduled.Apply(this);
            });
        }

        foreach (TimeSpan checkpoint in script.Checkpoints)
            Clock.ScheduleAt(checkpoint.Ticks, () => Dispatcher.Post(CheckSettled));

        // Duplicates appear when a refresh or recovery publishes rows, which is checked from MonitorsRefreshed;
        // the slow tick only catches anything published another way
        _continuousCheckTimer = Clock.CreateTimer(
            _ => Dispatcher.Post(CheckContinuous),
            state: null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));

        // The user saved their profile long before this session; capture it once startup settles
        Clock.ScheduleAt(TimeSpan.FromSeconds(12).Ticks, () => Dispatcher.Post(SaveProfile));

        try
        {
            // A sleep that is the last step jumps the clock past the script's end; the tail still runs after wake.
            // Virtual time advances in slices so a runaway scenario stops at its real-time budget
            while (true)
            {
                TimeSpan afterWake = TimeSpan.FromTicks(Interlocked.Read(ref _lastWakeTicks)) + script.Tail;
                TimeSpan end = script.End > afterWake ? script.End : afterWake;
                if (Clock.Elapsed >= end) break;

                if (_realTime.Elapsed > Configuration.RealTimeBudget)
                {
                    Oracle.Observe(
                        $"stopped at {Clock.Elapsed.TotalSeconds:F0}s of {end.TotalSeconds:F0}s virtual: "
                        + $"real-time budget {Configuration.RealTimeBudget.TotalSeconds:F0}s spent");
                    break;
                }

                TimeSpan slice = Clock.Elapsed + TimeSpan.FromSeconds(60);
                Clock.RunUntil(slice < end ? slice : end);
            }

            Dispatcher.Post(CheckSettled);
            Clock.RunFor(TimeSpan.FromMilliseconds(500));
        }
        catch (SimulationHangException exception)
        {
            _hung = true;
            Trace.WriteTo(Path.Combine(TestEnvironment.RootDirectory, $"{script.Name}-seed{Configuration.Seed}-hang.trace.txt"));
            throw new SimulationHangException(exception.Message + Environment.NewLine + Trace.Format(maximumLines: 60));
        }

        _continuousCheckTimer.Dispose();
        return BuildResult(script);
    }

    // Lifecycle

    public void StartApp() => Dispatcher.Post(StartAppOnUIThread);

    public void RestartApp()
    {
        Trace.Add("scenario", "app restart");
        Dispatcher.Post(() =>
        {
            StopAppOnUIThread();
            StartAppOnUIThread();
        });
    }

    // Scenario actions. All return immediately; work happens as virtual time advances

    public void PressPowerButton(string physicalID) => Monitor(physicalID).PressPowerButton();

    /// <summary>
    /// Suspends the machine for <paramref name="duration"/>; timers that fall due meanwhile fire late, after wake.
    /// Later script steps keep their absolute times, so put the sleep's duration into any step meant to follow it;
    /// the scenario always runs at least its tail past the last wake.
    /// </summary>
    public void SystemSleep(TimeSpan duration)
    {
        Host.EnterSystemSleep();
        Clock.Jump(duration);
        Interlocked.Add(ref _totalSleptTicks, duration.Ticks);
        Interlocked.Exchange(ref _lastWakeTicks, Clock.ElapsedTicks);
        Clock.Schedule(TimeSpan.Zero, Host.ResumeFromSleep);
    }

    public void DisplaysOff() => Host.SetDisplaysPowered(false);

    public void DisplaysOn() => Host.SetDisplaysPowered(true);

    public void Unplug(string physicalID)
    {
        GPUConnector? connector = Host.ConnectorOf(Monitor(physicalID));
        if (connector != null) Host.Unplug(connector);
    }

    public void Plug(string physicalID, int connectorIndex) =>
        Host.Plug(Host.Connectors[connectorIndex], Monitor(physicalID));

    /// <summary>Plugs an unplugged panel back in: its original connector when asked and free, else the first free one.</summary>
    public void PlugIntoFreeConnector(string physicalID, bool preferOriginal)
    {
        SimulatedMonitor monitor = Monitor(physicalID);
        if (Host.ConnectorOf(monitor) != null) return;

        GPUConnector original = Host.Connectors[_originalConnectorIndex[monitor]];
        GPUConnector? target = preferOriginal && original.Monitor == null
            ? original
            : Host.Connectors.FirstOrDefault(connector => connector.Monitor == null);
        if (target == null)
        {
            Trace.Add("scenario", $"no free connector for {physicalID}");
            return;
        }

        Host.Plug(target, monitor);
    }

    /// <summary>Presses the power button only when it moves the panel toward the requested state.</summary>
    public void PressPowerButtonToward(string physicalID, bool on)
    {
        SimulatedMonitor monitor = Monitor(physicalID);
        bool isOff = monitor.PowerState is PanelPowerState.SoftOff or PanelPowerState.HardOff;
        if (on == isOff) monitor.PressPowerButton();
    }

    /// <summary>Raises the bit error rate of every link for <paramref name="duration"/>.</summary>
    public void NoisyBus(double bitErrorRate, TimeSpan duration)
    {
        Controller.ExtraBitErrorRate = bitErrorRate;
        Clock.Schedule(duration, () => Controller.ExtraBitErrorRate = 0);
    }

    /// <summary>
    /// End-of-timeline reset so the settled checks see every panel present: cables back in, panels switched on,
    /// console displays on, and app power intent returned to on.
    /// </summary>
    public void RestoreAllPanels()
    {
        DisplaysOn();
        foreach (SimulatedMonitor monitor in _monitors)
        {
            PlugIntoFreeConnector(monitor.PhysicalID, preferOriginal: true);
            PressPowerButtonToward(monitor.PhysicalID, on: true);
        }

        AppPowerAll(true);
    }

    public void DriverTransient() => Host.BeginTransient(Configuration.Host.TransientDurationMs);

    public void ModeChange() => Host.ChangeDisplayMode();

    public void SessionUnlock() => Host.RaiseSessionUnlock();

    public void SetBrightness(string physicalID, int percent) =>
        Dispatcher.Post(() =>
        {
            MonitorInfo? row = FindRow(physicalID);
            if (row == null) return;

            Oracle.NoteUserBrightnessAction(Monitor(physicalID));

            // BrightnessFlyoutWindow.DisengageCurveForUserAdjustment: grabbing a curve-driven slider releases the row
            if (_app is { Curve: { Enabled: true, InDisabledPeriod: false } })
                row.SliderState = SliderStateMachine.OnUserRelease(row.SliderState);
            row.Brightness = percent;
        });

    public void Drag(string physicalID, int from, int to, int steps, TimeSpan duration)
    {
        for (int step = 0; step <= steps; step++)
        {
            int value = from + (to - from) * step / Math.Max(val1: 1, steps);
            Clock.Schedule(duration * step / Math.Max(val1: 1, steps), () => SetBrightness(physicalID, value));
        }
    }

    public void AppPower(string physicalID, bool on) =>
        Dispatcher.Post(() =>
        {
            MonitorInfo? row = FindRow(physicalID);
            if (row == null || Service == null) return;

            Oracle.NotePowerAction();
            Oracle.NoteAppPowerIntent(Monitor(physicalID), on);
            _ = Service.SetPowerStateAsync(row, on);
        });

    public void AppPowerAll(bool on) =>
        Dispatcher.Post(() =>
        {
            if (Service == null) return;

            Oracle.NotePowerAction();
            foreach (MonitorInfo row in Service.Monitors.ToList())
            {
                if (!row.SupportsPowerControl) continue;

                Oracle.NoteAppPowerIntent(Ownership.OwnerOf(row.EDIDKey), on);
                _ = Service.SetPowerStateAsync(row, on);
            }
        });

    public void HardPowerOffCtrlClick(string physicalID) =>
        Dispatcher.Post(() =>
        {
            MonitorInfo? row = FindRow(physicalID);
            MonitorService? service = Service;
            if (row == null || service == null) return;

            Oracle.NotePowerAction();
            string serial = row.EDIDSerial;
            _ = Task.Run(() =>
            {
                bool succeeded = service.TryHardPowerOffByEDIDSerial(serial, out string? error);
                Trace.Add("scenario", $"hard power-off '{serial}' -> {succeeded} {error}");
            });
        });

    public void OpenFlyout() => Bridge?.RunSingleGatedScan();

    public void SetCurve(bool enabled, Func<TimeSpan, double>? masterTarget = null) =>
        Dispatcher.Post(() =>
        {
            if (_app == null) return;

            _app.Settings.EnvironmentalBrightnessCurveEnabled = enabled;
            if (masterTarget != null) _app.Curve.MasterTarget = masterTarget;
            _app.Curve.SetEnabled(enabled);
            _app.Curve.Evaluate();
        });

    public void SetIdentityStrategy(MonitorIdentityStrategy strategy) =>
        Dispatcher.Post(() =>
        {
            if (_app == null) return;

            _app.Settings.MonitorIdentityStrategy = strategy;
            _app.Settings.RaiseChanged();
        });

    public void SaveProfile() =>
        Dispatcher.Post(() =>
        {
            if (Service == null) return;

            List<(string ID, string EDIDKey)> identities = [.. Service.Monitors.Select(row => (row.ID, row.EDIDKey))];
            lock (_profileGate) _profileIdentities = identities;
            Trace.Add("scenario", "profile saved: " + string.Join(", ", identities.Select(identity => identity.ID + "/" + identity.EDIDKey)));
        });

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _continuousCheckTimer?.Dispose();

        // After a hang the parked threads never wake; skip the shutdown pump so disposal cannot hang too
        if (!_hung)
        {
            Dispatcher.Post(StopAppOnUIThread);
            Clock.RunFor(TimeSpan.FromSeconds(2));
        }

        Dispatcher.Dispose();
        TADNLog.MessageObserver = null;
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException exception) { Trace.Add("world", "cleanup failed: " + exception.Message); }
    }

    /// <summary>The first live row the user would see for a physical panel.</summary>
    public MonitorInfo? FindRow(string physicalID)
    {
        if (Service == null) return null;

        SimulatedMonitor monitor = Monitor(physicalID);
        foreach (MonitorInfo row in Service.Monitors)
        {
            if (ReferenceEquals(Ownership.OwnerOf(row.EDIDKey), monitor)) return row;
        }

        return null;
    }

    public string DescribeRows()
    {
        if (Service == null) return "(app not running)";

        return string.Join(" | ", Service.Monitors.Select(row => RowSnapshot.From(row).Describe()));
    }

    private void StartAppOnUIThread()
    {
        AppConfiguration app = Configuration.App;
        AppSettings settings = new()
        {
            MonitorIdentityStrategy = app.IdentityStrategy,
            ValidationAttempts = app.ValidationAttempts,
            ValidationDwellMs = app.ValidationDwellMs,
            BrightnessUpdateRateMs = app.BrightnessUpdateRateMs,
            DDCOperationTimeoutMs = app.DDCOperationTimeoutMs,
            AllowBlindDDCWritesDuringDegradedState = app.AllowBlindDDCWrites,
            PowerOffMode = app.PowerOffMode,
            DefaultDisplaySortMode = app.SortMode,
            EnvironmentalBrightnessCurveEnabled = app.CurveEnabled
        };
        KnownDisplaysStore store = new(Path.Combine(_directory, "displays.json"));
        SimulatedDisplayService display = new(
            Clock,
            Trace,
            Host,
            Controller,
            Configuration.DisplayService,
            _monitors,
            unchecked(Configuration.Seed * 7 + 17 + _appStarts++));
        display.RoutingObserved += Oracle.ObserveRouting;
        display.WriteObserved += OnWriteObserved;
        _displayServices.Add(display);

        // Same order as App.StartServices: the constructor enumerates and probes synchronously on the UI thread
        MonitorService service = new(display, settings, store, Dispatcher, Clock);
        AppEventBridge bridge = new(Clock, Dispatcher, Trace, Host, service, ReadProfileIdentities);
        bridge.Start();
        DDCRecoveryService recovery = new(service, TimeConstants.DDCRecoveryRetryIntervalMs, Clock);
        recovery.Start();
        CurveDriver curve = new(Clock, Dispatcher, Trace, service, app.CurveTickIntervalMs) { Enabled = app.CurveEnabled };
        curve.Start();
        service.MonitorsRefreshed += () => Dispatcher.Post(CheckContinuous);
        _app = new AppInstance(settings, store, display, service, bridge, recovery, curve);
        Trace.Add("world", "app started");
    }

    private void StopAppOnUIThread()
    {
        AppInstance? app = _app;
        _app = null;
        if (app == null) return;

        app.Curve.Dispose();
        app.Bridge.Dispose();
        app.Recovery.Dispose();

        // Disposes the display service too, which kills the helpers the way their parent watchdog does
        app.Service.Dispose();
        Trace.Add("world", "app stopped");
    }

    private List<(string ID, string EDIDKey)>? ReadProfileIdentities()
    {
        lock (_profileGate) return _profileIdentities;
    }

    private void CheckContinuous()
    {
        if (Service == null) return;

        foreach (SimulatedMonitor monitor in _monitors)
        {
            if (Host.IsMonitorActive(monitor)) _lastActiveTicks[monitor] = Clock.ElapsedTicks;
        }

        _rowsSnapshot = [.. Service.Monitors];
        Oracle.CheckContinuous([.. _rowsSnapshot.Select(RowSnapshot.From)], Ownership);
    }

    /// <summary>
    /// Runs as each SET is submitted to the GPU's FIFO, so in the order panels receive them. A brightness SET is checked
    /// against the app's power-off intent only once the power-off SET of that intent has reached the panel: a SET
    /// already in flight when the intent was published lands first and cannot be recalled. Row power intent is read
    /// from the latest row snapshot; the flag itself is a volatile field, safe to read off the UI thread.
    /// </summary>
    private void OnWriteObserved(SimulatedMonitor monitor, byte code, uint value)
    {
        if (code == DDCCI.VCPConstants.PowerMode)
        {
            // SetPowerStateAsync publishes its intent before the SET. A Ctrl+click hard-off publishes none, so its SET
            // must not arm the check for brightness SETs that were already in flight
            bool armed = value != PowerModeOnValue && OwnedRows().Any(row => row.IsPowerSuppressed);
            lock (_powerWritesGate) _sentPowerOff[monitor] = armed;
            return;
        }

        if (code != DDCCI.VCPConstants.Brightness) return;

        lock (_powerWritesGate)
        {
            if (!_sentPowerOff.GetValueOrDefault(monitor)) return;
        }

        Oracle.CheckBrightnessWrite(monitor, value, OwnedRows());
        return;

        List<RowSnapshot> OwnedRows()
        {
            List<RowSnapshot> owned = [];
            foreach (MonitorInfo row in _rowsSnapshot)
            {
                if (ReferenceEquals(Ownership.OwnerOf(row.EDIDKey), monitor)) owned.Add(RowSnapshot.From(row));
            }

            return owned;
        }
    }

    private void CheckSettled()
    {
        if (_app == null) return;

        CheckContinuous();
        Oracle.CheckSettled(new SettledState
        {
            Rows = [.. _app.Service.Monitors.Select(RowSnapshot.From)],
            Bindings = _app.Service.CaptureBindings(),
            Monitors = _monitors,
            Host = Host,
            Ownership = Ownership,
            Curve = _app.Curve,
            LastActiveTicks = _lastActiveTicks,
            Grace = Configuration.SettleGrace,
            LiveHelpers = _app.Display.LiveHelpers,
            HelperKeys = _app.Display.HelperKeys,
            ValidationAttempts = _app.Settings.ValidationAttempts,
            LastBusBrightness = key => _app.Store.Find(key)?.LastBusBrightness
        });
    }

    private void OnAppLog(string message)
    {
        Trace.Add("app", message);
        foreach (string marker in AppExceptionMarkers)
        {
            if (!message.Contains(marker, StringComparison.Ordinal)) continue;

            Oracle.Report(ViolationKind.UnhandledException, "log:" + message, "app logged a caught exception: " + message);
            return;
        }
    }

    private ScenarioResult BuildResult(ScenarioScript script)
    {
        // Each helper start is a NativeAOT process launch; more than a couple per minute is churn, not recovery
        double spawnRate = HelperSpawnsPerAwakeHour;
        if (spawnRate > 120)
        {
            Oracle.Observe(
                $"helper churn: {spawnRate:F0} helper process launches per awake hour "
                + $"({_displayServices.Sum(display => display.HelperSpawns)} total, {Controller.Operations} DDC calls)");
        }

        StringBuilder report = new();
        report.AppendLine($"=== {script.Name} (seed {Configuration.Seed}) ===");
        report.Append(Configuration.Describe());
        report.Append(script.Describe());
        report.AppendLine("final topology: " + Host.DescribeTopology());
        report.AppendLine("final rows: " + DescribeRows());
        foreach (SimulatedMonitor monitor in _monitors) report.AppendLine("  " + monitor.Describe());
        report.AppendLine(
            $"stats: virtual {Clock.Elapsed.TotalSeconds:F1}s in {_realTime.Elapsed.TotalSeconds:F2}s real, "
            + $"{Clock.TimerFires} timer fires, {Clock.StallAdvances} stall advances, "
            + $"{Controller.Operations} bus calls (peak queue {Controller.MaximumQueueDepth}), "
            + $"{_displayServices.Sum(display => display.HelperSpawns)} helper spawns, "
            + $"{_displayServices.Sum(display => display.HelperKills)} kills, "
            + $"peak {_displayServices.Select(display => display.PeakLiveHelpers).DefaultIfEmpty(0).Max()} live, "
            + $"{_appStarts} app start(s)");

        IReadOnlyList<Violation> violations = Oracle.Violations;
        if (violations.Count > 0 || Oracle.Observations.Count > 0)
        {
            report.AppendLine("findings:");
            report.Append(Oracle.Summarize());
        }

        string tracePath = Path.Combine(
            TestEnvironment.RootDirectory,
            $"{script.Name}-seed{Configuration.Seed.ToString(CultureInfo.InvariantCulture)}.trace.txt");
        if (Configuration.AlwaysWriteTrace) Trace.WriteTo(tracePath);
        if (violations.Count > 0)
        {
            Trace.WriteTo(tracePath);
            long firstTicks = violations.Min(violation => violation.Ticks);
            report.AppendLine($"trace before first violation (full trace: {tracePath}):");
            report.Append(Trace.Format(maximumLines: 80, firstTicks));
        }

        return new ScenarioResult
        {
            Name = script.Name,
            Seed = Configuration.Seed,
            Violations = violations,
            Observations = Oracle.Observations,
            Report = report.ToString(),
            TracePath = violations.Count > 0 ? tracePath : string.Empty
        };
    }

    private sealed record AppInstance(
        AppSettings Settings,
        KnownDisplaysStore Store,
        SimulatedDisplayService Display,
        MonitorService Service,
        AppEventBridge Bridge,
        DDCRecoveryService Recovery,
        CurveDriver Curve);
}

using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Services;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>
/// Delivers host notifications to <see cref="MonitorService"/> the way DisplayEventManager and
/// App.OnDisplayTopologyChanged do. The Win32 plumbing is mirrored (250 ms UI-thread debounce, 1 s x 10 burst scanner
/// on a timer thread, flyout-open scan); the decisions call DisplayEventManager's own gate and gap logic.
/// </summary>
internal sealed class AppEventBridge(
    SimulationClock clock,
    SimulationDispatcher dispatcher,
    SimulationTrace trace,
    HostDisplayStack host,
    MonitorService monitorService,
    Func<IReadOnlyList<(string ID, string EDIDKey)>?> profileIdentities) : IDisposable
{
    private const int BurstMaximumTicks = 10;

    private readonly Lock _burstGate = new();
    private ITimer? _debounceTimer;
    private long _debounceGeneration;
    private ITimer? _burstTimer;
    private int _burstTickCount;
    private bool _burstActive;
    private long _burstGeneration;
    private int _scanInProgress;
    private volatile string? _lastRefreshedGap;
    private volatile bool _disposed;

    public void Start()
    {
        host.EventRaised += OnHostEvent;
        monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        host.EventRaised -= OnHostEvent;
        monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
        _debounceTimer?.Dispose();
        StopBurst(expectedGeneration: null);
    }

    /// <summary>
    /// BrightnessFlyout.Show calls DisplayEventManager.RunSingleGatedScan, which first asks recovery to probe the
    /// unavailable rows now. Must run on the simulated UI thread.
    /// </summary>
    public void RunSingleGatedScan()
    {
        if (_disposed) return;

        monitorService.RequestRecoveryOfUnavailableMonitors();

        if (AllProfileMonitorsLoaded())
        {
            trace.Add("bridge", "flyout scan short-circuit");
            return;
        }

        _ = Task.Run(ScanAndReconcile);
    }

    private void OnHostEvent(HostEventKind kind)
    {
        if (_disposed) return;

        switch (kind)
        {
            case HostEventKind.MonitorInterfaceArrival:
            case HostEventKind.MonitorInterfaceRemoval:
                Kick();
                OnMonitorHardwareEvent();
                break;
            case HostEventKind.DisplaySettingsChanged:
            case HostEventKind.PowerResume:
            case HostEventKind.SessionUnlock:
                Kick();
                break;
        }
    }

    /// <summary>Each signal pushes the debounce out another 250 ms; the tick runs on the UI thread.</summary>
    private void Kick() =>
        dispatcher.Post(() =>
        {
            if (_disposed) return;

            _debounceTimer?.Dispose();
            long generation = ++_debounceGeneration;
            _debounceTimer = clock.Schedule(
                TimeSpan.FromMilliseconds(TimeConstants.DisplayEventDebounceIntervalMs),
                () => dispatcher.Post(() =>
                {
                    if (_disposed || generation != _debounceGeneration) return;

                    trace.Add("bridge", "DisplayTopologyChanged -> NotifyTopologyEvent + Refresh");
                    monitorService.NotifyTopologyEvent();
                    monitorService.Refresh();
                }));
        });

    private void OnMonitorHardwareEvent()
    {
        _lastRefreshedGap = null;
        lock (_burstGate)
        {
            _burstTickCount = 0;
            if (_burstActive) return;

            _burstActive = true;
            long generation = ++_burstGeneration;
            _burstTimer = clock.CreateTimer(
                OnBurstTick,
                generation,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(TimeConstants.DisplayEventBurstIntervalMs));
        }

        trace.Add("bridge", "burst start");
    }

    private void OnBurstTick(object? state)
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
            trace.Add("bridge", "burst short-circuit");
            StopBurst(generation);
            return;
        }

        trace.Add("bridge", $"burst tick {tickCount}");
        ScanAndReconcile();

        bool timedOut;
        lock (_burstGate)
        {
            if (_disposed || !_burstActive || generation != _burstGeneration) return;

            timedOut = _burstTickCount >= BurstMaximumTicks;
        }

        if (!timedOut) return;

        trace.Add("bridge", "burst timed out");
        StopBurst(generation);
    }

    private void OnMonitorsRefreshed()
    {
        bool active;
        lock (_burstGate) active = _burstActive;
        if (active && AllProfileMonitorsLoaded()) StopBurst(expectedGeneration: null);
    }

    private void StopBurst(long? expectedGeneration)
    {
        ITimer? timer;
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
            // SetupDiGetClassDevs over the monitor class takes a few milliseconds
            clock.Sleep(TimeSpan.FromMilliseconds(2));
            HashSet<string> reported = new(host.PresentMonitorInstanceIDs(), StringComparer.OrdinalIgnoreCase);
            string? gap = DisplayEventManager.DescribeMonitorGap(reported, monitorService.CaptureMonitorPresence());
            string? refreshedGap = _lastRefreshedGap;
            _lastRefreshedGap = gap;
            if (gap == null) return;

            if (string.Equals(gap, refreshedGap, StringComparison.Ordinal))
            {
                trace.Add("bridge", "gap unchanged since its Refresh");
                return;
            }

            trace.Add("bridge", "gap detected -> NotifyTopologyEvent + Refresh");
            monitorService.NotifyTopologyEvent();
            monitorService.Refresh();
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, value: 0);
        }
    }

    private bool AllProfileMonitorsLoaded()
    {
        try
        {
            return DisplayEventManager.AreProfileMonitorsLoaded(profileIdentities(), monitorService.Monitors);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>
/// The parts of EnvironmentalCurveService that touch <see cref="MonitorService"/>, in absolute mode: harmonize rows into
/// curve states (never past Failed), stamp each curve-driven row's target, and send CurveActive rows to hardware through
/// EnqueueDirectBrightness. Evaluates on a periodic UI-thread tick and synchronously on MonitorsRefreshed.
/// </summary>
internal sealed class CurveDriver(
    SimulationClock clock,
    SimulationDispatcher dispatcher,
    SimulationTrace trace,
    MonitorService monitorService,
    int tickIntervalMs) : IDisposable
{
    private ITimer? _timer;
    private double _lastMasterTarget = double.NaN;
    private bool _disposed;

    public bool Enabled { get; set; }

    public bool InDisabledPeriod { get; set; }

    /// <summary>Master curve sample over virtual time since simulation start.</summary>
    public Func<TimeSpan, double> MasterTarget { get; set; } = static _ => 50;

    /// <summary>Virtual time the master target last changed, for the oracle's quiet-period test.</summary>
    public long LastTargetChangeTicks { get; private set; }

    public void Start()
    {
        monitorService.IsBrightnessCurveEnabledQuery = () => Enabled;
        monitorService.IsInDisabledPeriodQuery = () => InDisabledPeriod;
        monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
        TimeSpan interval = TimeSpan.FromMilliseconds(tickIntervalMs);
        _timer = clock.CreateTimer(_ => dispatcher.Post(Evaluate), state: null, interval, interval);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _timer?.Dispose();
        monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
        monitorService.IsBrightnessCurveEnabledQuery = null;
        monitorService.IsInDisabledPeriodQuery = null;
    }

    /// <summary>
    /// The flyout's curve toggle. On: EngageBrightnessCurveStates (every row but Disabled and Failed into curve
    /// control, releases cleared) then an immediate evaluation. Off: DisengageBrightnessCurveStates, then
    /// ResyncBrightnessHardwareToSliders writes each participating row's slider value back to its panel.
    /// Must run on the simulated UI thread.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == Enabled) return;

        Enabled = enabled;
        if (enabled)
        {
            foreach (MonitorInfo row in monitorService.Monitors)
                SetCurveStateWithSeed(row, SliderStateMachine.OnCurveEngaged(row.SliderState, InDisabledPeriod));
            Evaluate();
            return;
        }

        foreach (MonitorInfo row in monitorService.Monitors)
        {
            row.ClearCurveTargetBrightness();
            row.SliderState = SliderStateMachine.OnCurveDisengaged(row.SliderState);
        }

        foreach (MonitorInfo row in monitorService.Monitors.ToList())
        {
            if (row.IsParticipatingInMaster) monitorService.EnqueueDirectBrightness(row, row.RoundedBrightness);
        }

        trace.Add("curve", "curve off; sliders resynced to hardware");
    }

    /// <summary>Expected hardware percentage for a CurveActive row.</summary>
    public int ExpectedRowTarget(MonitorInfo row) =>
        (int)Math.Round(Math.Clamp(CurrentMasterTarget() + row.Offset, min: 0.0, max: 100.0));

    public double CurrentMasterTarget() => Math.Clamp(MasterTarget(clock.Elapsed), min: 0.0, max: 100.0);

    /// <summary>One curve tick. Must run on the simulated UI thread.</summary>
    public void Evaluate()
    {
        if (_disposed || !Enabled) return;

        foreach (MonitorInfo row in monitorService.Monitors) Harmonize(row);

        double master = CurrentMasterTarget();
        if (!master.Equals(_lastMasterTarget))
        {
            _lastMasterTarget = master;
            LastTargetChangeTicks = clock.ElapsedTicks;
            trace.Add("curve", $"master target {master:F1}");
        }

        foreach (MonitorInfo row in monitorService.Monitors)
        {
            if (row.IsCurveDriven) row.CurveTargetBrightness = Math.Clamp(master + row.Offset, min: 0.0, max: 100.0);
        }

        foreach (MonitorInfo row in monitorService.Monitors.ToList())
        {
            if (row.SliderState != SliderState.CurveActive) continue;

            monitorService.EnqueueDirectBrightness(row, ExpectedRowTarget(row));
        }
    }

    private void OnMonitorsRefreshed() => Evaluate();

    private void Harmonize(MonitorInfo row)
    {
        if (row.SliderState == SliderState.Failed || !row.IsHardwareFunctional) return;

        SliderState next = row.SliderState switch
        {
            SliderState.Enabled => InDisabledPeriod ? SliderState.CurveSleeping : SliderState.CurveActive,
            SliderState.CurveActive when InDisabledPeriod => SliderState.CurveSleeping,
            SliderState.CurveSleeping when !InDisabledPeriod => SliderState.CurveActive,
            _ => row.SliderState
        };

        SetCurveStateWithSeed(row, next);
    }

    private static void SetCurveStateWithSeed(MonitorInfo row, SliderState next)
    {
        bool wasCurveDriven = row.SliderState is SliderState.CurveActive or SliderState.CurveSleeping;
        bool willBeCurveDriven = next is SliderState.CurveActive or SliderState.CurveSleeping;
        if (willBeCurveDriven && !wasCurveDriven) row.SeedCurveTargetBrightnessFromSlider();
        row.SliderState = next;
    }
}

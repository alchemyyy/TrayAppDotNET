using System.Collections.ObjectModel;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.Utils;
using Microsoft.Win32;
using TrayAppDotNETCommon.Services;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Background evaluator for the active profile's <see cref="EnvironmentalCurve"/>. Owns the periodic
/// DispatcherTimer, the sun-shifted curve cache, and the per-tick hardware writes (brightness via
/// <see cref="MonitorService.EnqueueDirectBrightness"/>, night-light via
/// <see cref="NightLightProvider.SetStrength(int)"/>). Promoted out of <c>BrightnessFlyout</c> so curves
/// continue to drive monitors while the flyout is hidden.
///
/// LOAD-BEARING INVARIANT: the curve evaluator NEVER writes to per-monitor offset / user-intent state.
/// Specifically forbidden: any assignment to <see cref="MonitorInfo.Offset"/>,
/// <see cref="MonitorInfo.LastUserBrightness"/>, the per-row IsSliderEnabled state, or anything else
/// that records the user's intended per-monitor relationship. The curve consumes these fields as inputs
/// (absolute mode reads <c>Offset</c>; offset mode reads <c>LastUserBrightness</c>) and produces
/// hardware writes via <see cref="MonitorService.EnqueueDirectBrightness"/> + indicator writes via
/// <see cref="MonitorInfo.CurveTargetBrightness"/>. Nothing else.
///
/// Offsets and LastUserBrightness change only on user actions (slider touch / wheel / keyboard, group
/// toggle, individual re-engage) and topology / enrollment events (AttachMonitor, DetachMonitor,
/// PromoteRecovered, curve-toggle-ON snapshot). The curve walking the master across the day must NOT
/// reach into those fields - if it does, a user's per-monitor spread would drift over a curve session,
/// and re-engaging an individual would land at a corrupted baseline.
/// </summary>
public sealed class EnvironmentalCurveService : IDisposable
{
    private const string CurveEventEvaluationKey = "environmental-curve";
    private const string NightLightHardwareKey = "night-light";
    private const string BrightnessHardwareKeyPrefix = "brightness:";

    private readonly ProfileManager _profileManager;
    private readonly MonitorService _monitorService;
    private readonly AppSettings? _appSettings;
    private readonly ObservableCollection<MonitorInfo> _monitors;
    private readonly MonitorInfo _masterMonitor;
    private readonly MonitorInfo _nightLightMonitor;
    private readonly Func<int, int> _flipIfNightLightInverted;
    private readonly Action<bool>? _onDisabledPeriodChanged;
    private readonly AsyncThrottler<string> _curveEventEvaluationThrottler = new(0, StringComparer.Ordinal);
    private readonly AsyncThrottler<string> _curveHardwareThrottler = new(0, StringComparer.Ordinal);

    private DispatcherTimer? _curveTimer;

    // Sun-shifted runtime curve cache so we don't pay an SPA round trip every tick. Keyed by
    // (source-curve reference, source-curve Version, today's date, current location, DST flag);
    // rebuilds whenever any axis changes. The reference check catches "different curve object"
    // (profile switch, promote-on-edit), and the Version check catches "same reference, mutated points"
    // - the settings editor mutates the curve's lists in place rather than cloning, so without the
    // Version stamp a FollowTheSun-on profile would keep sampling the pre-edit shape until midnight.
    // Mutation sites that take the in-place path bump Version via <see cref="InvalidateCurveCache"/> /
    // <see cref="RequestEvaluation"/>; the cache key check below picks up the bump deterministically.
    private EnvironmentalCurve? _cachedShiftedCurve;
    private DateTime _cachedShiftedDate = DateTime.MinValue;
    private double _cachedShiftedLat;
    private double _cachedShiftedLon;
    private bool _cachedShiftedDst;
    private object? _cachedShiftedSourceCurve;
    private int _cachedShiftedSourceVersion;

    private bool _isInDisabledPeriod;
    private bool _isSuspended;
    private bool _disposed;

    public EnvironmentalCurveService(
        ProfileManager profileManager,
        MonitorService monitorService,
        AppSettings? appSettings,
        ObservableCollection<MonitorInfo> monitors,
        MonitorInfo masterMonitor,
        MonitorInfo nightLightMonitor,
        Func<int, int> flipIfNightLightInverted,
        Action<bool>? onDisabledPeriodChanged = null)
    {
        _profileManager = profileManager;
        _monitorService = monitorService;
        _appSettings = appSettings;
        _monitors = monitors;
        _masterMonitor = masterMonitor;
        _nightLightMonitor = nightLightMonitor;
        _flipIfNightLightInverted = flipIfNightLightInverted;
        _onDisabledPeriodChanged = onDisabledPeriodChanged;

        // Profile switch invalidates the sun-shifted cache: a new profile reference defeats the
        // ReferenceEquals(source) gate naturally, but explicitly nulling here is cheap insurance against a
        // future setter that mutates points in place on the same EnvironmentalCurve instance.
        _profileManager.SelectedProfileChanged += OnSelectedProfileChanged;

        // Topology / recovery / hot-plug fires MonitorsRefreshed synchronously after MonitorService's
        // read-only acquisition phase. We piggyback on it to run an immediate Evaluate so freshly-promoted
        // Enabled rows get harmonized into CurveActive and receive their curve target without waiting for
        // the next periodic tick.
        _monitorService.MonitorsRefreshed += OnMonitorsRefreshed;

        // DST transition / timezone change / user-set clock change invalidates the sun-shifted cache:
        // SunShifter's SPA evaluation honours the local UTC offset, so the cached shape can be stale by
        // up to one full day after a TZ change until the next midnight rolls over.
        // SystemEvents.TimeChanged marshals onto its own thread; the handler only nulls cache fields
        // and posts the immediate re-evaluation back to the dispatcher.
        SystemEvents.TimeChanged += OnSystemTimeChanged;

        // Arm the periodic timer unconditionally at construction so the disabled-period detection runs
        // regardless of the persisted curve-flag state - the flyout's crescent glyph swap and the
        // IsInDisabledPeriod mirror both depend on Evaluate firing even when both curve toggles are off.
        // Start() is idempotent and short-circuits cleanly if no profile is loaded yet.
        Start();
    }

    /// <summary>
    /// Whether the brightness curve is currently engaged. Mirrors <c>BrightnessFlyout.IsBrightnessCurveEnabled</c>
    /// - the flyout's setter pushes the new value here so the periodic evaluator picks it up on the next tick.
    /// </summary>
    public bool IsBrightnessCurveEnabled { get; set; }

    /// <summary>
    /// Whether the night-light curve is currently engaged. Mirrors <c>BrightnessFlyout.IsNightLightCurveEnabled</c>.
    /// </summary>
    public bool IsNightLightCurveEnabled { get; set; }

    /// <summary>
    /// Returns the night-light strength (0..100) the curve would write at this moment,
    /// or null when the curve isn't actively driving night light right now
    /// (curve disengaged, sleeping inside a disabled period, no profile loaded, backend unavailable).
    /// The toggle-on path uses this to seed the backend with the curve's value instead of
    /// <see cref="AppSettings.NightLightLastNonZeroStrength"/>,
    /// so enabling night light while the curve is engaged lands on the curve's warmth
    /// rather than the user's last manual pick.
    /// </summary>
    public int? GetActiveNightLightCurveStrength()
    {
        if (_disposed) return null;
        if (!IsNightLightCurveEnabled) return null;
        if (_nightLightMonitor.SliderState != SliderState.CurveActive) return null;

        EnvironmentalCurve? curve = ResolveLiveCurve();
        EnvironmentalCurve? stored = ResolveStoredCurve();
        if (curve == null || stored == null) return null;

        double t = EnvironmentalCurveSampler.CurrentDayFraction();
        if (EnvironmentalCurveSampler.IsInDisabledPeriod(stored, t)) return null;

        double smoothness = (_appSettings?.EnvironmentalCurveSmoothness ?? 100) / 100.0;
        if (IsCurveAbsoluteMode)
        {
            double sample = EnvironmentalCurveSampler.Sample(curve.NightLight, t, smoothness);
            if (!double.IsFinite(sample)) sample = 100.0;
            return Math.Clamp((int)Math.Round(sample), 0, 100);
        }

        // Offset mode mirrors ApplyNightLightCurve's offset branch: deviation around 50 -> +/-100 percent,
        // stacked on top of the slider's current strength (slider position run through the invert mapper).
        double offsetSample = EnvironmentalCurveSampler.Sample(curve.NightLightOffset, t, smoothness);
        if (!double.IsFinite(offsetSample)) offsetSample = 100.0;
        double offsetPercent = (offsetSample - 50.0) * 2.0;
        int currentStrength = _flipIfNightLightInverted(_nightLightMonitor.RoundedBrightness);
        return Math.Clamp((int)Math.Round(currentStrength + offsetPercent), 0, 100);
    }

    /// <summary>
    /// True while the active profile's disabled-period window is currently passing through. Updated only inside
    /// <see cref="Evaluate"/>; the flyout mirrors this onto its own <c>IsInCurveDisabledPeriod</c> via the
    /// <c>onDisabledPeriodChanged</c> callback.
    /// </summary>
    public bool IsInDisabledPeriod => _isInDisabledPeriod;

    /// <summary>
    /// While true the periodic <see cref="Evaluate"/> tick is paused. The flyout's preview-sweep flips this on
    /// for the duration of the 24h animation so a real-time tick can't stomp a simulated frame.
    /// </summary>
    public bool IsSuspended => _isSuspended;

    /// <summary>
    /// Ensures the periodic curve-evaluation timer is running whenever an environmental curve profile resolves,
    /// with its interval matching the user-configured tick rate. Runs even when both curve flags are off so the
    /// disabled-period flag stays accurate (the flyout's crescent glyph swap depends on it firing while the user
    /// has manually disabled curves). Each tick still short-circuits the apply paths via the curve-enable flags
    /// inside Evaluate, so the only flag-off cost is one IsInDisabledPeriod sample (a couple of float
    /// comparisons). Idle when no profile resolves so the app pays nothing while the feature is unused.
    /// Reentrant: callable on every settings change to pull a fresh interval value into the live timer without
    /// recreating it.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        bool wantTimer = ResolveStoredCurve() != null && !_isSuspended;
        TimeSpan interval = ResolveCurveTickInterval();

        if (wantTimer)
        {
            if (_curveTimer == null)
            {
                _curveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval, };
                _curveTimer.Tick += (_, _) => Evaluate();
                _curveTimer.Start();
                return;
            }

            // Live-update the interval if the user just changed it; cheap to write and DispatcherTimer picks
            // up the new value on the next tick.
            if (_curveTimer.Interval != interval) _curveTimer.Interval = interval;
            if (!_curveTimer.IsEnabled) _curveTimer.Start();
        }
        else if (_curveTimer is { IsEnabled: true }) _curveTimer.Stop();
    }

    /// <summary>
    /// Stops the periodic timer unconditionally. Curve flags are left untouched so a subsequent
    /// <see cref="Start"/> resumes the normal "engaged-if-flag-on" behaviour.
    /// </summary>
    public void Stop()
    {
        if (_disposed) return;
        if (_curveTimer is { IsEnabled: true }) _curveTimer.Stop();
    }

    /// <summary>
    /// Pauses the periodic tick (without forgetting the curve flags) and stops any pending throttled re-evaluation.
    /// Used by the flyout's 24h preview sweep so a real-time evaluator can't stomp a simulated frame mid-animation.
    /// </summary>
    public void Suspend()
    {
        if (_disposed) return;
        _isSuspended = true;
        if (_curveTimer is { IsEnabled: true }) _curveTimer.Stop();
        _curveEventEvaluationThrottler.Drop(CurveEventEvaluationKey);
        DropQueuedHardwareWrites();
    }

    /// <summary>
    /// Releases the suspend gate and re-arms the periodic tick if any curve flag is engaged.
    /// Drives one immediate evaluation so monitors snap back to real-now in the same frame.
    /// </summary>
    public void Resume()
    {
        if (_disposed) return;
        _isSuspended = false;
        DropQueuedHardwareWrites();
        Start();
        Evaluate();
    }

    /// <summary>
    /// Public entry point for event-driven curve re-evaluation. Settings-window curve edits (point drag, period
    /// pin drag, period toggle, follow-the-sun flip) call this to push the new shape onto the monitor in
    /// real-time, instead of waiting for the next periodic tick. No-op when neither curve flag is engaged so an
    /// open settings session doesn't run the evaluator while the user has both toggles off.
    ///
    /// Throttled to <see cref="AppSettings.BrightnessUpdateRateMs"/> with latest-pending-wins semantics:
    /// the first request posts an evaluation immediately, then replacements collapse while the cooldown is
    /// passing. During a drag this keeps hardware/UI targets moving at the same cadence as slider DDC writes
    /// while dropping intermediate curve shapes that would already be stale by the time they ran.
    /// </summary>
    public void RequestEvaluation()
    {
        if (_disposed) return;
        if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled) return;
        if (_isSuspended) return;

        // Invalidate the sun-shifted clone cache so the upcoming evaluation rebuilds from the freshly-edited
        // stored curve. The cache keys on (ReferenceEquals(source), Version) - reference catches the
        // profile-switch path, Version catches the in-place mutation path. Bump Version on the stored
        // curve as well so any other RequestEvaluation-bypassing call site that compares Version
        // post-edit sees the shape has changed.
        BumpCurveVersionAndDropCache();

        int rateMs = Math.Max(TimeConstants.BrightnessUpdateRateMinMs,
            _appSettings?.BrightnessUpdateRateMs ?? TimeConstants.BrightnessUpdateRateDefaultMs);
        _curveEventEvaluationThrottler.CooldownMs = rateMs;
        _ = _curveEventEvaluationThrottler.RunAsync(
            CurveEventEvaluationKey,
            _ => InvokeEvaluateOnDispatcherAsync());
    }

    private Task InvokeEvaluateOnDispatcherAsync()
    {
        if (_disposed || _isSuspended) return Task.CompletedTask;

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    if (!_disposed && !_isSuspended) Evaluate();
                    completionSource.TrySetResult();
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            },
            DispatcherPriority.Background);

        return completionSource.Task;
    }

    /// <summary>
    /// Drops the sun-shifted curve cache so the next evaluation rebuilds from scratch. Called when a curve's
    /// points have been mutated in place (settings-window edit) so a FollowTheSun-enabled profile picks up the
    /// freshly-edited shape. Also bumps the stored curve's <see cref="EnvironmentalCurve.Version"/> so any
    /// future cache key consumer can detect the in-place mutation via the integer stamp alone.
    /// </summary>
    public void InvalidateCurveCache()
    {
        if (_disposed) return;
        BumpCurveVersionAndDropCache();
    }

    /// <summary>
    /// Shared invalidation primitive: nulls the sun-shifted cache fields AND bumps the stored
    /// curve's <see cref="EnvironmentalCurve.Version"/> so the next <see cref="ResolveLiveCurve"/>
    /// cache lookup misses on both the reference path and the version path. Safe to call when no
    /// profile resolves - just nulls the cache.
    /// </summary>
    private void BumpCurveVersionAndDropCache()
    {
        EnvironmentalCurve? stored = ResolveStoredCurve();
        if (stored != null) stored.Version++;
        _cachedShiftedSourceCurve = null;
        _cachedShiftedSourceVersion = 0;
        _cachedShiftedCurve = null;
    }

    /// <summary>
    /// Drops every row out of curve states (CurveActive / CurveSleeping / CurveReleased)
    /// back to their non-curve baseline (Enabled, or stays Disabled / Failed).
    /// Called on full curve-off and on profile switch so a freshly-loaded profile starts from a clean visual slate -
    /// the indicator glyphs disappear via the IsCurveDriven binding flipping false.
    /// </summary>
    public void ClearCurveTargets()
    {
        if (_disposed) return;
        _masterMonitor.ClearCurveTargetBrightness();
        _nightLightMonitor.ClearCurveTargetBrightness();
        foreach (MonitorInfo monitor in _monitors)
            monitor.ClearCurveTargetBrightness();

        _masterMonitor.SliderState = SliderStateMachine.OnCurveDisengaged(_masterMonitor.SliderState);
        _nightLightMonitor.SliderState = SliderStateMachine.OnCurveDisengaged(_nightLightMonitor.SliderState);
        foreach (MonitorInfo monitor in _monitors)
            monitor.SliderState = SliderStateMachine.OnCurveDisengaged(monitor.SliderState);
    }

    /// <summary>
    /// Curve-toggle-ON transition: pushes every brightness-curve row into CurveActive / CurveSleeping
    /// given the live disabled-period flag. Clears stale CurveReleased from a prior session so the
    /// freshly-enabled curve drives every eligible row (the table the user signed off on).
    /// Disabled / Failed are preserved by <see cref="SliderStateMachine.OnCurveEngaged"/>.
    /// Caller-driven event - DO NOT use this in the per-tick path; <see cref="HarmonizeBrightnessCurveStates"/>
    /// is the per-tick variant that respects in-flight CurveReleased.
    /// </summary>
    public void EngageBrightnessCurveStates()
    {
        if (_disposed) return;
        bool inDisabled = _isInDisabledPeriod;
        SetCurveStateWithSeed(_masterMonitor,
            SliderStateMachine.OnCurveEngaged(_masterMonitor.SliderState, inDisabled));
        foreach (MonitorInfo monitor in _monitors)
            SetCurveStateWithSeed(monitor, SliderStateMachine.OnCurveEngaged(monitor.SliderState, inDisabled));
    }

    /// <summary>Symmetric counterpart to <see cref="EngageBrightnessCurveStates"/> for the night-light row.</summary>
    public void EngageNightLightCurveStates()
    {
        if (_disposed) return;
        bool inDisabled = _isInDisabledPeriod;
        SetCurveStateWithSeed(_nightLightMonitor,
            SliderStateMachine.OnCurveEngaged(_nightLightMonitor.SliderState, inDisabled));
    }

    /// <summary>
    /// Drops the brightness curve's hold on the master + every individual row.
    /// CurveActive / CurveSleeping / CurveReleased -> Enabled; Disabled / Failed stick.
    /// Called when the brightness curve toggles off. Does NOT touch the night-light row.
    /// </summary>
    public void DisengageBrightnessCurveStates()
    {
        if (_disposed) return;
        _masterMonitor.ClearCurveTargetBrightness();
        foreach (MonitorInfo monitor in _monitors)
            monitor.ClearCurveTargetBrightness();

        _masterMonitor.SliderState = SliderStateMachine.OnCurveDisengaged(_masterMonitor.SliderState);
        foreach (MonitorInfo monitor in _monitors)
            monitor.SliderState = SliderStateMachine.OnCurveDisengaged(monitor.SliderState);
    }

    /// <summary>
    /// Symmetric counterpart to <see cref="DisengageBrightnessCurveStates"/> for the night-light row.
    /// </summary>
    public void DisengageNightLightCurveStates()
    {
        if (_disposed) return;
        _curveHardwareThrottler.Drop(NightLightHardwareKey);
        _nightLightMonitor.ClearCurveTargetBrightness();
        _nightLightMonitor.SliderState = SliderStateMachine.OnCurveDisengaged(_nightLightMonitor.SliderState);
    }

    /// <summary>
    /// Per-tick reconciliation called from <see cref="Evaluate"/> when the brightness curve is engaged.
    /// Promotes any Enabled row (e.g. a freshly-recovered monitor or a hot-plug after curves were on)
    /// into CurveActive / CurveSleeping, and walks the disabled-period sleep transitions on rows
    /// already under curve control. Crucially does NOT touch CurveReleased rows - release info has
    /// to survive sleep transitions until the user explicitly re-engages or the curve toggles off.
    /// </summary>
    private void HarmonizeBrightnessCurveStates(bool inDisabled)
    {
        HarmonizeRow(_masterMonitor, inDisabled);
        bool holdIndividuals = inDisabled || _masterMonitor.SliderState == SliderState.CurveReleased;
        foreach (MonitorInfo monitor in _monitors) HarmonizeRow(monitor, holdIndividuals);
    }

    /// <summary>
    /// Per-tick reconciliation for the night-light row. Mirrors <see cref="HarmonizeBrightnessCurveStates"/>.
    /// </summary>
    private void HarmonizeNightLightCurveStates(bool inDisabled) => HarmonizeRow(_nightLightMonitor, inDisabled);

    private static void HarmonizeRow(MonitorInfo m, bool inDisabled)
    {
        // DDC-health gate: a Failed row (sticky until a successful re-probe) must never be promoted into
        // CurveActive / CurveSleeping by the per-tick walk - the SliderStateMachine treats Failed as
        // dominant for hardware-absent reasons, and harmonizing past it would let the curve enqueue
        // writes against unreachable hardware. The switch below already preserves Failed via the
        // wildcard arm, but the explicit guard matches audit_03 Finding 4 and forecloses any future
        // arm that would relax that. Same gate covers IsHardwareFunctional which is `!= Failed` today.
        if (m.SliderState == SliderState.Failed || !m.IsHardwareFunctional) return;

        SliderState next = m.SliderState switch
        {
            SliderState.Enabled =>
                // Newly arrived (recovery, hot-plug, profile-load) - promote into curve control.
                inDisabled ? SliderState.CurveSleeping : SliderState.CurveActive,
            SliderState.CurveActive when inDisabled => SliderState.CurveSleeping,
            SliderState.CurveSleeping when !inDisabled => SliderState.CurveActive,
            _ => m.SliderState
        };
        SetCurveStateWithSeed(m, next);
    }

    private static void SetCurveStateWithSeed(MonitorInfo monitor, SliderState next)
    {
        bool wasCurveDriven = monitor.SliderState is SliderState.CurveActive or SliderState.CurveSleeping;
        bool willBeCurveDriven = next is SliderState.CurveActive or SliderState.CurveSleeping;
        if (willBeCurveDriven && !wasCurveDriven)
            monitor.SeedCurveTargetBrightnessFromSlider();

        monitor.SliderState = next;
    }

    /// <summary>
    /// One round of curve evaluation.
    /// Computes today's day-fraction,
    /// resolves the active profile's curve (sun-shifted if the curve's FollowTheSun is on),
    /// updates the disabled-period flag,
    /// and applies / refreshes targets for whichever curve flags are currently engaged.
    /// Skipped silently if no profile is loaded or the curve is missing
    /// - the evaluator is best-effort and never throws past this method.
    /// </summary>
    public void Evaluate()
    {
        if (_disposed) return;

        try
        {
            EnvironmentalCurve? curve = ResolveLiveCurve();
            EnvironmentalCurve? stored = ResolveStoredCurve();
            if (curve == null || stored == null)
            {
                SetDisabledPeriod(false);
                ClearCurveTargets();
                return;
            }

            double t = EnvironmentalCurveSampler.CurrentDayFraction();
            // Disabled-period state lives on the stored curve, not the sun-shifted clone
            // (SunShifter.BuildPreview's ClonePoints intentionally only carries the Y-curves and their offset clamps,
            // since the period has its own independent anchor and shift handling).
            // Reading the stored curve here keeps disabled-period detection correct
            // regardless of the curve's FollowTheSun state.
            bool inDisabled = EnvironmentalCurveSampler.IsInDisabledPeriod(stored, t);
            // Capture the prior flag BEFORE updating it so we can detect false->true (sleep-enter)
            // and trigger a one-shot resync below. Sleep-exit doesn't need a resync - the apply path
            // resumes writing curve samples on its own.
            bool sleepEntering = inDisabled && !_isInDisabledPeriod;
            SetDisabledPeriod(inDisabled);

            // Per-tick reconciliation: pull freshly-recovered / hot-plugged rows into curve control,
            // and walk sleep enter/exit on rows already under curve. Idempotent for rows in the
            // right state, and intentionally leaves CurveReleased alone (release survives ticks
            // until explicit re-engage / curve-toggle-off, which is what the user signed off on).
            if (IsBrightnessCurveEnabled) HarmonizeBrightnessCurveStates(inDisabled);
            if (IsNightLightCurveEnabled) HarmonizeNightLightCurveStates(inDisabled);

            // On sleep-enter the curve relinquishes hardware to the slider, but nothing else writes
            // unless the user nudges a slider mid-window. Issue a one-shot write of each
            // now-sleeping row's slider value so the bus actually moves to where the user's thumb
            // sits, instead of staying frozen at the curve's last write through the whole sleep
            // window. Covers both natural boundary crossings AND a settings-window toggle that
            // turns the disabled-period feature on while the current time falls inside the configured window.
            if (sleepEntering)
            {
                if (IsBrightnessCurveEnabled) ResyncBrightnessHardwareToSliderForSleeping();
                if (IsNightLightCurveEnabled) ResyncNightLightHardwareToSliderForSleeping();
            }

            double smoothness = (_appSettings?.EnvironmentalCurveSmoothness ?? 100) / 100.0;
            bool absolute = IsCurveAbsoluteMode;

            if (IsBrightnessCurveEnabled) ApplyBrightnessCurve(curve, t, smoothness, absolute);
            else DisengageBrightnessCurveStates();

            if (IsNightLightCurveEnabled) ApplyNightLightCurve(curve, t, smoothness, absolute);
            else DisengageNightLightCurveStates();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"EnvironmentalCurveService.Evaluate failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the curve at a caller-provided day fraction <paramref name="t"/> instead of the
    /// real-time clock. Used by the flyout's 24h preview sweep, which steps t from 0..1 across a
    /// 10-second wall-clock animation. Honours the same engaged-flag gating <see cref="Evaluate"/>
    /// uses, and pushes the simulated-time disabled-period state through <see cref="SetDisabledPeriod"/>
    /// so the flyout's chrome (slider dimming, crescent-glyph swap, indicator opacity) reflects what
    /// the curve would do at the simulated moment - the whole point of the preview is to show the
    /// disabled-period "off" range as the sweep crosses it.
    /// <see cref="Resume"/> snaps the flag back to real-now via the immediate Evaluate call there,
    /// so the chrome doesn't get stuck in a simulated state once the sweep ends.
    /// Returns <c>false</c> when no profile / curve is loaded so the caller can stop the sweep.
    /// </summary>
    public bool ApplyAt(double t)
    {
        if (_disposed) return false;

        // No _isSuspended gate here: the preview-sweep tick (today's only caller) calls Suspend()
        // exactly to silence the periodic timer, then repeatedly calls ApplyAt for the duration of the
        // sweep. An early-return on _isSuspended (proposed in audit_07 F-12 as a defensive measure for
        // a hypothetical second caller) would break the preview. If a second caller is ever added,
        // give it a distinct gate rather than overloading _isSuspended.

        EnvironmentalCurve? curve = ResolveLiveCurve();
        EnvironmentalCurve? stored = ResolveStoredCurve();
        if (curve == null || stored == null) return false;

        bool inDisabled = EnvironmentalCurveSampler.IsInDisabledPeriod(stored, t);
        bool sleepEntering = inDisabled && !_isInDisabledPeriod;
        SetDisabledPeriod(inDisabled);
        // Same per-tick reconciliation Evaluate uses, so the simulated sleep enter/exit drives the
        // CurveActive <-> CurveSleeping transitions during the sweep too.
        if (IsBrightnessCurveEnabled) HarmonizeBrightnessCurveStates(inDisabled);
        if (IsNightLightCurveEnabled) HarmonizeNightLightCurveStates(inDisabled);

        // Same one-shot resync Evaluate fires on sleep-enter, so the preview sweep visibly snaps
        // monitors to slider values when the simulated time crosses into the disabled window.
        if (sleepEntering)
        {
            if (IsBrightnessCurveEnabled) ResyncBrightnessHardwareToSliderForSleeping(allowWhileSuspended: true);
            if (IsNightLightCurveEnabled) ResyncNightLightHardwareToSliderForSleeping(allowWhileSuspended: true);
        }

        double smoothness = (_appSettings?.EnvironmentalCurveSmoothness ?? 100) / 100.0;
        bool absolute = IsCurveAbsoluteMode;

        // Honour the show-toggles by gating on the same engaged flags Evaluate uses.
        if (IsBrightnessCurveEnabled) ApplyBrightnessCurve(curve, t, smoothness, absolute, allowWhileSuspended: true);
        if (IsNightLightCurveEnabled) ApplyNightLightCurve(curve, t, smoothness, absolute, allowWhileSuspended: true);

        return true;
    }

    /// <summary>
    /// Writes each sleeping row's current slider value to its DDC channel - one shot, called from
    /// <see cref="Evaluate"/> on the false->true transition of <see cref="IsInDisabledPeriod"/>.
    /// CurveSleeping is the post-harmonize state; CurveReleased / Disabled / Failed are skipped
    /// naturally because they aren't in CurveSleeping. Newly-promoted rows that were Enabled before
    /// the harmonize step are also Sleeping now, but their hardware was already at the slider value
    /// (the slider path was driving them), so the write is a harmless duplicate for those.
    /// </summary>
    private void ResyncBrightnessHardwareToSliderForSleeping(bool allowWhileSuspended = false)
    {
        foreach (MonitorInfo m in _monitors)
        {
            if (m.SliderState != SliderState.CurveSleeping) continue;
            QueueBrightnessHardwareWrite(m, m.RoundedBrightness, allowWhileSuspended);
        }
    }

    /// <summary>
    /// Symmetric night-light counterpart to <see cref="ResyncBrightnessHardwareToSliderForSleeping"/>.
    /// </summary>
    private void ResyncNightLightHardwareToSliderForSleeping(bool allowWhileSuspended = false)
    {
        if (_nightLightMonitor.SliderState != SliderState.CurveSleeping) return;
        if (!NightLightProvider.IsSupported() || !NightLightProvider.IsEnabled()) return;
        // Slider position -> backend strength: invert if the user has the slider inverted, so the
        // backend gets the strength that matches what the thumb visually represents.
        int sliderStrength = _flipIfNightLightInverted(_nightLightMonitor.RoundedBrightness);
        // Already-known user value being re-pushed for hardware sync. Skip the persist path -
        // it adds nothing here and would burn an XML save on every sleep-entry transition.
        QueueNightLightHardwareWrite(sliderStrength, allowWhileSuspended);
    }

    private void QueueBrightnessHardwareWrite(
        MonitorInfo monitor,
        int percent,
        bool allowWhileSuspended = false)
    {
        if (_disposed) return;
        if (_isSuspended && !allowWhileSuspended) return;
        if (string.IsNullOrEmpty(monitor.ID)) return;

        QueueHardwareWrite(
            BrightnessHardwareKey(monitor.ID),
            allowWhileSuspended,
            () => _monitorService.EnqueueDirectBrightness(monitor, percent));
    }

    private void QueueNightLightHardwareWrite(int strength, bool allowWhileSuspended = false)
    {
        if (_disposed) return;
        if (_isSuspended && !allowWhileSuspended) return;
        if (!NightLightProvider.IsSupported() || !NightLightProvider.IsEnabled())
        {
            _curveHardwareThrottler.Drop(NightLightHardwareKey);
            NightLightProvider.CancelPendingStrengthWrites();
            return;
        }

        QueueHardwareWrite(
            NightLightHardwareKey,
            allowWhileSuspended,
            () =>
            {
                if (!NightLightProvider.IsEnabled())
                {
                    NightLightProvider.CancelPendingStrengthWrites();
                    return;
                }

                NightLightProvider.SetStrength(strength, persistAsLastUserValue: false);
            });
    }

    private void QueueHardwareWrite(string key, bool allowWhileSuspended, Action action)
    {
        int rateMs = Math.Max(TimeConstants.BrightnessUpdateRateMinMs,
            _appSettings?.BrightnessUpdateRateMs ?? TimeConstants.BrightnessUpdateRateDefaultMs);
        _curveHardwareThrottler.CooldownMs = rateMs;
        _ = _curveHardwareThrottler.RunAsync(
            key,
            ctx => Task.Run(
                () =>
                {
                    if (_disposed) return;
                    if (_isSuspended && !allowWhileSuspended) return;
                    if (ctx.HasReplacement) return;
                    action();
                },
                ctx.CancellationToken));
    }

    private void DropQueuedHardwareWrites()
    {
        _curveHardwareThrottler.Drop(NightLightHardwareKey);
        foreach (MonitorInfo monitor in _monitors)
        {
            if (string.IsNullOrEmpty(monitor.ID)) continue;
            _curveHardwareThrottler.Drop(BrightnessHardwareKey(monitor.ID));
        }
    }

    private static string BrightnessHardwareKey(string monitorID) => BrightnessHardwareKeyPrefix + monitorID;

    private bool IsCurveAbsoluteMode => _appSettings?.EnvironmentalOffsetMode != true;

    private void SetDisabledPeriod(bool value)
    {
        // Push every Evaluate's value through the callback unconditionally.
        // The flyout's IsInCurveDisabledPeriod setter is itself idempotent (early-returns on equality),
        // so this stays cheap - and it covers the case where the flyout's mirror was reset directly
        // (e.g. OnCurveToggleStateChanged setting it false on full-off) and now needs to track the real state again.
        _isInDisabledPeriod = value;
        _onDisabledPeriodChanged?.Invoke(value);
    }

    /// <summary>
    /// Reads the user-configured tick interval from <see cref="AppSettings"/>,
    /// clamped to the same bounds the settings UI enforces (250ms .. 60s).
    /// The clamp is a defence against a hand-edited settings.xml:
    /// a 0 or negative interval would either busy-loop the dispatcher or silently never fire.
    /// </summary>
    private TimeSpan ResolveCurveTickInterval()
    {
        int ms = _appSettings?.EnvironmentalCurveTickIntervalMs ??
                 TimeConstants.EnvironmentalCurveTickIntervalDefaultMs;
        ms = Math.Clamp(ms, 250, 60_000);
        return TimeSpan.FromMilliseconds(ms);
    }

    private void OnSelectedProfileChanged(int newIndex)
    {
        if (_disposed) return;

        // Stale shifted clone keyed to the outgoing profile must not carry over.
        // The new profile's first tick rebuilds it from its own EnvironmentalCurve.
        InvalidateCurveCache();

        // Drive an immediate evaluation so the new profile's curve takes effect within the same
        // dispatcher frame instead of waiting up to one EnvironmentalCurveTickIntervalMs (default 5s)
        // for the periodic tick - audit_07 F-05 / M-03. The Evaluate itself short-circuits cleanly
        // when both curve flags are off or the profile has no curve resolved.
        Evaluate();
    }

    /// <summary>
    /// Returns the currently selected profile's stored <see cref="EnvironmentalCurve"/> without any sun shift,
    /// or null if no profile is loaded.
    /// Used by the runtime's disabled-period check, which lives on the stored curve
    /// and isn't carried through
    /// <see cref="SunShifter.BuildPreview(EnvironmentalCurve, SunAnchor, SunAnchor)"/>'s clone.
    /// </summary>
    private EnvironmentalCurve? ResolveStoredCurve()
    {
        int idx = _profileManager.SelectedIndex;
        if (idx < 0 || idx >= _profileManager.Profiles.Profiles.Count) return null;
        return _profileManager.Profiles.Profiles[idx].EnvironmentalCurve;
    }

    /// <summary>
    /// Returns the active profile's <see cref="EnvironmentalCurve"/>, sun-shifted to today
    /// if FollowTheSun is on and a from-anchor exists.
    /// The shifted clone is cached and reused across ticks
    /// until the date / location / DST flag / source curve identity changes
    /// - SPA evaluations are cheap but stacking one per tick is wasteful,
    /// and the shape only meaningfully changes once per day.
    /// Returns null when the profile manager hasn't been initialised,
    /// the index is out of range, or the curve is missing.
    /// </summary>
    private EnvironmentalCurve? ResolveLiveCurve()
    {
        int idx = _profileManager.SelectedIndex;
        if (idx < 0 || idx >= _profileManager.Profiles.Profiles.Count) return null;

        EnvironmentalCurve stored = _profileManager.Profiles.Profiles[idx].EnvironmentalCurve;
        if (!stored.FollowTheSun) return stored;

        double lat = _appSettings?.EnvironmentalLatitude ?? 0;
        double lon = _appSettings?.EnvironmentalLongitude ?? 0;
        if (lat == 0 && lon == 0) return stored;

        SunAnchor storedAnchor = stored.BrightnessAnchor;
        if (storedAnchor.Date == default) return stored;

        DateTime today = DateTime.Today;
        bool dst = stored.UseDaylightSavings;

        // Cache check uses BOTH ReferenceEquals(source) AND Version: reference catches "different curve
        // object" (profile switch / promote-on-edit), Version catches "same reference, mutated points"
        // (settings editor's in-place edit, which doesn't allocate a new EnvironmentalCurve). Without
        // the Version arm, an in-place mutation that bypasses RequestEvaluation / InvalidateCurveCache
        // would keep sampling the pre-edit shape until midnight.
        if (ReferenceEquals(_cachedShiftedSourceCurve, stored)
            && _cachedShiftedSourceVersion == stored.Version
            && _cachedShiftedDate == today
            && _cachedShiftedLat == lat
            && _cachedShiftedLon == lon
            && _cachedShiftedDst == dst
            && _cachedShiftedCurve != null)
            return _cachedShiftedCurve;

        double fromLat = storedAnchor.Latitude;
        double fromLon = storedAnchor.Longitude;
        if (fromLat == 0 && fromLon == 0)
        {
            fromLat = lat;
            fromLon = lon;
        }

        SunAnchor from = storedAnchor with { Latitude = fromLat, Longitude = fromLon };
        SunAnchor to = new(today, lat, lon, dst);
        EnvironmentalCurve shifted = SunShifter.BuildPreview(stored, from, to);

        _cachedShiftedCurve = shifted;
        _cachedShiftedDate = today;
        _cachedShiftedLat = lat;
        _cachedShiftedLon = lon;
        _cachedShiftedDst = dst;
        _cachedShiftedSourceCurve = stored;
        _cachedShiftedSourceVersion = stored.Version;
        return shifted;
    }

    /// <summary>
    /// Shared skeleton for the per-tick curve application.
    /// Owns the disabled / unavailable / absolute / offset branching
    /// and the curve-sample math (raw 0..100 in absolute mode; centred-around-50 in offset mode,
    /// with the deviation feeding straight in as a +/-100 percentage delta).
    /// The two callers wire in WHERE samples come from
    /// (which axis on <see cref="EnvironmentalCurve"/>),
    /// HOW indicator state is cleared,
    /// and HOW the resulting target is pushed to indicators + hardware.
    /// </summary>
    private static void ApplyCurveCore(
        List<EnvironmentalCurvePoint> absoluteSeries,
        List<EnvironmentalCurvePoint> offsetSeries,
        double t,
        double smoothness,
        bool absolute,
        Func<bool> isAvailable,
        Action clearTargets,
        Action<double> applyAbsoluteSample,
        Action<double> applyOffsetPercent)
    {
        // Hardware availability still gates everything - if the backend is gone, drop indicators too.
        if (!isAvailable())
        {
            clearTargets();
            return;
        }

        // The harmonize step before this method runs has already parked curve-driven rows into
        // CurveSleeping during a disabled period; IsCurveDriven stays true so CurveTargetBrightness
        // tracks the curve's would-be value and the slider-track dot stays visible-but-dimmed
        // (XAML opacity binds to IsCurveSleeping). The apply lambdas gate hardware writes on
        // SliderState == CurveActive, so the user's slider drives hardware during sleep windows.
        if (absolute)
        {
            double sample = EnvironmentalCurveSampler.Sample(absoluteSeries, t, smoothness);
            // Fail-bright on NaN/Infinity: a malformed sample silently casts to 0 via (int)Math.Round,
            // which would blank the screen with no log line. Substitute the max so a broken curve
            // leaves the user able to see what's happening instead of dark-locking them out.
            if (!double.IsFinite(sample)) sample = 100.0;
            applyAbsoluteSample(sample);
            return;
        }

        // Offset mode: sample is 0..100 with midpoint 50 = +0.
        // The editor's Y axis spans -100..+100 (200 display units) over the 100 storage units of the curve,
        // so one storage unit equals two displayed percentage points;
        // multiply the centred deviation by 2 to recover the +/-100 percentage delta the labels promise.
        // No drift tracking - the offset stacks on top of the slider's current value afresh every tick.
        double offsetSample = EnvironmentalCurveSampler.Sample(offsetSeries, t, smoothness);
        // Fail-bright on NaN/Infinity (see absolute branch). offsetSample = 100 yields offsetPercent = +100,
        // which clamps every row to its max after the per-row addition.
        if (!double.IsFinite(offsetSample)) offsetSample = 100.0;
        double offsetPercent = (offsetSample - 50.0) * 2.0;
        applyOffsetPercent(offsetPercent);
    }

    /// <summary>
    /// Applies the brightness curve for the current tick.
    /// The curve owns the hardware, the slider thumb owns the user's manual intent,
    /// and the indicator glyph at <see cref="MonitorInfo.CurveTargetBrightness"/> connects the two visually
    /// - so engaging the curve never moves the slider thumb.
    /// Hardware writes go directly through <see cref="MonitorService.EnqueueDirectBrightness"/>
    /// instead of the <see cref="MonitorInfo.Brightness"/> setter,
    /// which is what would otherwise drag the bound slider thumb along for the ride.
    ///
    /// In absolute mode every enabled, non-released individual is driven to the curve sample.
    /// In offset mode each enabled, non-released individual is driven to its own slider value plus the sampled offset,
    /// so the user's per-row intent stays the reference point and the curve stacks on top live
    /// (no drift tracking needed since nothing is compounding).
    /// Released individuals (touched in absolute mode) are skipped
    /// - their slider drives hardware via the normal PropertyChanged path.
    /// </summary>
    private void ApplyBrightnessCurve(
        EnvironmentalCurve curve,
        double t,
        double smoothness,
        bool absolute,
        bool allowWhileSuspended = false)
    {
        ApplyCurveCore(
            curve.Brightness,
            curve.BrightnessOffset,
            t,
            smoothness,
            absolute,
            isAvailable: static () => true,
            clearTargets: DisengageBrightnessCurveStates,
            applyAbsoluteSample: sample =>
            {
                // Curve drives the master value;
                // each individual then follows via the standard master->individual offset formula
                // (the same one a user master drag uses), so a 60/70/80 setup with master at 70
                // stays 60/70/80 when the curve walks the master from 70 to 90
                // - the relative spread between monitors is preserved.
                // Offsets are captured at toggle-on (and on individual re-engage)
                // and stay valid through the curve's run, since slider thumbs don't move while the curve drives.
                double absoluteMasterTarget = Math.Clamp(sample, 0.0, 100.0);

                // Indicator + value-label sources update for any row currently driven by the curve
                // (CurveActive or CurveSleeping). Released / disabled / failed rows have their slider
                // thumb own the displayed value and should not get a stale CurveTargetBrightness write
                // - the indicator dot's binding hides itself when SliderState isn't curve-driven.
                // Master row is gated on IsCurveDriven the same way as the per-row loop below,
                // matching the per-row pattern (audit_07 F-01 / H-29) so a future master-Disabled or
                // master-Released path can't accidentally land a stale curve target on a slider-owned row.
                if (_masterMonitor.IsCurveDriven) _masterMonitor.CurveTargetBrightness = absoluteMasterTarget;
                foreach (MonitorInfo monitor in _monitors)
                {
                    if (monitor.IsCurveDriven)
                        monitor.CurveTargetBrightness = Math.Clamp(absoluteMasterTarget + monitor.Offset, 0.0, 100.0);
                }

                // Hardware writes only on rows actively driven by the curve. CurveSleeping rows hold
                // their indicator at the would-be position but the user's slider owns hardware
                // during the sleep window. CurveReleased / Disabled / Failed are skipped naturally.
                // H-10: also skip writes against a row whose user is mid-drag - the curve write would
                // shove the bound slider thumb out from under the user's mouse.
                foreach (MonitorInfo monitor in _monitors)
                {
                    if (monitor.SliderState != SliderState.CurveActive) continue;
                    if (monitor.IsDragging) continue;
                    int rowTargetPct = (int)Math.Round(Math.Clamp(absoluteMasterTarget + monitor.Offset, 0.0, 100.0));
                    QueueBrightnessHardwareWrite(monitor, rowTargetPct, allowWhileSuspended);
                }
            },
            applyOffsetPercent: offsetPercent =>
            {
                // Per-row indicators follow only on curve-driven rows. Released / disabled / failed rows
                // show their slider's own value via RoundedBrightness (EffectiveRoundedBrightness
                // short-circuits when IsCurveDriven is false).
                // Master row is gated on IsCurveDriven for the same reasons as the per-row loop -
                // see absolute-mode comment and audit_07 F-01 / H-29.
                // Baseline reads LastUserBrightness, not Brightness, so a Brightness drift from a
                // recovery / hot-plug hardware-sync (which goes through SyncBrightnessFromHardware
                // and intentionally does NOT touch LastUserBrightness) can't reroute the offset onto
                // the wrong baseline and skew the master / per-row targets.
                double masterTarget = Math.Clamp(_masterMonitor.LastUserBrightness + offsetPercent, 0.0, 100.0);
                if (_masterMonitor.IsCurveDriven) _masterMonitor.CurveTargetBrightness = masterTarget;
                foreach (MonitorInfo monitor in _monitors)
                {
                    if (monitor.IsCurveDriven)
                    {
                        monitor.CurveTargetBrightness =
                            Math.Clamp(monitor.LastUserBrightness + offsetPercent, 0.0, 100.0);
                    }
                }

                foreach (MonitorInfo monitor in _monitors)
                {
                    if (monitor.SliderState != SliderState.CurveActive) continue;
                    // H-10: skip writes against a row whose user is mid-drag - the curve write would
                    // shove the bound slider thumb out from under the user's mouse.
                    if (monitor.IsDragging) continue;
                    int hwTarget = (int)Math.Round(
                        Math.Clamp(monitor.LastUserBrightness + offsetPercent, 0.0, 100.0));
                    QueueBrightnessHardwareWrite(monitor, hwTarget, allowWhileSuspended);
                }
            });
    }

    /// <summary>
    /// Applies the night-light curve for the current tick.
    /// Same direct-write contract the brightness curve uses:
    /// the slider thumb stays at the user's manual position while the curve drives the backend strength
    /// via <see cref="NightLightProvider.SetStrength(int)"/>.
    /// The indicator glyph at <see cref="MonitorInfo.CurveTargetBrightness"/> shows
    /// where the curve has the backend now;
    /// slider position and strength are 100's-complement when InvertNightLightSlider is on,
    /// so the indicator goes through the flyout-supplied invert mapper to land on the right end of the slider.
    /// </summary>
    private void ApplyNightLightCurve(
        EnvironmentalCurve curve,
        double t,
        double smoothness,
        bool absolute,
        bool allowWhileSuspended = false)
    {
        ApplyCurveCore(
            curve.NightLight,
            curve.NightLightOffset,
            t,
            smoothness,
            absolute,
            isAvailable: NightLightProvider.IsSupported,
            clearTargets: DisengageNightLightCurveStates,
            applyAbsoluteSample: sample =>
            {
                int strength = Math.Clamp((int)Math.Round(sample), 0, 100);
                int sliderPos = _flipIfNightLightInverted(strength);
                // Indicator value is updated whenever the row is curve-driven (Active or Sleeping)
                // - the dot stays visible-but-dimmed in CurveSleeping so the user can see where the
                // curve would be. Hardware writes only happen in CurveActive.
                if (_nightLightMonitor.IsCurveDriven) _nightLightMonitor.CurveTargetBrightness = sliderPos;
                if (_nightLightMonitor.SliderState != SliderState.CurveActive) return;
                // Curve-driven strength is not user intent - skip persisting NightLightLastNonZeroStrength.
                // Persisting it would jitter the dispatcher (sync XML write per tick) AND save curve
                // samples as the "last user-chosen warmth," breaking restore-on-toggle behavior.
                QueueNightLightHardwareWrite(strength, allowWhileSuspended);
            },
            applyOffsetPercent: strengthOffsetPercent =>
            {
                // Offset mode: backend strength = current strength + offset,
                // computed from the slider's last manual position (mapped through invert)
                // so the user's intent stays the reference point and the curve stacks on top live.
                int currentStrength = _flipIfNightLightInverted(_nightLightMonitor.RoundedBrightness);
                int targetStrength = Math.Clamp(
                    (int)Math.Round(currentStrength + strengthOffsetPercent),
                    0,
                    100);

                if (_nightLightMonitor.IsCurveDriven)
                    _nightLightMonitor.CurveTargetBrightness = _flipIfNightLightInverted(targetStrength);
                if (_nightLightMonitor.SliderState != SliderState.CurveActive) return;
                // Curve-driven strength is not user intent - see absolute-mode comment above.
                QueueNightLightHardwareWrite(targetStrength, allowWhileSuspended);
            });
    }

    /// <summary>
    /// SystemEvents.TimeChanged callback - DST transition, user-set clock change, or system TZ change.
    /// Each of these invalidates the sun-shifted cache because SunShifter's SPA evaluation depends on
    /// the active UTC offset (see audit_07 F-03 / F-04 / M-01). The handler runs on the
    /// SystemEvents background thread; cache nulling + Version bump are tiny benign writes, and the
    /// follow-up evaluation is dispatched onto the UI thread where the curve service's other
    /// callers all run.
    /// </summary>
    private void OnSystemTimeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        // Background-thread context: marshal the cache invalidation + re-evaluation onto the UI
        // dispatcher so we stay on the same thread as every other Evaluate / RequestEvaluation caller.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            InvalidateCurveCache();
            // Drive an immediate evaluation so the post-change shape lands within the same frame
            // rather than waiting for the next periodic tick (matches M-03's profile-switch path).
            Evaluate();
        });
    }

    /// <summary>
    /// MonitorService.Refresh just landed (topology event, recovery promotion, etc.).
    /// Run an immediate evaluation so freshly-promoted Enabled rows get harmonized into CurveActive
    /// and the curve target is written after read-only acquisition completes.
    /// MonitorsRefreshed fires synchronously inside Refresh on the dispatcher thread,
    /// so this runs before the caller's next instruction.
    /// </summary>
    private void OnMonitorsRefreshed()
    {
        if (_disposed) return;
        if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled) return;
        if (_isSuspended) return;
        Evaluate();
    }

    /// <summary>
    /// Tears down every subscription and timer the service owns.
    /// Idempotent (the _disposed guard makes a double-Dispose cheap).
    /// Every public method that the caller might re-enter post-dispose has its own _disposed check
    /// so a late event from a not-yet-unsubscribed source can't fault inside.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Symmetric unsubscribe order: external event sources first so no in-flight callback can
        // arrive mid-tear-down, then own timers.
        _profileManager.SelectedProfileChanged -= OnSelectedProfileChanged;
        _monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
        SystemEvents.TimeChanged -= OnSystemTimeChanged;

        if (_curveTimer != null)
        {
            _curveTimer.Stop();
            _curveTimer = null;
        }

        _curveEventEvaluationThrottler.Dispose();
        _curveHardwareThrottler.Dispose();
    }
}

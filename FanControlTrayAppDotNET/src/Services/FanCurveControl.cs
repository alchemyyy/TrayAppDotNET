using System.Globalization;

namespace FanControlTrayAppDotNET.Services;

/// <summary>
/// The one curve evaluator: the flyout's curve-target display and Curve-mode fan control both sample
/// curves through here, so the slider shows exactly what drives the fan.
/// </summary>
internal static class FanCurveEvaluator
{
    /// <summary>
    /// Samples the curve at the source's current reading, in the curve's own unit. False when the curve
    /// has no points, has inverted input clamps, or produces a non-finite value.
    /// </summary>
    public static bool TryEvaluate(Curve curve, DataSource source, out double target)
    {
        target = 0.0;
        if (curve.CurveNodes.Count == 0 || curve.ClampXMin > curve.ClampXMax) return false;

        double x = source.DisplayValue;
        if (!double.IsFinite(x)) return false;

        double y = curve.Evaluate(x);
        if (!double.IsFinite(y)) return false;

        target = y;
        return true;
    }
}

/// <summary>
/// What Curve mode does with a fan this tick.
/// - Pending: the data source hasn't been polled yet since the service started; write nothing.
/// - Drive: write DutyCyclePercent.
/// - HandBack: give the fan to hardware control (SetDefault).
/// </summary>
internal enum FanCurveAction
{
    Pending,
    Drive,
    HandBack
}

/// <summary>
/// One control decision. Fallback says why a curve isn't driving, and stays set when an untrusted firmware
/// default turns a hand-back into a conservative Drive.
/// </summary>
internal readonly record struct FanCurveCommand(FanCurveAction Action, double DutyCyclePercent, FanCurveFallback Fallback)
{
    public static FanCurveCommand Pending { get; } = new(FanCurveAction.Pending, 0.0, FanCurveFallback.None);

    public static FanCurveCommand HandBack(FanCurveFallback fallback) => new(FanCurveAction.HandBack, 0.0, fallback);

    public static FanCurveCommand Drive(double dutyCyclePercent) =>
        new(FanCurveAction.Drive, dutyCyclePercent, FanCurveFallback.None);
}

/// <summary>
/// Per-fan Curve-mode memory between poll ticks: the delta-limit baseline, the hysteresis commit, and
/// the jumpstart hold. All times are Environment.TickCount64 ms. Reset whenever the fan goes back to
/// hardware control so recovery starts clean.
/// </summary>
internal sealed class FanCurveControlState
{
    private const long NoTime = long.MinValue;

    // Last duty after Offset, clamps, Max Delta, and the final Min/Max Duty bound, but before Jumpstart,
    // so a kick never becomes the baseline the next tick ramps from.
    public bool HasCommand { get; set; }
    public double LastPipelineDutyCycle { get; set; }
    public long LastCommandMs { get; set; }

    // The hysteresis commit only holds for the curve and unit it was taken from.
    public bool HasCommittedTarget { get; set; }
    public double CommittedCurveTarget { get; set; }
    public Curve? CommittedCurve { get; set; }
    public bool CommittedRPMMode { get; set; }
    public long CommittedAtMs { get; set; }

    public long JumpstartUntilMs { get; set; } = NoTime;

    // Owned by LHMService: true once SetDefault was queued for the current hand-back, so it isn't
    // re-sent every tick. Not cleared by Reset.
    public bool HandedToHardware { get; set; }

    public void Reset()
    {
        HasCommand = false;
        LastPipelineDutyCycle = 0.0;
        LastCommandMs = 0;
        HasCommittedTarget = false;
        CommittedCurveTarget = 0.0;
        CommittedCurve = null;
        CommittedRPMMode = false;
        CommittedAtMs = 0;
        JumpstartUntilMs = NoTime;
    }
}

/// <summary>
/// Turns a fan, its curve, and the curve's data source into a duty cycle. Order: curve (with
/// hysteresis and input clamps), Offset, Min/Max Duty, Max Delta, Jumpstart, then Min/Max Duty again as a
/// hard bound on the written value. Offset and the first clamp work in the curve's unit; Max Delta,
/// Jumpstart, and the hard bound work in duty cycle % after conversion. Times are Environment.TickCount64 ms.
/// </summary>
internal static class FanCurveController
{
    private const double DutyCycleMinimum = 0.0;
    private const double DutyCycleMaximum = 100.0;
    private const long NoTime = long.MinValue;

    // Duty at or below this counts as "stopped" for Jumpstart.
    internal const double StoppedDutyCycleEpsilon = 0.5;

    /// <param name="firstPollMs">When the service's first poll tick pushed readings, or
    /// DataSource.NoSample before that. A source with no reading yet is pending until then, and its stale
    /// window starts there.</param>
    public static FanCurveCommand Evaluate(
        Fan fan,
        Curve? curve,
        DataSource? source,
        FanCurveControlState state,
        long nowMs,
        long firstPollMs)
    {
        if (curve == null || string.IsNullOrWhiteSpace(curve.SelectedDataSourceKey))
            return HandBack(state, FanCurveFallback.None);

        // Before the first poll tick nothing about the source is settled yet (discovery may not have reached
        // its hardware, and nothing has been read), so write nothing rather than hand the fan back.
        if (firstPollMs == DataSource.NoSample && (source == null || source.LastSampleTickMs == DataSource.NoSample))
            return FanCurveCommand.Pending;

        if (source == null || !source.IsLiveHardwareSensor)
            return HandBack(state, FanCurveFallback.SourceUnavailable);

        switch (ClassifySource(source, nowMs, firstPollMs))
        {
            case SourceStatus.Pending:
                return FanCurveCommand.Pending;
            case SourceStatus.Unavailable:
                return HandBack(state, FanCurveFallback.SourceUnavailable);
        }

        if (!FanCurveEvaluator.TryEvaluate(curve, source, out double curveTarget))
            return HandBack(state, FanCurveFallback.CurveUnusable);

        double target = ApplyHysteresis(curve, state, curveTarget, nowMs);

        target += fan.OffsetForCurve(curve);
        double clampLow = fan.ClampLowForCurve(curve);
        double clampHigh = Math.Max(clampLow, fan.ClampHighForCurve(curve));
        target = Math.Clamp(target, clampLow, clampHigh);

        double dutyLow = ClampDutyCycle(fan.CurveValueToDutyCycle(clampLow, curve));
        double dutyHigh = Math.Max(dutyLow, ClampDutyCycle(fan.CurveValueToDutyCycle(clampHigh, curve)));

        double dutyCycle = ClampDutyCycle(fan.CurveValueToDutyCycle(target, curve));
        dutyCycle = ApplyDeltaLimit(fan, curve, state, dutyCycle, nowMs);
        dutyCycle = Math.Clamp(dutyCycle, dutyLow, dutyHigh);
        double output = Math.Clamp(ApplyJumpstart(fan, curve, state, dutyCycle, nowMs), dutyLow, dutyHigh);

        if (!double.IsFinite(output))
            return HandBack(state, FanCurveFallback.CurveUnusable);

        state.HasCommand = true;
        state.LastPipelineDutyCycle = dutyCycle;
        state.LastCommandMs = nowMs;
        return FanCurveCommand.Drive(ClampDutyCycle(output));
    }

    /// <summary>
    /// Applies firmware trust to a curve decision. A curve fail-safe (source unavailable, curve unusable)
    /// normally hands the fan to firmware. When firmware defaults are untrusted (an earlier instance this
    /// boot crashed while controlling fans), SetDefault would restore that instance's last duty, so the fan
    /// gets its Max Duty instead. A plain "no curve" hand-back is left alone.
    /// </summary>
    public static FanCurveCommand ApplyFirmwareTrust(Fan fan, FanCurveCommand command, bool firmwareDefaultsTrusted)
    {
        if (command.Action != FanCurveAction.HandBack
            || firmwareDefaultsTrusted
            || command.Fallback == FanCurveFallback.None)
            return command;

        return new FanCurveCommand(FanCurveAction.Drive, ConservativeDutyCycle(fan), command.Fallback);
    }

    /// <summary>
    /// The fan's Max Duty as duty cycle %, clamped to 0-100.
    /// </summary>
    public static double ConservativeDutyCycle(Fan fan) =>
        ClampDutyCycle(fan.ClampHighForRPMMode(targetRPMMode: false));

    /// <summary>
    /// Manual target as duty cycle %. Min Duty / Max Duty bind Manual too; Offset, Max Delta, and
    /// Jumpstart are Curve-only.
    /// </summary>
    public static double ResolveManualDutyCyclePercent(Fan fan)
    {
        double rpmReference = Math.Max(val1: 1, fan.FanSliderMaximum);
        double value = Math.Clamp(fan.FanDisplayedValue, min: 0, rpmReference);
        double dutyCycle = fan.RPMMode ? Fan.RPMToDutyCyclePercent(value, rpmReference) : value;

        double clampLow = fan.ClampLowForRPMMode(targetRPMMode: false);
        double clampHigh = Math.Max(clampLow, fan.ClampHighForRPMMode(targetRPMMode: false));
        return ClampDutyCycle(Math.Clamp(dutyCycle, clampLow, clampHigh));
    }

    internal enum SourceStatus
    {
        Pending,
        Fresh,
        Unavailable
    }

    /// <summary>
    /// Fresh: the last reading was valid and within the stale window. Unavailable: the last reading was
    /// invalid (NaN/infinite, also after its transform) or too old. A source with no reading since start is
    /// Pending until the first poll tick, then ages from that tick.
    /// </summary>
    internal static SourceStatus ClassifySource(DataSource source, long nowMs, long firstPollMs)
    {
        if (source.LastSampleTickMs == DataSource.NoSample)
        {
            if (firstPollMs == DataSource.NoSample) return SourceStatus.Pending;
            return nowMs - firstPollMs <= TimeConstants.CurveSourceStaleMs
                ? SourceStatus.Pending
                : SourceStatus.Unavailable;
        }

        if (!source.LastSampleValid) return SourceStatus.Unavailable;
        return nowMs - source.LastSampleTickMs <= TimeConstants.CurveSourceStaleMs
            ? SourceStatus.Fresh
            : SourceStatus.Unavailable;
    }

    private static FanCurveCommand HandBack(FanCurveControlState state, FanCurveFallback fallback)
    {
        state.Reset();
        return FanCurveCommand.HandBack(fallback);
    }

    // Curve.HysteresisMs: a committed target is held for at least that long before a new one commits. A
    // commit from another curve or the other unit is meaningless here and is dropped.
    private static double ApplyHysteresis(Curve curve, FanCurveControlState state, double curveTarget, long nowMs)
    {
        if (curve.HysteresisMs > 0
            && state.HasCommittedTarget
            && ReferenceEquals(state.CommittedCurve, curve)
            && state.CommittedRPMMode == curve.RPMMode
            && nowMs - state.CommittedAtMs < curve.HysteresisMs)
            return state.CommittedCurveTarget;

        state.HasCommittedTarget = true;
        state.CommittedCurveTarget = curveTarget;
        state.CommittedCurve = curve;
        state.CommittedRPMMode = curve.RPMMode;
        state.CommittedAtMs = nowMs;
        return curveTarget;
    }

    // Max Delta is a rate: the allowed step is DeltaMax (converted to %/s) times the real time since the
    // previous command. The first command after taking over has no baseline and goes straight to target.
    private static double ApplyDeltaLimit(
        Fan fan,
        Curve curve,
        FanCurveControlState state,
        double dutyCycle,
        long nowMs)
    {
        if (!state.HasCommand) return dutyCycle;

        double deltaPerSecond = fan.CurveValueToDutyCycle(fan.DeltaMaxForCurve(curve), curve);
        if (deltaPerSecond <= 0.0 || !double.IsFinite(deltaPerSecond)) return dutyCycle;

        double elapsedSeconds = Math.Max(val1: 0.0, (nowMs - state.LastCommandMs) / 1000.0);
        double step = deltaPerSecond * elapsedSeconds;
        return Math.Clamp(dutyCycle, state.LastPipelineDutyCycle - step, state.LastPipelineDutyCycle + step);
    }

    // A stopped fan asked to spin below its startup speed gets the startup speed for one poll tick.
    private static double ApplyJumpstart(
        Fan fan,
        Curve curve,
        FanCurveControlState state,
        double dutyCycle,
        long nowMs)
    {
        if (dutyCycle <= StoppedDutyCycleEpsilon)
        {
            state.JumpstartUntilMs = NoTime;
            return dutyCycle;
        }

        double startupDutyCycle = ClampDutyCycle(fan.CurveValueToDutyCycle(fan.StartupSpeedForCurve(curve), curve));
        bool wasStopped = state.HasCommand
            ? state.LastPipelineDutyCycle <= StoppedDutyCycleEpsilon
            : IsMeasuredStopped(fan);
        if (wasStopped && dutyCycle < startupDutyCycle)
            state.JumpstartUntilMs = nowMs + TimeConstants.FanJumpstartHoldMs;

        return state.JumpstartUntilMs != NoTime && nowMs < state.JumpstartUntilMs
            ? Math.Max(dutyCycle, startupDutyCycle)
            : dutyCycle;
    }

    // Before the first command, "stopped" comes from telemetry: 0 RPM when an RPM sensor is paired,
    // otherwise the reported duty cycle.
    private static bool IsMeasuredStopped(Fan fan) =>
        fan.HasRPMSensor ? fan.CurrentRPM <= 0 : fan.CurrentDutyCycle <= StoppedDutyCycleEpsilon;

    private static double ClampDutyCycle(double dutyCycle) =>
        double.IsFinite(dutyCycle) ? Math.Clamp(dutyCycle, DutyCycleMinimum, DutyCycleMaximum) : dutyCycle;
}

/// <summary>
/// Warn Low / Warn High evaluation. Each threshold compares the measured speed in its own unit: RPM
/// thresholds against the paired RPM sensor, % thresholds against the reported duty cycle. Below Warn
/// Low and above Warn High warn; equal doesn't, so the 0% / 100% defaults never fire.
/// </summary>
internal static class FanSpeedWarnings
{
    public static FanSpeedWarning Classify(Fan fan)
    {
        if (fan.ForcedNonFunctioning) return FanSpeedWarning.None;
        if (TryMeasure(fan, fan.WarnLowRPMMode, out double low) && low < fan.WarnLow) return FanSpeedWarning.Low;
        if (TryMeasure(fan, fan.WarnHighRPMMode, out double high) && high > fan.WarnHigh) return FanSpeedWarning.High;
        return FanSpeedWarning.None;
    }

    /// <summary>
    /// Formats the measured speed and the crossed threshold for a warning, e.g. ("640 RPM", "800 RPM").
    /// </summary>
    public static (string Measured, string Threshold) Describe(Fan fan, FanSpeedWarning warning)
    {
        bool rpmMode = warning == FanSpeedWarning.High ? fan.WarnHighRPMMode : fan.WarnLowRPMMode;
        int threshold = warning == FanSpeedWarning.High ? fan.WarnHigh : fan.WarnLow;
        double measured = rpmMode ? fan.CurrentRPM : MeasuredDutyCycle(fan);
        return (FormatSpeed(measured, rpmMode), FormatSpeed(threshold, rpmMode));
    }

    private static bool TryMeasure(Fan fan, bool rpmMode, out double measured)
    {
        if (rpmMode)
        {
            measured = fan.CurrentRPM;
            return fan.HasRPMSensor;
        }

        measured = MeasuredDutyCycle(fan);
        return true;
    }

    private static double MeasuredDutyCycle(Fan fan) =>
        double.IsFinite(fan.CurrentDutyCycle) ? Math.Clamp(fan.CurrentDutyCycle, min: 0.0, max: 100.0) : 0.0;

    private static string FormatSpeed(double value, bool rpmMode) =>
        rpmMode
            ? $"{value.ToString(format: "0", CultureInfo.InvariantCulture)} RPM"
            : $"{value.ToString(format: "0", CultureInfo.InvariantCulture)}%";
}

/// <summary>
/// Debounces one fan's warning state on monotonic time (Environment.TickCount64 ms). A new state must hold
/// for FanSpeedWarningDebounceMs before it becomes active; Update returns true exactly once per confirmed
/// excursion.
/// </summary>
internal sealed class FanSpeedWarningTracker
{
    private FanSpeedWarning _pending;
    private long _pendingSinceMs;

    public FanSpeedWarning Active { get; private set; }

    public bool Update(FanSpeedWarning observed, long nowMs)
    {
        if (observed == Active)
        {
            _pending = Active;
            return false;
        }

        if (observed != _pending)
        {
            _pending = observed;
            _pendingSinceMs = nowMs;
        }

        if (nowMs - _pendingSinceMs < TimeConstants.FanSpeedWarningDebounceMs) return false;

        Active = observed;
        return Active != FanSpeedWarning.None;
    }
}

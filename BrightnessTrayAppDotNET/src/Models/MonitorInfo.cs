using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrightnessTrayAppDotNET.Models;

/// <summary>
/// Single-value-of-truth visible state of a slider row.
/// Replaces the previous quartet of orthogonal bools (IsSliderEnabled, IsReleasedFromCurve, HasCurveTarget,
/// IsDDCCISupported) with one enum so XAML triggers, apply paths, and dirty-checks all read from the same place
/// and impossible flag combinations are unrepresentable.
/// Transition logic lives on <see cref="SliderStateMachine"/>; this enum is just the value.
/// Master and night-light rows use a subset of the values:
/// master and night-light never enter <see cref="Disabled"/>, but can enter
/// <see cref="CurveReleased"/> for temporary manual curve overrides.
/// </summary>
public enum SliderState
{
    /// <summary>User owns the slider; no curve influence.</summary>
    Enabled,

    /// <summary>User excluded this row from master-driven changes; the row's own slider still works.</summary>
    Disabled,

    /// <summary>Hardware (DDC/CI) is unreachable; sticky until a successful re-probe.</summary>
    Failed,

    /// <summary>Curve is engaged AND driving this row's hardware right now.</summary>
    CurveActive,

    /// <summary>
    /// Curve engaged but the disabled-period window is currently passing through;
    /// user owns slider for now.
    /// </summary>
    CurveSleeping,

    /// <summary>
    /// Curve engaged; user pulled this individual out of curve control.
    /// User owns slider until re-engaged.
    /// </summary>
    CurveReleased,
}

/// <summary>
/// Pure transition functions for <see cref="SliderState"/>.
/// Each event method takes the current state plus any context the transition needs and returns
/// the next state. No side effects, no PropertyChanged - the caller writes the result back to
/// <see cref="MonitorInfo.SliderState"/> via the property setter, which fires PropertyChanged once.
/// Centralised here so the precedence rules (Failed sticks; Disabled survives curve toggles;
/// release info is cleared when the curve toggles back on) live in one auditable place.
/// </summary>
public static class SliderStateMachine
{
    /// <summary>Failed wins over everything; users can't interact with hardware that isn't there.</summary>
    public static SliderState OnHardwareFailed() => SliderState.Failed;

    /// <summary>
    /// Hardware came back. If we weren't failed, leave whatever state was running undisturbed.
    /// If we were failed, snap to whatever the curve flags currently demand.
    /// </summary>
    public static SliderState OnHardwareRecovered(SliderState current, bool curveEngaged, bool inDisabledPeriod) =>
        current != SliderState.Failed
            ? current
            : (curveEngaged
                ? (inDisabledPeriod ? SliderState.CurveSleeping : SliderState.CurveActive)
                : SliderState.Enabled);

    /// <summary>
    /// User toggled the slider's group-membership icon off.
    /// Failed wins; everything else goes Disabled.
    /// </summary>
    public static SliderState OnUserToggleOff(SliderState current) =>
        current == SliderState.Failed ? SliderState.Failed : SliderState.Disabled;

    /// <summary>
    /// User toggled the slider's group-membership icon back on.
    /// Only Disabled responds; other states already have a slider the user owns or are sticky.
    /// </summary>
    public static SliderState OnUserToggleOn(SliderState current, bool curveEngaged, bool inDisabledPeriod) =>
        current != SliderState.Disabled
            ? current
            : (curveEngaged
                ? (inDisabledPeriod ? SliderState.CurveSleeping : SliderState.CurveActive)
                : SliderState.Enabled);

    /// <summary>User dragged a curve-driven individual's slider; release that row from curve control.</summary>
    public static SliderState OnUserRelease(SliderState current) =>
        current is SliderState.CurveActive or SliderState.CurveSleeping
            ? SliderState.CurveReleased
            : current;

    /// <summary>User double-clicked a released individual; bring it back under curve control.</summary>
    public static SliderState OnUserReengage(SliderState current, bool inDisabledPeriod) =>
        current != SliderState.CurveReleased
            ? current
            : (inDisabledPeriod ? SliderState.CurveSleeping : SliderState.CurveActive);

    /// <summary>
    /// Curve flag flipped on. Disabled / Failed are sticky (user / hardware override curve);
    /// everything else - including stale CurveReleased from a prior session - snaps to active or sleeping
    /// so a freshly-enabled curve drives every eligible row.
    /// </summary>
    public static SliderState OnCurveEngaged(SliderState current, bool inDisabledPeriod) =>
        current is SliderState.Failed or SliderState.Disabled
            ? current
            : (inDisabledPeriod ? SliderState.CurveSleeping : SliderState.CurveActive);

    /// <summary>Curve flag flipped off. Curve-tagged states fall back to Enabled; Disabled/Failed stick.</summary>
    public static SliderState OnCurveDisengaged(SliderState current) =>
        current is SliderState.Failed or SliderState.Disabled ? current : SliderState.Enabled;

    /// <summary>Disabled-period window started passing through. Only active rows get parked.</summary>
    public static SliderState OnSleepEnter(SliderState current) =>
        current == SliderState.CurveActive ? SliderState.CurveSleeping : current;

    /// <summary>Disabled-period window ended. Only sleeping rows wake up; released rows stay released.</summary>
    public static SliderState OnSleepExit(SliderState current) =>
        current == SliderState.CurveSleeping ? SliderState.CurveActive : current;
}

/// <summary>
/// Represents a physical monitor with brightness control capabilities.
/// </summary>
public class MonitorInfo : INotifyPropertyChanged
{
    private double _brightness;
    private double _lastUserBrightness;
    private double _virtualBrightness;
    private bool _hasUserBrightness;
    private double _curveTargetBrightness;
    private bool _hasCurveTargetBrightness;
    private SliderState _sliderState = SliderState.Enabled;
    private SliderState _preFailureSliderState = SliderState.Enabled;
    private bool _isCurveStopwatchVisible;
    private bool _isCurveStopwatchEnabled;
    private DateTime _curveStopwatchReenableAtUtc;

    /// <summary>
    /// Unique identifier for the monitor.
    /// </summary>
    public string ID { get; set; } = string.Empty;

    /// <summary>
    /// Stable EDID-first identifier used by the "Display order &amp; overrides" settings section.
    /// Always derived as <c>edid:{serial}</c> when an EDID serial is present,
    /// otherwise falls back to <c>port:{deviceId}</c> or <c>port:{adapterName}</c>.
    /// Independent of <see cref="ID"/> (which follows the user's chosen identity strategy)
    /// so per-monitor name/order/dwell/min/max overrides survive identity-strategy changes.
    /// </summary>
    public string EDIDKey { get; set; } = string.Empty;

    /// <summary>
    /// Friendly name of the monitor (e.g., "LG ULTRAGEAR+"). May be a user override.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = string.Empty;

    /// <summary>
    /// EDID-derived friendly name (0xFC descriptor) before any user override is applied.
    /// Empty if the EDID didn't populate one.
    /// Used by the settings UI to show the monitor's original reported name alongside the override box.
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// Raw EDID serial value (0xFF descriptor or numeric serial).
    /// Empty if the EDID is unreadable or the monitor doesn't populate one.
    /// Shown in the settings UI appended to <see cref="OriginalName"/>.
    /// </summary>
    public string EDIDSerial { get; set; } = string.Empty;

    /// <summary>
    /// 1-based OS-assigned display number, matching the label shown in Windows Settings &gt; Display.
    /// Zero for the master entry or when unresolved.
    /// </summary>
    public int DisplayNumber { get; set; }

    /// <summary>Monitor's left coordinate on the virtual desktop - used to sort by "Arrangement".</summary>
    public int ArrangementX { get; set; }

    /// <summary>Monitor's top coordinate on the virtual desktop - used to sort by "Arrangement".</summary>
    public int ArrangementY { get; set; }

    /// <summary>
    /// Last successful raw VCP maximum for the monitor's brightness feature.
    /// Preserved while the row is Failed/ReadDegraded so write-only recovery can keep scaling
    /// percentage targets against the monitor's real range instead of falling back to raw 0-100.
    /// </summary>
    public uint LastKnownBrightnessMax { get; set; } = 100;

    /// <summary>
    /// Current brightness level (0-100).
    /// Always stores the raw double so the TwoWay binding stays in sync with Slider.Value
    /// (otherwise the UI binding would pull the stale source value back onto the slider thumb each tick,
    /// producing visible snapping).
    /// PropertyChanged is gated to integer transitions
    /// so downstream work (dirty-check, tray icon, dependent sync) only runs on meaningful changes.
    /// Per-monitor min/max overrides
    /// (<see cref="Models.MonitorOverrideEntry.MinBrightness"/> / <see cref="Models.MonitorOverrideEntry.MaxBrightness"/>)
    /// are not enforced here - the slider value stays on the normalised 0-100 range.
    /// Bounds clamping is applied at the bus boundary inside
    /// <see cref="Services.MonitorService.EnqueueDirectBrightness"/>
    /// so hardware never receives an out-of-range value
    /// without the slider, profile, or curve pipelines having to know about the cap.
    /// </summary>
    public double Brightness
    {
        get => _brightness;
        set
        {
            // Sync virtual to the intended value on every set.
            // Master-drag propagation explicitly overrides VirtualBrightness afterward with the unclamped value
            // (see BrightnessFlyout.ApplyMasterToEnabledMonitors),
            // so direct individual changes reset any prior overflow while master drags preserve it.
            _virtualBrightness = value;
            // Claim user intent: every write through this setter is a user-driven change
            // (slider drag, master propagation, profile load, hotkey delta, sync ops).
            // Hardware-sync writes from MonitorService.PromoteRecovered etc. go through
            // SyncBrightnessFromHardware, which bypasses LastUserBrightness so a drift between
            // the slider thumb and the user's intent can't be laundered into the curve baseline.
            // Unconditional even when _brightness == value: LastUserBrightness can legitimately
            // diverge from Brightness post-recovery, so a user-drag back to the current Brightness
            // still has to reclaim the baseline.
            _hasUserBrightness = true;
            _lastUserBrightness = value;
            // exact-primitive comparison is intentional
            if (_brightness == value) return;

            int oldRounded = (int)Math.Round(_brightness);
            _brightness = value;
            int newRounded = (int)Math.Round(value);
            if (oldRounded != newRounded)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(RoundedBrightness));
                // Only suppress when a seeded curve target is the effective value. A row can briefly be
                // CurveActive before the evaluator has produced its first target; in that gap the slider
                // value remains the safest value to expose.
                if (_sliderState != SliderState.CurveActive || !_hasCurveTargetBrightness)
                    OnPropertyChanged(nameof(EffectiveRoundedBrightness));
            }
        }
    }

    /// <summary>
    /// Unclamped brightness the master slider would have driven this monitor to, before the [0,100] clamp.
    /// Normally mirrors <see cref="Brightness"/>;
    /// diverges only while <c>ApplyMasterToEnabledMonitors</c> is propagating a master drag
    /// that pushed the monitor past an edge.
    /// Used by <c>CaptureOffsetsFromMaster</c> when the PreserveMasterSliderOffsets setting is on,
    /// so re-captured offsets survive clips.
    /// </summary>
    public double VirtualBrightness
    {
        get => _virtualBrightness;
        set => _virtualBrightness = value;
    }

    /// <summary>
    /// The slider value the user last expressed direct intent at.
    /// Tracks <see cref="Brightness"/> across every user-driven write
    /// (slider drag, keyboard, wheel, hotkey, sync ops, master propagation, profile load),
    /// but stays put when MonitorService syncs Brightness from a hardware reading via
    /// <see cref="SyncBrightnessFromHardware(double)"/> on recovery / hot-plug.
    /// Used by the brightness curve as the per-row baseline in offset mode and by
    /// <c>BrightnessFlyout.CaptureOffsetsFromMaster</c> when computing master-relative offsets,
    /// so a Brightness drift caused by a hardware-sync race never bakes into the curve's view of
    /// "what the user wanted."
    /// </summary>
    public double LastUserBrightness
    {
        get => _lastUserBrightness;
        set => _lastUserBrightness = value;
    }

    /// <summary>
    /// True after a user/profile/manual path has supplied an explicit slider value.
    /// Hardware acquisition reads initialize the slider baseline but do not set this flag,
    /// so later recovery reads can keep using the bus value until the user actually expresses intent.
    /// </summary>
    public bool HasUserBrightness => _hasUserBrightness;

    /// <summary>
    /// Initial DDC acquisition seed. Sets the slider and baseline to the hardware value without
    /// marking it as explicit user intent and without raising notifications before binding is attached.
    /// </summary>
    public void InitializeBrightnessFromHardware(double value)
    {
        _brightness = value;
        _virtualBrightness = value;
        _lastUserBrightness = value;
        _hasUserBrightness = false;
    }

    /// <summary>
    /// Hardware-sync setter that mutates <see cref="Brightness"/> without claiming user intent
    /// (i.e. without touching <see cref="LastUserBrightness"/>).
    /// Mirrors the public Brightness setter's notification gating; the only difference is
    /// LastUserBrightness is preserved once a user/profile value exists; before that, hardware reads
    /// are allowed to define the baseline.
    /// Called by MonitorService when a recovered or freshly-promoted monitor's bus value is
    /// authoritative for the slider thumb but does not represent a fresh user choice.
    /// </summary>
    public void SyncBrightnessFromHardware(double value)
    {
        _virtualBrightness = value;
        if (!_hasUserBrightness) _lastUserBrightness = value;
        if (_brightness == value) return;

        int oldRounded = (int)Math.Round(_brightness);
        _brightness = value;
        int newRounded = (int)Math.Round(value);
        if (oldRounded != newRounded)
        {
            OnPropertyChanged(nameof(Brightness));
            OnPropertyChanged(nameof(RoundedBrightness));
            if (_sliderState != SliderState.CurveActive || !_hasCurveTargetBrightness)
                OnPropertyChanged(nameof(EffectiveRoundedBrightness));
        }
    }

    /// <summary>
    /// Brightness rounded to the nearest integer (0-100).
    /// </summary>
    public int RoundedBrightness => (int)Math.Round(_brightness);

    /// <summary>
    /// The "actual current value" shown next to the slider: <see cref="CurveTargetBrightness"/> rounded
    /// when the curve is actively writing hardware (state is <see cref="SliderState.CurveActive"/>)
    /// and the evaluator has supplied a target, otherwise <see cref="RoundedBrightness"/>.
    /// In <see cref="SliderState.CurveSleeping"/> the curve indicator dot still shows its would-be value
    /// (via <see cref="IsCurveDriven"/>), but the bus is at the slider value -
    /// so the value label has to follow the slider, not the curve target.
    /// Released and disabled rows likewise fall through to the slider value
    /// because their hardware is driven by the slider directly
    /// (or not at all, in the disabled case where the row's slider still owns its own hardware
    /// even though master skips it).
    /// Notified whenever <see cref="Brightness"/>, <see cref="CurveTargetBrightness"/>,
    /// or <see cref="SliderState"/> changes
    /// - gated to integer transitions to match RoundedBrightness's contract.
    /// </summary>
    public int EffectiveRoundedBrightness =>
        _sliderState == SliderState.CurveActive && _hasCurveTargetBrightness
            ? (int)Math.Round(_curveTargetBrightness)
            : (int)Math.Round(_brightness);

    /// <summary>
    /// Whether the monitor is powered on.
    /// Managed by <see cref="Services.MonitorService.SetPowerStateAsync"/> after a successful VCP 0xD6 write,
    /// so it reflects the hardware's last-known state.
    /// </summary>
    public bool IsPoweredOn
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = true;

    /// <summary>
    /// Segoe Fluent Icons glyph code for the monitor icon.
    /// Default: <see cref="GlyphCatalog.MONITOR"/>.
    /// </summary>
    public string IconGlyph { get; set; } = GlyphCatalog.MONITOR;

    /// <summary>
    /// Whether this is the master "All Displays" control.
    /// </summary>
    public bool IsMaster { get; set; }

    /// <summary>
    /// Whether this is the standalone nightlight pseudo-monitor
    /// that drives Windows' night-light strength via the registry.
    /// Used by the flyout DataTemplate to swap the row's icon for the nightlight bitmap mask,
    /// hide the power button, and gate the optional kelvin suffix label next to the row name.
    /// </summary>
    public bool IsNightLight { get; set; }

    /// <summary>
    /// Individual monitors this master controls. Unused on non-master instances.
    /// </summary>
    public List<MonitorInfo> Dependents { get; } = [];

    /// <summary>
    /// Single source of truth for this slider's visible/behavioural state.
    /// All of "user excluded from master", "released from curve", "curve currently driving",
    /// and "hardware unreachable" collapse into one enum
    /// so XAML triggers and apply-paths read from the same place.
    /// Use <see cref="SliderStateMachine"/>'s pure transition functions to compute the next value
    /// before assigning back here - the setter raises PropertyChanged
    /// and re-emits the convenience computed properties (<see cref="IsHardwareFunctional"/>, etc.)
    /// so a single state change ripples through XAML in one shot.
    /// </summary>
    public SliderState SliderState
    {
        get => _sliderState;
        set
        {
            if (_sliderState == value) return;
            // EffectiveRoundedBrightness reads from CurveTargetBrightness only in CurveActive after
            // the curve target has been seeded; every other state reads from Brightness.
            // So the displayed value label has to re-bind whenever we cross that boundary in either direction
            // - including CurveActive <-> CurveSleeping, where the bus and the displayed source both flip.
            bool oldShowsCurveTarget = _sliderState == SliderState.CurveActive && _hasCurveTargetBrightness;
            // Stash the state we were in before going Failed so the recovery path can tell
            // a curve-driven row (CurveActive/Sleeping/Released) apart from a plain user-owned one.
            // Curve writes go through MonitorService.EnqueueDirectBrightness which bypasses the Brightness setter,
            // so a curve-active row's Brightness still holds the user's manual value
            // while hardware sits at CurveTargetBrightness.
            // Without this stash the recovery path can't distinguish
            // "hardware is at the curve target, don't trust it as the manual value"
            // from "hardware is at the user's last drag value, fine to sync".
            if (value == SliderState.Failed && _sliderState != SliderState.Failed)
                _preFailureSliderState = _sliderState;
            _sliderState = value;
            OnPropertyChanged();
            // Re-emit every derived bool that XAML or apply-paths might be bound to.
            // Cheaper than letting XAML watch a converter chain on the enum, and lets the setter
            // double as the single source for "this row's appearance changed."
            OnPropertyChanged(nameof(IsHardwareFunctional));
            OnPropertyChanged(nameof(IsParticipatingInMaster));
            OnPropertyChanged(nameof(IsCurveDriven));
            OnPropertyChanged(nameof(IsCurveSleeping));
            OnPropertyChanged(nameof(IsCurveReleased));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsDisabled));
            bool newShowsCurveTarget = _sliderState == SliderState.CurveActive && _hasCurveTargetBrightness;
            if (oldShowsCurveTarget != newShowsCurveTarget) OnPropertyChanged(nameof(EffectiveRoundedBrightness));
        }
    }

    /// <summary>True except in <see cref="SliderState.Failed"/>; gates the slider's IsEnabled binding.</summary>
    public bool IsHardwareFunctional => _sliderState != SliderState.Failed;

    /// <summary>
    /// True when the row was curve-driven (active / sleeping / released)
    /// at the moment it transitioned to <see cref="SliderState.Failed"/>.
    /// Used by the recovery path to decide whether the post-recovery hardware reading
    /// should overwrite <see cref="Brightness"/> or be discarded as the curve target -
    /// see the field-level comment on <see cref="_preFailureSliderState"/>.
    /// </summary>
    public bool WasCurveDrivenBeforeFailure =>
        _preFailureSliderState is SliderState.CurveActive
            or SliderState.CurveSleeping
            or SliderState.CurveReleased;

    /// <summary>True when the row was user-disabled from master writes before it failed.</summary>
    public bool WasDisabledBeforeFailure => _preFailureSliderState == SliderState.Disabled;

    /// <summary>True when this row participates in master-driven changes (not Disabled, not Failed).</summary>
    public bool IsParticipatingInMaster => _sliderState is not (SliderState.Disabled or SliderState.Failed);

    /// <summary>
    /// True when the curve is currently producing a target for this row -
    /// either actively writing hardware (<see cref="SliderState.CurveActive"/>)
    /// or holding a "would-be" indicator value during the disabled-period window
    /// (<see cref="SliderState.CurveSleeping"/>).
    /// Drives the indicator-glyph visibility and the EffectiveRoundedBrightness source.
    /// </summary>
    public bool IsCurveDriven => _sliderState is SliderState.CurveActive or SliderState.CurveSleeping;

    /// <summary>True specifically when the disabled-period window is parked over this row.</summary>
    public bool IsCurveSleeping => _sliderState == SliderState.CurveSleeping;

    /// <summary>True specifically when this individual was released from curve control.</summary>
    public bool IsCurveReleased => _sliderState == SliderState.CurveReleased;

    /// <summary>True when DDC/CI failed and the row's slider is unreachable.</summary>
    public bool IsFailed => _sliderState == SliderState.Failed;

    /// <summary>True when the user excluded this row from master-driven changes.</summary>
    public bool IsDisabled => _sliderState == SliderState.Disabled;

    public bool IsCurveStopwatchVisible
    {
        get => _isCurveStopwatchVisible;
        set
        {
            if (_isCurveStopwatchVisible == value) return;
            _isCurveStopwatchVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurveStopwatchSpinnerVisible));
        }
    }

    public bool IsCurveStopwatchEnabled
    {
        get => _isCurveStopwatchEnabled;
        set
        {
            if (_isCurveStopwatchEnabled == value) return;
            _isCurveStopwatchEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurveStopwatchSpinnerVisible));
            OnPropertyChanged(nameof(CurveStopwatchToolTip));
        }
    }

    public bool IsCurveStopwatchSpinnerVisible => _isCurveStopwatchVisible && _isCurveStopwatchEnabled;

    public int CurveStopwatchMinutes
    {
        get;
        set
        {
            int clamped = Math.Max(1, value);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
        }
    } = 60;

    public DateTime CurveStopwatchEngagedAtUtc
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    public DateTime CurveStopwatchReenableAtUtc
    {
        get => _curveStopwatchReenableAtUtc;
        set
        {
            if (_curveStopwatchReenableAtUtc == value) return;
            _curveStopwatchReenableAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurveStopwatchToolTip));
        }
    }

    public string CurveStopwatchToolTip => _isCurveStopwatchEnabled
        ? $"Current time left: {FormatCurveStopwatchTimeLeft(_curveStopwatchReenableAtUtc)}{Environment.NewLine}click to disable time delayed automatic curve mode reinitialization."
        : "click to enable time delayed automatic curve mode reinitialization.";

    public void RefreshCurveStopwatchToolTip() => OnPropertyChanged(nameof(CurveStopwatchToolTip));

    private static string FormatCurveStopwatchTimeLeft(DateTime endUtc)
    {
        TimeSpan remaining = endUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        int totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return $"{hours}h:{minutes:00}m";
    }

    /// <summary>
    /// Brightness offset captured relative to the master at drag-start.
    /// During a master drag, <c>Brightness = master.Brightness + Offset</c> (clamped to 0-100).
    /// Kept through clamps so the user's intended per-monitor relationship survives the edge.
    ///
    /// WRITE INVARIANT: this field records the user's per-monitor spread. Only the following code paths
    /// are allowed to assign it:
    ///   - <c>BrightnessFlyout.CaptureOffsetsFromMaster</c> from any of:
    ///       * flyout constructor (initial enrollment seed)
    ///       * Master slider PreviewMouseLeftButtonDown / wheel / keyboard (USER actions, including
    ///         offset-mode master drags that intentionally re-anchor the spread)
    ///       * <c>OnCurveToggleStateChanged</c> when curve transitions ON (one-shot snapshot)
    ///       * Sleep / disabled-period resyncs after a master change
    ///   - <c>BrightnessFlyout.InitializeOffsetFromMaster</c> for a newly attached row only
    ///   - <c>BrightnessFlyout.HandleCurveSliderTouch</c> re-engage branch (double-click on a
    ///     CurveReleased row - USER action)
    ///   - <c>MonitorService.PromoteRecovered</c> (Failed-monitor recovery; enrollment-equivalent)
    ///
    /// <see cref="Services.EnvironmentalCurveService"/> MUST NOT write this field. The curve tick
    /// reads <c>Offset</c> as an input only - a curve evaluator that wrote here would silently corrupt
    /// the user's per-monitor spread over the course of a curve session.
    /// </summary>
    public double Offset { get; set; }

    /// <summary>
    /// Runtime-only flag driving the profile-preview overlay on this row's slider.
    /// Set by <c>BrightnessFlyout</c> when the user hovers/focuses a non-selected profile button;
    /// cleared when focus/hover ends or that profile becomes selected.
    /// </summary>
    public bool ShowPreview
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Brightness value for the preview "ghost thumb"
    /// shown over this row's slider while <see cref="ShowPreview"/> is true.
    /// Ignored when ShowPreview is false.
    /// </summary>
    public double PreviewBrightness
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether the previewed profile's enablement state for this row differs from the current state.
    /// Drives the halfway-blend color on the icon and name
    /// to indicate an enable/disable change would occur on selection.
    /// </summary>
    public bool PreviewEnablementDiffers
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Mirror of <see cref="KnownDisplayEntry.WasEverDDCCapable"/> for the matching <see cref="EDIDKey"/>,
    /// projected onto the runtime model so XAML can bind to it directly.
    /// Drives the "warning, click to retry" affordance
    /// shown for monitors that previously talked DDC but currently aren't responding.
    /// </summary>
    public bool WasEverDDCCapable
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Asymmetric DDC health: most recent read failed but a confirmatory write probe succeeded,
    /// so the monitor's write half is still alive even though its reply pipeline is wedged.
    /// Independent of <see cref="IsFailed"/> - the slider stays operable in this state because
    /// brightness writes will still land; the UI just shows an informational glyph and routes
    /// power-off to Ctrl+click instead of plain click. Cleared by <c>MonitorService.PromoteRecovered</c>
    /// when reads come back, and only set when targeted recovery's write probe positively
    /// confirms the write half.
    /// </summary>
    public bool IsReadDegraded
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Most recent DDC/CI error message captured while reading or recovering this monitor.
    /// Null when the monitor is currently DDC-supported
    /// (cleared on successful promotion/recovery).
    /// Surfaced in the warning tooltip so the user gets the actual reason DDC failed
    /// instead of a generic "unsupported" hint.
    /// </summary>
    public string? LastDDCError
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }


    /// <summary>
    /// Most recent curve target the runtime evaluator computed for this row, on the slider's 0-100 range
    /// (already mapped through any night-light invert).
    /// Drives the position of the small curve indicator glyph drawn on the slider track
    /// when the "Show curve indicators on sliders" setting is on.
    /// Independent of <see cref="Brightness"/> so the indicator can sit at the curve's target value
    /// even when the slider thumb has been dragged elsewhere by the user
    /// (offset mode, released individuals, disabled period passes, etc.).
    /// </summary>
    public double CurveTargetBrightness
    {
        get => _curveTargetBrightness;
        set
        {
            int oldEffective = EffectiveRoundedBrightness;
            bool hadTarget = _hasCurveTargetBrightness;
            bool changed = _curveTargetBrightness != value;

            _curveTargetBrightness = value;
            _hasCurveTargetBrightness = true;

            if (changed) OnPropertyChanged();
            if (!hadTarget) OnPropertyChanged(nameof(HasCurveTargetBrightness));

            // EffectiveRoundedBrightness reads CurveTargetBrightness only in CurveActive after a target has
            // been seeded; a Sleeping/Released/Disabled/Enabled/Failed row's value label tracks Brightness,
            // so a curve-target update there shouldn't redundantly bump the label binding.
            if (_sliderState == SliderState.CurveActive && oldEffective != EffectiveRoundedBrightness)
                OnPropertyChanged(nameof(EffectiveRoundedBrightness));
        }
    }

    /// <summary>
    /// True after the curve evaluator or a transition seed has supplied a real target for this row.
    /// Prevents a new CurveActive row from exposing the field's default zero as if it were a sampled curve value.
    /// </summary>
    public bool HasCurveTargetBrightness => _hasCurveTargetBrightness;

    public void SeedCurveTargetBrightnessFromSlider() => CurveTargetBrightness = Math.Clamp(_brightness, 0.0, 100.0);

    public void ClearCurveTargetBrightness()
    {
        if (!_hasCurveTargetBrightness && _curveTargetBrightness == 0.0) return;

        int oldEffective = EffectiveRoundedBrightness;
        _curveTargetBrightness = 0.0;
        _hasCurveTargetBrightness = false;
        OnPropertyChanged(nameof(CurveTargetBrightness));
        OnPropertyChanged(nameof(HasCurveTargetBrightness));

        if (_sliderState == SliderState.CurveActive && oldEffective != EffectiveRoundedBrightness)
            OnPropertyChanged(nameof(EffectiveRoundedBrightness));
    }

    /// <summary>
    /// Runtime-only flag set by the flyout while the user is physically interacting with this row's
    /// slider thumb or track. Covers both the click-to-position track drag and the thumb drag
    /// (the flyout wires it from Slider.PreviewMouseLeftButtonDown/Up plus Thumb.DragStarted/Completed).
    /// Read by the curve service's per-row write path to suppress hardware writes that would visibly
    /// shove the thumb out from under the user's pointer mid-gesture.
    /// Plain field, not INPC: nothing in XAML binds to it - it's purely an apply-path gate.
    /// </summary>
    public bool IsDragging
    {
        get;
        set => field = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private int _suspendCount;
    private HashSet<string>? _pendingNotifications;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_suspendCount > 0)
        {
            // Coalesce: each suspended property name is recorded once,
            // regardless of how many times its setter ran during the suspension.
            // The flush at scope exit fires one PropertyChanged per name,
            // so subscribers see exactly one event per affected property
            // - the apply path's intermediate values are invisible to the rest of the system.
            (_pendingNotifications ??= []).Add(propertyName ?? string.Empty);
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Suspends <see cref="PropertyChanged"/> notifications until the returned scope is disposed.
    /// Setters continue to mutate the underlying fields normally,
    /// but each property's PropertyChanged event is held back and coalesced
    /// - on disposal, exactly one event fires per property whose setter ran while suspended.
    /// Used by <see cref="Services.ProfileManager.ApplyProfile"/>
    /// so subscribers never observe the in-flux state of a multi-step apply
    /// (which would, under autosave, write the partial state into the profile being switched away from,
    /// and would otherwise generate a flurry of redundant DDC writes from MonitorService's slider-driven write path).
    /// Counter-based, so nested suspensions compose:
    /// notifications resume only when every outstanding scope has been disposed.
    /// Disposal is idempotent and safe on the exception path - the apply is wrapped in a using scope,
    /// so a mid-apply throw still flushes pending notifications and decrements the counter.
    /// </summary>
    public IDisposable SuspendNotifications()
    {
        _suspendCount++;
        return new NotificationSuspension(this);
    }

    private void ReleaseSuspension()
    {
        if (_suspendCount == 0) return;
        _suspendCount--;
        if (_suspendCount > 0) return;
        // Flush only when the outermost scope has unwound, so a nested suspension's disposal
        // doesn't leak intermediate events to subscribers that the outer scope intends to hide.
        HashSet<string>? pending = _pendingNotifications;
        _pendingNotifications = null;
        if (pending == null) return;
        foreach (string name in pending)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class NotificationSuspension(MonitorInfo owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            owner.ReleaseSuspension();
        }
    }
}

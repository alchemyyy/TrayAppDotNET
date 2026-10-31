using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// Reflects whether the fan is currently controllable and reporting believable telemetry.
// - Normal: LHM sees the Controls entry and the fan is reporting a non-zero RPM for a non-zero
//   duty cycle (or zero RPM for zero duty cycle).
// - Detached: LHM sees the Controls entry but reports 0 RPM despite a non-zero duty cycle.
//   The fan is plugged in to LHM's view but the header isn't physically driving anything we
//   can read back.
// - Disabled: user has disabled LHM control (or our control) for this fan entirely.
public enum FanState
{
    Disabled,
    Detached,
    Normal
}

// Live Warn Low / Warn High state, from the measured speed compared in each threshold's own unit.
public enum FanSpeedWarning
{
    None,
    Low,
    High
}

// Why a Curve-mode fan with a curve and data source was handed back to hardware control.
// - SourceUnavailable: the data source is gone, not live, invalid (NaN), or stopped updating.
// - CurveUnusable: the curve has no points or produced a non-finite value.
public enum FanCurveFallback
{
    None,
    SourceUnavailable,
    CurveUnusable
}

// How the fan picks its target duty cycle / RPM each tick.
// - Manual: FanDisplayedValue is the target. The slider in the flyout drives this directly.
// - Curve: the assigned curve, sampled at its data source's reading, picks the target; LHMService runs
//   it through FanCurveController (Offset, clamps, Max Delta, Jumpstart, fail-safe) every poll tick.
public enum FanControlMode
{
    Manual,
    Curve
}

// One controllable fan header. Lives in LHMService's Fans collection; persists user-configurable
// fields via settings (clamps, curve assignment, group, etc.). The hardware telemetry fields
// (CurrentRPM, CurrentDutyCycle, LastUpdated) are written every poll by LHMService and are not
// persisted.
public class Fan : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const int DutyCycleDisplayedValueWidth = 44;
    private const int RPMDisplayedValueWidth = 84;
    private const string DutyCycleDisplayedValueSuffix = "%";
    private const string RPMDisplayedValueSuffix = " RPM";
    private const int DutyCycleMaximum = 100;
    private const int FallbackMaximumRPM = 3000;

    // Flyout/manual target unit. Individual fan-property rows have their own RPM flags and do
    // not follow this display mode or the assigned curve mode.

    [XmlAttribute]
    public bool RPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FanDisplayedValueText));
            OnPropertyChanged(nameof(FanDisplayedValueSuffix));
            OnPropertyChanged(nameof(FanDisplayedValueSlotWidth));
            OnPropertyChanged(nameof(FanSliderMaximum));
        }
    }

    // Hard limits on every written speed. Curve outputs clamp into [ClampLow, ClampHigh] after Offset and
    // again after Max Delta and Jumpstart; manual values clamp too. Defaults span the full duty-cycle range.

    [XmlAttribute]
    public int ClampLow
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public bool ClampLowRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public int ClampHigh
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

    [XmlAttribute]
    public bool ClampHighRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // Soft warnings. The measured speed is compared against each threshold in that threshold's own unit;
    // the flyout shows a warning glyph and the tray raises a notification. Doesn't gate the actual write.

    [XmlAttribute]
    public int WarnLow
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public bool WarnLowRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public int WarnHigh
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

    [XmlAttribute]
    public bool WarnHighRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // Maximum change per second of the curve-driven speed, in %/s or RPM/s per DeltaMaxRPMMode. Caps
    // how fast Curve mode is allowed to ramp the fan up or down. 0 disables the limit. Manual writes
    // are not rate-limited.

    [XmlAttribute]
    public int DeltaMax
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

    [XmlAttribute]
    public bool DeltaMaxRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public int Offset
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public bool OffsetRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // Target speed when CurrentControlMode is Manual. The flyout slider edits this.
    [XmlAttribute]
    public int FanDisplayedValue
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FanDisplayedValueText));
            OnPropertyChanged(nameof(FanSliderMaximum));
        }
    } = 50;

    [XmlIgnore]
    public string FanDisplayedValueSuffix => RPMMode ? RPMDisplayedValueSuffix : DutyCycleDisplayedValueSuffix;

    [XmlIgnore]
    public string FanDisplayedValueText => $"{FanDisplayedValue}{FanDisplayedValueSuffix}";

    [XmlIgnore]
    public double FanDisplayedValueSlotWidth => RPMMode ? RPMDisplayedValueWidth : DutyCycleDisplayedValueWidth;

    [XmlIgnore]
    public int FanSliderMaximum =>
        RPMMode ? Math.Max(val1: 100, MaxRPM > 0 ? MaxRPM : Math.Max(_observedMaxRPM, FanDisplayedValue)) : 100;

    /// <summary>
    /// Converts a stored fan-property value to the requested target unit.
    /// </summary>
    public int ConvertSpeedPropertyValue(int value, bool sourceRPMMode, bool targetRPMMode)
        => ConvertSpeedPropertyValue(value, sourceRPMMode, targetRPMMode, RPMReference(null));

    /// <summary>
    /// Gets the startup speed in the requested target unit.
    /// </summary>
    public int StartupSpeedForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(StartupSpeed, StartupSpeedRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the lower clamp in the requested target unit.
    /// </summary>
    public int ClampLowForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(ClampLow, ClampLowRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the upper clamp in the requested target unit.
    /// </summary>
    public int ClampHighForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(ClampHigh, ClampHighRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the lower warning threshold in the requested target unit.
    /// </summary>
    public int WarnLowForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(WarnLow, WarnLowRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the upper warning threshold in the requested target unit.
    /// </summary>
    public int WarnHighForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(WarnHigh, WarnHighRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the maximum delta in the requested target unit.
    /// </summary>
    public int DeltaMaxForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(DeltaMax, DeltaMaxRPMMode, targetRPMMode);

    /// <summary>
    /// Gets the speed offset in the requested target unit.
    /// </summary>
    public int OffsetForRPMMode(bool targetRPMMode) =>
        ConvertSpeedPropertyValue(Offset, OffsetRPMMode, targetRPMMode);

    /// <summary>
    /// Converts a stored fan-property value to the assigned curve unit.
    /// </summary>
    public int ConvertSpeedPropertyValueForCurve(int value, bool sourceRPMMode, Curve? curve) =>
        ConvertSpeedPropertyValue(value, sourceRPMMode, curve?.RPMMode ?? RPMMode, RPMReference(curve));

    /// <summary>
    /// Gets the startup speed in the assigned curve unit.
    /// </summary>
    public int StartupSpeedForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(StartupSpeed, StartupSpeedRPMMode, curve);

    /// <summary>
    /// Gets the lower clamp in the assigned curve unit.
    /// </summary>
    public int ClampLowForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(ClampLow, ClampLowRPMMode, curve);

    /// <summary>
    /// Gets the upper clamp in the assigned curve unit.
    /// </summary>
    public int ClampHighForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(ClampHigh, ClampHighRPMMode, curve);

    /// <summary>
    /// Gets the lower warning threshold in the assigned curve unit.
    /// </summary>
    public int WarnLowForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(WarnLow, WarnLowRPMMode, curve);

    /// <summary>
    /// Gets the upper warning threshold in the assigned curve unit.
    /// </summary>
    public int WarnHighForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(WarnHigh, WarnHighRPMMode, curve);

    /// <summary>
    /// Gets the maximum delta in the assigned curve unit.
    /// </summary>
    public int DeltaMaxForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(DeltaMax, DeltaMaxRPMMode, curve);

    /// <summary>
    /// Gets the speed offset in the assigned curve unit.
    /// </summary>
    public int OffsetForCurve(Curve? curve) =>
        ConvertSpeedPropertyValueForCurve(Offset, OffsetRPMMode, curve);

    /// <summary>
    /// Converts a value in the assigned curve unit to duty cycle %, using the same RPM reference as the
    /// ...ForCurve property conversions so property limits round-trip exactly.
    /// </summary>
    public double CurveValueToDutyCycle(double value, Curve? curve) =>
        curve?.RPMMode ?? RPMMode
            ? RPMToDutyCyclePercent(value, RPMReference(curve))
            : value;

    /// <summary>
    /// Converts an RPM value to duty cycle % against an RPM reference (the speed treated as 100%).
    /// Shared by Manual RPM mode and RPM-unit curves. Not clamped.
    /// </summary>
    public static double RPMToDutyCyclePercent(double rpm, double rpmReference) =>
        rpm / Math.Max(val1: 1.0, rpmReference) * DutyCycleMaximum;

    // Speed to drive the fan at briefly during startup to overcome stiction. If the curve target is
    // above zero but below StartupSpeed and the fan was stopped, Curve mode kicks to StartupSpeed for
    // one poll tick then drops to the target, never above ClampHigh. Skipped if the target is already >= startup.

    [XmlAttribute]
    public int StartupSpeed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = 50;

    [XmlAttribute]
    public bool StartupSpeedRPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // Optional user-supplied maximum RPM. Sentinel -1 == "fall back to peak observed via LHM".

    // Used to compute the duty-cycle <-> RPM conversion for RPMMode and for percentage displays.
    [XmlAttribute]
    public int MaxRPM
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FanSliderMaximum));
        }
    } = -1;

    // Reference by name into Curve.Curves when this fan is not assigned to a group. Grouped
    // fans keep this direct assignment persisted, but resolve their effective curve through
    // their FanGroup while the group assignment is active.

    [XmlAttribute]
    [AllowNull]
    public string AssignedCurveName
    {
        get;
        set
        {
            string normalized = value ?? string.Empty;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignedCurve));
            OnPropertyChanged(nameof(AssignedCurveDisplayLabel));
        }
    } = string.Empty;

    [XmlIgnore]
    public FanGroup? AssignedGroup => FanGroup.Find(Group);

    [XmlIgnore]
    public Curve? AssignedCurve =>
        string.IsNullOrWhiteSpace(Group) ? Curve.Find(AssignedCurveName) : AssignedGroup?.AssignedCurve;

    // Flyout-facing display label. Mirrors the "None" sentinel used by the settings dropdown so
    // the UI reads identically whether the user is looking at this fan in the flyout subtitle or
    // in the Fan Properties page.
    [XmlIgnore]
    public string AssignedCurveDisplayLabel =>
        string.IsNullOrWhiteSpace(Group)
            ? string.IsNullOrEmpty(AssignedCurveName)
                ? LocalizationManager.Instance[nameof(AppStrings.Fan_CurveLabel_None)]
                : string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Instance[nameof(AppStrings.Fan_CurveLabel_Format)],
                    AssignedCurveName)
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance[nameof(AppStrings.Fan_GroupLabel_Format)],
                Group);

    // Period-delimited LHM tree path that locates this fan's Control entry. Spaces -> underscores.
    // Also serves as this Fan's key when serialized so curves/triggers can reference it.
    [XmlAttribute]
    public string DataSourceKey { get; set; } = string.Empty;

    // Parsed from DataSourceKey. ControllerModel is the motherboard / GPU model; ControlsName
    // is LHM's intermediate "Controls" sub-tree; FansName is the specific header label.
    [XmlAttribute]
    public string ControllerModel { get; set; } = string.Empty;

    [XmlAttribute]
    public string ControlsName { get; set; } = string.Empty;

    [XmlAttribute]
    public string FansName { get; set; } = string.Empty;

    // Flyout-facing controller subtitle. Pairs the LHM-assigned fan ID with the controller model
    // so users can disambiguate identically-named headers across multiple controllers.
    [XmlIgnore]
    public string ControllerDisplayLabel => $"{FansName} - {ControllerModel}";

    // User-facing rename. Falls back to FansName for display when empty.

    [XmlAttribute]
    [AllowNull]
    public string UserDefinedName
    {
        get;
        set
        {
            string normalized = value ?? string.Empty;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    } = string.Empty;

    [XmlIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(UserDefinedName) ? FansName : UserDefinedName;

    private int _observedMaxRPM;

    // Live telemetry from LHM. Not persisted.
    [XmlIgnore]
    public int CurrentRPM
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _lastUpdated = DateTime.UtcNow;
            bool observedMaxChanged = value > _observedMaxRPM;
            if (observedMaxChanged) _observedMaxRPM = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastUpdated));
            if (observedMaxChanged) OnPropertyChanged(nameof(FanSliderMaximum));
        }
    }

    [XmlIgnore]
    public double CurrentDutyCycle
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.001) return;
            field = value;
            _lastUpdated = DateTime.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastUpdated));
        }
    }

    private DateTime _lastUpdated = DateTime.UtcNow;

    [XmlIgnore]
    public DateTime LastUpdated
    {
        get => _lastUpdated;
        private set
        {
            if (_lastUpdated == value) return;
            _lastUpdated = value;
            OnPropertyChanged();
        }
    }

    [XmlIgnore]
    public FanState CurrentState
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = FanState.Normal;

    // True once LHM has paired an RPM sensor with this fan. RPM-unit warnings stay quiet without one,
    // since CurrentRPM would read 0 forever.
    [XmlIgnore]
    public bool HasRPMSensor { get; set; }

    // Live Warn Low / Warn High state, debounced by LHMService.
    [XmlIgnore]
    public FanSpeedWarning SpeedWarning
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // Set while Curve mode has handed this fan back to hardware control because the curve's data
    // source or the curve itself can't be trusted. Cleared as soon as the curve drives the fan again.
    [XmlIgnore]
    public FanCurveFallback CurveFallback
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    // User override that forces this fan into a non-functioning state even if LHM still exposes
    // the control. This is persisted separately from CurrentState because CurrentState is live
    // telemetry-derived status.
    [XmlAttribute]
    public bool ForcedNonFunctioning
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ForceNonFunctional));
            if (value) CurrentState = FanState.Detached;
        }
    }

    [XmlIgnore]
    public bool ForceNonFunctional
    {
        get => ForcedNonFunctioning;
        set => ForcedNonFunctioning = value;
    }

    // Persisted lock for the flyout mode button. The evaluator/control layer can use this to avoid
    // changing CurrentControlMode implicitly while still allowing explicit profile application.
    [XmlAttribute]
    public bool ModeLocked
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public FanControlMode CurrentControlMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = FanControlMode.Curve;

    // Reference by name into DeadbandsList.DeadbandsLists. Empty == no deadbands applied.

    [XmlAttribute]
    [AllowNull]
    public string DeadbandsName
    {
        get;
        set
        {
            string normalized = value ?? string.Empty;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Deadbands));
        }
    } = string.Empty;

    [XmlIgnore]
    public DeadbandsList? Deadbands => DeadbandsList.Find(DeadbandsName);

    // Group assignment. When empty, the fan renders in its own cell in the flyout. When set,
    // fans sharing the same group string render together in one cell. Null and empty are
    // treated as equivalent.

    [XmlAttribute]
    public string? Group
    {
        get;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignedGroup));
            OnPropertyChanged(nameof(AssignedCurve));
            OnPropertyChanged(nameof(AssignedCurveDisplayLabel));
        }
    }

    [XmlAttribute]
    public int FlyoutDisplayOrder
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = -1;

    public FanUserSettings SnapshotUserSettings() => new()
    {
        DataSourceKey = DataSourceKey,
        RPMMode = RPMMode,
        ClampLow = ClampLow,
        ClampLowRPMMode = ClampLowRPMMode,
        ClampHigh = ClampHigh,
        ClampHighRPMMode = ClampHighRPMMode,
        WarnLow = WarnLow,
        WarnLowRPMMode = WarnLowRPMMode,
        WarnHigh = WarnHigh,
        WarnHighRPMMode = WarnHighRPMMode,
        DeltaMax = DeltaMax,
        DeltaMaxRPMMode = DeltaMaxRPMMode,
        Offset = Offset,
        OffsetRPMMode = OffsetRPMMode,
        FanDisplayedValue = FanDisplayedValue,
        StartupSpeed = StartupSpeed,
        StartupSpeedRPMMode = StartupSpeedRPMMode,
        MaxRPM = MaxRPM,
        AssignedCurveName = AssignedCurveName,
        UserDefinedName = UserDefinedName,
        CurrentControlMode = CurrentControlMode,
        DeadbandsName = DeadbandsName,
        Group = Group,
        FlyoutDisplayOrder = FlyoutDisplayOrder,
        ModeLocked = ModeLocked,
        ForcedNonFunctioning = ForcedNonFunctioning
    };

    public void ApplyUserSettings(FanUserSettings? settings)
    {
        if (settings == null) return;

        RPMMode = settings.RPMMode;
        ClampLow = settings.ClampLow;
        ClampLowRPMMode = settings.ClampLowRPMMode;
        ClampHigh = settings.ClampHigh;
        ClampHighRPMMode = settings.ClampHighRPMMode;
        WarnLow = settings.WarnLow;
        WarnLowRPMMode = settings.WarnLowRPMMode;
        WarnHigh = settings.WarnHigh;
        WarnHighRPMMode = settings.WarnHighRPMMode;
        DeltaMax = settings.DeltaMax;
        DeltaMaxRPMMode = settings.DeltaMaxRPMMode;
        Offset = settings.Offset;
        OffsetRPMMode = settings.OffsetRPMMode;
        FanDisplayedValue = settings.FanDisplayedValue;
        StartupSpeed = settings.StartupSpeed;
        StartupSpeedRPMMode = settings.StartupSpeedRPMMode;
        MaxRPM = settings.MaxRPM;
        AssignedCurveName = settings.AssignedCurveName;
        UserDefinedName = settings.UserDefinedName;
        CurrentControlMode = settings.CurrentControlMode;
        DeadbandsName = settings.DeadbandsName;
        Group = settings.Group;
        FlyoutDisplayOrder = settings.FlyoutDisplayOrder;
        ModeLocked = settings.ModeLocked;
        ForcedNonFunctioning = settings.ForcedNonFunctioning;
    }

    public void ApplyUserSettings(Fan? source)
    {
        if (source == null) return;
        ApplyUserSettings(source.SnapshotUserSettings());
    }

    public void SwapUserSettingsWith(Fan other)
    {
        FanUserSettings mine = SnapshotUserSettings();
        FanUserSettings theirs = other.SnapshotUserSettings();
        ApplyUserSettings(theirs);
        other.ApplyUserSettings(mine);
    }

    public static void SwapUserSettings(Fan first, Fan second) => first.SwapUserSettingsWith(second);

    public Fan CloneForPersistence()
    {
        Fan clone = new()
        {
            DataSourceKey = DataSourceKey,
            ControllerModel = ControllerModel,
            ControlsName = ControlsName,
            FansName = FansName
        };
        clone.ApplyUserSettings(SnapshotUserSettings());
        return clone;
    }

    private int RPMReference(Curve? curve)
    {
        int candidate = MaxRPM > 0
            ? MaxRPM
            : _observedMaxRPM > 0
                ? _observedMaxRPM
                : CurrentRPM > 0
                    ? CurrentRPM
                    : curve?.MaxRPM > 0
                        ? curve.MaxRPM
                        : FallbackMaximumRPM;
        return Math.Max(DutyCycleMaximum, candidate);
    }

    private static int ConvertSpeedPropertyValue(
        int value,
        bool sourceRPMMode,
        bool targetRPMMode,
        int rpmReference)
    {
        if (sourceRPMMode == targetRPMMode) return value;

        double converted = targetRPMMode
            ? value / (double)DutyCycleMaximum * rpmReference
            : value / (double)Math.Max(val1: 1, rpmReference) * DutyCycleMaximum;
        return (int)Math.Round(converted);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

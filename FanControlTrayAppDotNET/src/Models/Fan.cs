using System.ComponentModel;
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
    Normal,
}

// How the fan picks its target duty cycle / RPM each tick.
// - Manual: FanDisplayedValue is the target. The slider in the flyout drives this directly.
// - Curve: AssignedCurve.Evaluate(SelectedDataSource.Value) picks the target.
public enum FanControlMode
{
    Manual,
    Curve,
}

// One controllable fan header. Lives in LHMService's Fans collection; persists user-configurable
// fields via settings (clamps, curve assignment, group, etc.). The hardware telemetry fields
// (CurrentRPM, CurrentDutyCycle, LastUpdated) are written every poll by LHMService and are not
// persisted.
public class Fan : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const int DutyCycleDisplayedValueWidth = 44;
    private const int RPMDisplayedValueWidth = 64;
    private const string DutyCycleDisplayedValueSuffix = "%";
    private const string RPMDisplayedValueSuffix = "";

    // When true, all speed properties on this fan (Clamps, Warns, FanDisplayedValue, StartupSpeed)
    // are interpreted as RPM. When false, they're duty cycle %. Flipping this flag should
    // recompute the stored values using the current RPM/duty-cycle ratio fetched from LHM at
    // flip time so the user-facing speed stays the same after the unit change.

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

    // Hard limits applied to the resolved target speed. Curve outputs and manual values clamp
    // into [ClampLow, ClampHigh]. Defaults span the full duty-cycle range.

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

    // Soft warnings. The flyout colors the slider / shows an icon if the resolved speed crosses
    // these bounds. Doesn't gate the actual write.

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

    // Maximum change per second, in the active speed unit. Caps how fast the curve service is
    // allowed to ramp the fan up or down. Always expressed in duty cycle % even when RPMMode is
    // true (rate-of-change feels more natural per-PWM than per-RPM).

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

    [XmlIgnore] public string FanDisplayedValueText => $"{FanDisplayedValue}{FanDisplayedValueSuffix}";

    [XmlIgnore]
    public double FanDisplayedValueSlotWidth => RPMMode ? RPMDisplayedValueWidth : DutyCycleDisplayedValueWidth;

    [XmlIgnore]
    public int FanSliderMaximum =>
        RPMMode ? Math.Max(100, MaxRPM > 0 ? MaxRPM : Math.Max(_observedMaxRPM, FanDisplayedValue)) : 100;

    // Speed to drive the fan at briefly during startup to overcome stiction. If the curve or
    // manual target is below StartupSpeed and the fan was previously at zero, we kick to
    // StartupSpeed for one tick then drop to target. Skipped if the target is already >= startup.

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

    [XmlIgnore] public FanGroup? AssignedGroup => FanGroup.Find(Group);

    [XmlIgnore]
    public Curve? AssignedCurve =>
        string.IsNullOrWhiteSpace(Group) ? Curve.Find(AssignedCurveName) : AssignedGroup?.AssignedCurve;

    // Flyout-facing display label. Mirrors the "None" sentinel used by the settings dropdown so
    // the UI reads identically whether the user is looking at this fan in the flyout subtitle or
    // in the Fan Properties page.
    [XmlIgnore]
    public string AssignedCurveDisplayLabel =>
        string.IsNullOrWhiteSpace(Group)
            ? string.IsNullOrEmpty(AssignedCurveName) ? "Curve: None" : $"Curve: {AssignedCurveName}"
            : $"Group: {Group}";

    // Period-delimited LHM tree path that locates this fan's Control entry. Spaces -> underscores.
    // Also serves as this Fan's key when serialized so curves/triggers can reference it.
    [XmlAttribute] public string DataSourceKey { get; set; } = string.Empty;

    // Parsed from DataSourceKey. ControllerModel is the motherboard / GPU model; ControlsName
    // is LHM's intermediate "Controls" sub-tree; FansName is the specific header label.
    [XmlAttribute] public string ControllerModel { get; set; } = string.Empty;

    [XmlAttribute] public string ControlsName { get; set; } = string.Empty;

    [XmlAttribute] public string FansName { get; set; } = string.Empty;

    // Flyout-facing controller subtitle. Pairs the LHM-assigned fan ID with the controller model
    // so users can disambiguate identically-named headers across multiple controllers.
    [XmlIgnore] public string ControllerDisplayLabel => $"{FansName} - {ControllerModel}";

    // User-facing rename. Falls back to FansName for display when empty.

    [XmlAttribute]
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
    } = FanControlMode.Manual;

    [XmlArray("Triggers")]
    [XmlArrayItem("Trigger")]
    public List<Trigger> Triggers { get; set; } = [];

    // Reference by name into DeadbandsList.DeadbandsLists. Empty == no deadbands applied.

    [XmlAttribute]
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

    [XmlIgnore] public DeadbandsList? Deadbands => DeadbandsList.Find(DeadbandsName);

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
        ClampHigh = ClampHigh,
        WarnLow = WarnLow,
        WarnHigh = WarnHigh,
        DeltaMax = DeltaMax,
        Offset = Offset,
        FanDisplayedValue = FanDisplayedValue,
        StartupSpeed = StartupSpeed,
        MaxRPM = MaxRPM,
        AssignedCurveName = AssignedCurveName,
        UserDefinedName = UserDefinedName,
        CurrentControlMode = CurrentControlMode,
        DeadbandsName = DeadbandsName,
        Group = Group,
        FlyoutDisplayOrder = FlyoutDisplayOrder,
        ModeLocked = ModeLocked,
        ForcedNonFunctioning = ForcedNonFunctioning,
        Triggers = CloneTriggers(Triggers),
    };

    public void ApplyUserSettings(FanUserSettings? settings)
    {
        if (settings == null) return;

        RPMMode = settings.RPMMode;
        ClampLow = settings.ClampLow;
        ClampHigh = settings.ClampHigh;
        WarnLow = settings.WarnLow;
        WarnHigh = settings.WarnHigh;
        DeltaMax = settings.DeltaMax;
        Offset = settings.Offset;
        FanDisplayedValue = settings.FanDisplayedValue;
        StartupSpeed = settings.StartupSpeed;
        MaxRPM = settings.MaxRPM;
        AssignedCurveName = settings.AssignedCurveName;
        UserDefinedName = settings.UserDefinedName;
        CurrentControlMode = settings.CurrentControlMode;
        DeadbandsName = settings.DeadbandsName;
        Group = settings.Group;
        FlyoutDisplayOrder = settings.FlyoutDisplayOrder;
        ModeLocked = settings.ModeLocked;
        ForcedNonFunctioning = settings.ForcedNonFunctioning;
        Triggers = CloneTriggers(settings.Triggers);
        OnPropertyChanged(nameof(Triggers));
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
            FansName = FansName,
        };
        clone.ApplyUserSettings(SnapshotUserSettings());
        return clone;
    }

    private static List<Trigger> CloneTriggers(IEnumerable<Trigger> triggers)
    {
        List<Trigger> cloned = [];
        foreach (Trigger trigger in triggers)
        {
            cloned.Add(new Trigger { Name = trigger.Name, Enabled = trigger.Enabled, });
        }

        return cloned;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

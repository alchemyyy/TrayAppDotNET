using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// Persisted catalog entry for named fan groups. The actual fan membership remains on Fan.Group
// so applying a fan profile or swapping fan settings can move a fan without changing the catalog.
public class FanGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly Dictionary<string, FanGroup> FanGroups =
        new(StringComparer.OrdinalIgnoreCase);

    [XmlAttribute]
    public string? Name
    {
        get;
        set
        {
            string normalized = value ?? string.Empty;
            if (field == normalized) return;
            string oldName = field ?? string.Empty;
            field = normalized;
            if (!string.IsNullOrEmpty(oldName)
                && FanGroups.TryGetValue(oldName, out FanGroup? registered)
                && ReferenceEquals(registered, this))
                FanGroups.Remove(oldName);

            Register(this);
            OnPropertyChanged();
        }
    } = string.Empty;

    [XmlAttribute]
    public int DisplayOrder
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
    public bool IsCollapsed
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
    public bool RPMMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (!field && FanDisplayedValue > 100)
                FanDisplayedValue = 100;

            OnPropertyChanged();
        }
    }

    [XmlAttribute]
    public int FanDisplayedValue
    {
        get;
        set
        {
            int normalized = Math.Clamp(value, 0, RPMMode ? int.MaxValue : 100);
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
        }
    } = 50;

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

    [XmlAttribute]
    public string AssignedCurveName
    {
        get;
        set
        {
            string normalized = value ?? string.Empty;
            if (field == normalized) return;
            field = normalized;
            Curve? curve = Curve.Find(normalized);
            if (curve != null)
                RPMMode = curve.RPMMode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignedCurve));
            OnPropertyChanged(nameof(AssignedCurveDisplayLabel));
        }
    } = string.Empty;

    [XmlIgnore] public Curve? AssignedCurve => Curve.Find(AssignedCurveName);

    [XmlIgnore]
    public string AssignedCurveDisplayLabel =>
        string.IsNullOrEmpty(AssignedCurveName) ? "Curve: None" : $"Curve: {AssignedCurveName}";

    public static void Register(FanGroup group)
    {
        if (string.IsNullOrEmpty(group.Name)) return;
        FanGroups[group.Name] = group;
    }

    public static void Unregister(string name) => FanGroups.Remove(name);

    public static FanGroup? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return FanGroups.GetValueOrDefault(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Persisted probe-card layout.
/// </summary>
public class ProbeCard
{
    [XmlAttribute] public string Name { get; set; } = string.Empty;

    [XmlAttribute] public int DisplayOrder { get; set; } = -1;

    [XmlAttribute] public bool IsCollapsed { get; set; }

    [XmlArray("Probes")]
    [XmlArrayItem("Probe")]
    public List<ProbeCardProbe> Probes { get; set; } = [];

    [XmlIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? "Probe Card" : Name;

    /// <summary>
    /// Finds stored probe settings by their data-source key.
    /// </summary>
    public ProbeCardProbe? FindProbe(string? dataSourceKey)
    {
        if (string.IsNullOrWhiteSpace(dataSourceKey)) return null;
        foreach (ProbeCardProbe probe in Probes)
        {
            if (string.Equals(probe.DataSourceKey, dataSourceKey, StringComparison.OrdinalIgnoreCase))
                return probe;
        }

        return null;
    }
}

/// <summary>
/// Persisted probe settings for a probe card.
/// </summary>
public class ProbeCardProbe
{
    [XmlAttribute] public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute] public bool IsSelected { get; set; } = true;

    [XmlAttribute] public string TransformString { get; set; } = string.Empty;

    [XmlAttribute] public bool TruncateValue { get; set; }
}

/// <summary>
/// Global regex replacement rule for display names.
/// </summary>
public class DeviceNicknameRule
{
    [XmlAttribute] public string TargetRegex { get; set; } = string.Empty;

    [XmlAttribute] public string ReplacementString { get; set; } = string.Empty;
}

// User-editable fan settings only. Hardware identity and live telemetry intentionally do not live
// here so these records can be applied or swapped between Fan instances safely.
public class FanUserSettings
{
    [XmlAttribute] public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute] public bool RPMMode { get; set; }

    [XmlAttribute] public int ClampLow { get; set; }

    [XmlAttribute] public int ClampHigh { get; set; } = 100;

    [XmlAttribute] public int WarnLow { get; set; }

    [XmlAttribute] public int WarnHigh { get; set; } = 100;

    [XmlAttribute] public int DeltaMax { get; set; } = 100;

    [XmlAttribute] public int Offset { get; set; }

    [XmlAttribute] public int FanDisplayedValue { get; set; } = 50;

    [XmlAttribute] public int StartupSpeed { get; set; } = 50;

    [XmlAttribute] public int MaxRPM { get; set; } = -1;

    [XmlAttribute] public string AssignedCurveName { get; set; } = string.Empty;

    [XmlAttribute] public string UserDefinedName { get; set; } = string.Empty;

    [XmlAttribute] public FanControlMode CurrentControlMode { get; set; } = FanControlMode.Curve;

    [XmlAttribute] public string DeadbandsName { get; set; } = string.Empty;

    [XmlAttribute] public string? Group { get; set; }

    [XmlAttribute] public int FlyoutDisplayOrder { get; set; } = -1;

    [XmlAttribute] public bool ModeLocked { get; set; }

    [XmlAttribute] public bool ForcedNonFunctioning { get; set; }

    [XmlIgnore]
    public bool ForceNonFunctional
    {
        get => ForcedNonFunctioning;
        set => ForcedNonFunctioning = value;
    }

    [XmlArray("Triggers")]
    [XmlArrayItem("Trigger")]
    public List<Trigger> Triggers { get; set; } = [];
}

// Profile data is deliberately narrower than full FanUserSettings. It captures the flyout-facing
// control state only: curve assignment, lock flag, manual target, and active mode.
public class FanProfile
{
    [XmlAttribute] public string Name { get; set; } = string.Empty;

    [XmlArray("Fans")]
    [XmlArrayItem("Fan")]
    public List<FanProfileEntry> Fans { get; set; } = [];

    public static FanProfile FromFans(string name, IEnumerable<Fan> fans)
    {
        FanProfile profile = new() { Name = name };
        foreach (Fan fan in fans)
        {
            if (string.IsNullOrEmpty(fan.DataSourceKey)) continue;
            profile.Fans.Add(FanProfileEntry.FromFan(fan));
        }

        return profile;
    }

    public void ApplyTo(IEnumerable<Fan> fans)
    {
        Dictionary<string, FanProfileEntry> entries = Fans
            .Where(f => !string.IsNullOrEmpty(f.DataSourceKey))
            .GroupBy(f => f.DataSourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (Fan fan in fans)
        {
            if (!entries.TryGetValue(fan.DataSourceKey, out FanProfileEntry? entry)) continue;
            entry.ApplyTo(fan);
        }
    }
}

public class FanProfileEntry
{
    [XmlAttribute] public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute] public string AssignedCurveName { get; set; } = string.Empty;

    [XmlAttribute] public bool ModeLocked { get; set; }

    [XmlAttribute] public int FanDisplayedValue { get; set; } = 50;

    [XmlAttribute] public FanControlMode CurrentControlMode { get; set; } = FanControlMode.Curve;

    public static FanProfileEntry FromFan(Fan fan) => new()
    {
        DataSourceKey = fan.DataSourceKey,
        AssignedCurveName = fan.AssignedCurveName,
        ModeLocked = fan.ModeLocked,
        FanDisplayedValue = fan.FanDisplayedValue,
        CurrentControlMode = fan.CurrentControlMode
    };

    public void ApplyTo(Fan fan)
    {
        fan.AssignedCurveName = AssignedCurveName;
        fan.ModeLocked = ModeLocked;
        fan.FanDisplayedValue = FanDisplayedValue;
        fan.CurrentControlMode = CurrentControlMode;
    }
}

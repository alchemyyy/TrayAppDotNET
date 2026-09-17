using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// Persisted catalog entry for named fan groups. The actual fan membership remains on Fan.Group
// so applying a fan profile or swapping fan settings can move a fan without changing the catalog.
public class FanGroup : INotifyPropertyChanged
{
    private bool _suppressRegistryUpdate;

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

            if (!_suppressRegistryUpdate)
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
            int normalized = Math.Clamp(value, min: 0, RPMMode ? int.MaxValue : 100);
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
    public Curve? AssignedCurve => Curve.Find(AssignedCurveName);

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

    /// <summary>Creates a named group without publishing it to the process registry.</summary>
    internal static FanGroup CreateUnregistered(string name)
    {
        FanGroup group = new() { _suppressRegistryUpdate = true };
        try
        {
            group.Name = name;
        }
        finally
        {
            group._suppressRegistryUpdate = false;
        }

        return group;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Persisted probe-card layout.
/// </summary>
public class ProbeCard
{
    private const int ValidProfileMask = (1 << FanProfile.SlotCount) - 1;

    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute]
    public int DisplayOrder { get; set; } = -1;

    [XmlAttribute]
    public bool IsCollapsed { get; set; }

    [XmlAttribute]
    public int DisplayProfileMask { get; set; }

    [XmlArray("Probes")]
    [XmlArrayItem("Probe")]
    public List<ProbeCardProbe> Probes { get; set; } = [];

    [XmlIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? "Probe Card" : Name;

    /// <summary>Repairs missing or invalid visibility without assigning a card to every profile.</summary>
    public bool EnsureProfileVisibility(int selectedProfileIndex)
    {
        int normalized = DisplayProfileMask & ValidProfileMask;
        if (normalized == 0)
            normalized = 1 << Math.Clamp(selectedProfileIndex, 0, FanProfile.SlotCount - 1);
        if (normalized == DisplayProfileMask) return false;

        DisplayProfileMask = normalized;
        return true;
    }

    /// <summary>Checks whether the card belongs on the specified profile's flyout.</summary>
    public bool IsVisibleOnProfile(int profileIndex) =>
        profileIndex >= 0 && profileIndex < FanProfile.SlotCount
        && (DisplayProfileMask & (1 << profileIndex)) != 0;

    /// <summary>Changes visibility while refusing to remove the last valid profile.</summary>
    public bool TrySetProfileVisibility(int profileIndex, bool visible)
    {
        if (profileIndex < 0 || profileIndex >= FanProfile.SlotCount) return false;

        int profileBit = 1 << profileIndex;
        int validMask = DisplayProfileMask & ValidProfileMask;
        int updatedMask = visible ? validMask | profileBit : validMask & ~profileBit;
        if (updatedMask == 0) return false;

        DisplayProfileMask = updatedMask;
        return true;
    }

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
    [XmlAttribute]
    public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute]
    public bool IsSelected { get; set; } = true;

    [XmlAttribute]
    public string TransformString { get; set; } = string.Empty;

    [XmlAttribute]
    public bool TruncateValue { get; set; }
}

/// <summary>
/// Global regex replacement rule for display names.
/// </summary>
public class DeviceNicknameRule
{
    [XmlAttribute]
    [AllowNull]
    public string TargetRegex
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    [AllowNull]
    public string ReplacementString
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;
}

// User-editable fan settings only. Hardware identity and live telemetry intentionally do not live
// here so these records can be applied or swapped between Fan instances safely.
public class FanUserSettings
{
    [XmlAttribute]
    public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute]
    public bool RPMMode { get; set; }

    [XmlAttribute]
    public int ClampLow { get; set; }

    [XmlAttribute]
    public bool ClampLowRPMMode { get; set; }

    [XmlAttribute]
    public int ClampHigh { get; set; } = 100;

    [XmlAttribute]
    public bool ClampHighRPMMode { get; set; }

    [XmlAttribute]
    public int WarnLow { get; set; }

    [XmlAttribute]
    public bool WarnLowRPMMode { get; set; }

    [XmlAttribute]
    public int WarnHigh { get; set; } = 100;

    [XmlAttribute]
    public bool WarnHighRPMMode { get; set; }

    [XmlAttribute]
    public int DeltaMax { get; set; } = 100;

    [XmlAttribute]
    public bool DeltaMaxRPMMode { get; set; }

    [XmlAttribute]
    public int Offset { get; set; }

    [XmlAttribute]
    public bool OffsetRPMMode { get; set; }

    [XmlAttribute]
    public int FanDisplayedValue { get; set; } = 50;

    [XmlAttribute]
    public int StartupSpeed { get; set; } = 50;

    [XmlAttribute]
    public bool StartupSpeedRPMMode { get; set; }

    [XmlAttribute]
    public int MaxRPM { get; set; } = -1;

    [XmlAttribute]
    public string AssignedCurveName { get; set; } = string.Empty;

    [XmlAttribute]
    public string UserDefinedName { get; set; } = string.Empty;

    [XmlAttribute]
    public FanControlMode CurrentControlMode { get; set; } = FanControlMode.Curve;

    [XmlAttribute]
    public string DeadbandsName { get; set; } = string.Empty;

    [XmlAttribute]
    public string? Group { get; set; }

    [XmlAttribute]
    public int FlyoutDisplayOrder { get; set; } = -1;

    [XmlAttribute]
    public bool ModeLocked { get; set; }

    [XmlAttribute]
    public bool ForcedNonFunctioning { get; set; }

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

// Profiles own fan names, group membership, and layout as well as flyout control state.
// Hardware calibration and safety limits remain on the fan, outside profile snapshots.
public class FanProfile
{
    public const int SlotCount = 3;

    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    [XmlArray("Fans")]
    [XmlArrayItem("Fan")]
    public List<FanProfileEntry> Fans { get; set; } = [];

    [XmlArray("Groups")]
    [XmlArrayItem("Group")]
    public List<FanProfileGroup> Groups { get; set; } = [];

    /// <summary>Resolves a custom profile name or its numbered fallback.</summary>
    public string DisplayName(int profileIndex) =>
        string.IsNullOrWhiteSpace(Name) ? $"Profile {profileIndex + 1}" : Name;

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

    /// <summary>Saves layout independently of control autosave and retains disconnected fan entries.</summary>
    public void Capture(IEnumerable<Fan> fans, IEnumerable<FanGroup> groups, bool includeControlState)
    {
        Dictionary<string, FanProfileEntry> entries = EntriesByKey();
        foreach (Fan fan in fans)
        {
            if (string.IsNullOrWhiteSpace(fan.DataSourceKey)) continue;
            if (includeControlState || !entries.TryGetValue(fan.DataSourceKey, out FanProfileEntry? entry))
            {
                entries[fan.DataSourceKey] = FanProfileEntry.FromFan(fan);
                continue;
            }

            entry.UserDefinedName = fan.UserDefinedName;
            entry.Group = fan.Group;
            entry.FlyoutDisplayOrder = fan.FlyoutDisplayOrder;
        }

        Fans = [.. entries.Values];
        Groups = [];
        foreach (FanGroup group in groups)
            Groups.Add(FanProfileGroup.FromGroup(group));
    }

    /// <summary>Restores saved fans and clears layout inherited by fans absent from this profile.</summary>
    public void ApplyTo(IEnumerable<Fan> fans)
    {
        Dictionary<string, FanProfileEntry> entries = EntriesByKey();
        foreach (Fan fan in fans)
        {
            if (entries.TryGetValue(fan.DataSourceKey, out FanProfileEntry? entry))
            {
                entry.ApplyTo(fan);
                continue;
            }

            fan.UserDefinedName = string.Empty;
            fan.Group = null;
            fan.FlyoutDisplayOrder = -1;
        }
    }

    private Dictionary<string, FanProfileEntry> EntriesByKey() => Fans
        .Where(entry => !string.IsNullOrWhiteSpace(entry.DataSourceKey))
        .GroupBy(entry => entry.DataSourceKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
}

public class FanProfileEntry
{
    [XmlAttribute]
    public string DataSourceKey { get; set; } = string.Empty;

    [XmlAttribute]
    public string AssignedCurveName { get; set; } = string.Empty;

    [XmlAttribute]
    public bool ModeLocked { get; set; }

    [XmlAttribute]
    public int FanDisplayedValue { get; set; } = 50;

    [XmlAttribute]
    public FanControlMode CurrentControlMode { get; set; } = FanControlMode.Curve;

    [XmlAttribute]
    public string UserDefinedName { get; set; } = string.Empty;

    [XmlAttribute]
    public string? Group { get; set; }

    [XmlAttribute]
    public int FlyoutDisplayOrder { get; set; } = -1;

    public static FanProfileEntry FromFan(Fan fan) => new()
    {
        DataSourceKey = fan.DataSourceKey,
        AssignedCurveName = fan.AssignedCurveName,
        ModeLocked = fan.ModeLocked,
        FanDisplayedValue = fan.FanDisplayedValue,
        CurrentControlMode = fan.CurrentControlMode,
        UserDefinedName = fan.UserDefinedName,
        Group = fan.Group,
        FlyoutDisplayOrder = fan.FlyoutDisplayOrder
    };

    public void ApplyTo(Fan fan)
    {
        fan.AssignedCurveName = AssignedCurveName;
        fan.ModeLocked = ModeLocked;
        fan.FanDisplayedValue = FanDisplayedValue;
        fan.CurrentControlMode = CurrentControlMode;
        fan.UserDefinedName = UserDefinedName;
        fan.Group = Group;
        fan.FlyoutDisplayOrder = FlyoutDisplayOrder;
    }
}

/// <summary>Detached group state that cannot publish inactive profiles into the live group registry.</summary>
public class FanProfileGroup
{
    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute]
    public int DisplayOrder { get; set; }

    [XmlAttribute]
    public bool IsCollapsed { get; set; }

    [XmlAttribute]
    public bool RPMMode { get; set; }

    [XmlAttribute]
    public int FanDisplayedValue { get; set; } = 50;

    [XmlAttribute]
    public FanControlMode CurrentControlMode { get; set; } = FanControlMode.Curve;

    [XmlAttribute]
    public string AssignedCurveName { get; set; } = string.Empty;

    public static FanProfileGroup FromGroup(FanGroup group) => new()
    {
        Name = group.Name ?? string.Empty,
        DisplayOrder = group.DisplayOrder,
        IsCollapsed = group.IsCollapsed,
        RPMMode = group.RPMMode,
        FanDisplayedValue = group.FanDisplayedValue,
        CurrentControlMode = group.CurrentControlMode,
        AssignedCurveName = group.AssignedCurveName
    };

    /// <summary>Creates fresh mutable state for the active profile without sharing the snapshot.</summary>
    public FanGroup ToGroup()
    {
        FanGroup group = FanGroup.CreateUnregistered(Name);
        group.DisplayOrder = DisplayOrder;
        group.IsCollapsed = IsCollapsed;
        group.RPMMode = RPMMode;
        group.FanDisplayedValue = FanDisplayedValue;
        group.CurrentControlMode = CurrentControlMode;
        group.AssignedCurveName = AssignedCurveName;
        return group;
    }
}

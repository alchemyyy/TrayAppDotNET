using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FanControlTrayAppDotNET.UI;

public sealed class FanFlyoutCell : INotifyPropertyChanged
{
    private const string ResourcePath =
        "avares://FanControlTrayAppDotNET/UI/Flyout/FanFlyoutCell.axaml";

    private const string ResourcePrefix = "FanFlyoutCell.";

    private static readonly Lazy<ResourceDictionary> Resources = new(LoadResources);

    public FanFlyoutCell(FanGroup? groupSettings, IEnumerable<Fan> fans)
    {
        GroupSettings = groupSettings;
        Fans = new ObservableCollection<Fan>(fans);
        if (GroupSettings != null)
            GroupSettings.PropertyChanged += OnGroupSettingsPropertyChanged;

        Fans.CollectionChanged += OnFansCollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FanGroup? GroupSettings { get; }

    public string? GroupName => GroupSettings?.Name;

    public bool HasGroupHeader => GroupName != null;

    public bool IsEmptyGroup => HasGroupHeader && Fans.Count == 0;

    public bool IsGroupCollapsed => GroupSettings?.IsCollapsed ?? false;

    public bool AreGroupFansVisible => HasGroupHeader && !IsGroupCollapsed;

    public string GroupExpansionGlyph =>
        IsGroupCollapsed ? GlyphCatalog.COLLAPSED : GlyphCatalog.EXPANDED;

    public string GroupExpansionTooltip =>
        IsGroupCollapsed ? "Expand group" : "Collapse group";

    public FanControlMode GroupCurrentControlMode
    {
        get => GroupSettings?.CurrentControlMode ?? FanControlMode.Curve;
        set => GroupSettings?.CurrentControlMode = value;
    }

    public int GroupFanDisplayedValue
    {
        get => GroupSettings?.FanDisplayedValue ?? 50;
        set => GroupSettings?.FanDisplayedValue = value;
    }

    public bool GroupRPMMode
    {
        get => GroupSettings?.AssignedCurve?.RPMMode ?? GroupSettings?.RPMMode ?? false;
        set => GroupSettings?.RPMMode = value;
    }

    public string GroupFanDisplayedValueText => $"{GroupFanDisplayedValue}{GroupFanDisplayedValueSuffix}";

    public string GroupFanDisplayedValueSuffix => GroupRPMMode ? " RPM" : "%";

    public static int GroupFanDisplayedValueSlotWidth => IntResource("GroupDisplayedValueWidth");

    public static int GroupFanSliderMaximum => IntResource("GroupSliderMaximumValue");

    public int GroupDisplayedValueSlotWidth =>
        GroupRPMMode ? IntResource("GroupRPMDisplayedValueWidth") : GroupFanDisplayedValueSlotWidth;

    public int GroupSliderMaximum => GroupRPMMode ? ResolveGroupRPMMaximum() : GroupFanSliderMaximum;

    public string ActiveCurveText
    {
        get
        {
            if (GroupSettings != null) return GroupSettings.AssignedCurveDisplayLabel;
            if (Fans.Count == 0) return "Curve: None";

            string first = Fans[0].AssignedCurveDisplayLabel;
            foreach (Fan fan in Fans)
            {
                if (!string.Equals(first, fan.AssignedCurveDisplayLabel, StringComparison.Ordinal))
                    return "Mixed curves";
            }

            return first;
        }
    }

    public ObservableCollection<Fan> Fans { get; }

    private void OnFansCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmptyGroup));
        OnPropertyChanged(nameof(ActiveCurveText));
        OnPropertyChanged(nameof(GroupSliderMaximum));
        OnPropertyChanged(nameof(GroupDisplayedValueSlotWidth));
    }

    private void OnGroupSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FanGroup.Name):
                OnPropertyChanged(nameof(GroupName));
                OnPropertyChanged(nameof(HasGroupHeader));
                OnPropertyChanged(nameof(AreGroupFansVisible));
                break;
            case nameof(FanGroup.IsCollapsed):
                OnPropertyChanged(nameof(IsGroupCollapsed));
                OnPropertyChanged(nameof(AreGroupFansVisible));
                OnPropertyChanged(nameof(GroupExpansionGlyph));
                OnPropertyChanged(nameof(GroupExpansionTooltip));
                break;
            case nameof(FanGroup.CurrentControlMode):
                OnPropertyChanged(nameof(GroupCurrentControlMode));
                break;
            case nameof(FanGroup.FanDisplayedValue):
                OnPropertyChanged(nameof(GroupFanDisplayedValue));
                OnPropertyChanged(nameof(GroupFanDisplayedValueText));
                break;
            case nameof(FanGroup.RPMMode):
                OnPropertyChanged(nameof(GroupRPMMode));
                OnPropertyChanged(nameof(GroupFanDisplayedValueText));
                OnPropertyChanged(nameof(GroupFanDisplayedValueSuffix));
                OnPropertyChanged(nameof(GroupSliderMaximum));
                OnPropertyChanged(nameof(GroupDisplayedValueSlotWidth));
                break;
            case nameof(FanGroup.AssignedCurveName):
            case nameof(FanGroup.AssignedCurveDisplayLabel):
                OnPropertyChanged(nameof(ActiveCurveText));
                OnPropertyChanged(nameof(GroupRPMMode));
                OnPropertyChanged(nameof(GroupFanDisplayedValueText));
                OnPropertyChanged(nameof(GroupFanDisplayedValueSuffix));
                OnPropertyChanged(nameof(GroupSliderMaximum));
                OnPropertyChanged(nameof(GroupDisplayedValueSlotWidth));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Resolves the largest useful RPM bound for this grouped slider.
    /// </summary>
    private int ResolveGroupRPMMaximum()
    {
        Curve? curve = GroupSettings?.AssignedCurve;
        int maximum = curve?.MaxRPM > 0 ? curve.MaxRPM : 100;
        foreach (Fan fan in Fans)
        {
            int fanMaximum = fan.MaxRPM > 0
                ? fan.MaxRPM
                : fan.CurrentRPM > 0
                    ? Math.Max(100, fan.CurrentRPM)
                    : maximum;
            if (fanMaximum > maximum)
                maximum = fanMaximum;
        }

        return Math.Max(100, maximum);
    }

    /// <summary>
    /// Reads an integer layout resource for flyout-cell shared sizing.
    /// </summary>
    private static int IntResource(string name)
    {
        object value = Resource(name);
        return value switch
        {
            int intValue => intValue,
            double doubleValue => (int)Math.Round(doubleValue),
            object objectValue => Convert.ToInt32(objectValue, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Loads flyout-cell layout resources from AXAML.
    /// </summary>
    private static ResourceDictionary LoadResources()
    {
        Uri resourceUri = new(ResourcePath);
        object? resources = AvaloniaXamlLoader.Load(resourceUri);
        return resources as ResourceDictionary ??
               throw new InvalidOperationException($"Resource dictionary '{ResourcePath}' could not be loaded.");
    }

    /// <summary>
    /// Resolves one prefixed flyout-cell resource.
    /// </summary>
    private static object Resource(string name)
    {
        string key = ResourcePrefix + name;
        object? value = Resources.Value[key];
        return value ?? throw new InvalidOperationException($"Missing resource '{key}'.");
    }
}

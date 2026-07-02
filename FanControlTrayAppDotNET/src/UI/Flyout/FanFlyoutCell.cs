using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FanControlTrayAppDotNET.UI;

public sealed class FanFlyoutCell : INotifyPropertyChanged
{
    private const int GroupDisplayedValueWidth = 44;
    private const int GroupSliderMaximumValue = 100;

    public FanFlyoutCell(FanGroup? groupSettings, IEnumerable<Fan> fans)
    {
        GroupSettings = groupSettings;
        if (GroupSettings != null)
            GroupSettings.PropertyChanged += OnGroupSettingsPropertyChanged;

        Fans = new ObservableCollection<Fan>(fans);
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

    public string GroupFanDisplayedValueText => $"{GroupFanDisplayedValue}%";

    public static int GroupFanDisplayedValueSlotWidth => GroupDisplayedValueWidth;

    public static int GroupFanSliderMaximum => GroupSliderMaximumValue;

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
            case nameof(FanGroup.AssignedCurveName):
            case nameof(FanGroup.AssignedCurveDisplayLabel):
                OnPropertyChanged(nameof(ActiveCurveText));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Configures Processes-column visibility and left-to-right ordering.</summary>
internal sealed class ProcessColumnChooserWindow : TaskManagerReorderDialog<ProcessColumnSetting>
{
    private const int RedLuminanceWeight = 299;
    private const int GreenLuminanceWeight = 587;
    private const int BlueLuminanceWeight = 114;
    private const int LuminanceDivisor = 1000;
    private const int LightSurfaceThreshold = 128;

    private readonly CheckBox _hideUnusedColumns;

    public ProcessColumnChooserWindow(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<IReadOnlyList<ProcessColumnSetting>> columnsChanged)
        : this(
            ProcessColumnSettings.CloneList(settings?.DetailsColumns),
            settings ?? throw new ArgumentNullException(nameof(settings)),
            palette ?? throw new ArgumentNullException(nameof(palette)),
            resources ?? throw new ArgumentNullException(nameof(resources)),
            ResolveBackground(palette),
            CreateHideUnusedColumnsCheckBox(palette),
            columnsChanged)
    {
    }

    private ProcessColumnChooserWindow(
        List<ProcessColumnSetting> items,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Color background,
        CheckBox hideUnusedColumns,
        Action<IReadOnlyList<ProcessColumnSetting>> columnsChanged)
        : base(
            "Select Processes columns",
            "Choose visible columns and arrange their left-to-right order.",
            items,
            GetSearchText,
            (setting, itemChanged) => BuildVisibilityCheckBox(
                setting,
                items,
                palette,
                resources,
                itemChanged),
            ProcessColumnSettings.CreateDefault,
            orderedItems => columnsChanged(ProcessColumnSettings.CloneList(orderedItems)),
            palette,
            settings.EnableRoundedCorners,
            resources,
            background,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowWidth,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowHeight,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowMinHeight,
            showSearch: true,
            searchPlaceholder: "Search columns",
            CreateScrollBarStyle(resources, background),
            TaskManagerContextMenuWindow.CreateOptions(
                palette,
                settings.EnableRoundedCorners,
                settings),
            setting => ToggleVisibility(items, setting),
            headerTrailingControl: hideUnusedColumns)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(columnsChanged);

        _hideUnusedColumns = hideUnusedColumns;
        _hideUnusedColumns.IsCheckedChanged += OnHideUnusedColumnsChanged;
        Closed += OnChooserClosed;
    }

    private static CheckBox CreateHideUnusedColumnsCheckBox(SettingsPalette palette) => new()
    {
        Content = "Hide unused columns",
        Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
        IsChecked = false,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };

    private void OnHideUnusedColumnsChanged(object? sender, RoutedEventArgs eventArgs)
    {
        Func<ProcessColumnSetting, bool>? includeItem = _hideUnusedColumns.IsChecked == true
            ? static setting => setting.Visible
            : null;
        SetItemFilter(includeItem);
    }

    private void OnChooserClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnChooserClosed;
        _hideUnusedColumns.IsCheckedChanged -= OnHideUnusedColumnsChanged;
    }

    private static string GetSearchText(ProcessColumnSetting setting)
    {
        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        return string.IsNullOrWhiteSpace(setting.Nickname)
            ? definition.Title
            : definition.Title + " " + setting.Nickname;
    }

    private static Control BuildVisibilityCheckBox(
        ProcessColumnSetting setting,
        IReadOnlyList<ProcessColumnSetting> settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action itemChanged)
    {
        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        CheckBox visibility = new()
        {
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            IsChecked = setting.Visible,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        visibility.IsCheckedChanged += (_, _) =>
        {
            bool isVisible = visibility.IsChecked == true;
            if (setting.Visible == isVisible) return;
            if (!isVisible && !HasOtherVisibleColumn(settings, setting))
            {
                visibility.IsChecked = true;
                return;
            }

            setting.Visible = isVisible;
            itemChanged();
        };

        TextBlock label = TrayAppDotNETSettingsUI.Text(definition.Title, palette);
        label.IsHitTestVisible = false;
        label.Margin = resources.AxamlTaskManagerReorderDialog.CheckBoxLabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;

        Grid content = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            }
        };
        content.Children.Add(visibility);
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        return content;
    }

    private static void ToggleVisibility(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessColumnSetting setting)
    {
        if (setting.Visible && !HasOtherVisibleColumn(settings, setting)) return;
        setting.Visible = !setting.Visible;
    }

    private static bool HasOtherVisibleColumn(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessColumnSetting excludedSetting)
    {
        for (int settingIndex = 0; settingIndex < settings.Count; settingIndex++)
        {
            ProcessColumnSetting candidate = settings[settingIndex];
            if (!ReferenceEquals(candidate, excludedSetting) && candidate.Visible) return true;
        }

        return false;
    }

    /// <summary>Asks before restoring every column property to its default value.</summary>
    protected override async Task<bool> ConfirmResetAsync()
    {
        using TrayAppDotNETUpdateConfirmationWindow confirmation = new(
            "Reset process columns?",
            "This will restore the default column visibility, order, widths, and display options.",
            "Reset",
            Palette,
            RoundedCornersEnabled,
            cancelText: "Cancel")
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        TrayAppDotNETUpdatePromptResult result =
            await confirmation.ShowDialog<TrayAppDotNETUpdatePromptResult>(this);
        return result == TrayAppDotNETUpdatePromptResult.Confirmed;
    }

    private static Color ResolveBackground(SettingsPalette palette)
    {
        Color paletteBackground = palette.Background;
        int luminance = (paletteBackground.R * RedLuminanceWeight
                         + paletteBackground.G * GreenLuminanceWeight
                         + paletteBackground.B * BlueLuminanceWeight)
                        / LuminanceDivisor;
        return luminance >= LightSurfaceThreshold
            ? TaskManagerWindowResources.ProcessColumnChooserLightBackgroundColor
            : TaskManagerWindowResources.ProcessColumnChooserDarkBackgroundColor;
    }

    private static SettingsScrollBarStyle CreateScrollBarStyle(
        TaskManagerWindowResources resources,
        Color background) =>
        new(
            resources.AxamlProcessTable.ScrollBarTrackThickness,
            resources.AxamlProcessTable.ScrollBarIdleThumbThickness,
            resources.AxamlProcessTable.ScrollBarHoverThumbThickness,
            resources.AxamlProcessTable.ScrollBarThumbEndMargin,
            resources.AxamlProcessTable.ScrollBarMinimumThumbLength,
            background,
            TaskManagerWindowResources.ProcessGridScrollThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            ShowButtonsOnHover: true);
}

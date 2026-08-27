using Avalonia.Controls;
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

    public ProcessColumnChooserWindow(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<List<ProcessColumnSetting>> apply)
        : this(
            ProcessColumnSettings.CloneList(settings?.DetailsColumns),
            settings ?? throw new ArgumentNullException(nameof(settings)),
            palette ?? throw new ArgumentNullException(nameof(palette)),
            resources ?? throw new ArgumentNullException(nameof(resources)),
            ResolveBackground(palette),
            apply)
    {
    }

    private ProcessColumnChooserWindow(
        List<ProcessColumnSetting> items,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Color background,
        Action<List<ProcessColumnSetting>> apply)
        : base(
            "Select Processes columns",
            "Choose visible columns and arrange their left-to-right order.",
            items,
            GetSearchText,
            setting => BuildVisibilityCheckBox(setting, palette, resources),
            ProcessColumnSettings.CreateDefault,
            orderedItems => apply(ProcessColumnSettings.CloneList(orderedItems)),
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
            ToggleVisibility)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(apply);
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
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        CheckBox visibility = new()
        {
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            IsChecked = setting.Visible,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        visibility.IsCheckedChanged += (_, _) => setting.Visible = visibility.IsChecked == true;

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

    private static void ToggleVisibility(ProcessColumnSetting setting) => setting.Visible = !setting.Visible;

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

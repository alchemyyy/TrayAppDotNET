using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.UI.Flyout;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private const int CurveSmoothnessMin = 0;
    private const int CurveSmoothnessMax = 100;

    private Environmental.EnvironmentalCurveEditor? _environmentalCurveEditor;
    private SettingsComboBox? _environmentalProfileCombo;
    private SettingsToggle? _showBrightnessCurveToggle;
    private SettingsToggle? _showNightLightCurveToggle;
    private SettingsToggle? _offsetModeToggle;
    private SettingsToggle? _followTheSunToggle;
    private SettingsToggle? _useDaylightSavingsToggle;
    private SettingsToggle? _disabledPeriodToggle;
    private SettingsToggle? _disabledPeriodFollowTheSunToggle;
    private SettingsToggle? _showCursorReadoutToggle;
    private SettingsToggle? _showSunOverlayToggle;
    private TextBox? _disabledPeriodStartBox;
    private TextBox? _disabledPeriodEndBox;
    private TextBox? _sunOverlayDateBox;
    private TextBox? _latitudeBox;
    private TextBox? _longitudeBox;
    private StackPanel? _legendPanel;
    private StackPanel? _brightnessLegendItem;
    private StackPanel? _nightLightLegendItem;
    private StackPanel? _currentTimeLegendItem;
    private Control? _disabledPeriodFollowTheSunRow;
    private Control? _disabledPeriodFieldsRow;
    private SettingsButton? _previewSweepButton;
    private BrightnessFlyoutWindow? _environmentalFlyout;
    private EnvironmentalCurve? _environmentalCurveDisplay;
    private DispatcherTimer? _curveSaveDebounceTimer;
    private DateTime _environmentalSunOverlayDate = DateTime.Today;
    private int _environmentalProfileIndex = -1;
    private bool _suppressEnvironmentalEvents;
    private bool _environmentalEventsAttached;
    private bool _environmentalCurveRuntimeNotifyQueued;
    private bool _environmentalCurveColorCallbacksWired;

    private static HttpClient EnvironmentalHttpClient =>
        field ??= new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(TimeConstants.EnvironmentalHttpClientTimeoutMs),
        };

    private StackPanel BuildEnvironmentalPage()
    {
        StopEnvironmentalPageSession();

        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Environmental_SectionHeader", "Environmental"), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Environmental_SectionDescription",
                "Edit per-profile time-of-day curves for brightness and night light."),
            p,
            new Thickness(0, 0, 0, 12)));

        _environmentalCurveEditor = new Environmental.EnvironmentalCurveEditor
        {
            Height = 270,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Palette = BuildEnvironmentalEditorPalette(p),
        };
        stack.Children.Add(_environmentalCurveEditor);

        Grid top = new();
        top.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        top.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        StackPanel left = new() { VerticalAlignment = VerticalAlignment.Top, };
        left.Children.Add(BuildEnvironmentalLegend(p));
        left.Children.Add(BuildEnvironmentalProfileRow(p));
        left.Children.Add(BuildEnvironmentalModeRows(p));
        left.Children.Add(BuildEnvironmentalDisabledPeriodRows(p));
        left.Children.Add(BuildEnvironmentalResetButton(p));
        Grid.SetColumn(left, 0);
        top.Children.Add(left);

        StackPanel right = new() { VerticalAlignment = VerticalAlignment.Top, };
        right.Children.Add(BuildEnvironmentalPreviewControls(p));
        right.Children.Add(BuildEnvironmentalLocationCard(p));
        Grid.SetColumn(right, 1);
        top.Children.Add(right);

        stack.Children.Add(top);

        AttachEnvironmentalEvents();
        SeedEnvironmentalPage();
        return stack;
    }
}

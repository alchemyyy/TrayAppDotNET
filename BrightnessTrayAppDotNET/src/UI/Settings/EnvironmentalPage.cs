using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private const int CurveSmoothnessMin = 0;
    private const int CurveSmoothnessMax = 100;

    private readonly List<EnvironmentalPageState> _environmentalPageGenerations = [];
    private EnvironmentalPageState? _environmentalPageState;
    private long _nextEnvironmentalPageGeneration;

    private EnvironmentalCurveEditor? _environmentalCurveEditor
    {
        get => _environmentalPageState?.CurveEditor;
        set { if (_environmentalPageState != null) _environmentalPageState.CurveEditor = value; }
    }

    private SettingsComboBox? _environmentalProfileCombo
    {
        get => _environmentalPageState?.ProfileCombo;
        set { if (_environmentalPageState != null) _environmentalPageState.ProfileCombo = value; }
    }

    private SettingsToggle? _showBrightnessCurveToggle
    {
        get => _environmentalPageState?.ShowBrightnessCurveToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.ShowBrightnessCurveToggle = value; }
    }

    private SettingsToggle? _showNightLightCurveToggle
    {
        get => _environmentalPageState?.ShowNightLightCurveToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.ShowNightLightCurveToggle = value; }
    }

    private SettingsToggle? _offsetModeToggle
    {
        get => _environmentalPageState?.OffsetModeToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.OffsetModeToggle = value; }
    }

    private SettingsToggle? _followTheSunToggle
    {
        get => _environmentalPageState?.FollowTheSunToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.FollowTheSunToggle = value; }
    }

    private SettingsToggle? _useDaylightSavingsToggle
    {
        get => _environmentalPageState?.UseDaylightSavingsToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.UseDaylightSavingsToggle = value; }
    }

    private SettingsToggle? _disabledPeriodToggle
    {
        get => _environmentalPageState?.DisabledPeriodToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodToggle = value; }
    }

    private SettingsToggle? _disabledPeriodFollowTheSunToggle
    {
        get => _environmentalPageState?.DisabledPeriodFollowTheSunToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodFollowTheSunToggle = value; }
    }

    private SettingsToggle? _showCursorReadoutToggle
    {
        get => _environmentalPageState?.ShowCursorReadoutToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.ShowCursorReadoutToggle = value; }
    }

    private SettingsToggle? _showSunOverlayToggle
    {
        get => _environmentalPageState?.ShowSunOverlayToggle;
        set { if (_environmentalPageState != null) _environmentalPageState.ShowSunOverlayToggle = value; }
    }

    private TextBox? _disabledPeriodStartBox
    {
        get => _environmentalPageState?.DisabledPeriodStartBox;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodStartBox = value; }
    }

    private TextBox? _disabledPeriodEndBox
    {
        get => _environmentalPageState?.DisabledPeriodEndBox;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodEndBox = value; }
    }

    private TextBox? _sunOverlayDateBox
    {
        get => _environmentalPageState?.SunOverlayDateBox;
        set { if (_environmentalPageState != null) _environmentalPageState.SunOverlayDateBox = value; }
    }

    private Calendar? _sunOverlayCalendar
    {
        get => _environmentalPageState?.SunOverlayCalendar;
        set { if (_environmentalPageState != null) _environmentalPageState.SunOverlayCalendar = value; }
    }

    private Popup? _sunOverlayDatePopup
    {
        get => _environmentalPageState?.SunOverlayDatePopup;
        set { if (_environmentalPageState != null) _environmentalPageState.SunOverlayDatePopup = value; }
    }

    private TextBox? _latitudeBox
    {
        get => _environmentalPageState?.LatitudeBox;
        set { if (_environmentalPageState != null) _environmentalPageState.LatitudeBox = value; }
    }

    private TextBox? _longitudeBox
    {
        get => _environmentalPageState?.LongitudeBox;
        set { if (_environmentalPageState != null) _environmentalPageState.LongitudeBox = value; }
    }

    private StackPanel? _legendPanel
    {
        get => _environmentalPageState?.LegendPanel;
        set { if (_environmentalPageState != null) _environmentalPageState.LegendPanel = value; }
    }

    private StackPanel? _brightnessLegendItem
    {
        get => _environmentalPageState?.BrightnessLegendItem;
        set { if (_environmentalPageState != null) _environmentalPageState.BrightnessLegendItem = value; }
    }

    private StackPanel? _nightLightLegendItem
    {
        get => _environmentalPageState?.NightLightLegendItem;
        set { if (_environmentalPageState != null) _environmentalPageState.NightLightLegendItem = value; }
    }

    private StackPanel? _currentTimeLegendItem
    {
        get => _environmentalPageState?.CurrentTimeLegendItem;
        set { if (_environmentalPageState != null) _environmentalPageState.CurrentTimeLegendItem = value; }
    }

    private Control? _disabledPeriodFollowTheSunRow
    {
        get => _environmentalPageState?.DisabledPeriodFollowTheSunRow;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodFollowTheSunRow = value; }
    }

    private Control? _disabledPeriodFieldsRow
    {
        get => _environmentalPageState?.DisabledPeriodFieldsRow;
        set { if (_environmentalPageState != null) _environmentalPageState.DisabledPeriodFieldsRow = value; }
    }

    private SettingsButton? _previewSweepButton
    {
        get => _environmentalPageState?.PreviewSweepButton;
        set { if (_environmentalPageState != null) _environmentalPageState.PreviewSweepButton = value; }
    }

    private BrightnessFlyoutWindow? _environmentalFlyout
    {
        get => _environmentalPageState?.Flyout;
        set { if (_environmentalPageState != null) _environmentalPageState.Flyout = value; }
    }

    private EnvironmentalCurve? _environmentalCurveDisplay
    {
        get => _environmentalPageState?.CurveDisplay;
        set { if (_environmentalPageState != null) _environmentalPageState.CurveDisplay = value; }
    }

    private DispatcherTimer? _curveSaveDebounceTimer
    {
        get => _environmentalPageState?.CurveSaveDebounceTimer;
        set { if (_environmentalPageState != null) _environmentalPageState.CurveSaveDebounceTimer = value; }
    }

    private UIResourceScope? _environmentalPageResources
    {
        get => _environmentalPageState?.Resources;
        set { if (_environmentalPageState != null) _environmentalPageState.Resources = value; }
    }

    private EnvironmentalMapPickerWindow? _environmentalMapPicker
    {
        get => _environmentalPageState?.MapPicker;
        set { if (_environmentalPageState != null) _environmentalPageState.MapPicker = value; }
    }

    private DateTime _environmentalSunOverlayDate
    {
        get => _environmentalPageState?.SunOverlayDate ?? DateTime.Today;
        set { if (_environmentalPageState != null) _environmentalPageState.SunOverlayDate = value; }
    }

    private int _environmentalProfileIndex
    {
        get => _environmentalPageState?.ProfileIndex ?? -1;
        set { if (_environmentalPageState != null) _environmentalPageState.ProfileIndex = value; }
    }

    private long _environmentalPageGeneration => _environmentalPageState?.Generation ?? 0;

    private bool _suppressEnvironmentalEvents
    {
        get => _environmentalPageState?.SuppressEnvironmentalEvents ?? false;
        set { if (_environmentalPageState != null) _environmentalPageState.SuppressEnvironmentalEvents = value; }
    }

    private bool _suppressSunOverlayCalendarEvents
    {
        get => _environmentalPageState?.SuppressSunOverlayCalendarEvents ?? false;
        set { if (_environmentalPageState != null) _environmentalPageState.SuppressSunOverlayCalendarEvents = value; }
    }

    private bool _environmentalEventsAttached
    {
        get => _environmentalPageState?.EventsAttached ?? false;
        set { if (_environmentalPageState != null) _environmentalPageState.EventsAttached = value; }
    }

    private bool _environmentalCurveRuntimeNotifyQueued
    {
        get => _environmentalPageState?.CurveRuntimeNotifyQueued ?? false;
        set { if (_environmentalPageState != null) _environmentalPageState.CurveRuntimeNotifyQueued = value; }
    }

    private bool _environmentalCurveColorCallbacksWired
    {
        get => _environmentalPageState?.CurveColorCallbacksWired ?? false;
        set { if (_environmentalPageState != null) _environmentalPageState.CurveColorCallbacksWired = value; }
    }

    private static HttpClient EnvironmentalHttpClient =>
        field ??= new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(TimeConstants.EnvironmentalHttpClientTimeoutMs)
        };

    private StackPanel BuildEnvironmentalPage()
    {
        EnvironmentalPageState pageState = new(++_nextEnvironmentalPageGeneration);
        _environmentalPageGenerations.Add(pageState);
        EnvironmentalPageState? previousState = _environmentalPageState;
        _environmentalPageState = pageState;
        try
        {
            return BuildEnvironmentalPageCandidate(pageState);
        }
        finally
        {
            if (ReferenceEquals(_environmentalPageState, pageState) && !pageState.IsActivated)
                _environmentalPageState = previousState;
        }
    }

    private StackPanel BuildEnvironmentalPageCandidate(EnvironmentalPageState pageState)
    {
        UIResourceScope pageResources = OwnPageResource(new UIResourceScope(
            nameof(BrightnessSettingsWindow) + ".EnvironmentalPage",
            exception => WPFLog.Log(
                $"Brightness environmental page cleanup failed: {exception.GetType().Name}: {exception.Message}")));
        _environmentalPageResources = pageResources;
        AddPageCleanup(() => RetireEnvironmentalPage(pageState));

        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Environmental_SectionHeader", "Environmental"), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Environmental_SectionDescription",
                "Edit per-profile time-of-day curves for brightness and night light."),
            p,
            new Thickness(0, 0, 0, 12)));

        EnvironmentalCurveEditor curveEditor = pageResources.Own(new EnvironmentalCurveEditor
        {
            Height = 270,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Palette = BuildEnvironmentalEditorPalette(p)
        });
        _environmentalCurveEditor = curveEditor;
        stack.Children.Add(curveEditor);

        Grid top = new();
        top.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        top.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        StackPanel left = new() { VerticalAlignment = VerticalAlignment.Top };
        left.Children.Add(BuildEnvironmentalLegend(p));
        left.Children.Add(BuildEnvironmentalProfileRow(p));
        left.Children.Add(BuildEnvironmentalModeRows(p));
        left.Children.Add(BuildEnvironmentalDisabledPeriodRows(p));
        left.Children.Add(BuildEnvironmentalResetButton(p));
        Grid.SetColumn(left, 0);
        top.Children.Add(left);

        StackPanel right = new() { VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(BuildEnvironmentalPreviewControls(p));
        right.Children.Add(BuildEnvironmentalLocationCard(p));
        Grid.SetColumn(right, 1);
        top.Children.Add(right);

        stack.Children.Add(top);

        // Activate before publication so initialization failure rolls back to the active page generation
        ActivateEnvironmentalPage(pageState);
        return stack;
    }

    private bool IsCurrentEnvironmentalPage(long pageGeneration) =>
        _environmentalPageState is
        {
            IsRetired: false,
            Resources: { IsDisposed: false }
        } pageState && pageState.Generation == pageGeneration;

    private void ActivateEnvironmentalPage(EnvironmentalPageState pageState)
    {
        if (pageState.IsRetired || pageState.IsActivated) return;

        _environmentalPageState = pageState;
        try
        {
            AttachEnvironmentalEvents();
            SeedEnvironmentalPage();
            pageState.IsActivated = true;
        }
        catch
        {
            RunEnvironmentalPageCleanup(
                "ActivateEnvironmentalPage",
                () => StopEnvironmentalPageSession(clearPreviewHardware: false));
            throw;
        }
    }

    private void RetireEnvironmentalPage(EnvironmentalPageState pageState)
    {
        if (pageState.IsRetired) return;

        EnvironmentalPageState? publishedState = _environmentalPageState;
        bool wasPublished = ReferenceEquals(publishedState, pageState);
        pageState.IsRetired = true;
        _environmentalPageState = pageState;
        try
        {
            RunEnvironmentalPageCleanup(
                nameof(StopEnvironmentalPageSession),
                () => StopEnvironmentalPageSession(wasPublished && pageState.IsActivated));
            RunEnvironmentalPageCleanup(nameof(CloseEnvironmentalMapPicker), CloseEnvironmentalMapPicker);
            RunEnvironmentalPageCleanup(
                nameof(StopCurveSaveDebounceTimer),
                () => StopCurveSaveDebounceTimer(flushPendingSave: true));
            pageState.Resources?.Dispose();
        }
        finally
        {
            pageState.ClearReferences();
            _environmentalPageGenerations.Remove(pageState);
            EnvironmentalPageState? restoredState = wasPublished
                ? _environmentalPageGenerations.Count > 0
                    ? _environmentalPageGenerations[^1]
                    : null
                : publishedState;
            _environmentalPageState = restoredState;
            if (wasPublished && restoredState is { IsActivated: true, IsRetired: false })
            {
                RunEnvironmentalPageCleanup(
                    "RestorePreviousEnvironmentalPage",
                    () => ApplyEnvironmentalPreviewState(restoredState.SunOverlayDate));
            }
        }
    }

    private static void RunEnvironmentalPageCleanup(string operation, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception exception)
        {
            WPFLog.Log($"Brightness environmental page {operation} cleanup failed: {exception.Message}");
        }
    }

    private sealed class EnvironmentalPageState(long generation)
    {
        public readonly long Generation = generation;
        public EnvironmentalCurveEditor? CurveEditor;
        public SettingsComboBox? ProfileCombo;
        public SettingsToggle? ShowBrightnessCurveToggle;
        public SettingsToggle? ShowNightLightCurveToggle;
        public SettingsToggle? OffsetModeToggle;
        public SettingsToggle? FollowTheSunToggle;
        public SettingsToggle? UseDaylightSavingsToggle;
        public SettingsToggle? DisabledPeriodToggle;
        public SettingsToggle? DisabledPeriodFollowTheSunToggle;
        public SettingsToggle? ShowCursorReadoutToggle;
        public SettingsToggle? ShowSunOverlayToggle;
        public TextBox? DisabledPeriodStartBox;
        public TextBox? DisabledPeriodEndBox;
        public TextBox? SunOverlayDateBox;
        public Calendar? SunOverlayCalendar;
        public Popup? SunOverlayDatePopup;
        public TextBox? LatitudeBox;
        public TextBox? LongitudeBox;
        public StackPanel? LegendPanel;
        public StackPanel? BrightnessLegendItem;
        public StackPanel? NightLightLegendItem;
        public StackPanel? CurrentTimeLegendItem;
        public Control? DisabledPeriodFollowTheSunRow;
        public Control? DisabledPeriodFieldsRow;
        public SettingsButton? PreviewSweepButton;
        public BrightnessFlyoutWindow? Flyout;
        public EnvironmentalCurve? CurveDisplay;
        public DispatcherTimer? CurveSaveDebounceTimer;
        public UIResourceScope? Resources;
        public EnvironmentalMapPickerWindow? MapPicker;
        public DateTime SunOverlayDate = DateTime.Today;
        public int ProfileIndex = -1;
        public bool SuppressEnvironmentalEvents;
        public bool SuppressSunOverlayCalendarEvents;
        public bool EventsAttached;
        public bool CurveRuntimeNotifyQueued;
        public bool CurveColorCallbacksWired;
        public bool IsActivated;
        public bool IsRetired;

        public void ClearReferences()
        {
            CurveEditor = null;
            ProfileCombo = null;
            ShowBrightnessCurveToggle = null;
            ShowNightLightCurveToggle = null;
            OffsetModeToggle = null;
            FollowTheSunToggle = null;
            UseDaylightSavingsToggle = null;
            DisabledPeriodToggle = null;
            DisabledPeriodFollowTheSunToggle = null;
            ShowCursorReadoutToggle = null;
            ShowSunOverlayToggle = null;
            DisabledPeriodStartBox = null;
            DisabledPeriodEndBox = null;
            SunOverlayDateBox = null;
            SunOverlayCalendar = null;
            SunOverlayDatePopup = null;
            LatitudeBox = null;
            LongitudeBox = null;
            LegendPanel = null;
            BrightnessLegendItem = null;
            NightLightLegendItem = null;
            CurrentTimeLegendItem = null;
            DisabledPeriodFollowTheSunRow = null;
            DisabledPeriodFieldsRow = null;
            PreviewSweepButton = null;
            Flyout = null;
            CurveDisplay = null;
            CurveSaveDebounceTimer = null;
            Resources = null;
            MapPicker = null;
            EventsAttached = false;
            CurveRuntimeNotifyQueued = false;
            CurveColorCallbacksWired = false;
        }
    }
}

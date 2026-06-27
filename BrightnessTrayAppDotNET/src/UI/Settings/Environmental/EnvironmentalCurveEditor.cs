using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.SunriseSunset;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public readonly record struct EnvironmentalCurveEditorPalette(
    Color Background,
    Color Foreground,
    Color SecondaryForeground,
    Color CardBackground,
    Color Border,
    Color GridLine,
    Color BrightnessCurve,
    Color NightLightCurve,
    Color CurrentTime,
    Color TwilightBackdrop,
    Color NightBackdrop,
    Color DisabledBand,
    Color PreviewTint,
    Color Accent)
{
    public static EnvironmentalCurveEditorPalette Default { get; } =
        FromAppTheme(AppTheme.Default, false);

    public static EnvironmentalCurveEditorPalette FromAppTheme(AppTheme theme, bool isLight) =>
        FromSettingsPalette(
            CreateSettingsPalette(theme, isLight),
            theme.EnvironmentalBrightnessCurve.For(isLight),
            theme.EnvironmentalNightLightCurve.For(isLight),
            theme.EnvironmentalCurrentTime.For(isLight),
            theme.EnvironmentalTwilightBackdrop.For(isLight),
            theme.EnvironmentalNightBackdrop.For(isLight),
            theme.EnvironmentalGridLine.For(isLight),
            theme.CurveDisabledBandOverlay.For(isLight),
            theme.EnvironmentalPreviewTint.For(isLight));

    public static EnvironmentalCurveEditorPalette FromSettingsPalette(
        SettingsPalette palette,
        Color brightnessCurve,
        Color nightLightCurve,
        Color currentTime,
        Color twilightBackdrop,
        Color nightBackdrop,
        Color gridLine,
        Color disabledBand,
        Color previewTint)
    {
        return new EnvironmentalCurveEditorPalette(
            Background: palette.Background,
            Foreground: palette.Foreground,
            SecondaryForeground: palette.SecondaryForeground,
            CardBackground: palette.CardBackground,
            Border: palette.Border,
            GridLine: gridLine,
            BrightnessCurve: brightnessCurve,
            NightLightCurve: nightLightCurve,
            CurrentTime: currentTime,
            TwilightBackdrop: twilightBackdrop,
            NightBackdrop: nightBackdrop,
            DisabledBand: disabledBand,
            PreviewTint: previewTint,
            Accent: palette.Accent);
    }

    private static SettingsPalette CreateSettingsPalette(AppTheme theme, bool isLight) =>
        new(
            theme.Background.For(isLight),
            theme.Foreground.For(isLight),
            theme.Border.For(isLight),
            theme.Hover.For(isLight),
            theme.Pressed.For(isLight),
            theme.CardBackground.For(isLight),
            theme.ControlBackground.For(isLight),
            theme.SecondaryForeground.For(isLight),
            theme.DisabledForeground.For(isLight),
            theme.Accent.For(isLight),
            theme.ToggleSwitchOnTrack.For(isLight),
            theme.ToggleSwitchOnThumb.For(isLight),
            theme.TextBoxFocused.For(isLight),
            theme.SliderProgress.For(isLight),
            theme.SliderTrack.For(isLight),
            theme.SliderThumb.For(isLight),
            theme.CloseButtonHover.For(isLight),
            theme.CloseButtonPressed.For(isLight),
            theme.CloseButtonGlyphActive.For(isLight));
}

/// <summary>
/// Brightness environmental curve editor. The control mutates the supplied
/// <see cref="EnvironmentalCurve"/> directly and leaves persistence with the settings page.
/// </summary>
public sealed partial class EnvironmentalCurveEditor : Control
{
    private enum Series
    {
        Brightness,
        NightLight,
    }

    private enum LimitKind
    {
        Min,
        Max,
    }

    private enum DisabledPin
    {
        Start,
        End,
    }

    private readonly record struct LimitLabelHit(Series Series, LimitKind Kind, Rect Rect);

    private readonly record struct SunOverlayCacheKey(
        double Latitude,
        double Longitude,
        bool UseDaylightSavings,
        DateTime Date);

    private const double AxisGutterWidth = 28.0;
    private const double TimeAxisHeight = 22.0;
    private const double PlotInsetX = 10.0;
    private const double PlotInsetYBase = 8.0;
    private const double DisabledPeriodPinAreaHeight = 14.0;
    private const double ThumbSize = 14.0;
    private const double ThumbHitPadding = ThumbSize / 2.0;
    private const double LimitLineHitTolerance = 5.0;
    private const double TimeAxisLabelFontSize = 11.0;
    private const int VerticalGridDivisions = 4;
    private const int HorizontalGridDivisions = 8;
    private const double KeyboardStepFine = 1.0;
    private const double KeyboardStepCoarse = 6.0;
    private const double KeyboardSpacebarOffset = 0.04;
    private const double KeyboardStepOneMinute = 1.0 / (24.0 * 60.0);
    private const double KeyboardStepOneYUnitAbsolute = 1.0;
    private const double KeyboardStepOneYUnitOffset = 0.5;
    private const double NodeCreationSnapWindowDayFraction = 5.0 / (24.0 * 60.0);
    private const double MinimumPointSeparation = 0.001;
    private const double MinimumLimitSeparation = 0.5;
    private const double DefaultMeasureWidth = 720.0;
    private const double DefaultMeasureHeight = 320.0;
    private const double MinimumMeasureWidth = 260.0;
    private const double MinimumMeasureHeight = 160.0;

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private EnvironmentalCurve? _curveData;
    private List<EnvironmentalCurvePoint> _brightness = EnvironmentalCurve.CreateDefaultBrightness();
    private List<EnvironmentalCurvePoint> _nightLight = EnvironmentalCurve.CreateDefaultNightLight();
    private EnvironmentalCurveEditorPalette _palette = EnvironmentalCurveEditorPalette.Default;
    private bool _showBrightness = true;
    private bool _showNightLight;
    private bool _offsetMode;
    private bool _showCursorReadout;
    private bool _showSunOverlay = true;
    private bool _useDaylightSavings = true;
    private bool _previewMode;
    private bool _previewSweepRunning;
    private double _previewSweepCursor;
    private double _smoothness = 1.0;
    private double _latitude;
    private double _longitude;
    private DateTime? _sunOverlayDate;
    private double? _activeMinBrightness;
    private double? _activeMaxBrightness;
    private bool _disabledPeriodEnabled;
    private double _disabledPeriodStart = 0.25;
    private double _disabledPeriodEnd = 0.75;

    private EnvironmentalCurvePoint? _dragPoint;
    private Series _dragSeries;
    private bool _draggingLimit;
    private Series _limitDragSeries;
    private LimitKind _limitDragKind;
    private DisabledPin? _dragDisabledPin;
    private IPointer? _capturedPointer;

    private (Series Series, EnvironmentalCurvePoint Point)? _hoveredThumb;
    private (Series Series, LimitKind Kind)? _hoveredLimit;
    private DisabledPin? _hoveredDisabledPin;
    private EnvironmentalCurvePoint? _selectedPoint;
    private Series _selectedSeries;
    private Point? _cursorPos;
    private Rect? _exitPreviewButtonRect;
    private DispatcherTimer? _currentTimeTimer;
    private readonly List<LimitLabelHit> _limitLabelHits = [];
    private SunOverlayCacheKey? _sunOverlayCacheKey;
    private SunTimes? _sunOverlayCache;
    private bool _sunOverlayCacheFailed;
    private SolarPosition? _sunOverlayNoonPositionCache;
    private bool _sunOverlayNoonPositionCached;

    public EnvironmentalCurveEditor()
    {
        Focusable = true;
        Cursor = ArrowCursor;
        GotFocus += (_, _) => EnsureSelectionOnFocus();
        LostFocus += (_, _) => ClearSelectionOnLostFocus();
    }

    public event Action? CurveChanged;
    public event Action? ExitPreviewModeRequested;
    public event Action<double, double>? DisabledPeriodChanged;

    public EnvironmentalCurveEditorPalette Palette
    {
        get => _palette;
        set
        {
            if (_palette == value) return;
            _palette = value;
            InvalidateVisual();
        }
    }

    public void SetShowCursorReadout(bool show)
    {
        if (_showCursorReadout == show) return;
        _showCursorReadout = show;
        InvalidateVisual();
    }

    public void SetShowSunOverlay(bool show)
    {
        if (_showSunOverlay == show) return;
        _showSunOverlay = show;
        InvalidateVisual();
    }

    public void SetUseDaylightSavings(bool useDaylightSavings)
    {
        if (_useDaylightSavings == useDaylightSavings) return;
        _useDaylightSavings = useDaylightSavings;
        InvalidateVisual();
    }

    public void SetSunOverlayDate(DateTime? date)
    {
        DateTime? normalized = date?.Date;
        if (_sunOverlayDate == normalized) return;
        _sunOverlayDate = normalized;
        InvalidateVisual();
    }

    public void SetPreviewMode(bool preview)
    {
        if (_previewMode == preview) return;
        _previewMode = preview;
        if (preview)
        {
            ClearDragState(raiseChanged: false);
            _hoveredLimit = null;
            _hoveredThumb = null;
            _hoveredDisabledPin = null;
            Cursor = ArrowCursor;
        }

        InvalidateVisual();
    }

    public void SetPreviewSweepRunning(bool running)
    {
        if (_previewSweepRunning == running) return;
        _previewSweepRunning = running;
        if (!running) _previewSweepCursor = 0.0;
        InvalidateVisual();
    }

    public void SetPreviewSweepCursor(double t)
    {
        if (!_previewSweepRunning) return;
        _previewSweepCursor = Math.Clamp(t, 0.0, 1.0);
        InvalidateVisual();
    }

    public void SetGeoLocation(double latitude, double longitude)
    {
        if (_latitude == latitude && _longitude == longitude) return;
        _latitude = latitude;
        _longitude = longitude;
        InvalidateVisual();
    }

    public void SetDisabledPeriod(bool enabled, double start, double end)
    {
        double clampedStart = Math.Clamp(start, 0.0, 1.0);
        double clampedEnd = Math.Clamp(end, 0.0, 1.0);
        if (_disabledPeriodEnabled == enabled
            && _disabledPeriodStart == clampedStart
            && _disabledPeriodEnd == clampedEnd)
            return;

        if (_disabledPeriodEnabled && !enabled)
        {
            _dragDisabledPin = null;
            _hoveredDisabledPin = null;
            ReleasePointerCapture();
        }

        _disabledPeriodEnabled = enabled;
        _disabledPeriodStart = clampedStart;
        _disabledPeriodEnd = clampedEnd;
        InvalidateVisual();
    }

    public void SetCurves(EnvironmentalCurve curve)
    {
        _curveData = curve;
        _selectedPoint = null;
        ApplyCurveSelection();
        InvalidateVisual();
    }

    public void SetVisibility(bool showBrightness, bool showNightLight)
    {
        _showBrightness = showBrightness;
        _showNightLight = showNightLight;
        if (_selectedPoint != null)
        {
            bool visible = _selectedSeries == Series.Brightness ? showBrightness : showNightLight;
            if (!visible) _selectedPoint = null;
        }

        InvalidateVisual();
    }

    public void SetOffsetMode(bool offsetMode)
    {
        if (_offsetMode == offsetMode) return;
        _offsetMode = offsetMode;
        _selectedPoint = null;
        ApplyCurveSelection();
        InvalidateVisual();
    }

    public void SetActiveBrightnessRange(double? minBrightness, double? maxBrightness)
    {
        _activeMinBrightness = minBrightness;
        _activeMaxBrightness = maxBrightness;
        InvalidateVisual();
    }

    public void SetSmoothness(double smoothness)
    {
        _smoothness = Math.Clamp(smoothness, 0.0, 1.0);
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? DefaultMeasureWidth : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? DefaultMeasureHeight : availableSize.Height;
        return new Size(Math.Max(MinimumMeasureWidth, width), Math.Max(MinimumMeasureHeight, height));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartCurrentTimeTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopCurrentTimeTimer();
        ReleasePointerCapture();
        base.OnDetachedFromVisualTree(e);
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class FlyoutSliderLayout
{
    private static readonly Lazy<FlyoutSliderResources> Resources = new(static () => new FlyoutSliderResources());

    private static FlyoutSliderResources AXAMLResources => Resources.Value;

    public static double TrackHeight => AXAMLResources.AxamlFlyoutSlider.TrackHeight;
    public static CornerRadius CapsuleRadius => AXAMLResources.AxamlFlyoutSlider.CapsuleRadius;
    public static double DefaultMaximum => AXAMLResources.AxamlFlyoutSlider.DefaultMaximum;
    public static double SecondaryOpacity => AXAMLResources.AxamlFlyoutSlider.SecondaryOpacity;
    public static double WheelStep => AXAMLResources.AxamlFlyoutSlider.WheelStep;
    public static double KeyboardStep => AXAMLResources.AxamlFlyoutSlider.KeyboardStep;
    public static double LargeKeyboardStep => AXAMLResources.AxamlFlyoutSlider.LargeKeyboardStep;
    public static double ThumbOpacity => AXAMLResources.AxamlFlyoutSlider.ThumbOpacity;
    public static double PreviewOpacity => AXAMLResources.AxamlFlyoutSlider.PreviewOpacity;
    public static double IndicatorOpacity => AXAMLResources.AxamlFlyoutSlider.IndicatorOpacity;
    public static double IndicatorWidth => AXAMLResources.AxamlFlyoutSlider.IndicatorWidth;
    public static double IndicatorFontSize => AXAMLResources.AxamlFlyoutSlider.IndicatorFontSize;
    public static double DefaultThumbWidth => AXAMLResources.AxamlFlyoutSlider.DefaultThumbWidth;
    public static double DefaultThumbHeight => AXAMLResources.AxamlFlyoutSlider.DefaultThumbHeight;
    public static double DefaultMeasureWidth => AXAMLResources.AxamlFlyoutSlider.DefaultMeasureWidth;
}

public readonly record struct FlyoutSliderPeakValues(float Min, float Max)
{
    public static readonly FlyoutSliderPeakValues Zero = new(0f, 0f);
}

public sealed class FlyoutSlider : Control, IDisposable
{
    private bool _dragging;
    private bool _disposed;
    private IPointer? _capturedPointer;
    private double _minimum;
    private double _maximum = FlyoutSliderLayout.DefaultMaximum;
    private double _value;

    public FlyoutSlider()
    {
        Focusable = true;
        Cursor = TrayAppDotNETCursors.Hand;
    }

    public event EventHandler<double>? ValueChanged;
    public event EventHandler? UserAdjustmentStarted;
    public event EventHandler? UserAdjustmentCompleted;
    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;

    public double Minimum
    {
        get => _minimum;
        set
        {
            if (Math.Abs(_minimum - value) < 0.001) return;
            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;
            Value = _value;
            InvalidateVisual();
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            double next = Math.Max(_minimum, value);
            if (Math.Abs(_maximum - next) < 0.001) return;
            _maximum = next;
            Value = _value;
            InvalidateVisual();
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            double next = ClampValue(value);
            if (Math.Abs(_value - next) < 0.001) return;
            _value = next;
            InvalidateVisual();
        }
    }

    public FlyoutSliderPeakValues PeakValues
    {
        get;
        set
        {
            if (field.Equals(value)) return;
            field = value;
            InvalidateVisual();
        }
    }

    public double? ProgressValueOverride
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public Color? ProgressOverrideColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public double? SecondaryValue
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public SliderThumbGlyphOption? SecondaryThumb
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Color? SecondaryProgressColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public double SecondaryOpacity
    {
        get;
        set => SetUnitInterval(ref field, value);
    } = FlyoutSliderLayout.SecondaryOpacity;

    public double? PreviewValue
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public double? IndicatorValue
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    public Color TrackColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public Color ProgressColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public Color ThumbColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public Color MeterPeakColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public Color MeterPeakStereoColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public Color IndicatorColor
    {
        get;
        set => SetColor(ref field, value);
    }

    public double WheelStep
    {
        get;
        set => field = Math.Max(0, value);
    } = FlyoutSliderLayout.WheelStep;

    public double WheelStepPercent
    {
        get => WheelStep;
        set => WheelStep = value;
    }

    public double? CoarseWheelStep
    {
        get;
        set => field = value.HasValue ? Math.Max(0, value.Value) : null;
    }

    public double KeyboardStep
    {
        get;
        set => field = Math.Max(0, value);
    } = FlyoutSliderLayout.KeyboardStep;

    public double LargeKeyboardStep
    {
        get;
        set => field = Math.Max(0, value);
    } = FlyoutSliderLayout.LargeKeyboardStep;

    public double HitTestVerticalPadding
    {
        get;
        set
        {
            double next = Math.Max(0, value);
            if (Math.Abs(field - next) < 0.001) return;
            field = next;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double ThumbOpacity
    {
        get;
        set => SetUnitInterval(ref field, value);
    } = FlyoutSliderLayout.ThumbOpacity;

    public double PreviewOpacity
    {
        get;
        set => SetUnitInterval(ref field, value);
    } = FlyoutSliderLayout.PreviewOpacity;

    public double IndicatorOpacity
    {
        get;
        set => SetUnitInterval(ref field, value);
    } = FlyoutSliderLayout.IndicatorOpacity;

    public double IndicatorWidth
    {
        get;
        set
        {
            double next = Math.Max(1, value);
            if (Math.Abs(field - next) < 0.001) return;
            field = next;
            InvalidateVisual();
        }
    } = FlyoutSliderLayout.IndicatorWidth;

    public double IndicatorFontSize
    {
        get;
        set
        {
            double next = Math.Max(1, value);
            if (Math.Abs(field - next) < 0.001) return;
            field = next;
            InvalidateVisual();
        }
    } = FlyoutSliderLayout.IndicatorFontSize;

    public string IndicatorGlyph
    {
        get;
        set
        {
            string next = value ?? string.Empty;
            if (field == next) return;
            field = next;
            InvalidateVisual();
        }
    } = GlyphCatalog.SLIDER_THUMB_DIAMOND.Text;

    public string IndicatorFontFamily
    {
        get;
        set
        {
            string next = value ?? string.Empty;
            if (field == next) return;
            field = next;
            InvalidateVisual();
        }
    } = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public SliderThumbGlyphOption Thumb
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = new()
    {
        Name = "Capsule",
        Shape = SliderThumbShape.Capsule,
        Width = FlyoutSliderLayout.DefaultThumbWidth,
        Height = FlyoutSliderLayout.DefaultThumbHeight
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width)
            ? FlyoutSliderLayout.DefaultMeasureWidth
            : availableSize.Width;
        double hitHeight = FlyoutSliderLayout.TrackHeight + HitTestVerticalPadding * 2.0;
        double secondaryThumbHeight = SecondaryThumb?.Height ?? 0;
        double height = Math.Max(hitHeight, Math.Max(1, Math.Max(Thumb.Height, secondaryThumbHeight)));
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Math.Max(0, Bounds.Width);
        double height = Math.Max(1, Bounds.Height);
        if (width <= 0) return;

        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        double thumbWidth = Math.Max(1, Thumb.Width);
        double thumbHeight = Math.Max(1, Thumb.Height);
        double trackY = Math.Round((height - FlyoutSliderLayout.TrackHeight) / 2.0) + 0.5;
        double progressWidth = ProgressValueOverride.HasValue
            ? ValuePosition(width, ProgressValueOverride.Value)
            : ValuePosition(width, Value);
        Color progressColor = ProgressValueOverride.HasValue
            ? ProgressOverrideColor ?? ProgressColor
            : ProgressColor;

        Rect track = new(0, trackY, width, FlyoutSliderLayout.TrackHeight);
        DrawRoundedRect(context, track, TrackColor);
        if (SecondaryValue.HasValue && SecondaryOpacity > 0)
        {
            Color secondaryProgressColor = SecondaryProgressColor ?? ProgressColor;
            double secondaryProgressWidth = ValuePosition(width, SecondaryValue.Value);
            DrawProgress(
                context,
                trackY,
                width,
                secondaryProgressWidth,
                secondaryProgressColor,
                SecondaryOpacity);
        }

        if (progressWidth > 0)
            DrawProgress(context, trackY, width, progressWidth, progressColor, 1);

        double peakExtent = ValuePosition(width, Value);
        double peakRadius = FlyoutSliderLayout.TrackHeight / 2.0;
        if (PeakValues.Max > 0)
        {
            double stereoWidth = CalculatePeakWidth(peakExtent, PeakValues.Max, peakRadius);
            DrawRoundedRect(
                context,
                new Rect(0, trackY, stereoWidth, FlyoutSliderLayout.TrackHeight),
                MeterPeakStereoColor);
        }

        if (PeakValues.Min > 0)
        {
            double baseWidth = CalculatePeakWidth(peakExtent, PeakValues.Min, peakRadius);
            DrawRoundedRect(
                context,
                new Rect(0, trackY, baseWidth, FlyoutSliderLayout.TrackHeight),
                MeterPeakColor);
        }

        Rect thumb = ThumbRect(width, height, thumbWidth, thumbHeight, Value);

        if (PreviewValue.HasValue && IsVisibleTranslucent(PreviewOpacity))
        {
            DrawThumbFillCutoutAtValue(
                context,
                track,
                width,
                height,
                thumbWidth,
                thumbHeight,
                PreviewValue.Value,
                Thumb);
        }

        if (SecondaryValue.HasValue && SecondaryOpacity > 0)
        {
            SliderThumbGlyphOption secondaryThumb = SecondaryThumb ?? Thumb;
            double secondaryThumbWidth = Math.Max(1, secondaryThumb.Width);
            double secondaryThumbHeight = Math.Max(1, secondaryThumb.Height);
            if (IsVisibleTranslucent(SecondaryOpacity))
            {
                DrawThumbFillCutoutAtValue(
                    context,
                    track,
                    width,
                    height,
                    secondaryThumbWidth,
                    secondaryThumbHeight,
                    SecondaryValue.Value,
                    secondaryThumb);
            }
        }

        if (IsVisibleTranslucent(ThumbOpacity))
            DrawThumbFillCutout(context, track, thumb, Thumb);

        if (PreviewValue.HasValue && PreviewOpacity > 0)
            DrawThumbAtValue(context, width, height, thumbWidth, thumbHeight, PreviewValue.Value, PreviewOpacity, Thumb);

        if (SecondaryValue.HasValue && SecondaryOpacity > 0)
        {
            SliderThumbGlyphOption secondaryThumb = SecondaryThumb ?? Thumb;
            double secondaryThumbWidth = Math.Max(1, secondaryThumb.Width);
            double secondaryThumbHeight = Math.Max(1, secondaryThumb.Height);
            DrawThumbAtValue(
                context,
                width,
                height,
                secondaryThumbWidth,
                secondaryThumbHeight,
                SecondaryValue.Value,
                SecondaryOpacity,
                secondaryThumb);
        }

        DrawThumb(context, thumb, ThumbOpacity, Thumb);

        if (IndicatorValue.HasValue && IndicatorOpacity > 0 && !string.IsNullOrEmpty(IndicatorGlyph))
            DrawIndicator(context, width, height, IndicatorValue.Value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        Focus();
        BeginUserAdjustment();
        _dragging = true;
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        DragStarted?.Invoke(this, EventArgs.Empty);
        SetValueFromPoint(e.GetPosition(this).X, notify: true);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        SetValueFromPoint(e.GetPosition(this).X, notify: true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        SetValueFromPoint(e.GetPosition(this).X, notify: true);
        EndDrag(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_dragging) return;
        EndDrag(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled) return;

        double step = (e.KeyModifiers & KeyModifiers.Control) != 0
            ? CoarseWheelStep ?? WheelStep
            : WheelStep;
        if (step <= 0) return;

        BeginUserAdjustment();
        ApplyValueDelta(e.Delta.Y > 0 ? step : -step);
        EndUserAdjustment();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;

        switch (e.Key)
        {
            case Key.Up:
            case Key.Right:
                BeginUserAdjustment();
                ApplyValueDelta(KeyboardStep);
                EndUserAdjustment();
                e.Handled = true;
                break;
            case Key.Down:
            case Key.Left:
                BeginUserAdjustment();
                ApplyValueDelta(-KeyboardStep);
                EndUserAdjustment();
                e.Handled = true;
                break;
            case Key.PageUp:
                BeginUserAdjustment();
                ApplyValueDelta(LargeKeyboardStep);
                EndUserAdjustment();
                e.Handled = true;
                break;
            case Key.PageDown:
                BeginUserAdjustment();
                ApplyValueDelta(-LargeKeyboardStep);
                EndUserAdjustment();
                e.Handled = true;
                break;
            case Key.Home:
                BeginUserAdjustment();
                SetValueAndNotify(Minimum);
                EndUserAdjustment();
                e.Handled = true;
                break;
            case Key.End:
                BeginUserAdjustment();
                SetValueAndNotify(Maximum);
                EndUserAdjustment();
                e.Handled = true;
                break;
        }
    }

    private void SetValueFromPoint(double x, bool notify)
    {
        double width = Math.Max(1, Bounds.Width);
        double thumbWidth = Math.Max(1, Thumb.Width);
        double trackStart = thumbWidth / 2.0;
        double trackEnd = width - thumbWidth / 2.0;
        double trackLength = Math.Max(1, trackEnd - trackStart);
        SetValueAndNotify(Minimum + Math.Clamp((x - trackStart) / trackLength, 0, 1) * Range, notify);
    }

    private void ApplyValueDelta(double delta) => SetValueAndNotify(Value + delta);

    private void SetValueAndNotify(double value, bool notify = true)
    {
        double previous = Value;
        Value = value;
        if (notify && Math.Abs(previous - Value) >= 0.001)
            ValueChanged?.Invoke(this, Value);
    }

    private void EndDrag(IPointer? pointer)
    {
        _dragging = false;
        IPointer? capturedPointer = pointer ?? _capturedPointer;
        _capturedPointer = null;
        capturedPointer?.Capture(null);
        DragCompleted?.Invoke(this, EventArgs.Empty);
        EndUserAdjustment();
    }

    /// <summary>Releases pointer capture and subscribers owned by a retired UI generation.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IPointer? capturedPointer = _capturedPointer;
        _capturedPointer = null;
        _dragging = false;
        if (capturedPointer != null)
        {
            try
            {
                capturedPointer.Capture(null);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"FlyoutSlider pointer release failed: {exception.Message}");
            }
        }

        ValueChanged = null;
        UserAdjustmentStarted = null;
        UserAdjustmentCompleted = null;
        DragStarted = null;
        DragCompleted = null;
        Cursor = null;
    }

    private void BeginUserAdjustment() => UserAdjustmentStarted?.Invoke(this, EventArgs.Empty);

    private void EndUserAdjustment() => UserAdjustmentCompleted?.Invoke(this, EventArgs.Empty);

    private double Range => Math.Max(0.001, Maximum - Minimum);

    private double ClampValue(double value) => Math.Clamp(value, Minimum, Maximum);

    private double Normalize(double value) => Math.Clamp((ClampValue(value) - Minimum) / Range, 0, 1);

    private double ThumbLeft(double width, double thumbWidth, double value)
    {
        double available = Math.Max(0, width - thumbWidth);
        return available * Normalize(value);
    }

    private double ValuePosition(double width, double value) => Math.Max(0, width) * Normalize(value);

    private Rect ThumbRect(double width, double height, double thumbWidth, double thumbHeight, double value) =>
        new(
            ThumbLeft(width, thumbWidth, value),
            Math.Round((height - thumbHeight) / 2.0),
            thumbWidth,
            thumbHeight);

    /// <summary>Compensates for the rounded cap without drawing beyond the volume extent.</summary>
    internal static double CalculatePeakWidth(double peakExtent, float peak, double radius)
    {
        double clampedPeak = Math.Clamp(peak, 0f, 1f);
        return Math.Min(peakExtent, (peakExtent + radius) * clampedPeak);
    }

    private void DrawThumbAtValue(
        DrawingContext context,
        double width,
        double height,
        double thumbWidth,
        double thumbHeight,
        double value,
        double opacity,
        SliderThumbGlyphOption thumb) =>
        DrawThumb(context, ThumbRect(width, height, thumbWidth, thumbHeight, value), opacity, thumb);

    /// <summary>
    /// Draws a track-colored thumb silhouette over filled regions before translucent thumbs render.
    /// </summary>
    private void DrawThumbFillCutoutAtValue(
        DrawingContext context,
        Rect clip,
        double width,
        double height,
        double thumbWidth,
        double thumbHeight,
        double value,
        SliderThumbGlyphOption thumb) =>
        DrawThumbFillCutout(context, clip, ThumbRect(width, height, thumbWidth, thumbHeight, value), thumb);

    /// <summary>
    /// Draws a track-colored thumb silhouette over filled regions before translucent thumbs render.
    /// </summary>
    private void DrawThumbFillCutout(
        DrawingContext context,
        Rect clip,
        Rect thumbBounds,
        SliderThumbGlyphOption thumb)
    {
        using (context.PushClip(clip))
            DrawThumbShape(context, thumbBounds, thumb, TrackColor);
    }

    /// <summary>
    /// Draws a thumb with the requested opacity.
    /// </summary>
    private void DrawThumb(DrawingContext context, Rect thumbBounds, double opacity, SliderThumbGlyphOption thumb) =>
        DrawThumbShape(context, thumbBounds, thumb, WithOpacity(ThumbColor, opacity));

    /// <summary>
    /// Draws the slider thumb geometry with an explicit color.
    /// </summary>
    private static void DrawThumbShape(
        DrawingContext context,
        Rect thumbBounds,
        SliderThumbGlyphOption thumb,
        Color color)
    {
        if (thumb.IsCapsule)
        {
            context.DrawRectangle(
                new SolidColorBrush(color),
                null,
                new RoundedRect(thumbBounds, FlyoutSliderLayout.CapsuleRadius));
            return;
        }

        FormattedText text = new(
            thumb.Glyph,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(thumb.FontFamily),
            thumb.FontSize,
            new SolidColorBrush(color));

        double x = thumbBounds.Center.X - text.Width / 2.0;
        double y = thumbBounds.Center.Y - text.Height / 2.0;

        if (Math.Abs(thumb.XScale - 1.0) < 0.001)
        {
            context.DrawText(text, new Point(x, y));
            return;
        }

        using (context.PushTransform(Matrix.CreateTranslation(-thumbBounds.Center.X, -thumbBounds.Center.Y)))
        using (context.PushTransform(Matrix.CreateScale(thumb.XScale, 1.0)))
        using (context.PushTransform(Matrix.CreateTranslation(thumbBounds.Center.X, thumbBounds.Center.Y)))
            context.DrawText(text, new Point(x, y));
    }

    /// <summary>
    /// Returns true when a visible thumb needs the fill cut out behind it.
    /// </summary>
    private static bool IsVisibleTranslucent(double opacity) =>
        opacity is > 0 and < 0.999;

    private void DrawIndicator(DrawingContext context, double width, double height, double value)
    {
        double indicatorSize = Math.Max(1, IndicatorWidth);
        SliderThumbGlyphOption indicatorThumb = new()
        {
            Name = "CurveIndicator",
            Shape = SliderThumbShape.Capsule,
            Width = indicatorSize,
            Height = indicatorSize
        };
        Rect indicatorBounds = ThumbRect(width, height, indicatorSize, indicatorSize, value);
        DrawThumbShape(
            context,
            indicatorBounds,
            indicatorThumb,
            WithOpacity(IndicatorColor, IndicatorOpacity));
    }

    private static void DrawRoundedRect(DrawingContext context, Rect rect, Color color, double radius = double.NaN) =>
        context.DrawRectangle(
            new SolidColorBrush(color),
            null,
            new RoundedRect(rect, double.IsNaN(radius) ? FlyoutSliderLayout.TrackHeight / 2.0 : radius));

    /// <summary>
    /// Draws a full-height slider progress segment.
    /// </summary>
    private static void DrawProgress(
        DrawingContext context,
        double trackY,
        double width,
        double progressWidth,
        Color color,
        double opacity)
    {
        if (progressWidth <= 0 || opacity <= 0) return;

        DrawRoundedRect(
            context,
            new Rect(0, trackY, Math.Min(width, progressWidth), FlyoutSliderLayout.TrackHeight),
            WithOpacity(color, opacity),
            FlyoutSliderLayout.TrackHeight / 2.0);
    }

    private void SetColor(ref Color field, Color value)
    {
        if (field == value) return;
        field = value;
        InvalidateVisual();
    }

    private void SetUnitInterval(ref double field, double value)
    {
        double next = Math.Clamp(value, 0, 1);
        if (Math.Abs(field - next) < 0.001) return;
        field = next;
        InvalidateVisual();
    }

    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);
}

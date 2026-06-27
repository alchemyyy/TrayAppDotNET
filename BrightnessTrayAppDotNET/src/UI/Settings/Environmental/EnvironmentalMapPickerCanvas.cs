using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Controls.Maps;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

internal sealed class EnvironmentalMapPickerCanvas : Control
{
    private const string MapResourceUri = "avares://BrightnessTrayAppDotNET/Visuals/map_fla-shop.com_ccby4.0.svg";

    private const double MapWidth = 2000.0;
    private const double MapHeight = 1280.0;
    private const double MinScale = 0.2;
    private const double MaxScale = 12.0;
    private const double InitialScaleMultiplier = 2.0;
    private const double MinimumInitialScale = 0.4;
    private const double EdgeGrabFraction = 0.10;
    private const double EdgeAutoPanPeakSpeed = 525.0;
    private const double PinFontSize = 28.0;
    private const double PinAnchorXFraction = 0.5;
    private const double PinAnchorYFraction = 1.0;
    private const double PinHitPadding = 3.0;
    private const double MapStrokeThickness = 0.6;
    private const double WheelZoomStep = 1.15;

    internal const double HudPanStep = 80.0;
    internal const double HudZoomStep = 1.25;

    private static readonly FontFamily PinFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Geometry? MapGeometry = LoadMapGeometry();
    private static readonly MercatorMapProjection Projection = MercatorMapProjection.FromMapSize(MapWidth, MapHeight);

    private readonly SettingsPalette _palette;
    private readonly Color _pinColor;
    private GeoCoordinate _selectedCoordinate = GeoCoordinate.Zero;
    private MapViewportTransform _viewport = MapViewportTransform.Identity;
    private bool _needsCenterOnPin = true;
    private bool _isDraggingPin;
    private bool _isPanning;
    private Vector _pinDragOffset;
    private Point _lastPanPoint;
    private Point _lastDragViewport;
    private Rect _pinHitRect;
    private Vector _autoPanVelocity;
    private bool _autoPanFrameQueued;
    private TimeSpan _lastAutoPanFrameTime;

    public EnvironmentalMapPickerCanvas(SettingsPalette palette, Color pinColor)
    {
        _palette = palette;
        _pinColor = pinColor;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        PointerCaptureLost += (_, _) =>
        {
            _isDraggingPin = false;
            _isPanning = false;
            StopAutoPan();
        };
    }

    public event EventHandler? CoordinateChanged;

    public GeoCoordinate SelectedCoordinate
    {
        get => _selectedCoordinate;
        set => SetSelectedCoordinate(value, centerOnPin: true);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        EnsureInitialViewport(bounds.Size);
        context.FillRectangle(TrayAppDotNETSettingsUI.Brush(_palette.Background), bounds);

        using (context.PushClip(bounds))
        {
            DrawMap(context);
            DrawPin(context);
        }
    }

    public void PanViewport(double dx, double dy)
    {
        _viewport = _viewport.Pan(new Vector(dx, dy));
        InvalidateVisual();
    }

    public void ZoomAtViewportCenter(double factor) =>
        ZoomAt(new Point(Bounds.Width / 2.0, Bounds.Height / 2.0), factor);

    public void SetPinToViewportCenter() =>
        SetCoordinateFromMapPoint(_viewport.ViewportToMap(new Point(Bounds.Width / 2.0, Bounds.Height / 2.0)));

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopAutoPan();
        base.OnDetachedFromVisualTree(e);
    }

    private void EnsureInitialViewport(Size viewportSize)
    {
        if (!_needsCenterOnPin) return;
        if (viewportSize.Width <= 0.0 || viewportSize.Height <= 0.0) return;

        double scale = Math.Max(
            MinimumInitialScale,
            Math.Min(viewportSize.Width / MapWidth, viewportSize.Height / MapHeight)) * InitialScaleMultiplier;
        Point pin = Projection.Project(_selectedCoordinate);
        _viewport = new MapViewportTransform(
            scale,
            new Vector(
                viewportSize.Width / 2.0 - pin.X * scale,
                viewportSize.Height / 2.0 - pin.Y * scale));
        _needsCenterOnPin = false;
    }

    private void DrawMap(DrawingContext context)
    {
        if (MapGeometry == null) return;

        Matrix matrix = Matrix.CreateScale(_viewport.Scale, _viewport.Scale)
                        * Matrix.CreateTranslation(_viewport.Offset.X, _viewport.Offset.Y);
        using (context.PushTransform(matrix))
        {
            IBrush fill = TrayAppDotNETSettingsUI.Brush(_palette.Hover);
            Pen stroke = new(TrayAppDotNETSettingsUI.Brush(_palette.Foreground), MapStrokeThickness);
            context.DrawGeometry(fill, stroke, MapGeometry);
        }
    }

    private void DrawPin(DrawingContext context)
    {
        Point center = _viewport.MapToViewport(Projection.Project(_selectedCoordinate));
        FormattedText text = new(
            "\uECAF",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(PinFont),
            PinFontSize,
            TrayAppDotNETSettingsUI.Brush(_pinColor));

        Point origin = new(
            center.X - text.Width * PinAnchorXFraction,
            center.Y - text.Height * PinAnchorYFraction);
        _pinHitRect = new Rect(origin, new Size(text.Width, text.Height)).Inflate(PinHitPadding);
        context.DrawText(text, origin);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        Point position = e.GetPosition(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = position;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        if (_pinHitRect.Contains(position))
        {
            _isDraggingPin = true;
            _lastDragViewport = position;
            Point pin = Projection.Project(_selectedCoordinate);
            Point cursorMap = _viewport.ViewportToMap(position);
            _pinDragOffset = cursorMap - pin;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        SetCoordinateFromMapPoint(_viewport.ViewportToMap(position));
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point position = e.GetPosition(this);
        if (_isPanning)
        {
            _viewport = _viewport.Pan(position - _lastPanPoint);
            _lastPanPoint = position;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_isDraggingPin) return;
        _lastDragViewport = position;
        SetCoordinateFromMapPoint(_viewport.ViewportToMap(position) - _pinDragOffset);
        UpdateAutoPanFromEdges(position);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (!_isDraggingPin) return;
        _isDraggingPin = false;
        StopAutoPan();
        e.Pointer.Capture(null);
        SetCoordinateFromMapPoint(_viewport.ViewportToMap(e.GetPosition(this)) - _pinDragOffset);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep;
        ZoomAt(e.GetPosition(this), factor);
        e.Handled = true;
    }

    private void ZoomAt(Point viewport, double factor)
    {
        _viewport = _viewport.ZoomAt(viewport, factor, MinScale, MaxScale);
        InvalidateVisual();
    }

    private void SetCoordinateFromMapPoint(Point mapPoint)
    {
        SetSelectedCoordinate(Projection.Unproject(new Point(
            Math.Clamp(mapPoint.X, 0.0, MapWidth),
            Math.Clamp(mapPoint.Y, 0.0, MapHeight))).ClampToWorld(), centerOnPin: false);
    }

    private void SetSelectedCoordinate(GeoCoordinate coordinate, bool centerOnPin)
    {
        GeoCoordinate clamped = coordinate.ClampToWorld();
        if (_selectedCoordinate == clamped) return;
        _selectedCoordinate = clamped;
        if (centerOnPin) _needsCenterOnPin = true;
        CoordinateChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void UpdateAutoPanFromEdges(Point viewport)
    {
        _autoPanVelocity = MapViewportTransform.EdgeAutoPanVelocity(
            viewport,
            Bounds.Size,
            EdgeGrabFraction,
            EdgeAutoPanPeakSpeed);

        if (_autoPanVelocity.X != 0.0 || _autoPanVelocity.Y != 0.0)
            QueueAutoPanFrame();
        else
            StopAutoPan();
    }

    private void QueueAutoPanFrame()
    {
        if (_autoPanFrameQueued) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        _autoPanFrameQueued = true;
        topLevel.RequestAnimationFrame(OnAutoPanFrame);
    }

    private void OnAutoPanFrame(TimeSpan timestamp)
    {
        _autoPanFrameQueued = false;
        if (!_isDraggingPin || _autoPanVelocity is { X: 0.0, Y: 0.0 })
        {
            _lastAutoPanFrameTime = TimeSpan.Zero;
            return;
        }

        if (_lastAutoPanFrameTime == TimeSpan.Zero)
        {
            _lastAutoPanFrameTime = timestamp;
            QueueAutoPanFrame();
            return;
        }

        double seconds = Math.Max(0.0, (timestamp - _lastAutoPanFrameTime).TotalSeconds);
        _lastAutoPanFrameTime = timestamp;
        _viewport = _viewport.Pan(new Vector(_autoPanVelocity.X * seconds, _autoPanVelocity.Y * seconds));
        SetCoordinateFromMapPoint(_viewport.ViewportToMap(_lastDragViewport) - _pinDragOffset);
        InvalidateVisual();
        QueueAutoPanFrame();
    }

    private void StopAutoPan()
    {
        _autoPanVelocity = default;
        _lastAutoPanFrameTime = TimeSpan.Zero;
    }

    private static Geometry? LoadMapGeometry()
    {
        try
        {
            Uri uri = new(MapResourceUri);
            if (!AssetLoader.Exists(uri)) return null;

            using Stream stream = AssetLoader.Open(uri);
            using XmlReader reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true,
                });

            while (reader.Read())
            {
                if (reader is not { NodeType: XmlNodeType.Element, LocalName: "path" }) continue;
                string? data = reader.GetAttribute("d");
                if (!string.IsNullOrWhiteSpace(data))
                    return Geometry.Parse(data);
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"EnvironmentalMapPickerCanvas.LoadMapGeometry: {ex.Message}");
        }

        return null;
    }
}

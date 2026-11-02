using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Paints a Task Manager-style utilization history.</summary>
internal sealed class PerformanceHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly PerformanceGraphHoverPresenter _hover;
    private readonly double _hoverTextInset;
    private readonly int _hoverMaximumTextLines;
    private readonly double _lineThickness;
    private readonly double _secondaryLineThickness;
    private readonly double _secondaryLineOpacity;
    private readonly double _underfillOpacity;
    private readonly int _underfillDarkenAmount;
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private readonly Func<long, string?>? _hoverMetricProvider;
    private PerformanceHistory _history;
    private PerformanceHistory? _secondaryHistory;
    private Color _accent;
    private Pen _linePen;
    private Pen _secondaryLinePen;
    private IBrush _underfillBrush;
    private Point _hoverPointerPosition;
    private Size _hoverTrailingExtent;
    private double[] _hoverSampleFractions = [];
    private PerformanceHistory? _hoverTimelineHistory;
    private int _hoverTimelineCount;
    private long _hoverTimelineTimestamp;
    private bool _showUnderfill = true;

    public PerformanceHistoryGraph(
        PerformanceHistory history,
        Color accent,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme,
        Func<long, string?>? hoverMetricProvider = null)
    {
        _history = history;
        _accent = accent;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(resources.GraphBackgroundColor(isLightTheme));
        _borderPen = new Pen(
            TrayAppDotNETSettingsUI.Brush(palette.Border),
            resources.AxamlTaskManagerPerformance.GraphBorderThickness);
        byte gridAlpha = (byte)Math.Round(
            byte.MaxValue * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphGridLineOpacity,
                min: 0,
                max: 1));
        Color gridColor = Color.FromArgb(
            gridAlpha,
            palette.Border.R,
            palette.Border.G,
            palette.Border.B);
        _gridPen = new Pen(
            new SolidColorBrush(gridColor),
            resources.AxamlTaskManagerPerformance.GraphGridLineThickness);
        Color hoverLineColor = resources.GraphHoverLineColor(isLightTheme);
        byte hoverLineAlpha = (byte)Math.Round(
            hoverLineColor.A * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphHoverLineOpacity,
                min: 0,
                max: 1));
        ImmutableSolidColorBrush hoverLineBrush = new(Color.FromArgb(
            hoverLineAlpha,
            hoverLineColor.R,
            hoverLineColor.G,
            hoverLineColor.B));
        double hoverLineThickness =
            resources.AxamlTaskManagerPerformance.GraphHoverLineThickness;
        _hoverTextInset = resources.AxamlTaskManagerPerformance.GraphHoverTextInset;
        _hoverMaximumTextLines = Math.Max(
            val1: 1,
            resources.AxamlTaskManagerPerformance.GraphHoverMaximumTextLines);
        _hover = new PerformanceGraphHoverPresenter(
            this,
            new PerformanceGraphHoverStyle(
                new PerformanceGraphHoverMetrics(
                    PerformanceGraphHoverLineMode.SnapToSample,
                    _hoverTextInset,
                    resources.AxamlTaskManagerPerformance.GraphHoverTextCursorGap,
                    resources.AxamlTaskManagerPerformance.GraphHoverLineClipPadding,
                    hoverLineThickness * resources.AxamlTaskManagerPerformance.GraphHoverLineDashGapLength),
                // AXAML hot-reload exception: DashStyle is not a linker-supported AXAML primitive, so
                // the immutable Pen retains Avalonia's built-in Dash preset
                new ImmutablePen(hoverLineBrush, hoverLineThickness, DashStyle.Dash.ToImmutable()),
                new ImmutablePen(hoverLineBrush, hoverLineThickness),
                palette.Foreground,
                new Typeface(TrayAppDotNETSettingsUI.UIFont),
                resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize));
        _lineThickness = resources.AxamlTaskManagerPerformance.GraphLineThickness;
        _secondaryLineThickness =
            resources.AxamlTaskManagerPerformance.GraphSecondaryLineThickness;
        _secondaryLineOpacity = Math.Clamp(
            resources.AxamlTaskManagerPerformance.GraphSecondaryLineOpacity,
            min: 0,
            max: 1);
        _underfillOpacity = resources.AxamlTaskManagerPerformance.GraphUnderfillOpacity;
        _underfillDarkenAmount =
            resources.AxamlTaskManagerPerformance.GraphUnderfillDarkenAmount;
        _linePen = new Pen(new SolidColorBrush(accent), _lineThickness);
        _secondaryLinePen = CreateSecondaryLinePen(accent);
        _underfillBrush = CreateUnderfillBrush(accent);
        _gridColumns = resources.AxamlTaskManagerPerformance.GraphGridColumns;
        _gridRows = resources.AxamlTaskManagerPerformance.GraphGridRows;
        _hoverMetricProvider = hoverMetricProvider;
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    /// <summary>Changes the displayed history and schedules a repaint.</summary>
    public void SetHistory(PerformanceHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        InvalidateVisual();
        UpdateHover();
    }

    /// <summary>Changes the optional history drawn beneath the primary utilization trace.</summary>
    public void SetSecondaryHistory(PerformanceHistory? history)
    {
        if (ReferenceEquals(_secondaryHistory, history)) return;

        _secondaryHistory = history;
        InvalidateVisual();
    }

    /// <summary>Shows or hides the translucent area beneath the primary trace.</summary>
    public void SetUnderfillVisible(bool isVisible)
    {
        if (_showUnderfill == isVisible) return;

        _showUnderfill = isVisible;
        InvalidateVisual();
    }

    /// <summary>Changes the device accent used for the utilization trace.</summary>
    public void SetAccent(Color accent)
    {
        if (_accent == accent) return;
        _accent = accent;
        _linePen = new Pen(new SolidColorBrush(accent), _lineThickness);
        _secondaryLinePen = CreateSecondaryLinePen(accent);
        _underfillBrush = CreateUnderfillBrush(accent);
        InvalidateVisual();
    }

    /// <summary>Schedules a repaint after the current history receives a sample.</summary>
    public void Refresh()
    {
        InvalidateVisual();
        UpdateHover();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        _hover.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _hover.Detach();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        _hover.SetSize(eventArgs.NewSize);
        UpdateHover();
    }

    protected override void OnPointerEntered(PointerEventArgs eventArgs)
    {
        base.OnPointerEntered(eventArgs);
        TrackPointer(eventArgs.GetLatestPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        TrackPointer(eventArgs.GetLatestPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        ClearPointer();
    }

    /// <summary>Extends the hover area over the hit-test spacing an enclosing hit target adds after the graph.</summary>
    internal void SetHoverTrailingExtent(Size trailingExtent)
    {
        _hoverTrailingExtent = trailingExtent;
        UpdateHover();
    }

    /// <summary>Tracks a pointer supplied by this graph or an enclosing hit target.</summary>
    internal void TrackPointer(Point pointerPosition)
    {
        _hoverPointerPosition = pointerPosition;
        _hover.SetPointerOver(true);
        UpdateHover();
    }

    /// <summary>Clears hover state when the pointer leaves this graph's full hit target.</summary>
    internal void ClearPointer() => _hover.SetPointerOver(false);

    /// <summary>Sends the render-thread hover the current timeline and the label for the latest UI pointer sample.</summary>
    private void UpdateHover()
    {
        if (!_hover.IsActive) return;

        // Snapshot the timeline only after the history changes, not on every pointer move
        if (!ReferenceEquals(_hoverTimelineHistory, _history)
            || _hoverTimelineCount != _history.Count
            || _hoverTimelineTimestamp != _history.CurrentTimestamp)
        {
            _hoverSampleFractions = PerformanceHistoryGraphLayout.CreateSampleFractions(_history);
            _hoverTimelineHistory = _history;
            _hoverTimelineCount = _history.Count;
            _hoverTimelineTimestamp = _history.CurrentTimestamp;
        }

        Size graphSize = Bounds.Size;
        double maximumTextWidth = Math.Max(val1: 0, graphSize.Width - _hoverTextInset * 2);
        bool hasSample = PerformanceHistoryGraphLayout.TryGetHoverSample(
            _hoverSampleFractions,
            _hoverPointerPosition.X,
            graphSize.Width,
            out int sampleIndex,
            out _);
        _hover.Update(
            graphSize,
            new Rect(
                x: 0,
                y: 0,
                graphSize.Width + _hoverTrailingExtent.Width,
                graphSize.Height + _hoverTrailingExtent.Height),
            _hoverSampleFractions,
            hasSample,
            hasSample && maximumTextWidth > 0 ? FormatHoverMetric(sampleIndex) : null,
            maximumTextWidth,
            _hoverMaximumTextLines);
    }

    private string FormatHoverMetric(int sampleIndex)
    {
        string? providedMetric = _hoverMetricProvider?.Invoke(_history.GetTimestampChronological(sampleIndex));
        return string.IsNullOrEmpty(providedMetric)
            ? string.Concat(
                _history.GetChronological(sampleIndex).ToString(format: "N0", CultureInfo.CurrentCulture),
                str1: "%")
            : providedMetric;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        Rect graphBounds = new(x: 0, y: 0, width, height);
        context.DrawRectangle(_backgroundBrush, _borderPen, graphBounds);
        if (_showUnderfill)
            DrawHistoryUnderfill(context, _history, _underfillBrush, width, height);
        for (int columnIndex = 1; columnIndex < _gridColumns; columnIndex++)
        {
            double positionX = width * columnIndex / _gridColumns;
            context.DrawLine(_gridPen, new Point(positionX, y: 0), new Point(positionX, height));
        }

        for (int rowIndex = 1; rowIndex < _gridRows; rowIndex++)
        {
            double positionY = height * rowIndex / _gridRows;
            context.DrawLine(_gridPen, new Point(x: 0, positionY), new Point(width, positionY));
        }

        if (_secondaryHistory != null)
            DrawHistoryTrace(context, _secondaryHistory, _secondaryLinePen, width, height);
        DrawHistoryTrace(context, _history, _linePen, width, height);
    }

    /// <summary>Draws one history against its fixed-duration timeline.</summary>
    private static void DrawHistoryTrace(
        DrawingContext context,
        PerformanceHistory history,
        Pen pen,
        double width,
        double height)
    {
        int sampleCount = history.Count;
        if (sampleCount < 2) return;

        double windowStartTimestamp = history.CurrentTimestamp
                                      - (double)history.WindowDurationTicks;
        Point previousPoint = PointForSample(
            history,
            sampleIndex: 0,
            width,
            height,
            windowStartTimestamp);
        for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
        {
            Point currentPoint = PointForSample(
                history,
                sampleIndex,
                width,
                height,
                windowStartTimestamp);
            context.DrawLine(pen, previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    /// <summary>Fills the polygon bounded by a history trace and the graph baseline.</summary>
    private static void DrawHistoryUnderfill(
        DrawingContext context,
        PerformanceHistory history,
        IBrush brush,
        double width,
        double height)
    {
        int sampleCount = history.Count;
        if (sampleCount < 2) return;

        double windowStartTimestamp = history.CurrentTimestamp
                                      - (double)history.WindowDurationTicks;
        Point firstPoint = PointForSample(
            history,
            sampleIndex: 0,
            width,
            height,
            windowStartTimestamp);
        Point lastPoint = firstPoint;
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(new Point(firstPoint.X, height), isFilled: true);
            geometryContext.LineTo(firstPoint);
            for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
            {
                lastPoint = PointForSample(
                    history,
                    sampleIndex,
                    width,
                    height,
                    windowStartTimestamp);
                geometryContext.LineTo(lastPoint);
            }

            geometryContext.LineTo(new Point(lastPoint.X, height));
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, pen: null, geometry);
    }

    /// <summary>Maps one history sample onto the graph's fixed-duration timeline.</summary>
    private static Point PointForSample(
        PerformanceHistory history,
        int sampleIndex,
        double width,
        double height,
        double windowStartTimestamp)
    {
        double value = history.GetChronological(sampleIndex);
        long timestamp = history.GetTimestampChronological(sampleIndex);
        double elapsedWindowFraction = (timestamp - windowStartTimestamp)
                                       / history.WindowDurationTicks;
        double positionX = Math.Clamp(elapsedWindowFraction, min: 0, max: 1) * width;
        double positionY = height - value / 100.0 * height;
        return new Point(positionX, positionY);
    }

    private Pen CreateSecondaryLinePen(Color accent)
    {
        byte alpha = (byte)Math.Round(accent.A * _secondaryLineOpacity);
        Color dimAccent = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
        return new Pen(
            new SolidColorBrush(dimAccent),
            _secondaryLineThickness);
    }

    private SolidColorBrush CreateUnderfillBrush(Color accent) =>
        new(PerformanceGraphRendering.CreateUnderfillColor(
            accent,
            _underfillOpacity,
            _underfillDarkenAmount));
}

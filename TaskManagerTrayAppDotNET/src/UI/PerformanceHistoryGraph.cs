using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints a Task Manager-style utilization history.</summary>
internal sealed class PerformanceHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly Pen _hoverLinePen;
    private readonly Pen _hoverLineTerminalPen;
    private readonly IBrush _hoverTextBrush;
    private readonly Typeface _hoverTypeface;
    private readonly double _hoverFontSize;
    private readonly double _hoverLineClipPadding;
    private readonly double _hoverLineDashGapLength;
    private readonly double _hoverTextCursorGap;
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
    private bool _isPointerOver;
    private bool _showUnderfill = true;

    public PerformanceHistoryGraph(
        PerformanceHistory history,
        Color accent,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<long, string?>? hoverMetricProvider = null)
    {
        _history = history;
        _accent = accent;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Background);
        _borderPen = new Pen(
            TrayAppDotNETSettingsUI.Brush(palette.Border),
            resources.AxamlTaskManagerPerformance.GraphBorderThickness);
        byte gridAlpha = (byte)Math.Round(
            byte.MaxValue * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphGridLineOpacity,
                0,
                1));
        Color gridColor = Color.FromArgb(
            gridAlpha,
            palette.Border.R,
            palette.Border.G,
            palette.Border.B);
        _gridPen = new Pen(
            new SolidColorBrush(gridColor),
            resources.AxamlTaskManagerPerformance.GraphGridLineThickness);
        Color hoverLineColor = resources.AxamlTaskManagerPerformance.GraphHoverLineColor;
        byte hoverLineAlpha = (byte)Math.Round(
            hoverLineColor.A * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphHoverLineOpacity,
                0,
                1));
        IBrush hoverLineBrush = new SolidColorBrush(Color.FromArgb(
            hoverLineAlpha,
            hoverLineColor.R,
            hoverLineColor.G,
            hoverLineColor.B));
        double hoverLineThickness =
            resources.AxamlTaskManagerPerformance.GraphHoverLineThickness;
        // AXAML hot-reload exception: DashStyle is not a linker-supported AXAML primitive, so
        // the immutable Pen retains Avalonia's built-in Dash preset
        _hoverLinePen = new Pen(
            hoverLineBrush,
            hoverLineThickness,
            DashStyle.Dash);
        _hoverLineTerminalPen = new Pen(hoverLineBrush, hoverLineThickness);
        _hoverTextBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _hoverTypeface = new Typeface(TrayAppDotNETSettingsUI.UIFont);
        _hoverFontSize = resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize;
        _hoverLineClipPadding =
            resources.AxamlTaskManagerPerformance.GraphHoverLineClipPadding;
        _hoverLineDashGapLength =
            resources.AxamlTaskManagerPerformance.GraphHoverLineDashGapLength;
        _hoverTextCursorGap = resources.AxamlTaskManagerPerformance.GraphHoverTextCursorGap;
        _hoverTextInset = resources.AxamlTaskManagerPerformance.GraphHoverTextInset;
        _hoverMaximumTextLines = Math.Max(
            1,
            resources.AxamlTaskManagerPerformance.GraphHoverMaximumTextLines);
        _lineThickness = resources.AxamlTaskManagerPerformance.GraphLineThickness;
        _secondaryLineThickness =
            resources.AxamlTaskManagerPerformance.GraphSecondaryLineThickness;
        _secondaryLineOpacity = Math.Clamp(
            resources.AxamlTaskManagerPerformance.GraphSecondaryLineOpacity,
            0,
            1);
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
    }

    protected override void OnPointerEntered(PointerEventArgs eventArgs)
    {
        base.OnPointerEntered(eventArgs);
        TrackPointer(eventArgs.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        TrackPointer(eventArgs.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        ClearPointer();
    }

    /// <summary>Tracks a pointer supplied by this graph or an enclosing hit target.</summary>
    internal void TrackPointer(Point pointerPosition)
    {
        if (_isPointerOver && _hoverPointerPosition == pointerPosition) return;

        _isPointerOver = true;
        _hoverPointerPosition = pointerPosition;
        InvalidateVisual();
    }

    /// <summary>Clears hover state when the pointer leaves this graph's full hit target.</summary>
    internal void ClearPointer()
    {
        if (!_isPointerOver) return;

        _isPointerOver = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        Rect graphBounds = new(0, 0, width, height);
        context.DrawRectangle(_backgroundBrush, _borderPen, graphBounds);
        if (_showUnderfill)
            DrawHistoryUnderfill(context, _history, _underfillBrush, width, height);
        for (int columnIndex = 1; columnIndex < _gridColumns; columnIndex++)
        {
            double positionX = width * columnIndex / _gridColumns;
            context.DrawLine(_gridPen, new Point(positionX, 0), new Point(positionX, height));
        }
        for (int rowIndex = 1; rowIndex < _gridRows; rowIndex++)
        {
            double positionY = height * rowIndex / _gridRows;
            context.DrawLine(_gridPen, new Point(0, positionY), new Point(width, positionY));
        }

        if (_secondaryHistory != null)
            DrawHistoryTrace(context, _secondaryHistory, _secondaryLinePen, width, height);
        DrawHistoryTrace(context, _history, _linePen, width, height);

        DrawHover(context, width, height);
    }

    /// <summary>Draws the hovered sample marker and its bounded metric label.</summary>
    private void DrawHover(DrawingContext context, double width, double height)
    {
        if (!_isPointerOver
            || !PerformanceHistoryGraphLayout.TryGetHoverSample(
                _history,
                _hoverPointerPosition.X,
                width,
                out PerformanceHistoryGraphHoverSample sample))
        {
            return;
        }

        double maximumTextWidth = Math.Max(0, width - _hoverTextInset * 2);
        if (maximumTextWidth <= 0)
        {
            context.DrawLine(
                _hoverLinePen,
                new Point(sample.PositionX, 0),
                new Point(sample.PositionX, height));
            return;
        }

        string? providedMetric = _hoverMetricProvider?.Invoke(sample.Timestamp);
        string metric = string.IsNullOrEmpty(providedMetric)
            ? string.Concat(
                sample.Value.ToString("N0", CultureInfo.CurrentCulture),
                "%")
            : providedMetric;
        using TextLayout metricText = new(
            metric,
            _hoverTypeface,
            _hoverFontSize,
            _hoverTextBrush,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: maximumTextWidth,
            maxLines: _hoverMaximumTextLines);

        double preferredTextLeft = sample.PositionX - metricText.Width / 2;
        double textLeft = ClampMetricCoordinate(
            preferredTextLeft,
            width,
            metricText.Width);
        double preferredTextTop = _hoverPointerPosition.Y
                                  - _hoverTextCursorGap
                                  - metricText.Height;
        double textTop = ClampMetricCoordinate(
            preferredTextTop,
            height,
            metricText.Height);
        Rect textBounds = new(textLeft, textTop, metricText.Width, metricText.Height);
        double metricInkBottom = textBounds.Top
                                 + metricText.Height
                                 + metricText.OverhangAfter;
        double metricInkTop = metricInkBottom - metricText.Extent;
        DrawHoverLineAroundMetric(
            context,
            sample.PositionX,
            height,
            metricInkTop,
            metricInkBottom);
        metricText.Draw(context, textBounds.TopLeft);
    }

    /// <summary>Clamps one label coordinate while preserving an edge inset when space permits.</summary>
    private double ClampMetricCoordinate(
        double preferredCoordinate,
        double availableLength,
        double metricLength)
    {
        double maximumCoordinate = Math.Max(0, availableLength - metricLength);
        double edgeInset = Math.Min(_hoverTextInset, maximumCoordinate / 2);
        return Math.Clamp(
            preferredCoordinate,
            edgeInset,
            maximumCoordinate - edgeInset);
    }

    /// <summary>Draws the indicator above and below a transparent gap around the metric text.</summary>
    private void DrawHoverLineAroundMetric(
        DrawingContext context,
        double positionX,
        double height,
        double metricInkTop,
        double metricInkBottom)
    {
        double upperLineEnd = Math.Clamp(metricInkTop - _hoverLineClipPadding, 0, height);
        double lowerLineStart = Math.Clamp(metricInkBottom + _hoverLineClipPadding, 0, height);
        if (upperLineEnd > 0)
        {
            context.DrawLine(
                _hoverLinePen,
                new Point(positionX, 0),
                new Point(positionX, upperLineEnd));
            double terminalStart = Math.Max(
                0,
                upperLineEnd - _hoverLineTerminalPen.Thickness * _hoverLineDashGapLength);
            context.DrawLine(
                _hoverLineTerminalPen,
                new Point(positionX, terminalStart),
                new Point(positionX, upperLineEnd));
        }
        if (lowerLineStart < height)
        {
            context.DrawLine(
                _hoverLinePen,
                new Point(positionX, lowerLineStart),
                new Point(positionX, height));
        }
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
            0,
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
            0,
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
            geometryContext.EndFigure(isClosed: true);
        }
        context.DrawGeometry(brush, null, geometry);
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
        double positionX = Math.Clamp(elapsedWindowFraction, 0, 1) * width;
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

    private IBrush CreateUnderfillBrush(Color accent) =>
        new SolidColorBrush(PerformanceGraphRendering.CreateUnderfillColor(
            accent,
            _underfillOpacity,
            _underfillDarkenAmount));
}

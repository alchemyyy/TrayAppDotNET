using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints one or two raw metric histories against an explicit vertical scale.</summary>
internal sealed class PerformanceMetricHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly Pen _hoverLinePen;
    private readonly IBrush _hoverTextBrush;
    private readonly IBrush _underfillBrush;
    private readonly Typeface _hoverTypeface;
    private readonly double _hoverFontSize;
    private readonly double _hoverTextInset;
    private readonly double _hoverTextCursorGap;
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private readonly Func<double, string> _metricFormatter;
    private PerformanceMetricHistory _primaryHistory;
    private PerformanceMetricHistory? _secondaryHistory;
    private string _primaryLabel;
    private string _secondaryLabel;
    private double _maximumValue = 1;
    private Point _hoverPointerPosition;
    private bool _isPointerOver;
    private bool _showUnderfill = true;

    public PerformanceMetricHistoryGraph(
        PerformanceMetricHistory primaryHistory,
        PerformanceMetricHistory? secondaryHistory,
        string primaryLabel,
        string secondaryLabel,
        Func<double, string> metricFormatter,
        Color accent,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        _primaryHistory = primaryHistory;
        _secondaryHistory = secondaryHistory;
        _primaryLabel = primaryLabel;
        _secondaryLabel = secondaryLabel;
        _metricFormatter = metricFormatter;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(
            resources.AxamlTaskManagerPerformance.GraphBackgroundColor);
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
        IBrush accentBrush = new SolidColorBrush(accent);
        PrimaryPen = new Pen(
            accentBrush,
            resources.AxamlTaskManagerPerformance.GraphLineThickness);
        // AXAML hot-reload exception: DashStyle is not a linker-supported AXAML primitive, so
        // the immutable Pens retain Avalonia's built-in Dash preset
        SecondaryPen = new Pen(
            accentBrush,
            resources.AxamlTaskManagerPerformance.GraphLineThickness,
            DashStyle.Dash);
        _underfillBrush = new SolidColorBrush(
            PerformanceGraphRendering.CreateUnderfillColor(
                accent,
                resources.AxamlTaskManagerPerformance.GraphUnderfillOpacity,
                resources.AxamlTaskManagerPerformance.GraphUnderfillDarkenAmount));
        Color configuredHoverLineColor =
            resources.AxamlTaskManagerPerformance.GraphHoverLineColor;
        byte hoverLineAlpha = (byte)Math.Round(
            configuredHoverLineColor.A * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphHoverLineOpacity,
                min: 0,
                max: 1));
        Color hoverLineColor = Color.FromArgb(
            hoverLineAlpha,
            configuredHoverLineColor.R,
            configuredHoverLineColor.G,
            configuredHoverLineColor.B);
        _hoverLinePen = new Pen(
            new SolidColorBrush(hoverLineColor),
            resources.AxamlTaskManagerPerformance.GraphHoverLineThickness,
            DashStyle.Dash);
        _hoverTextBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _hoverTypeface = new Typeface(TrayAppDotNETSettingsUI.UIFont);
        _hoverFontSize = resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize;
        _hoverTextInset = resources.AxamlTaskManagerPerformance.GraphHoverTextInset;
        _hoverTextCursorGap = resources.AxamlTaskManagerPerformance.GraphHoverTextCursorGap;
        _gridColumns = resources.AxamlTaskManagerPerformance.GraphGridColumns;
        _gridRows = resources.AxamlTaskManagerPerformance.GraphGridRows;
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    private Pen PrimaryPen { get; }
    private Pen SecondaryPen { get; }

    /// <summary>Changes the raw histories without rebuilding the graph control.</summary>
    public void SetHistories(
        PerformanceMetricHistory primaryHistory,
        PerformanceMetricHistory? secondaryHistory)
    {
        ArgumentNullException.ThrowIfNull(primaryHistory);
        _primaryHistory = primaryHistory;
        _secondaryHistory = secondaryHistory;
        InvalidateVisual();
    }

    /// <summary>Changes the labels shown beside hovered series values.</summary>
    public void SetSeriesLabels(string primaryLabel, string secondaryLabel)
    {
        _primaryLabel = primaryLabel;
        _secondaryLabel = secondaryLabel;
    }

    /// <summary>Shows or hides translucent areas beneath the metric traces.</summary>
    public void SetUnderfillVisible(bool isVisible)
    {
        if (_showUnderfill == isVisible) return;

        _showUnderfill = isVisible;
        InvalidateVisual();
    }

    /// <summary>Changes the raw value represented by the top edge of the graph.</summary>
    public void SetMaximumValue(double maximumValue)
    {
        double normalizedMaximum = double.IsFinite(maximumValue) && maximumValue > 0
            ? maximumValue
            : 1;
        if (_maximumValue == normalizedMaximum) return;

        _maximumValue = normalizedMaximum;
        InvalidateVisual();
    }

    /// <summary>Schedules a repaint after either history receives a sample.</summary>
    public void Refresh() => InvalidateVisual();

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
        _isPointerOver = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        Rect graphBounds = new(x: 0, y: 0, width, height);
        context.DrawRectangle(_backgroundBrush, _borderPen, graphBounds);
        long currentTimestamp = Math.Max(
            _primaryHistory.CurrentTimestamp,
            _secondaryHistory?.CurrentTimestamp ?? 0);
        long durationTicks = Math.Max(
            _primaryHistory.WindowDurationTicks,
            _secondaryHistory?.WindowDurationTicks ?? 0);
        double windowStartTimestamp = currentTimestamp - (double)durationTicks;
        if (_showUnderfill)
        {
            if (_secondaryHistory != null)
            {
                DrawHistoryUnderfill(
                    context,
                    _secondaryHistory,
                    width,
                    height,
                    windowStartTimestamp,
                    durationTicks);
            }

            DrawHistoryUnderfill(
                context,
                _primaryHistory,
                width,
                height,
                windowStartTimestamp,
                durationTicks);
        }

        DrawGrid(context, width, height);
        DrawHistory(
            context,
            _primaryHistory,
            PrimaryPen,
            width,
            height,
            windowStartTimestamp,
            durationTicks);
        if (_secondaryHistory != null)
        {
            DrawHistory(
                context,
                _secondaryHistory,
                SecondaryPen,
                width,
                height,
                windowStartTimestamp,
                durationTicks);
        }

        DrawHover(context, width, height, currentTimestamp, durationTicks);
    }

    private void TrackPointer(Point pointerPosition)
    {
        _isPointerOver = true;
        _hoverPointerPosition = pointerPosition;
        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext context, double width, double height)
    {
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
    }

    private void DrawHistory(
        DrawingContext context,
        PerformanceMetricHistory history,
        Pen pen,
        double width,
        double height,
        double windowStartTimestamp,
        long durationTicks)
    {
        if (history.Count < 2 || durationTicks <= 0) return;

        Point previousPoint = PointForSample(
            history,
            sampleIndex: 0,
            width,
            height,
            windowStartTimestamp,
            durationTicks);
        for (int sampleIndex = 1; sampleIndex < history.Count; sampleIndex++)
        {
            Point currentPoint = PointForSample(
                history,
                sampleIndex,
                width,
                height,
                windowStartTimestamp,
                durationTicks);
            context.DrawLine(pen, previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    /// <summary>Fills the polygon bounded by one metric trace and the graph baseline.</summary>
    private void DrawHistoryUnderfill(
        DrawingContext context,
        PerformanceMetricHistory history,
        double width,
        double height,
        double windowStartTimestamp,
        long durationTicks)
    {
        int sampleCount = history.Count;
        if (sampleCount < 2 || durationTicks <= 0) return;

        Point firstPoint = PointForSample(
            history,
            sampleIndex: 0,
            width,
            height,
            windowStartTimestamp,
            durationTicks);
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
                    windowStartTimestamp,
                    durationTicks);
                geometryContext.LineTo(lastPoint);
            }

            geometryContext.LineTo(new Point(lastPoint.X, height));
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(_underfillBrush, pen: null, geometry);
    }

    private Point PointForSample(
        PerformanceMetricHistory history,
        int sampleIndex,
        double width,
        double height,
        double windowStartTimestamp,
        long durationTicks)
    {
        double value = history.GetChronological(sampleIndex);
        long timestamp = history.GetTimestampChronological(sampleIndex);
        double elapsedWindowFraction = (timestamp - windowStartTimestamp) / durationTicks;
        double positionX = Math.Clamp(elapsedWindowFraction, min: 0, max: 1) * width;
        double positionY = height - Math.Clamp(value / _maximumValue, min: 0, max: 1) * height;
        return new Point(positionX, positionY);
    }

    private void DrawHover(
        DrawingContext context,
        double width,
        double height,
        long currentTimestamp,
        long durationTicks)
    {
        if (!_isPointerOver || durationTicks <= 0) return;

        double horizontalFraction = Math.Clamp(_hoverPointerPosition.X / width, min: 0, max: 1);
        long windowStartTimestamp = currentTimestamp - durationTicks;
        long targetTimestamp = windowStartTimestamp
                               + (long)Math.Round(durationTicks * horizontalFraction);
        bool hasPrimary = _primaryHistory.TryGetNearest(targetTimestamp, out double primaryValue);
        double secondaryValue = 0;
        bool hasSecondary = _secondaryHistory?.TryGetNearest(
            targetTimestamp,
            out secondaryValue) == true;
        if (!hasPrimary && !hasSecondary) return;

        string metric = BuildHoverMetric(
            hasPrimary,
            primaryValue,
            hasSecondary,
            secondaryValue);
        using TextLayout metricText = new(
            metric,
            _hoverTypeface,
            _hoverFontSize,
            _hoverTextBrush,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: Math.Max(val1: 0, width - _hoverTextInset * 2));
        double positionX = horizontalFraction * width;
        double textLeft = Math.Clamp(
            positionX - metricText.Width / 2,
            min: 0,
            Math.Max(val1: 0, width - metricText.Width));
        double textTop = Math.Clamp(
            _hoverPointerPosition.Y - _hoverTextCursorGap - metricText.Height,
            min: 0,
            Math.Max(val1: 0, height - metricText.Height));
        context.DrawLine(_hoverLinePen, new Point(positionX, y: 0), new Point(positionX, height));
        metricText.Draw(context, new Point(textLeft, textTop));
    }

    private string BuildHoverMetric(
        bool hasPrimary,
        double primaryValue,
        bool hasSecondary,
        double secondaryValue)
    {
        if (!hasSecondary)
            return hasPrimary ? _metricFormatter(primaryValue) : string.Empty;
        if (!hasPrimary)
            return string.Concat(_secondaryLabel, str1: ": ", _metricFormatter(secondaryValue));

        return string.Concat(
            _primaryLabel,
            ": ",
            _metricFormatter(primaryValue),
            "\n",
            _secondaryLabel,
            ": ",
            _metricFormatter(secondaryValue));
    }
}

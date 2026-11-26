using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Paints one or two raw metric histories against an explicit vertical scale.</summary>
internal sealed class PerformanceMetricHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly PerformanceGraphHoverPresenter _hover;
    private readonly IBrush _underfillBrush;
    private readonly double _hoverTextInset;
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private readonly Func<double, string> _metricFormatter;
    private PerformanceMetricHistory _primaryHistory;
    private PerformanceMetricHistory? _secondaryHistory;
    private string _primaryLabel;
    private string _secondaryLabel;
    private double _maximumValue = 1;
    private Point _hoverPointerPosition;
    private bool _showUnderfill = true;

    public PerformanceMetricHistoryGraph(
        PerformanceMetricHistory primaryHistory,
        PerformanceMetricHistory? secondaryHistory,
        string primaryLabel,
        string secondaryLabel,
        Func<double, string> metricFormatter,
        Color accent,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme)
    {
        _primaryHistory = primaryHistory;
        _secondaryHistory = secondaryHistory;
        _primaryLabel = primaryLabel;
        _secondaryLabel = secondaryLabel;
        _metricFormatter = metricFormatter;
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
        Color configuredHoverLineColor = resources.GraphHoverLineColor(isLightTheme);
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
        _hoverTextInset = resources.AxamlTaskManagerPerformance.GraphHoverTextInset;
        _hover = new PerformanceGraphHoverPresenter(
            this,
            new PerformanceGraphHoverStyle(
                new PerformanceGraphHoverMetrics(
                    PerformanceGraphHoverLineMode.FollowPointer,
                    _hoverTextInset,
                    resources.AxamlTaskManagerPerformance.GraphHoverTextCursorGap,
                    LineClipPadding: 0,
                    LineTerminalLength: 0),
                new ImmutablePen(
                    new ImmutableSolidColorBrush(hoverLineColor),
                    resources.AxamlTaskManagerPerformance.GraphHoverLineThickness,
                    DashStyle.Dash.ToImmutable()),
                LineTerminalPen: null,
                palette.Foreground,
                new Typeface(TrayAppDotNETSettingsUI.UIFont),
                resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize));
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
        UpdateHover();
    }

    /// <summary>Changes the labels shown beside hovered series values.</summary>
    public void SetSeriesLabels(string primaryLabel, string secondaryLabel)
    {
        _primaryLabel = primaryLabel;
        _secondaryLabel = secondaryLabel;
        UpdateHover();
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
        _hover.SetPointerOver(false);
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
    }

    private void TrackPointer(Point pointerPosition)
    {
        _hoverPointerPosition = pointerPosition;
        _hover.SetPointerOver(true);
        UpdateHover();
    }

    /// <summary>Sends the render-thread hover the label for the latest UI pointer sample.</summary>
    private void UpdateHover()
    {
        if (!_hover.IsActive) return;

        Size graphSize = Bounds.Size;
        long currentTimestamp = Math.Max(
            _primaryHistory.CurrentTimestamp,
            _secondaryHistory?.CurrentTimestamp ?? 0);
        long durationTicks = Math.Max(
            _primaryHistory.WindowDurationTicks,
            _secondaryHistory?.WindowDurationTicks ?? 0);
        string? metric = null;
        if (graphSize.Width > 0 && durationTicks > 0)
        {
            double horizontalFraction = Math.Clamp(_hoverPointerPosition.X / graphSize.Width, min: 0, max: 1);
            long targetTimestamp = currentTimestamp
                                   - durationTicks
                                   + (long)Math.Round(durationTicks * horizontalFraction);
            bool hasPrimary = _primaryHistory.TryGetNearest(targetTimestamp, out double primaryValue);
            double secondaryValue = 0;
            bool hasSecondary = _secondaryHistory?.TryGetNearest(
                targetTimestamp,
                out secondaryValue) == true;
            if (hasPrimary || hasSecondary)
            {
                metric = BuildHoverMetric(
                    hasPrimary,
                    primaryValue,
                    hasSecondary,
                    secondaryValue);
            }
        }

        _hover.Update(
            graphSize,
            new Rect(graphSize),
            sampleFractions: [],
            hasIndicator: metric != null,
            metric,
            Math.Max(val1: 0, graphSize.Width - _hoverTextInset * 2),
            labelMaximumLines: 0);
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

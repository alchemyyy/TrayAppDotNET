using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints an allocation-light, Task Manager-style utilization history.</summary>
internal sealed class PerformanceHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly Pen _hoverLinePen;
    private readonly IBrush _hoverTextBrush;
    private readonly Typeface _hoverTypeface;
    private readonly double _hoverFontSize;
    private readonly double _hoverTextInset;
    private readonly double _lineThickness;
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private PerformanceHistory _history;
    private Color _accent;
    private Pen _linePen;
    private double _hoverPointerPositionX;
    private int _hoveredSampleIndex = -1;
    private bool _isPointerOver;

    public PerformanceHistoryGraph(
        PerformanceHistory history,
        Color accent,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        _history = history;
        _accent = accent;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Background);
        _borderPen = new Pen(
            TrayAppDotNETSettingsUI.Brush(palette.Border),
            resources.AxamlTaskManagerPerformance.GraphBorderThickness);
        Color gridColor = Color.FromArgb(
            92,
            palette.Border.R,
            palette.Border.G,
            palette.Border.B);
        _gridPen = new Pen(
            new SolidColorBrush(gridColor),
            resources.AxamlTaskManagerPerformance.GraphGridLineThickness);
        byte hoverLineAlpha = (byte)Math.Round(
            byte.MaxValue * Math.Clamp(
                resources.AxamlTaskManagerPerformance.GraphHoverLineOpacity,
                0,
                1));
        _hoverLinePen = new Pen(
            new SolidColorBrush(Color.FromArgb(
                hoverLineAlpha,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue)),
            resources.AxamlTaskManagerPerformance.GraphHoverLineThickness,
            DashStyle.Dash);
        _hoverTextBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _hoverTypeface = new Typeface(TrayAppDotNETSettingsUI.UIFont);
        _hoverFontSize = resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize;
        _hoverTextInset = resources.AxamlTaskManagerPerformance.GraphHoverTextInset;
        _lineThickness = resources.AxamlTaskManagerPerformance.GraphLineThickness;
        _linePen = new Pen(new SolidColorBrush(accent), _lineThickness);
        _gridColumns = resources.AxamlTaskManagerPerformance.GraphGridColumns;
        _gridRows = resources.AxamlTaskManagerPerformance.GraphGridRows;
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    /// <summary>Changes the displayed history and schedules a repaint.</summary>
    public void SetHistory(PerformanceHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        SynchronizeHoveredSample();
        InvalidateVisual();
    }

    /// <summary>Changes the device accent used for the utilization trace.</summary>
    public void SetAccent(Color accent)
    {
        if (_accent == accent) return;
        _accent = accent;
        _linePen = new Pen(new SolidColorBrush(accent), _lineThickness);
        InvalidateVisual();
    }

    /// <summary>Schedules a repaint after the current history receives a sample.</summary>
    public void Refresh()
    {
        SynchronizeHoveredSample();
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs eventArgs)
    {
        base.OnPointerEntered(eventArgs);
        UpdateHover(eventArgs.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        UpdateHover(eventArgs.GetPosition(this).X);
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        if (!_isPointerOver) return;

        _isPointerOver = false;
        _hoveredSampleIndex = -1;
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

        int sampleCount = _history.Count;
        if (sampleCount >= 2)
        {
            double windowStartTimestamp = _history.CurrentTimestamp
                                          - (double)_history.WindowDurationTicks;
            Point previousPoint = PointForSample(0, width, height, windowStartTimestamp);
            for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
            {
                Point currentPoint = PointForSample(
                    sampleIndex,
                    width,
                    height,
                    windowStartTimestamp);
                context.DrawLine(_linePen, previousPoint, currentPoint);
                previousPoint = currentPoint;
            }
        }

        DrawHover(context, width, height);
    }

    /// <summary>Updates the selected sample when the pointer crosses a sample boundary.</summary>
    private void UpdateHover(double pointerPositionX)
    {
        bool wasPointerOver = _isPointerOver;
        int previousSampleIndex = _hoveredSampleIndex;
        _isPointerOver = true;
        _hoverPointerPositionX = pointerPositionX;
        SynchronizeHoveredSample();
        if (!wasPointerOver || previousSampleIndex != _hoveredSampleIndex)
            InvalidateVisual();
    }

    /// <summary>Synchronizes the cached hover index after history or pointer changes.</summary>
    private void SynchronizeHoveredSample()
    {
        _hoveredSampleIndex = _isPointerOver
                              && PerformanceHistoryGraphLayout.TryGetHoverSample(
                                  _history,
                                  _hoverPointerPositionX,
                                  Bounds.Width,
                                  out PerformanceHistoryGraphHoverSample sample)
            ? sample.ChronologicalIndex
            : -1;
    }

    /// <summary>Draws the hovered sample marker and its bounded percentage label.</summary>
    private void DrawHover(DrawingContext context, double width, double height)
    {
        if (!_isPointerOver
            || !PerformanceHistoryGraphLayout.TryGetHoverSample(
                _history,
                _hoverPointerPositionX,
                width,
                out PerformanceHistoryGraphHoverSample sample))
        {
            return;
        }

        context.DrawLine(
            _hoverLinePen,
            new Point(sample.PositionX, 0),
            new Point(sample.PositionX, height));

        double maximumTextWidth = Math.Max(0, width - _hoverTextInset * 2);
        if (maximumTextWidth <= 0) return;

        string metric = string.Concat(
            sample.Value.ToString("N0", CultureInfo.CurrentCulture),
            "%");
        using TextLayout metricText = new(
            metric,
            _hoverTypeface,
            _hoverFontSize,
            _hoverTextBrush,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: maximumTextWidth,
            maxLines: 1);

        double preferredTextLeft = sample.PositionX + _hoverTextInset;
        if (preferredTextLeft + metricText.Width > width - _hoverTextInset)
            preferredTextLeft = sample.PositionX - _hoverTextInset - metricText.Width;

        double maximumTextLeft = Math.Max(
            _hoverTextInset,
            width - _hoverTextInset - metricText.Width);
        double textLeft = Math.Clamp(
            preferredTextLeft,
            _hoverTextInset,
            maximumTextLeft);
        double textTop = Math.Clamp(
            _hoverTextInset,
            0,
            Math.Max(0, height - metricText.Height));
        Rect textBounds = new(textLeft, textTop, metricText.Width, metricText.Height);
        context.DrawRectangle(_backgroundBrush, null, textBounds);
        metricText.Draw(context, textBounds.TopLeft);
    }

    /// <summary>Maps one history sample onto the graph's fixed-duration timeline.</summary>
    private Point PointForSample(
        int sampleIndex,
        double width,
        double height,
        double windowStartTimestamp)
    {
        double value = _history.GetChronological(sampleIndex);
        long timestamp = _history.GetTimestampChronological(sampleIndex);
        double elapsedWindowFraction = (timestamp - windowStartTimestamp)
                                       / _history.WindowDurationTicks;
        double positionX = Math.Clamp(elapsedWindowFraction, 0, 1) * width;
        double positionY = height - value / 100.0 * height;
        return new Point(positionX, positionY);
    }
}

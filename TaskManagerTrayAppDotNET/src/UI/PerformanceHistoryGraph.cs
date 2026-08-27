using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints an allocation-light, Task Manager-style utilization history.</summary>
internal sealed class PerformanceHistoryGraph : Control
{
    private readonly IBrush _backgroundBrush;
    private readonly Pen _borderPen;
    private readonly Pen _gridPen;
    private readonly double _lineThickness;
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private PerformanceHistory _history;
    private Color _accent;
    private Pen _linePen;

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
        _lineThickness = resources.AxamlTaskManagerPerformance.GraphLineThickness;
        _linePen = new Pen(new SolidColorBrush(accent), _lineThickness);
        _gridColumns = resources.AxamlTaskManagerPerformance.GraphGridColumns;
        _gridRows = resources.AxamlTaskManagerPerformance.GraphGridRows;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    /// <summary>Changes the displayed history and schedules a repaint.</summary>
    public void SetHistory(PerformanceHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
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
    public void Refresh() => InvalidateVisual();

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
        if (sampleCount < 2) return;

        long windowStartTimestamp = _history.CurrentTimestamp - _history.WindowDurationTicks;
        Point previousPoint = PointForSample(0);
        for (int sampleIndex = 1; sampleIndex < sampleCount; sampleIndex++)
        {
            Point currentPoint = PointForSample(sampleIndex);
            context.DrawLine(_linePen, previousPoint, currentPoint);
            previousPoint = currentPoint;
        }

        return;

        Point PointForSample(int sampleIndex)
        {
            double value = _history.GetChronological(sampleIndex);
            long timestamp = _history.GetTimestampChronological(sampleIndex);
            double elapsedWindowFraction = (timestamp - windowStartTimestamp)
                                           / (double)_history.WindowDurationTicks;
            double positionX = Math.Clamp(elapsedWindowFraction, 0, 1) * width;
            double positionY = height - value / 100.0 * height;
            return new Point(positionX, positionY);
        }
    }
}

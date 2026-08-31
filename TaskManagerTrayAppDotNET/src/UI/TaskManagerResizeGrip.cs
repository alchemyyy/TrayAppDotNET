using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints and handles the southeast resize grip in the process-table scrollbar corner.</summary>
internal sealed class TaskManagerResizeGrip : Control
{
    private IBrush _backgroundBrush = Brushes.Transparent;
    private IBrush _dotBrush = Brushes.Transparent;
    private double _dotSize;
    private double _dotStep;
    private int _dotRows;

    public TaskManagerResizeGrip(TaskManagerWindowResources resources)
    {
        ApplyResources(resources);
        Cursor = TrayAppDotNETCursors.BottomRightCorner;
        Focusable = false;
    }

    /// <summary>Applies hot-reloaded process-table geometry.</summary>
    public void ApplyResources(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(
            resources.AxamlProcessTable.GridBackgroundColor);
        _dotBrush = TrayAppDotNETSettingsUI.Brush(resources.AxamlProcessTable.ResizeGripColor);
        _dotSize = resources.AxamlProcessTable.ResizeGripDotSize;
        _dotStep = resources.AxamlProcessTable.ResizeGripDotStep;
        _dotRows = resources.AxamlProcessTable.ResizeGripDotRows;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_backgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        double availableSize = Math.Min(Bounds.Width, Bounds.Height);
        int visibleRows = Math.Clamp(
            (int)Math.Floor((availableSize - _dotSize * 2) / _dotStep) + 1,
            0,
            _dotRows);
        for (int row = 0; row < visibleRows; row++)
        {
            double top = Bounds.Height - _dotSize * 2 - row * _dotStep;
            int columnCount = visibleRows - row;
            for (int column = 0; column < columnCount; column++)
            {
                double left = Bounds.Width - _dotSize * 2 - column * _dotStep;
                context.FillRectangle(_dotBrush, new Rect(left, top, _dotSize, _dotSize));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        try
        {
            window.BeginResizeDrag(WindowEdge.SouthEast, eventArgs);
            eventArgs.Handled = true;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Task Manager resize grip failed: {exception.Message}");
        }
    }
}

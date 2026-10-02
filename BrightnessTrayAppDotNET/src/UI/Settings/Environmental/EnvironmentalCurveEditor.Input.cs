using Avalonia;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed partial class EnvironmentalCurveEditor
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;

        PointerPoint point = e.GetCurrentPoint(this);
        Point pos = e.GetPosition(this);
        Rect plot = PlotRect();
        if (plot.Width <= 0 || plot.Height <= 0) return;
        bool insidePlot = plot.Contains(pos);

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            if (insidePlot)
            {
                DeletePointAt(pos);
                e.Handled = true;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        Focus();
        _cursorPos = insidePlot ? pos : null;

        if (_previewMode)
        {
            if (_exitPreviewButtonRect is { } exitRect && exitRect.Contains(pos))
            {
                ExitPreviewModeRequested?.Invoke();
                e.Handled = true;
            }

            return;
        }

        if (_disabledPeriodEnabled && TryHitDisabledPin(pos, plot, out DisabledPin pin))
        {
            _dragDisabledPin = pin;
            CapturePointer(e);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!insidePlot)
        {
            UpdateHover(pos, plot);
            InvalidateVisual();
            return;
        }

        if (TryHitThumb(pos, plot, out (Series series, EnvironmentalCurvePoint point) hit))
        {
            _dragPoint = hit.point;
            _dragSeries = hit.series;
            _selectedPoint = hit.point;
            _selectedSeries = hit.series;
            CapturePointer(e);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_offsetMode && TryHitLimitLine(pos, plot, out (Series series, LimitKind kind) limitHit))
        {
            _draggingLimit = true;
            _limitDragSeries = limitHit.series;
            _limitDragKind = limitHit.kind;
            CapturePointer(e);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (AddPointFromPointer(pos, plot) is { } added)
        {
            _dragPoint = added.Point;
            _dragSeries = added.Series;
            CapturePointer(e);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point pos = e.GetPosition(this);
        Rect plot = PlotRect();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            InvalidateVisual();
            return;
        }

        bool insidePlot = plot.Contains(pos);
        _cursorPos = insidePlot ? pos : null;

        if (_dragDisabledPin is { } draggedPin)
        {
            double t = Math.Clamp(FromScreenX(pos.X, plot), min: 0.0, max: 1.0);
            if (draggedPin == DisabledPin.Start)
                _disabledPeriodStart = t;
            else
                _disabledPeriodEnd = t;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_draggingLimit && _curveData != null)
        {
            DragLimit(pos, plot);
            InvalidateVisual();
            CurveChanged?.Invoke();
            e.Handled = true;
            return;
        }

        if (_dragPoint != null)
        {
            DragPoint(pos, plot);
            InvalidateVisual();
            CurveChanged?.Invoke();
            e.Handled = true;
            return;
        }

        UpdateHover(pos, plot);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsDragging) return;

        bool changed = _dragPoint != null || _draggingLimit;
        bool disabledPeriodChanged = _dragDisabledPin != null;
        ClearDragState(false);
        if (changed) CurveChanged?.Invoke();
        if (disabledPeriodChanged) DisabledPeriodChanged?.Invoke(_disabledPeriodStart, _disabledPeriodEnd);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!IsDragging) return;

        bool changed = _dragPoint != null || _draggingLimit;
        bool disabledPeriodChanged = _dragDisabledPin != null;
        ClearDragState(false);
        if (changed) CurveChanged?.Invoke();
        if (disabledPeriodChanged) DisabledPeriodChanged?.Invoke(_disabledPeriodStart, _disabledPeriodEnd);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsDragging)
        {
            _cursorPos = null;
            _hoveredThumb = null;
            _hoveredLimit = null;
            _hoveredDisabledPin = null;
            Cursor = ArrowCursor;
            InvalidateVisual();
        }
    }

    private void EnsureSelectionOnFocus()
    {
        if (_selectedPoint != null) return;

        List<(Series series, EnvironmentalCurvePoint point)> nodes = GetNavigableNodes();
        if (nodes.Count == 0) return;
        _selectedSeries = nodes[0].series;
        _selectedPoint = nodes[0].point;
        InvalidateVisual();
    }

    private void ClearSelectionOnLostFocus()
    {
        if (_selectedPoint == null) return;
        _selectedPoint = null;
        InvalidateVisual();
    }

    // Tab stays here because Avalonia's Tab navigation handles Tab before the control map can dispatch it
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Tab || !CanHandleKeys) return;

        NavigateSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
        e.Handled = true;
    }

    /// <summary>Registers the editor's keyboard commands, which the control map dispatches while it has focus.</summary>
    private void MapKeyboardCommands()
    {
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.NudgeNodeUp,
            activation => NudgeSelected(activation, horizontalDirection: 0, verticalDirection: 1));
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.NudgeNodeDown,
            activation => NudgeSelected(activation, horizontalDirection: 0, verticalDirection: -1));
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.NudgeNodeLeft,
            activation => NudgeSelected(activation, horizontalDirection: -1, verticalDirection: 0));
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.NudgeNodeRight,
            activation => NudgeSelected(activation, horizontalDirection: 1, verticalDirection: 0));
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.InsertNode, () =>
        {
            if (CanHandleKeys) InsertNodeNearSelected();
        });
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.DeleteNode, () =>
        {
            if (CanHandleKeys) DeleteSelected();
        });
        this.MapCommand(ControlMap.Settings.EnvironmentalPage.CurveEditor.ClearSelection, () =>
        {
            if (!CanHandleKeys) return;

            _selectedPoint = null;
            InvalidateVisual();
        });
    }

    // Shift moves one minute or one value unit, Ctrl a coarse step, and no modifier a fine step
    private void NudgeSelected(ControlActivation activation, int horizontalDirection, int verticalDirection)
    {
        if (!CanHandleKeys) return;

        double xStep;
        double yStep;
        if ((activation.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            xStep = KeyboardStepOneMinute;
            yStep = _offsetMode ? KeyboardStepOneYUnitOffset : KeyboardStepOneYUnitAbsolute;
        }
        else if ((activation.KeyModifiers & KeyModifiers.Control) != 0)
        {
            xStep = KeyboardStepCoarse;
            yStep = KeyboardStepCoarse;
        }
        else
        {
            xStep = KeyboardStepFine;
            yStep = KeyboardStepFine;
        }

        AdjustSelected(horizontalDirection * xStep, verticalDirection * yStep);
    }

    // Keys are inactive while previewing another date or dragging
    private bool CanHandleKeys => IsEnabled && !_previewMode && !IsDragging;

    private bool IsDragging => _dragPoint != null || _draggingLimit || _dragDisabledPin != null;
}

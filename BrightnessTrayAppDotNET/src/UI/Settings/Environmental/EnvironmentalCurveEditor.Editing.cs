using Avalonia;
using Avalonia.Input;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed partial class EnvironmentalCurveEditor
{
    private (Series Series, EnvironmentalCurvePoint Point)? AddPointFromPointer(Point pos, Rect plot)
    {
        if (!plot.Contains(pos)) return null;
        double t = Math.Clamp(FromScreenX(pos.X, plot), 0.0, 1.0);
        double v = Math.Clamp(FromScreenY(pos.Y, plot), 0.0, 100.0);
        List<EnvironmentalCurvePoint>? target = PickClosestVisibleSeries(t, pos.Y, plot);
        if (target == null) return null;

        if (AddPoint(target, t, v))
        {
            _selectedSeries = ReferenceEquals(target, _brightness) ? Series.Brightness : Series.NightLight;
            _selectedPoint = FindNearestByTime(target, t);
            InvalidateVisual();
            CurveChanged?.Invoke();
            return _selectedPoint == null ? null : (_selectedSeries, _selectedPoint);
        }

        return null;
    }

    private void DeletePointAt(Point pos)
    {
        if (_previewMode) return;
        Rect plot = PlotRect();
        if (!plot.Contains(pos)) return;
        if (!TryHitThumb(pos, plot, out (Series series, EnvironmentalCurvePoint point) hit)) return;

        List<EnvironmentalCurvePoint> series = GetSeries(hit.series);
        if (IsEndpoint(hit.point, series)) return;

        double removedTime = hit.point.Time;
        series.Remove(hit.point);
        if (_selectedPoint != null && ReferenceEquals(_selectedPoint, hit.point))
            _selectedPoint = PickNeighbourAfterRemoval(series, removedTime);
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private void DragPoint(Point pos, Rect plot)
    {
        if (_dragPoint == null) return;

        _dragPoint.Value = Math.Clamp(FromScreenY(pos.Y, plot), 0.0, 100.0);
        List<EnvironmentalCurvePoint> series = GetSeries(_dragSeries);
        SyncEdgeYIfEdge(_dragPoint, series);
        if (IsEndpoint(_dragPoint, series)) return;

        double t = Math.Clamp(FromScreenX(pos.X, plot), 0.0, 1.0);
        List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(p => p.Time)];
        int index = ordered.IndexOf(_dragPoint);
        if (index > 0) t = Math.Max(t, ordered[index - 1].Time + MinimumPointSeparation);
        if (index < ordered.Count - 1) t = Math.Min(t, ordered[index + 1].Time - MinimumPointSeparation);
        _dragPoint.Time = t;
    }

    private void DragLimit(Point pos, Rect plot)
    {
        if (_curveData == null) return;

        double value = Math.Clamp(FromScreenY(pos.Y, plot), 0.0, 100.0);
        switch (_limitDragSeries, _limitDragKind)
        {
            case (Series.Brightness, LimitKind.Min):
                _curveData.BrightnessOffsetMin =
                    Math.Min(value, _curveData.BrightnessOffsetMax - MinimumLimitSeparation);
                break;
            case (Series.Brightness, LimitKind.Max):
                _curveData.BrightnessOffsetMax =
                    Math.Max(value, _curveData.BrightnessOffsetMin + MinimumLimitSeparation);
                break;
            case (Series.NightLight, LimitKind.Min):
                _curveData.NightLightOffsetMin =
                    Math.Min(value, _curveData.NightLightOffsetMax - MinimumLimitSeparation);
                break;
            case (Series.NightLight, LimitKind.Max):
                _curveData.NightLightOffsetMax =
                    Math.Max(value, _curveData.NightLightOffsetMin + MinimumLimitSeparation);
                break;
        }
    }

    private void UpdateHover(Point pos, Rect plot)
    {
        if (_previewMode)
        {
            Cursor = ArrowCursor;
            return;
        }

        DisabledPin? nextPin = null;
        bool overPin = false;
        if (_disabledPeriodEnabled && TryHitDisabledPin(pos, plot, out DisabledPin pin))
        {
            overPin = true;
            nextPin = pin;
        }

        if (!overPin && !plot.Contains(pos))
        {
            _hoveredThumb = null;
            _hoveredDisabledPin = null;
            _hoveredLimit = null;
            Cursor = ArrowCursor;
            return;
        }

        (Series series, EnvironmentalCurvePoint point) thumbHit = default;
        bool overThumb = !overPin && TryHitThumb(pos, plot, out thumbHit);
        _hoveredThumb = overThumb ? thumbHit : null;
        _hoveredDisabledPin = nextPin;
        _hoveredLimit = null;
        if (!overPin && !overThumb && _offsetMode &&
            TryHitLimitLine(pos, plot, out (Series series, LimitKind kind) limit))
            _hoveredLimit = limit;

        Cursor = overPin || _hoveredLimit != null || overThumb ? HandCursor : ArrowCursor;
    }

    private List<(Series series, EnvironmentalCurvePoint point)> GetNavigableNodes()
    {
        List<(Series, EnvironmentalCurvePoint)> nodes = [];
        if (_showBrightness)
        {
            foreach (EnvironmentalCurvePoint point in _brightness.OrderBy(p => p.Time))
                nodes.Add((Series.Brightness, point));
        }

        if (_showNightLight)
        {
            foreach (EnvironmentalCurvePoint point in _nightLight.OrderBy(p => p.Time))
                nodes.Add((Series.NightLight, point));
        }

        return nodes;
    }

    private void NavigateSelection(int direction)
    {
        List<(Series series, EnvironmentalCurvePoint point)> nodes = GetNavigableNodes();
        if (nodes.Count == 0) return;

        int currentIndex = -1;
        if (_selectedPoint is { } selected)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].series == _selectedSeries && ReferenceEquals(nodes[i].point, selected))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        int nextIndex = currentIndex < 0
            ? direction >= 0 ? 0 : nodes.Count - 1
            : ((currentIndex + direction) % nodes.Count + nodes.Count) % nodes.Count;
        _selectedSeries = nodes[nextIndex].series;
        _selectedPoint = nodes[nextIndex].point;
        InvalidateVisual();
    }

    private void AdjustSelected(double dx, double dy)
    {
        if (_selectedPoint == null) return;

        List<EnvironmentalCurvePoint> series = GetSeries(_selectedSeries);
        if (dy != 0.0)
        {
            _selectedPoint.Value = Math.Clamp(_selectedPoint.Value + dy, 0.0, 100.0);
            SyncEdgeYIfEdge(_selectedPoint, series);
        }

        if (dx != 0.0 && !IsEndpoint(_selectedPoint, series))
        {
            List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(p => p.Time)];
            int index = ordered.IndexOf(_selectedPoint);
            double next = Math.Clamp(_selectedPoint.Time + dx, 0.0, 1.0);
            if (index > 0) next = Math.Max(next, ordered[index - 1].Time + MinimumPointSeparation);
            if (index < ordered.Count - 1) next = Math.Min(next, ordered[index + 1].Time - MinimumPointSeparation);
            _selectedPoint.Time = next;
        }

        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private void InsertNodeNearSelected()
    {
        if (_selectedPoint == null) return;

        List<EnvironmentalCurvePoint> series = GetSeries(_selectedSeries);
        double offset = _selectedPoint.Time > 0.5 ? -KeyboardSpacebarOffset : KeyboardSpacebarOffset;
        double t = Math.Clamp(_selectedPoint.Time + offset, 0.0, 1.0);
        if (AddPoint(series, t, _selectedPoint.Value))
        {
            _selectedPoint = FindNearestByTime(series, t);
            InvalidateVisual();
            CurveChanged?.Invoke();
        }
    }

    private void DeleteSelected()
    {
        if (_selectedPoint == null) return;

        List<EnvironmentalCurvePoint> series = GetSeries(_selectedSeries);
        if (IsEndpoint(_selectedPoint, series)) return;

        double removedTime = _selectedPoint.Time;
        series.Remove(_selectedPoint);
        _selectedPoint = PickNeighbourAfterRemoval(series, removedTime);
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private List<EnvironmentalCurvePoint> GetSeries(Series series) =>
        series == Series.Brightness ? _brightness : _nightLight;

    private static bool AddPoint(List<EnvironmentalCurvePoint> series, double t, double v)
    {
        EnvironmentalCurvePoint? near =
            series.FirstOrDefault(p => Math.Abs(p.Time - t) < NodeCreationSnapWindowDayFraction);
        if (near != null)
        {
            near.Value = v;
            SyncEdgeYIfEdge(near, series);
            return true;
        }

        series.Add(new EnvironmentalCurvePoint { Time = t, Value = v });
        return true;
    }

    private static EnvironmentalCurvePoint? FindNearestByTime(List<EnvironmentalCurvePoint> series, double t)
    {
        EnvironmentalCurvePoint? best = null;
        double bestDelta = double.PositiveInfinity;
        foreach (EnvironmentalCurvePoint point in series)
        {
            double delta = Math.Abs(point.Time - t);
            if (delta < bestDelta)
            {
                best = point;
                bestDelta = delta;
            }
        }

        return best;
    }

    private static EnvironmentalCurvePoint? PickNeighbourAfterRemoval(
        List<EnvironmentalCurvePoint> series,
        double removedTime)
    {
        if (series.Count == 0) return null;
        return series.OrderBy(p => Math.Abs(p.Time - removedTime)).FirstOrDefault();
    }

    private static bool IsEndpoint(EnvironmentalCurvePoint point, List<EnvironmentalCurvePoint> series)
    {
        if (series.Count == 0) return false;
        List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(p => p.Time)];
        return ReferenceEquals(point, ordered[0]) || ReferenceEquals(point, ordered[^1]);
    }

    private static void SyncEdgeYIfEdge(EnvironmentalCurvePoint mutated, List<EnvironmentalCurvePoint> series)
    {
        if (series.Count < 2) return;

        List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(p => p.Time)];
        EnvironmentalCurvePoint first = ordered[0];
        EnvironmentalCurvePoint last = ordered[^1];

        if (ReferenceEquals(mutated, first) && !ReferenceEquals(mutated, last))
            last.Value = mutated.Value;
        else if (ReferenceEquals(mutated, last) && !ReferenceEquals(mutated, first))
            first.Value = mutated.Value;
    }

    private static Point PointFor(EnvironmentalCurvePoint point, Rect plot) =>
        new(ScreenX(point.Time, plot), ScreenY(point.Value, plot));

    private void CapturePointer(PointerPressedEventArgs e)
    {
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
    }

    private void ReleasePointerCapture()
    {
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
    }

    private void ClearDragState(bool raiseChanged)
    {
        bool changed = _dragPoint != null || _draggingLimit;
        _dragPoint = null;
        _draggingLimit = false;
        _dragDisabledPin = null;
        ReleasePointerCapture();
        if (raiseChanged && changed) CurveChanged?.Invoke();
        InvalidateVisual();
    }
}

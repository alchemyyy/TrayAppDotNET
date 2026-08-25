using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace FanControlTrayAppDotNET.UI.Curves;

public sealed class FanCurveEditor : Control, IDisposable
{
    private readonly record struct DisplayNode(CurveNode Raw, double X, double Y);

    private static readonly bool IsSelectedReadoutAutoSwitchEnabled = false;

    private Curve? _curve;
    private DataSource? _dataSource;
    private Curve? _subscribedCurve;
    private DataSource? _subscribedDataSource;
    private FanCurveEditorPalette _palette = FanCurveEditorPalette.Default;
    private FanCurveEditorWindow.FanCurveEditorAxamlProperties? _layout;

    private CurveNode? _dragNode;
    private CurveNode? _hoverNode;
    private CurveNode? _selectedNode;
    private IPointer? _capturedPointer;
    private Point? _cursorPos;
    private bool _isAttached;
    private bool _isResettingPointerCapture;
    private bool _disposed;

    public FanCurveEditor()
    {
        Focusable = true;
        Cursor = TrayAppDotNETCursors.Arrow;
        GotFocus += (_, _) => EnsureSelectionOnFocus();
        LostFocus += (_, _) =>
        {
            _selectedNode = null;
            InvalidateVisual();
        };
    }

    public event Action? CurveChanged;

    /// <summary>
    /// Raised immediately before a user graph edit mutates curve nodes.
    /// </summary>
    public event Action? GraphEditStarting;

    /// <summary>
    /// Gets or sets graph layout values sourced from AXAML.
    /// </summary>
    internal FanCurveEditorWindow.FanCurveEditorAxamlProperties EditorLayout
    {
        get => Layout;
        set
        {
            _layout = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private FanCurveEditorWindow.FanCurveEditorAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Fan curve editor graph layout resources have not been loaded.");

    private int VerticalGridDivisions =>
        Math.Max(1, (int)Math.Round(Layout.GraphVerticalGridDivisions));

    private int HorizontalGridDivisions =>
        Math.Max(1, (int)Math.Round(Layout.GraphHorizontalGridDivisions));

    public FanCurveEditorPalette Palette
    {
        get => _palette;
        set
        {
            if (_palette == value) return;
            _palette = value;
            InvalidateVisual();
        }
    }

    public void SetCurve(Curve curve, DataSource? source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_curve, curve) && ReferenceEquals(_dataSource, source)) return;
        UnsubscribeModels();
        _curve = curve;
        _dataSource = source;
        _selectedNode = null;
        SubscribeModels();
        InvalidateVisual();
    }

    public void SetDataSource(DataSource? source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_dataSource, source)) return;
        UnsubscribeDataSource();
        _dataSource = source;
        SubscribeDataSource();
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Layout.GraphDefaultMeasureWidth : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? Layout.GraphDefaultMeasureHeight : availableSize.Height;
        return new Size(
            Math.Max(Layout.GraphMinimumMeasureWidth, width),
            Math.Max(Layout.GraphMinimumMeasureHeight, height));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed) return;
        _isAttached = true;
        SubscribeModels();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UnsubscribeModels();
        ResetPointerInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(Brushes.Transparent, bounds);
        Rect plot = PlotRect();
        if (plot.Width <= 0 || plot.Height <= 0) return;

        DrawGrid(context, plot, bounds);
        DrawMinimumBand(context, plot);
        DrawCurve(context, plot);
        DrawCurrentDataSource(context, plot);
        DrawSelectedReadout(context, plot);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || _curve == null) return;
        if (_capturedPointer != null) return;

        PointerPoint point = e.GetCurrentPoint(this);
        Point pos = e.GetPosition(this);
        Rect plot = PlotRect();
        bool insidePlot = plot.Contains(pos);

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            if (insidePlot) DeletePointAt(pos, plot);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        Focus();
        _cursorPos = insidePlot ? pos : null;
        if (!insidePlot)
        {
            UpdateHover(pos, plot);
            InvalidateVisual();
            return;
        }

        if (TryHitNode(pos, plot, out CurveNode? hit))
        {
            _dragNode = hit;
            _selectedNode = hit;
            CapturePointer(e);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        CurveNode added = AddPoint(pos, plot);
        _dragNode = added;
        _selectedNode = added;
        CapturePointer(e);
        InvalidateVisual();
        CurveChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_curve == null) return;

        Point pos = e.GetPosition(this);
        Rect plot = PlotRect();
        _cursorPos = plot.Contains(pos) ? pos : null;

        if (_dragNode != null)
        {
            if (!ReferenceEquals(_capturedPointer, e.Pointer)) return;
            DragNode(_dragNode, pos, plot);
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
        if (_dragNode == null || !ReferenceEquals(_capturedPointer, e.Pointer)) return;

        _dragNode = null;
        ReleasePointerCapture();
        CurveChanged?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isResettingPointerCapture
            || _disposed
            || !ReferenceEquals(_capturedPointer, e.Pointer))
        {
            return;
        }

        _capturedPointer = null;
        if (_dragNode == null) return;
        _dragNode = null;
        CurveChanged?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragNode != null) return;
        _cursorPos = null;
        _hoverNode = null;
        Cursor = TrayAppDotNETCursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_curve == null || _selectedNode == null || _dragNode != null) return;

        double xRange = XMaximum - XMinimum;
        double yRange = YMaximum - YMinimum;
        double xStep = xRange / Math.Max(1.0, PlotRect().Width) * Layout.GraphKeyboardStepFinePixels;
        double yStep = yRange / Math.Max(1.0, PlotRect().Height) * Layout.GraphKeyboardStepFinePixels;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            xStep *= Layout.GraphKeyboardStepCoarsePixels;
            yStep *= Layout.GraphKeyboardStepCoarsePixels;
        }

        switch (e.Key)
        {
            case Key.Tab:
                NavigateSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Left:
                MoveSelected(-xStep, 0.0);
                e.Handled = true;
                break;
            case Key.Right:
                MoveSelected(xStep, 0.0);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelected(0.0, yStep);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelected(0.0, -yStep);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                _selectedNode = null;
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    private void DrawGrid(DrawingContext context, Rect plot, Rect bounds)
    {
        int verticalGridDivisions = VerticalGridDivisions;
        int horizontalGridDivisions = HorizontalGridDivisions;
        double yAxisGutterWidth = Math.Max(0.0, plot.Left - Layout.GraphPlotInsetX);
        for (int i = 0; i <= verticalGridDivisions; i++)
        {
            double yValue = YGridValue(i);
            double y = ScreenY(yValue, plot);
            DrawLine(context, new Point(plot.Left, y), new Point(plot.Right, y),
                WithOpacity(_palette.GridLine, 0.4), 1.0);

            using TextLayout left = Text(FormatYAxisValue(yValue), Layout.GraphLabelFontSize,
                WithOpacity(_palette.SecondaryForeground, 0.75));
            left.Draw(
                context,
                new Point(yAxisGutterWidth - left.Width - Layout.GraphYAxisLabelGap, y - left.Height / 2.0));
        }

        for (int i = 0; i <= horizontalGridDivisions; i++)
        {
            double xValue = XMinimum + (XMaximum - XMinimum) * i / horizontalGridDivisions;
            double x = ScreenX(xValue, plot);
            DrawLine(context, new Point(x, plot.Top), new Point(x, plot.Bottom),
                WithOpacity(_palette.GridLine, 0.4), 1.0);

            string label = FormatAxisValue(xValue);
            string unit = _dataSource?.DisplayUnit ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(unit)) label += unit;
            using TextLayout formatted = Text(label, Layout.GraphLabelFontSize,
                WithOpacity(_palette.SecondaryForeground, 0.75));
            formatted.Draw(
                context,
                new Point(x - formatted.Width / 2.0, bounds.Height - Layout.GraphXAxisHeight + 3.0));
        }
    }

    private void DrawMinimumBand(DrawingContext context, Rect plot)
    {
        if (_curve == null) return;
        double min = Math.Clamp(_curve.ActiveYMinLine, YMinimum, YMaximum);
        if (min <= YMinimum) return;

        double y = ScreenY(min, plot);
        context.FillRectangle(Brush(_palette.DisabledBand), new Rect(plot.Left, y, plot.Width, plot.Bottom - y));
        DrawDashedLine(context, new Point(plot.Left, y), new Point(plot.Right, y),
            WithOpacity(_palette.SecondaryForeground, 0.65), 1.0, 4.0, 3.0);

        string label = $"Min {FormatYAxisValue(min)}";
        using TextLayout text = Text(
            label,
            Layout.GraphLabelFontSize,
            WithOpacity(_palette.SecondaryForeground, 0.75));
        text.Draw(context, new Point(plot.Left + 6.0, Math.Clamp(y + 3.0, plot.Top, plot.Bottom - text.Height)));
    }

    private void DrawCurve(DrawingContext context, Rect plot)
    {
        if (_curve == null || _curve.CurveNodes.Count == 0) return;

        List<DisplayNode> display = DisplayNodes();
        if (display.Count >= 2)
        {
            StreamGeometry geometry = BuildCurveGeometry(display, plot);
            context.DrawGeometry(null, new Pen(Brush(_palette.Curve), 2.0), geometry);
        }

        foreach (DisplayNode node in display)
        {
            Point center = PointFor(node, plot);
            bool active = ReferenceEquals(_hoverNode, node.Raw) || ReferenceEquals(_dragNode, node.Raw);
            bool selected = ReferenceEquals(_selectedNode, node.Raw);
            DrawThumb(context, center, _palette.Curve, active, selected);
        }
    }

    private StreamGeometry BuildCurveGeometry(List<DisplayNode> nodes, Rect plot)
    {
        double[] xs = new double[nodes.Count];
        double[] ys = new double[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            xs[i] = nodes[i].X;
            ys[i] = nodes[i].Y;
        }

        double smoothness = Math.Clamp((_curve?.SmoothingFactor ?? 0) / 100.0, 0.0, 1.0);
        double[] tangents = ComputeMonotonicTangents(xs, ys);
        int samples = Math.Max(2, (int)Math.Ceiling(plot.Width));
        StreamGeometry geometry = new();
        using StreamGeometryContext geometryContext = geometry.Open();
        for (int i = 0; i < samples; i++)
        {
            double x = XMinimum + (XMaximum - XMinimum) * i / Math.Max(1, samples - 1);
            double linear = InterpolateLinear(xs, ys, x);
            double cubic = InterpolateMonotonicCubic(xs, ys, tangents, x);
            double yValue = linear + (cubic - linear) * smoothness;
            Point current = new(ScreenX(x, plot), ScreenY(yValue, plot));
            if (i == 0)
                geometryContext.BeginFigure(current, isFilled: false);
            else
                geometryContext.LineTo(current);
        }

        return geometry;
    }

    private void DrawCurrentDataSource(DrawingContext context, Rect plot)
    {
        if (_dataSource == null) return;

        double value = _dataSource.DisplayValue;
        if (value >= XMinimum && value <= XMaximum)
        {
            double x = ScreenX(value, plot);
            DrawDashedLine(
                context,
                new Point(x, plot.Top),
                new Point(x, plot.Bottom),
                WithOpacity(_palette.CurrentValue, 0.85),
                1.25,
                3.0,
                2.0);
        }

        string unit = _dataSource.DisplayUnit;
        string label = string.IsNullOrWhiteSpace(unit)
            ? $"{_dataSource.DisplayName}  {FormatDataValue(value)}"
            : $"{_dataSource.DisplayName}  {FormatDataValue(value)} {unit}";
        using TextLayout sourceText = Text(label, 12.0, _palette.Foreground);
        using TextLayout? curveText = _curve == null
            ? null
            : Text($"Curve {FormatYAxisValue(_curve.Evaluate(value))}", 12.0, _palette.Curve);
        double bottom = plot.Bottom - Layout.GraphLegendInset;

        Rect sourcePill = GraphLegendPill(plot, sourceText, bottom);
        DrawPill(context, sourcePill, sourceText, _palette.CardBackground, _palette.CurrentValue);
        bottom = sourcePill.Top - Layout.GraphLegendGap;

        if (curveText != null)
        {
            Rect curvePill = GraphLegendPill(plot, curveText, bottom);
            DrawPill(context, curvePill, curveText, _palette.CardBackground, _palette.Curve);
        }
    }

    private void DrawSelectedReadout(DrawingContext context, Rect plot)
    {
        if (_selectedNode == null) return;
        DisplayNode? selected = null;
        foreach (DisplayNode node in DisplayNodes())
        {
            if (!ReferenceEquals(node.Raw, _selectedNode)) continue;
            selected = node;
            break;
        }

        if (selected is not { } displayNode) return;

        string xUnit = _dataSource?.DisplayUnit ?? string.Empty;
        string textValue = string.IsNullOrWhiteSpace(xUnit)
            ? $"{FormatDataValue(displayNode.X)}  {FormatYAxisValue(displayNode.Y)}"
            : $"{FormatDataValue(displayNode.X)} {xUnit}  {FormatYAxisValue(displayNode.Y)}";
        using TextLayout text = Text(textValue, 12.0, _palette.Curve, monospace: true);
        double width = text.Width + 12.0;
        double height = text.Height + 5.0;
        double x = SelectedReadoutX(plot, width);
        Rect pill = new(x, plot.Top + Layout.GraphLegendInset, width, height);
        DrawPill(context, pill, text, _palette.CardBackground, _palette.Curve);
    }

    /// <summary>
    /// Sizes and right-aligns one graph legend pill above the requested bottom edge.
    /// </summary>
    private Rect GraphLegendPill(Rect plot, TextLayout text, double bottom)
    {
        double width = text.Width + 12.0;
        double height = text.Height + 5.0;
        return new Rect(plot.Right - width - Layout.GraphLegendInset, bottom - height, width, height);
    }

    /// <summary>
    /// Places the selected-node readout, with the old auto-switch behavior kept disabled.
    /// </summary>
    private double SelectedReadoutX(Rect plot, double width)
    {
        if (!IsSelectedReadoutAutoSwitchEnabled) return plot.Left + Layout.GraphLegendInset;

        Point anchor = _cursorPos ?? new Point(plot.Right, plot.Top);
        return anchor.X > plot.Center.X
            ? plot.Left + Layout.GraphLegendInset
            : plot.Right - width - Layout.GraphLegendInset;
    }

    private CurveNode AddPoint(Point pos, Rect plot)
    {
        if (_curve == null) throw new InvalidOperationException("No curve is active.");

        double x = Math.Clamp(FromScreenX(pos.X, plot), XMinimum, XMaximum);
        double y = Math.Clamp(FromScreenY(pos.Y, plot), YMinimum, YMaximum);
        double snap = Math.Max(0.001, (XMaximum - XMinimum) * 0.01);
        CurveNode? near = _curve.CurveNodes.FirstOrDefault(n => Math.Abs(n.X - x) <= snap);
        if (near != null)
        {
            BeginGraphEdit();
            near.Y = y;
            FinishGraphEdit();
            return near;
        }

        BeginGraphEdit();
        CurveNode node = new(x, y);
        _curve.CurveNodes.Add(node);
        FinishGraphEdit();
        return node;
    }

    private void DragNode(CurveNode node, Point pos, Rect plot)
    {
        if (_curve == null) return;

        BeginGraphEdit();
        node.X = ClampNodeXToNeighbours(node, FromScreenX(pos.X, plot));
        node.Y = Math.Clamp(FromScreenY(pos.Y, plot), YMinimum, YMaximum);
        FinishGraphEdit();
    }

    private void MoveSelected(double dx, double dy)
    {
        if (_curve == null || _selectedNode == null) return;

        BeginGraphEdit();
        _selectedNode.X = ClampNodeXToNeighbours(_selectedNode, _selectedNode.X + dx);
        _selectedNode.Y = Math.Clamp(_selectedNode.Y + dy, YMinimum, YMaximum);
        FinishGraphEdit();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    /// <summary>
    /// Clamps a dragged node against its original neighbors so it cannot cross them.
    /// </summary>
    private double ClampNodeXToNeighbours(CurveNode node, double desiredX)
    {
        if (_curve == null || _curve.CurveNodes.Count < 2)
            return Math.Clamp(desiredX, XMinimum, XMaximum);

        List<CurveNode> ordered = [.. _curve.CurveNodes.OrderBy(n => n.X)];
        int index = ordered.IndexOf(node);
        if (index < 0) return Math.Clamp(desiredX, XMinimum, XMaximum);

        double gap = Math.Max(0.001, (XMaximum - XMinimum) * 0.001);
        double x = Math.Clamp(desiredX, XMinimum, XMaximum);
        if (index > 0) x = Math.Max(x, ordered[index - 1].X + gap);
        if (index < ordered.Count - 1) x = Math.Min(x, ordered[index + 1].X - gap);
        return Math.Clamp(x, XMinimum, XMaximum);
    }

    private void DeletePointAt(Point pos, Rect plot)
    {
        if (_curve == null || _curve.CurveNodes.Count <= 2) return;
        if (!TryHitNode(pos, plot, out CurveNode? hit) || hit == null) return;
        BeginGraphEdit();
        double removedX = hit.X;
        _curve.CurveNodes.Remove(hit);
        _selectedNode = PickNeighbourAfterRemoval(removedX);
        FinishGraphEdit();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private void DeleteSelected()
    {
        if (_curve == null || _selectedNode == null || _curve.CurveNodes.Count <= 2) return;
        BeginGraphEdit();
        double removedX = _selectedNode.X;
        _curve.CurveNodes.Remove(_selectedNode);
        _selectedNode = PickNeighbourAfterRemoval(removedX);
        FinishGraphEdit();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private CurveNode? PickNeighbourAfterRemoval(double removedX)
    {
        if (_curve == null || _curve.CurveNodes.Count == 0) return null;
        return _curve.CurveNodes.OrderBy(n => Math.Abs(n.X - removedX)).FirstOrDefault();
    }

    /// <summary>
    /// Notifies the owner before hidden monotonic-preservation state can become stale.
    /// </summary>
    private void BeginGraphEdit() => GraphEditStarting?.Invoke();

    /// <summary>
    /// Commits graph edits without leaving hidden non-monotonic raw values behind.
    /// </summary>
    private void FinishGraphEdit()
    {
        if (_curve == null) return;
        if (_curve.PreventDecreasing)
        {
            _curve.BurnInEffectiveNodes();
            return;
        }

        _curve.BumpVersion();
    }

    private void UpdateHover(Point pos, Rect plot)
    {
        if (!plot.Contains(pos))
        {
            _hoverNode = null;
            Cursor = TrayAppDotNETCursors.Arrow;
            return;
        }

        _hoverNode = TryHitNode(pos, plot, out CurveNode? hit) ? hit : null;
        Cursor = _hoverNode != null ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow;
    }

    private bool TryHitNode(Point pos, Rect plot, out CurveNode? hit)
    {
        List<DisplayNode> nodes = DisplayNodes();
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            Point center = PointFor(nodes[i], plot);
            double radius = Layout.GraphThumbSize / 2.0 + Layout.GraphThumbHitPadding;
            if (Math.Abs(pos.X - center.X) <= radius && Math.Abs(pos.Y - center.Y) <= radius)
            {
                hit = nodes[i].Raw;
                return true;
            }
        }

        hit = null;
        return false;
    }

    private void EnsureSelectionOnFocus()
    {
        if (_selectedNode != null || _curve == null) return;
        _selectedNode = _curve.CurveNodes.OrderBy(n => n.X).FirstOrDefault();
        InvalidateVisual();
    }

    private void NavigateSelection(int direction)
    {
        if (_curve == null) return;
        List<CurveNode> ordered = [.. _curve.CurveNodes.OrderBy(n => n.X)];
        if (ordered.Count == 0) return;

        int current = _selectedNode == null ? -1 : ordered.IndexOf(_selectedNode);
        int next = current < 0
            ? direction >= 0 ? 0 : ordered.Count - 1
            : ((current + direction) % ordered.Count + ordered.Count) % ordered.Count;
        _selectedNode = ordered[next];
        InvalidateVisual();
    }

    private List<DisplayNode> DisplayNodes()
    {
        if (_curve == null) return [];
        List<CurveNode> ordered = [.. _curve.CurveNodes.OrderBy(n => n.X)];
        List<DisplayNode> nodes = [];
        if (ordered.Count == 0) return nodes;

        CurveNode first = ordered[0];
        double floor = first.Y;
        nodes.Add(new DisplayNode(first, first.X, Math.Clamp(first.Y, YMinimum, YMaximum)));
        for (int i = 1; i < ordered.Count; i++)
        {
            CurveNode raw = ordered[i];
            double y = raw.Y;
            if (_curve.PreventDecreasing)
            {
                if (y < floor) y = floor;
                else floor = y;
            }

            nodes.Add(new DisplayNode(raw, raw.X, Math.Clamp(y, YMinimum, YMaximum)));
        }

        return nodes;
    }

    private Rect PlotRect()
    {
        double left = CalculateYAxisGutterWidth() + Layout.GraphPlotInsetX;
        double right = Math.Max(left, Bounds.Width - Layout.GraphPlotInsetX);
        double top = Layout.GraphPlotInsetY;
        double bottom = Math.Max(top, Bounds.Height - Layout.GraphXAxisHeight - Layout.GraphPlotInsetY);
        return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    /// <summary>
    /// Measures the active Y-axis labels so the plot can contract only when needed.
    /// </summary>
    private double CalculateYAxisGutterWidth()
    {
        double width = 0.0;
        int verticalGridDivisions = VerticalGridDivisions;
        for (int i = 0; i <= verticalGridDivisions; i++)
        {
            using TextLayout text = Text(
                FormatYAxisValue(YGridValue(i)),
                Layout.GraphLabelFontSize,
                Colors.Transparent);
            width = Math.Max(width, text.Width);
        }

        return Math.Ceiling(width + Layout.GraphYAxisLabelGap);
    }

    /// <summary>
    /// Calculates the value for a Y-axis grid division.
    /// </summary>
    private double YGridValue(int index) =>
        YMaximum - (YMaximum - YMinimum) * index / VerticalGridDivisions;

    private double XMinimum => _dataSource?.DisplayMinimum ?? 0.0;

    private double XMaximum => _dataSource?.DisplayMaximum ?? 100.0;

    private double YMinimum => _curve?.ActiveYMinimum ?? 0.0;

    private double YMaximum => _curve?.ActiveYMaximum ?? 100.0;

    private double ScreenX(double x, Rect plot)
    {
        double span = Math.Max(0.001, XMaximum - XMinimum);
        return plot.Left + (x - XMinimum) / span * plot.Width;
    }

    private double FromScreenX(double x, Rect plot)
    {
        double span = Math.Max(0.001, XMaximum - XMinimum);
        return XMinimum + (x - plot.Left) / Math.Max(1.0, plot.Width) * span;
    }

    private double ScreenY(double y, Rect plot)
    {
        double span = Math.Max(0.001, YMaximum - YMinimum);
        return plot.Top + (1.0 - (y - YMinimum) / span) * plot.Height;
    }

    private double FromScreenY(double y, Rect plot)
    {
        double span = Math.Max(0.001, YMaximum - YMinimum);
        return YMinimum + (1.0 - (y - plot.Top) / Math.Max(1.0, plot.Height)) * span;
    }

    private Point PointFor(DisplayNode node, Rect plot) => new(ScreenX(node.X, plot), ScreenY(node.Y, plot));

    private void DrawThumb(DrawingContext context, Point center, Color fill, bool active, bool selected)
    {
        double stroke = selected ? 1.5 : active ? 1.25 : 0.0;
        Pen? ring = stroke > 0.0 ? new Pen(Brush(_palette.Foreground), stroke) : null;
        double radius = stroke > 0.0
            ? Math.Max(0.0, Layout.GraphThumbSize / 2.0 - stroke / 2.0)
            : Layout.GraphThumbSize / 2.0;
        context.DrawEllipse(Brush(fill), ring, center, radius, radius);
    }

    private static void DrawPill(DrawingContext context, Rect rect, TextLayout text, Color background, Color border)
    {
        context.FillRectangle(Brush(WithOpacity(background, 0.90)), rect, 3);
        context.DrawRectangle(new Pen(Brush(WithOpacity(border, 0.24))), rect, 3);
        text.Draw(context, new Point(rect.X + 6.0, rect.Y + 2.0));
    }

    private static IBrush Brush(Color color) =>
        color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

    private static Color WithOpacity(Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0)), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void DrawLine(DrawingContext context, Point a, Point b, Color color, double thickness)
    {
        if (Math.Abs(thickness - 1.0) < 0.001)
        {
            if (Math.Abs(a.Y - b.Y) < 0.001)
            {
                double y = Math.Round(a.Y) + 0.5;
                a = new Point(a.X, y);
                b = new Point(b.X, y);
            }
            else if (Math.Abs(a.X - b.X) < 0.001)
            {
                double x = Math.Round(a.X) + 0.5;
                a = new Point(x, a.Y);
                b = new Point(x, b.Y);
            }
        }

        context.DrawLine(new Pen(Brush(color), thickness), a, b);
    }

    private static void DrawDashedLine(
        DrawingContext context,
        Point a,
        Point b,
        Color color,
        double thickness,
        double dash,
        double gap)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0) return;

        double ux = dx / length;
        double uy = dy / length;
        double cursor = 0.0;
        while (cursor < length)
        {
            double end = Math.Min(length, cursor + dash);
            Point p1 = new(a.X + ux * cursor, a.Y + uy * cursor);
            Point p2 = new(a.X + ux * end, a.Y + uy * end);
            DrawLine(context, p1, p2, color, thickness);
            cursor += dash + gap;
        }
    }

    private static TextLayout Text(string text, double size, Color color, bool monospace = false) =>
        new(
            text,
            new Typeface(monospace
                ? new FontFamily("Consolas, Cascadia Mono, Segoe UI")
                : new FontFamily("Segoe UI Variable, Segoe UI")),
            size,
            Brush(color),
            textWrapping: TextWrapping.NoWrap,
            maxLines: 1);

    private static string FormatAxisValue(double value)
        => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    private string FormatYAxisValue(double value)
    {
        string text = FormatAxisValue(value);
        return _curve?.ActiveYSuffix switch
        {
            "%" => $"{text}%",
            "RPM" => $"{text} RPM",
            { } suffix when !string.IsNullOrWhiteSpace(suffix) => $"{text} {suffix}",
            _ => text
        };
    }

    private static string FormatDataValue(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100 || Math.Abs(value - Math.Round(value)) < 0.001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void SubscribeModels()
    {
        SubscribeCurve();
        SubscribeDataSource();
    }

    private void UnsubscribeModels()
    {
        UnsubscribeCurve();
        UnsubscribeDataSource();
    }

    private void SubscribeCurve()
    {
        if (!_isAttached || _curve == null || ReferenceEquals(_subscribedCurve, _curve)) return;

        UnsubscribeCurve();
        _curve.PropertyChanged += OnCurvePropertyChanged;
        _subscribedCurve = _curve;
    }

    private void UnsubscribeCurve()
    {
        Curve? subscribedCurve = _subscribedCurve;
        _subscribedCurve = null;
        if (subscribedCurve != null)
            subscribedCurve.PropertyChanged -= OnCurvePropertyChanged;
    }

    private void SubscribeDataSource()
    {
        if (!_isAttached || _dataSource == null || ReferenceEquals(_subscribedDataSource, _dataSource)) return;

        UnsubscribeDataSource();
        _dataSource.PropertyChanged += OnDataSourcePropertyChanged;
        _subscribedDataSource = _dataSource;
    }

    private void UnsubscribeDataSource()
    {
        DataSource? subscribedDataSource = _subscribedDataSource;
        _subscribedDataSource = null;
        if (subscribedDataSource != null)
            subscribedDataSource.PropertyChanged -= OnDataSourcePropertyChanged;
    }

    private void OnDataSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _subscribedDataSource)
            && e.PropertyName is nameof(DataSource.Value) or nameof(DataSource.UserDefinedName))
        {
            RequestModelRedraw(sender);
        }
    }

    private void OnCurvePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _subscribedCurve))
            RequestModelRedraw(sender);
    }

    private void RequestModelRedraw(object? publisher)
    {
        if (_disposed || !_isAttached || !IsCurrentModelPublisher(publisher)) return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvalidateVisual();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _isAttached && IsCurrentModelPublisher(publisher))
                InvalidateVisual();
        });
    }

    private bool IsCurrentModelPublisher(object? publisher) =>
        ReferenceEquals(publisher, _subscribedCurve)
        || ReferenceEquals(publisher, _subscribedDataSource);

    private void CapturePointer(PointerPressedEventArgs e)
    {
        IPointer pointer = e.Pointer;
        if (_capturedPointer != null)
            throw new InvalidOperationException("The curve editor already owns a pointer capture.");

        _capturedPointer = pointer;
        try
        {
            pointer.Capture(this);
        }
        catch
        {
            Interlocked.CompareExchange(ref _capturedPointer, null, pointer);
            bool wasResetting = _isResettingPointerCapture;
            _isResettingPointerCapture = true;
            _dragNode = null;
            try
            {
                try { pointer.Capture(null); }
                catch (Exception releaseException)
                {
                    TADNLog.Log($"FanCurveEditor capture rollback failed: {releaseException.Message}");
                }
            }
            finally
            {
                _isResettingPointerCapture = wasResetting;
            }

            throw;
        }
    }

    private void ReleasePointerCapture()
    {
        IPointer? capturedPointer = Interlocked.Exchange(ref _capturedPointer, null);
        if (capturedPointer == null) return;

        try
        {
            capturedPointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FanCurveEditor pointer release failed: {exception.Message}");
        }
    }

    private void ResetPointerInteraction()
    {
        bool wasResetting = _isResettingPointerCapture;
        _isResettingPointerCapture = true;
        _dragNode = null;
        _hoverNode = null;
        _cursorPos = null;
        try
        {
            ReleasePointerCapture();
        }
        finally
        {
            _isResettingPointerCapture = wasResetting;
        }
    }

    /// <summary>Releases model subscriptions, pointer capture, and event subscribers.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isAttached = false;
        UnsubscribeModels();
        _dragNode = null;
        _hoverNode = null;
        _selectedNode = null;
        _cursorPos = null;
        CurveChanged = null;
        GraphEditStarting = null;
        ResetPointerInteraction();
        _curve = null;
        _dataSource = null;
        Cursor = null;
    }

    private static double InterpolateLinear(double[] xs, double[] ys, double x)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0) return 0.0;
        if (count == 1 || x <= xs[0]) return ys[0];
        if (x >= xs[count - 1]) return ys[count - 1];

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (xs[middle] <= x) low = middle;
            else high = middle;
        }

        double dx = xs[high] - xs[low];
        double t = dx > 0.0 ? (x - xs[low]) / dx : 0.0;
        return ys[low] + t * (ys[high] - ys[low]);
    }

    private static double[] ComputeMonotonicTangents(double[] xs, double[] ys)
    {
        int count = Math.Min(xs.Length, ys.Length);
        double[] tangents = new double[count];
        if (count < 2) return tangents;

        double[] intervals = new double[count - 1];
        double[] slopes = new double[count - 1];
        for (int i = 0; i < count - 1; i++)
        {
            intervals[i] = xs[i + 1] - xs[i];
            slopes[i] = intervals[i] > 0.0 ? (ys[i + 1] - ys[i]) / intervals[i] : 0.0;
        }

        if (count == 2)
        {
            tangents[0] = slopes[0];
            tangents[1] = slopes[0];
            return tangents;
        }

        for (int i = 1; i < count - 1; i++)
        {
            if (slopes[i - 1] == 0.0 || slopes[i] == 0.0 || slopes[i - 1] * slopes[i] < 0.0)
            {
                tangents[i] = 0.0;
                continue;
            }

            double w1 = 2.0 * intervals[i] + intervals[i - 1];
            double w2 = intervals[i] + 2.0 * intervals[i - 1];
            tangents[i] = (w1 + w2) / (w1 / slopes[i - 1] + w2 / slopes[i]);
        }

        tangents[0] = EndpointTangent(intervals[0], intervals[1], slopes[0], slopes[1]);
        tangents[count - 1] = EndpointTangent(
            intervals[count - 2],
            intervals[count - 3],
            slopes[count - 2],
            slopes[count - 3]);
        return tangents;
    }

    private static double InterpolateMonotonicCubic(
        double[] xs,
        double[] ys,
        double[] tangents,
        double x)
    {
        int count = Math.Min(Math.Min(xs.Length, ys.Length), tangents.Length);
        if (count == 0) return 0.0;
        if (count == 1 || x <= xs[0]) return ys[0];
        if (x >= xs[count - 1]) return ys[count - 1];

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (xs[middle] <= x) low = middle;
            else high = middle;
        }

        double h = xs[high] - xs[low];
        if (h <= 0.0) return ys[low];

        double t = (x - xs[low]) / h;
        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
        double h10 = t3 - 2.0 * t2 + t;
        double h01 = -2.0 * t3 + 3.0 * t2;
        double h11 = t3 - t2;

        return h00 * ys[low] +
               h10 * h * tangents[low] +
               h01 * ys[high] +
               h11 * h * tangents[high];
    }

    private static double EndpointTangent(double hEnd, double hNext, double mEnd, double mNext)
    {
        double tangent = ((2.0 * hEnd + hNext) * mEnd - hEnd * mNext) / (hEnd + hNext);
        if (tangent * mEnd <= 0.0) return 0.0;

        double cap = 3.0 * Math.Abs(mEnd);
        if (Math.Abs(tangent) > cap) return mEnd >= 0.0 ? cap : -cap;
        return tangent;
    }
}

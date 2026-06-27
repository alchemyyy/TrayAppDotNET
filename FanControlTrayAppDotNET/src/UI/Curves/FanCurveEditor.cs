using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace FanControlTrayAppDotNET.UI.Curves;

public sealed class FanCurveEditor : Control
{
    private readonly record struct DisplayNode(CurveNode Raw, double X, double Y);

    private const double AxisGutterWidth = 42.0;
    private const double XAxisHeight = 24.0;
    private const double PlotInsetX = 10.0;
    private const double PlotInsetY = 8.0;
    private const double ThumbSize = 14.0;
    private const double ThumbHitPadding = ThumbSize / 2.0;
    private const double LabelFontSize = 11.0;
    private const int VerticalGridDivisions = 4;
    private const int HorizontalGridDivisions = 5;
    private const double KeyboardStepFinePixels = 1.0;
    private const double KeyboardStepCoarsePixels = 10.0;
    private const double MinimumMeasureWidth = 360.0;
    private const double MinimumMeasureHeight = 180.0;
    private const double DefaultMeasureWidth = 540.0;
    private const double DefaultMeasureHeight = 270.0;

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private Curve? _curve;
    private DataSource? _dataSource;
    private FanCurveEditorPalette _palette = FanCurveEditorPalette.Default;

    private CurveNode? _dragNode;
    private CurveNode? _hoverNode;
    private CurveNode? _selectedNode;
    private IPointer? _capturedPointer;
    private Point? _cursorPos;
    private DispatcherTimer? _redrawTimer;

    public FanCurveEditor()
    {
        Focusable = true;
        Cursor = ArrowCursor;
        GotFocus += (_, _) => EnsureSelectionOnFocus();
        LostFocus += (_, _) =>
        {
            _selectedNode = null;
            InvalidateVisual();
        };
    }

    public event Action? CurveChanged;

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
        if (ReferenceEquals(_curve, curve) && ReferenceEquals(_dataSource, source)) return;
        UnsubscribeDataSource();
        _curve = curve;
        _dataSource = source;
        _selectedNode = null;
        SubscribeDataSource();
        InvalidateVisual();
    }

    public void SetDataSource(DataSource? source)
    {
        if (ReferenceEquals(_dataSource, source)) return;
        UnsubscribeDataSource();
        _dataSource = source;
        SubscribeDataSource();
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? DefaultMeasureWidth : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? DefaultMeasureHeight : availableSize.Height;
        return new Size(Math.Max(MinimumMeasureWidth, width), Math.Max(MinimumMeasureHeight, height));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopTimer();
        UnsubscribeDataSource();
        ReleasePointerCapture();
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
        if (_dragNode == null) return;

        _dragNode = null;
        ReleasePointerCapture();
        CurveChanged?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragNode == null) return;
        _dragNode = null;
        ReleasePointerCapture();
        CurveChanged?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragNode != null) return;
        _cursorPos = null;
        _hoverNode = null;
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_curve == null || _selectedNode == null || _dragNode != null) return;

        double xRange = XMaximum - XMinimum;
        double yRange = YMaximum - YMinimum;
        double xStep = xRange / Math.Max(1.0, PlotRect().Width) * KeyboardStepFinePixels;
        double yStep = yRange / Math.Max(1.0, PlotRect().Height) * KeyboardStepFinePixels;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            xStep *= KeyboardStepCoarsePixels;
            yStep *= KeyboardStepCoarsePixels;
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
        for (int i = 0; i <= VerticalGridDivisions; i++)
        {
            double yValue = YMaximum - ((YMaximum - YMinimum) * i / VerticalGridDivisions);
            double y = ScreenY(yValue, plot);
            DrawLine(context, new Point(plot.Left, y), new Point(plot.Right, y),
                WithOpacity(_palette.GridLine, 0.4), 1.0);

            FormattedText left = Text(FormatAxisValue(yValue), LabelFontSize,
                WithOpacity(_palette.SecondaryForeground, 0.75));
            context.DrawText(left, new Point(AxisGutterWidth - left.Width - 2.0, y - left.Height / 2.0));
        }

        for (int i = 0; i <= HorizontalGridDivisions; i++)
        {
            double xValue = XMinimum + ((XMaximum - XMinimum) * i / HorizontalGridDivisions);
            double x = ScreenX(xValue, plot);
            DrawLine(context, new Point(x, plot.Top), new Point(x, plot.Bottom),
                WithOpacity(_palette.GridLine, 0.4), 1.0);

            string label = FormatAxisValue(xValue);
            string unit = _dataSource?.DisplayUnit ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(unit)) label += unit;
            FormattedText formatted = Text(label, LabelFontSize, WithOpacity(_palette.SecondaryForeground, 0.75));
            context.DrawText(formatted, new Point(x - formatted.Width / 2.0, bounds.Height - XAxisHeight + 3.0));
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

        string label = $"Min {FormatAxisValue(min)}{_curve.ActiveYSuffix}";
        FormattedText text = Text(label, LabelFontSize, WithOpacity(_palette.SecondaryForeground, 0.75));
        context.DrawText(text, new Point(plot.Left + 6.0, Math.Clamp(y + 3.0, plot.Top, plot.Bottom - text.Height)));
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
            double x = XMinimum + ((XMaximum - XMinimum) * i / Math.Max(1, samples - 1));
            double linear = InterpolateLinear(xs, ys, x);
            double cubic = InterpolateMonotonicCubic(xs, ys, tangents, x);
            double yValue = linear + ((cubic - linear) * smoothness);
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
        FormattedText text = Text(label, 12.0, _palette.Foreground);
        double width = text.Width + 12.0;
        double height = text.Height + 5.0;
        Rect pill = new(plot.Right - width - 6.0, plot.Bottom - height - 6.0, width, height);
        DrawPill(context, pill, text, _palette.CardBackground, _palette.CurrentValue);
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
            ? $"{FormatDataValue(displayNode.X)}  {FormatAxisValue(displayNode.Y)}{_curve?.ActiveYSuffix}"
            : $"{FormatDataValue(displayNode.X)} {xUnit}  {FormatAxisValue(displayNode.Y)}{_curve?.ActiveYSuffix}";
        FormattedText text = Text(textValue, 12.0, _palette.Curve, monospace: true);
        double width = text.Width + 12.0;
        double height = text.Height + 5.0;
        Point anchor = _cursorPos ?? new Point(plot.Right, plot.Top);
        double x = anchor.X > plot.Center.X ? plot.Left + 6.0 : plot.Right - width - 6.0;
        Rect pill = new(x, plot.Top + 6.0, width, height);
        DrawPill(context, pill, text, _palette.CardBackground, _palette.Curve);
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
            near.Y = y;
            _curve.BumpVersion();
            return near;
        }

        CurveNode node = new(x, y);
        _curve.CurveNodes.Add(node);
        _curve.BumpVersion();
        return node;
    }

    private void DragNode(CurveNode node, Point pos, Rect plot)
    {
        if (_curve == null) return;

        node.X = Math.Clamp(FromScreenX(pos.X, plot), XMinimum, XMaximum);
        node.Y = Math.Clamp(FromScreenY(pos.Y, plot), YMinimum, YMaximum);
        ClampNodeXSeparation(node);
        _curve.BumpVersion();
    }

    private void MoveSelected(double dx, double dy)
    {
        if (_curve == null || _selectedNode == null) return;

        _selectedNode.X = Math.Clamp(_selectedNode.X + dx, XMinimum, XMaximum);
        _selectedNode.Y = Math.Clamp(_selectedNode.Y + dy, YMinimum, YMaximum);
        ClampNodeXSeparation(_selectedNode);
        _curve.BumpVersion();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private void ClampNodeXSeparation(CurveNode node)
    {
        if (_curve == null || _curve.CurveNodes.Count < 2) return;
        List<CurveNode> ordered = [.. _curve.CurveNodes.OrderBy(n => n.X)];
        int index = ordered.IndexOf(node);
        if (index < 0) return;

        double gap = Math.Max(0.001, (XMaximum - XMinimum) * 0.001);
        double x = node.X;
        if (index > 0) x = Math.Max(x, ordered[index - 1].X + gap);
        if (index < ordered.Count - 1) x = Math.Min(x, ordered[index + 1].X - gap);
        node.X = Math.Clamp(x, XMinimum, XMaximum);
    }

    private void DeletePointAt(Point pos, Rect plot)
    {
        if (_curve == null || _curve.CurveNodes.Count <= 2) return;
        if (!TryHitNode(pos, plot, out CurveNode? hit) || hit == null) return;
        double removedX = hit.X;
        _curve.CurveNodes.Remove(hit);
        _selectedNode = PickNeighbourAfterRemoval(removedX);
        _curve.BumpVersion();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private void DeleteSelected()
    {
        if (_curve == null || _selectedNode == null || _curve.CurveNodes.Count <= 2) return;
        double removedX = _selectedNode.X;
        _curve.CurveNodes.Remove(_selectedNode);
        _selectedNode = PickNeighbourAfterRemoval(removedX);
        _curve.BumpVersion();
        InvalidateVisual();
        CurveChanged?.Invoke();
    }

    private CurveNode? PickNeighbourAfterRemoval(double removedX)
    {
        if (_curve == null || _curve.CurveNodes.Count == 0) return null;
        return _curve.CurveNodes.OrderBy(n => Math.Abs(n.X - removedX)).FirstOrDefault();
    }

    private void UpdateHover(Point pos, Rect plot)
    {
        if (!plot.Contains(pos))
        {
            _hoverNode = null;
            Cursor = ArrowCursor;
            return;
        }

        _hoverNode = TryHitNode(pos, plot, out CurveNode? hit) ? hit : null;
        Cursor = _hoverNode != null ? HandCursor : ArrowCursor;
    }

    private bool TryHitNode(Point pos, Rect plot, out CurveNode? hit)
    {
        List<DisplayNode> nodes = DisplayNodes();
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            Point center = PointFor(nodes[i], plot);
            double radius = ThumbSize / 2.0 + ThumbHitPadding;
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
        double floor = double.NegativeInfinity;
        foreach (CurveNode raw in ordered)
        {
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
        double left = AxisGutterWidth + PlotInsetX;
        double right = Math.Max(left, Bounds.Width - PlotInsetX);
        double top = PlotInsetY;
        double bottom = Math.Max(top, Bounds.Height - XAxisHeight - PlotInsetY);
        return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    private double XMinimum => _dataSource?.DisplayMinimum ?? 0.0;

    private double XMaximum => _dataSource?.DisplayMaximum ?? 100.0;

    private double YMinimum => _curve?.ActiveYMinimum ?? 0.0;

    private double YMaximum => _curve?.ActiveYMaximum ?? 100.0;

    private double ScreenX(double x, Rect plot)
    {
        double span = Math.Max(0.001, XMaximum - XMinimum);
        return plot.Left + ((x - XMinimum) / span * plot.Width);
    }

    private double FromScreenX(double x, Rect plot)
    {
        double span = Math.Max(0.001, XMaximum - XMinimum);
        return XMinimum + ((x - plot.Left) / Math.Max(1.0, plot.Width) * span);
    }

    private double ScreenY(double y, Rect plot)
    {
        double span = Math.Max(0.001, YMaximum - YMinimum);
        return plot.Top + ((1.0 - (y - YMinimum) / span) * plot.Height);
    }

    private double FromScreenY(double y, Rect plot)
    {
        double span = Math.Max(0.001, YMaximum - YMinimum);
        return YMinimum + ((1.0 - (y - plot.Top) / Math.Max(1.0, plot.Height)) * span);
    }

    private Point PointFor(DisplayNode node, Rect plot) => new(ScreenX(node.X, plot), ScreenY(node.Y, plot));

    private void DrawThumb(DrawingContext context, Point center, Color fill, bool active, bool selected)
    {
        double stroke = selected ? 1.5 : active ? 1.25 : 0.0;
        Pen? ring = stroke > 0.0 ? new Pen(Brush(_palette.Foreground), stroke) : null;
        double radius = stroke > 0.0
            ? Math.Max(0.0, ThumbSize / 2.0 - stroke / 2.0)
            : ThumbSize / 2.0;
        context.DrawEllipse(Brush(fill), ring, center, radius, radius);
    }

    private static void DrawPill(DrawingContext context, Rect rect, FormattedText text, Color background, Color border)
    {
        context.FillRectangle(Brush(WithOpacity(background, 0.90)), rect, 3);
        context.DrawRectangle(new Pen(Brush(WithOpacity(border, 0.24))), rect, 3);
        context.DrawText(text, new Point(rect.X + 6.0, rect.Y + 2.0));
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

    private static FormattedText Text(string text, double size, Color color, bool monospace = false) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(monospace
                ? new FontFamily("Consolas, Cascadia Mono, Segoe UI")
                : new FontFamily("Segoe UI Variable, Segoe UI")),
            size,
            Brush(color));

    private static string FormatAxisValue(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100 || Math.Abs(value - Math.Round(value)) < 0.001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatDataValue(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100 || Math.Abs(value - Math.Round(value)) < 0.001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void SubscribeDataSource()
    {
        if (_dataSource != null)
            _dataSource.PropertyChanged += OnDataSourcePropertyChanged;
    }

    private void UnsubscribeDataSource()
    {
        if (_dataSource != null)
            _dataSource.PropertyChanged -= OnDataSourcePropertyChanged;
    }

    private void OnDataSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DataSource.Value) or nameof(DataSource.UserDefinedName))
            Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void StartTimer()
    {
        if (_redrawTimer != null) return;
        _redrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimeConstants.LHMPollIntervalMs) };
        _redrawTimer.Tick += (_, _) => InvalidateVisual();
        _redrawTimer.Start();
    }

    private void StopTimer()
    {
        if (_redrawTimer == null) return;
        _redrawTimer.Stop();
        _redrawTimer = null;
    }

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

    private static double InterpolateLinear(double[] xs, double[] ys, double x)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0) return 0.0;
        if (count == 1) return ys[0];
        if (x <= xs[0]) return ys[0];
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
        return ys[low] + (t * (ys[high] - ys[low]));
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

            double w1 = (2.0 * intervals[i]) + intervals[i - 1];
            double w2 = intervals[i] + (2.0 * intervals[i - 1]);
            tangents[i] = (w1 + w2) / ((w1 / slopes[i - 1]) + (w2 / slopes[i]));
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
        if (count == 1) return ys[0];
        if (x <= xs[0]) return ys[0];
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
        double h00 = (2.0 * t3) - (3.0 * t2) + 1.0;
        double h10 = t3 - (2.0 * t2) + t;
        double h01 = (-2.0 * t3) + (3.0 * t2);
        double h11 = t3 - t2;

        return (h00 * ys[low]) +
               (h10 * h * tangents[low]) +
               (h01 * ys[high]) +
               (h11 * h * tangents[high]);
    }

    private static double EndpointTangent(double hEnd, double hNext, double mEnd, double mNext)
    {
        double tangent = (((2.0 * hEnd) + hNext) * mEnd - (hEnd * mNext)) / (hEnd + hNext);
        if (tangent * mEnd <= 0.0) return 0.0;

        double cap = 3.0 * Math.Abs(mEnd);
        if (Math.Abs(tangent) > cap) return mEnd >= 0.0 ? cap : -cap;
        return tangent;
    }
}

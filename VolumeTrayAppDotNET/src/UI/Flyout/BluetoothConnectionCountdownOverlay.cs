using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI;

namespace VolumeTrayAppDotNET.UI.Flyout;

/// <summary>
/// Draws the remaining reconnect window as a circular pie that is eaten clockwise. Frames come from the window's
/// render loop and stop while the window is hidden; with animations off the pie steps once a second.
/// </summary>
internal sealed class BluetoothConnectionCountdownOverlay : Control, IDisposable
{
    private const double FullCircleRadians = Math.PI * 2;
    private const double TopRadians = -Math.PI / 2;

    // One pie geometry per degree; frames that land on the same step neither redraw nor allocate
    internal const int PieSteps = 360;

    private readonly long _deadlineMilliseconds;
    private readonly int _timeoutMilliseconds;
    private readonly IImmutableSolidColorBrush _fillBrush;
    private readonly ImmutablePen _outlinePen;
    private readonly Action<TimeSpan> _onAnimationFrame;
    private DispatcherTimer? _steppedTimer;
    private TopLevel? _topLevel;
    private StreamGeometry? _pie;
    private int _pieStep = -1;
    private double _pieRadius;
    private int _renderedStep = -1;
    private bool _animating;
    private bool _framePending;
    private bool _disposed;

    public BluetoothConnectionCountdownOverlay(
        long deadlineMilliseconds,
        int timeoutMilliseconds,
        Color color,
        double size,
        double opacity,
        double strokeThickness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegative(opacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, other: 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strokeThickness);

        _deadlineMilliseconds = deadlineMilliseconds;
        _timeoutMilliseconds = timeoutMilliseconds;
        _fillBrush = new ImmutableSolidColorBrush(color);
        _outlinePen = new ImmutablePen(_fillBrush, strokeThickness);
        _onAnimationFrame = OnAnimationFrame;

        Width = size;
        Height = size;
        Opacity = opacity;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    /// <summary>Returns the clamped fraction of the observation window remaining.</summary>
    internal static double ResolveRemainingFraction(
        long deadlineMilliseconds,
        long nowMilliseconds,
        int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0) return 0;
        long remainingMilliseconds = deadlineMilliseconds - nowMilliseconds;
        return Math.Clamp((double)remainingMilliseconds / timeoutMilliseconds, min: 0, max: 1);
    }

    /// <summary>Quantizes a remaining fraction to the pie step drawn for it; a sliver left still draws.</summary>
    internal static int ResolvePieStep(double remainingFraction) =>
        (int)Math.Ceiling(Math.Clamp(remainingFraction, min: 0, max: 1) * PieSteps);

    private int CurrentStep() => ResolvePieStep(
        ResolveRemainingFraction(_deadlineMilliseconds, Environment.TickCount64, _timeoutMilliseconds));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null) _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        StartFrames();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopFrames();
        if (_topLevel != null) _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // A hidden warm flyout keeps this control attached, so its frames follow the window's visibility
    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty) return;
        if (_topLevel?.IsVisible == true) StartFrames();
        else StopFrames();
    }

    private void StartFrames()
    {
        if (_disposed || _topLevel is not { IsVisible: true } topLevel) return;

        InvalidateVisual();
        if (Environment.TickCount64 >= _deadlineMilliseconds) return;

        if (TrayAppDotNETAnimationPolicy.ResolveAnimationsEnabled(TrayAppDotNETAnimationPolicy.Mode))
        {
            _animating = true;
            if (_framePending) return;
            _framePending = true;
            topLevel.RequestAnimationFrame(_onAnimationFrame);
            return;
        }

        if (_steppedTimer != null) return;
        _steppedTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _steppedTimer.Tick += OnSteppedTick;
        _steppedTimer.Start();
    }

    private void StopFrames()
    {
        // A requested frame still arrives once; the callback sees the cleared flag and stops there
        _animating = false;
        if (_steppedTimer == null) return;

        _steppedTimer.Stop();
        _steppedTimer.Tick -= OnSteppedTick;
        _steppedTimer = null;
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        _framePending = false;
        if (!_animating || _disposed || _topLevel is not { IsVisible: true } topLevel)
        {
            _animating = false;
            return;
        }

        if (CurrentStep() != _renderedStep) InvalidateVisual();
        if (Environment.TickCount64 >= _deadlineMilliseconds)
        {
            _animating = false;
            return;
        }

        _framePending = true;
        topLevel.RequestAnimationFrame(_onAnimationFrame);
    }

    private void OnSteppedTick(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (Environment.TickCount64 >= _deadlineMilliseconds) StopFrames();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Math.Max(val1: 0, Bounds.Width);
        double height = Math.Max(val1: 0, Bounds.Height);
        double radius = Math.Max(val1: 0, Math.Min(width, height) / 2 - _outlinePen.Thickness / 2);
        if (radius <= 0) return;

        Point center = new(width / 2, height / 2);
        int step = CurrentStep();
        _renderedStep = step;

        if (step >= PieSteps)
        {
            context.DrawEllipse(_fillBrush, _outlinePen, center, radius, radius);
            return;
        }

        if (step > 0)
            context.DrawGeometry(_fillBrush, pen: null, ResolvePie(step, center, radius));

        context.DrawEllipse(brush: null, _outlinePen, center, radius, radius);
    }

    private StreamGeometry ResolvePie(int step, Point center, double radius)
    {
        if (_pie != null && _pieStep == step && _pieRadius.Equals(radius)) return _pie;

        double remainingFraction = (double)step / PieSteps;
        double startRadians = TopRadians + (1 - remainingFraction) * FullCircleRadians;
        Point start = new(
            center.X + Math.Cos(startRadians) * radius,
            center.Y + Math.Sin(startRadians) * radius);
        Point top = new(center.X, center.Y - radius);

        StreamGeometry pie = new();
        using (StreamGeometryContext geometryContext = pie.Open())
        {
            geometryContext.BeginFigure(center, isFilled: true);
            geometryContext.LineTo(start, isStroked: false);
            geometryContext.ArcTo(
                top,
                new Size(radius, radius),
                rotationAngle: 0,
                remainingFraction > 0.5,
                SweepDirection.Clockwise,
                isStroked: false);
            geometryContext.LineTo(center, isStroked: false);
            geometryContext.EndFigure(true);
        }

        _pie = pie;
        _pieStep = step;
        _pieRadius = radius;
        return pie;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopFrames();
    }
}

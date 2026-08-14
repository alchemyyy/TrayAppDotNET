using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace VolumeTrayAppDotNET.UI.Flyout;

/// <summary>Draws the remaining reconnect window as a circular pie that is eaten clockwise.</summary>
internal sealed class BluetoothConnectionCountdownOverlay : Control, IDisposable
{
    private const double FullCircleRadians = Math.PI * 2;
    private const double TopRadians = -Math.PI / 2;
    private const double FullCircleThreshold = 0.999_999;

    private readonly long _deadlineMilliseconds;
    private readonly int _timeoutMilliseconds;
    private readonly SolidColorBrush _fillBrush;
    private readonly Pen _outlinePen;
    private readonly DispatcherTimer _animationTimer;
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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strokeThickness);

        _deadlineMilliseconds = deadlineMilliseconds;
        _timeoutMilliseconds = timeoutMilliseconds;
        _fillBrush = new SolidColorBrush(color);
        _outlinePen = new Pen(_fillBrush, strokeThickness);

        Width = size;
        Height = size;
        Opacity = opacity;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        ClipToBounds = false;

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.BluetoothConnectionAnimationIntervalMs)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    /// <summary>Returns the clamped fraction of the observation window remaining.</summary>
    internal static double ResolveRemainingFraction(
        long deadlineMilliseconds,
        long nowMilliseconds,
        int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0) return 0;
        long remainingMilliseconds = deadlineMilliseconds - nowMilliseconds;
        return Math.Clamp((double)remainingMilliseconds / timeoutMilliseconds, 0, 1);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Math.Max(0, Bounds.Width);
        double height = Math.Max(0, Bounds.Height);
        double radius = Math.Max(0, Math.Min(width, height) / 2 - _outlinePen.Thickness / 2);
        if (radius <= 0) return;

        Point center = new(width / 2, height / 2);
        double remainingFraction = ResolveRemainingFraction(
            _deadlineMilliseconds,
            Environment.TickCount64,
            _timeoutMilliseconds);

        if (remainingFraction >= FullCircleThreshold)
        {
            context.DrawEllipse(_fillBrush, _outlinePen, center, radius, radius);
            return;
        }

        if (remainingFraction > 0)
        {
            double eatenRadians = (1 - remainingFraction) * FullCircleRadians;
            double startRadians = TopRadians + eatenRadians;
            Point start = new(
                center.X + Math.Cos(startRadians) * radius,
                center.Y + Math.Sin(startRadians) * radius);
            Point top = new(center.X, center.Y - radius);

            StreamGeometry remainingPie = new();
            using (StreamGeometryContext geometryContext = remainingPie.Open())
            {
                geometryContext.BeginFigure(center, isFilled: true);
                geometryContext.LineTo(start, isStroked: false);
                geometryContext.ArcTo(
                    top,
                    new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: remainingFraction > 0.5,
                    SweepDirection.Clockwise,
                    isStroked: false);
                geometryContext.LineTo(center, isStroked: false);
                geometryContext.EndFigure(isClosed: true);
            }

            context.DrawGeometry(_fillBrush, null, remainingPie);
        }

        context.DrawEllipse(Brushes.Transparent, _outlinePen, center, radius, radius);
    }

    private void OnAnimationTick(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        InvalidateVisual();
        if (Environment.TickCount64 < _deadlineMilliseconds) return;

        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
    }
}

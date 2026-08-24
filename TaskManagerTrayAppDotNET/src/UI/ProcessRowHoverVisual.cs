using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Moves one GPU-composited row highlight while discarding superseded pointer positions.</summary>
internal sealed class ProcessRowHoverVisual : Control, IDisposable
{
    private readonly Color _color;
    private readonly Action<TimeSpan> _applyPendingFrame;
    private CompositionSolidColorVisual? _compositionVisual;
    private double? _pendingRowTop;
    private double _rowHeight;
    private bool _frameRequested;
    private bool _disposed;

    public ProcessRowHoverVisual(Color color, double rowHeight)
    {
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));

        _color = color;
        _rowHeight = rowHeight;
        _applyPendingFrame = ApplyPendingFrame;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    /// <summary>Queues only the newest row position for the next compositor frame.</summary>
    public void SetRowTop(double? rowTop)
    {
        if (_disposed) return;
        if (rowTop.HasValue && !double.IsFinite(rowTop.Value))
            throw new ArgumentOutOfRangeException(nameof(rowTop));

        _pendingRowTop = rowTop;
        QueueFrame();
    }

    /// <summary>Updates the compositor rectangle height without rebuilding table drawings.</summary>
    public void SetRowHeight(double rowHeight)
    {
        if (_disposed) return;
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (Math.Abs(_rowHeight - rowHeight) < 0.01) return;

        _rowHeight = rowHeight;
        UpdateCompositionSize(Bounds.Size);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (_disposed) return;

        CompositionVisual? hostVisual = ElementComposition.GetElementVisual(this);
        if (hostVisual == null) return;

        CompositionSolidColorVisual compositionVisual = hostVisual.Compositor.CreateSolidColorVisual();
        compositionVisual.Color = _color;
        compositionVisual.Visible = false;
        _compositionVisual = compositionVisual;
        ElementComposition.SetElementChildVisual(this, compositionVisual);
        UpdateCompositionSize(Bounds.Size);
        QueueFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
        _frameRequested = false;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        UpdateCompositionSize(arrangedSize);
        return arrangedSize;
    }

    private void QueueFrame()
    {
        if (_disposed || _frameRequested || _compositionVisual == null) return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        _frameRequested = true;
        topLevel.RequestAnimationFrame(_applyPendingFrame);
    }

    private void ApplyPendingFrame(TimeSpan timestamp)
    {
        _frameRequested = false;
        if (_disposed || _compositionVisual == null) return;

        double? rowTop = _pendingRowTop;
        if (!rowTop.HasValue)
        {
            _compositionVisual.Visible = false;
            return;
        }

        _compositionVisual.Offset = new Vector3D(0, rowTop.Value, 0);
        _compositionVisual.Visible = true;
    }

    private void UpdateCompositionSize(Size size)
    {
        if (_compositionVisual == null) return;

        _compositionVisual.Size = new Vector(Math.Max(0, size.Width), _rowHeight);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _frameRequested = false;
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Moves one GPU-composited row highlight without invalidating table drawings.</summary>
internal sealed class ProcessRowHoverVisual : Control, IDisposable
{
    private readonly Color _color;
    private CompositionSolidColorVisual? _compositionVisual;
    private double? _rowTop;
    private double _rowHeight;
    private bool _disposed;

    public ProcessRowHoverVisual(Color color, double rowHeight)
    {
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));

        _color = color;
        _rowHeight = rowHeight;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    /// <summary>Applies a row position directly to the compositor-owned highlight.</summary>
    public void SetRowTop(double? rowTop)
    {
        if (_disposed) return;
        if (rowTop.HasValue && !double.IsFinite(rowTop.Value))
            throw new ArgumentOutOfRangeException(nameof(rowTop));
        if (_rowTop == rowTop) return;

        _rowTop = rowTop;
        ApplyRowTop();
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
        ApplyRowTop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        UpdateCompositionSize(arrangedSize);
        return arrangedSize;
    }

    private void ApplyRowTop()
    {
        if (_disposed || _compositionVisual == null) return;

        double? rowTop = _rowTop;
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
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }
}

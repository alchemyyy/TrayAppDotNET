using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts one GPU-composited highlight without invalidating retained table drawings.</summary>
internal abstract class ProcessTableHighlightVisual : Control, IDisposable
{
    private readonly Color _color;
    private CompositionSolidColorVisual? _compositionVisual;
    private bool _disposed;

    protected ProcessTableHighlightVisual(Color color)
    {
        _color = color;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    protected bool IsDisposed => _disposed;

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
        ApplyHighlight(Bounds.Size);
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
        ApplyHighlight(arrangedSize);
        return arrangedSize;
    }

    protected void UpdateHighlight()
    {
        if (!_disposed) ApplyHighlight(Bounds.Size);
    }

    protected abstract Rect? ResolveHighlightBounds(Size hostSize);

    private void ApplyHighlight(Size hostSize)
    {
        if (_disposed || _compositionVisual == null) return;

        Rect? highlightBounds = ResolveHighlightBounds(hostSize);
        if (!highlightBounds.HasValue
            || highlightBounds.Value.Width <= 0
            || highlightBounds.Value.Height <= 0)
        {
            _compositionVisual.Visible = false;
            return;
        }

        Rect bounds = highlightBounds.Value;
        _compositionVisual.Offset = new Vector3D(bounds.X, bounds.Y, 0);
        _compositionVisual.Size = new Vector(bounds.Width, bounds.Height);
        _compositionVisual.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        ElementComposition.SetElementChildVisual(this, null);
        _compositionVisual = null;
    }
}

/// <summary>Moves the full-width process row hover band on the composition thread.</summary>
internal sealed class ProcessRowHoverVisual : ProcessTableHighlightVisual
{
    private double? _rowTop;
    private double _rowHeight;

    public ProcessRowHoverVisual(Color color, double rowHeight)
        : base(color)
    {
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));

        _rowHeight = rowHeight;
    }

    /// <summary>Applies a row position directly to the compositor-owned highlight.</summary>
    public void SetRowTop(double? rowTop)
    {
        if (IsDisposed) return;
        if (rowTop.HasValue && !double.IsFinite(rowTop.Value))
            throw new ArgumentOutOfRangeException(nameof(rowTop));
        if (_rowTop == rowTop) return;

        _rowTop = rowTop;
        UpdateHighlight();
    }

    /// <summary>Updates the compositor rectangle height without rebuilding table drawings.</summary>
    public void SetRowHeight(double rowHeight)
    {
        if (IsDisposed) return;
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (Math.Abs(_rowHeight - rowHeight) < 0.01) return;

        _rowHeight = rowHeight;
        UpdateHighlight();
    }

    protected override Rect? ResolveHighlightBounds(Size hostSize) => _rowTop.HasValue
        ? new Rect(0, _rowTop.Value, Math.Max(0, hostSize.Width), _rowHeight)
        : null;
}

/// <summary>Moves the hovered header-cell rectangle without invalidating header or row drawings.</summary>
internal sealed class ProcessHeaderHoverVisual(Color color) : ProcessTableHighlightVisual(color)
{
    private Rect? _highlightBounds;

    /// <summary>Applies the current header cell bounds directly to the compositor.</summary>
    public void SetHighlightBounds(Rect? highlightBounds)
    {
        if (IsDisposed || _highlightBounds == highlightBounds) return;
        if (highlightBounds.HasValue)
        {
            Rect bounds = highlightBounds.Value;
            if (!double.IsFinite(bounds.X)
                || !double.IsFinite(bounds.Y)
                || !double.IsFinite(bounds.Width)
                || !double.IsFinite(bounds.Height)
                || bounds.Width < 0
                || bounds.Height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(highlightBounds));
            }
        }

        _highlightBounds = highlightBounds;
        UpdateHighlight();
    }

    protected override Rect? ResolveHighlightBounds(Size hostSize) => _highlightBounds;
}

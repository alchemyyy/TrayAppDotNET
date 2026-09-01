using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Calculates caption avoidance without changing the normal search alignment.</summary>
internal static class TaskManagerSearchOverlayGeometry
{
    public static double CalculateHorizontalOffset(
        double overlayWidth,
        double unshiftedSearchRight,
        double captionButtonAreaWidth,
        double spacing)
    {
        if (!double.IsFinite(overlayWidth)
            || !double.IsFinite(unshiftedSearchRight)
            || overlayWidth <= 0)
            return 0;

        double normalizedCaptionButtonAreaWidth =
            double.IsFinite(captionButtonAreaWidth) && captionButtonAreaWidth > 0
                ? captionButtonAreaWidth
                : 0;
        double normalizedSpacing = double.IsFinite(spacing) && spacing > 0 ? spacing : 0;
        double reservedWidth = normalizedCaptionButtonAreaWidth + normalizedSpacing;
        double maximumSearchRight = overlayWidth - reservedWidth;
        return Math.Min(val1: 0, maximumSearchRight - unshiftedSearchRight);
    }
}

/// <summary>Hosts a search surface and shifts it only when it would intersect caption buttons.</summary>
internal sealed class TaskManagerSearchOverlay : Grid, IDisposable
{
    private const double PositionTolerance = 0.01;

    private readonly Control _searchBox;
    private readonly Grid _positioner;
    private readonly TranslateTransform _captionAvoidanceTransform = new();
    private double _captionButtonAreaWidth;
    private double _captionSpacing;
    private bool _disposed;

    public TaskManagerSearchOverlay(
        Control searchContent,
        Control searchBox,
        bool leftAligned,
        Thickness margin,
        double captionSpacing)
    {
        ArgumentNullException.ThrowIfNull(searchContent);
        ArgumentNullException.ThrowIfNull(searchBox);

        _searchBox = searchBox;
        _captionSpacing = captionSpacing;
        _positioner = new Grid
        {
            HorizontalAlignment = leftAligned ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = margin,
            RenderTransform = _captionAvoidanceTransform,
            Children = { searchContent }
        };
        Children.Add(_positioner);
        _searchBox.LayoutUpdated += OnSearchBoxLayoutUpdated;
    }

    /// <summary>Adds non-measuring overlay content such as an Avalonia popup.</summary>
    public void AddOverlay(Control overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        Children.Add(overlay);
    }

    /// <summary>Updates the live caption exclusion width supplied by the owning window shell.</summary>
    public void SetCaptionButtonAreaWidth(double width)
    {
        _captionButtonAreaWidth = width;
        UpdateCaptionAvoidance();
    }

#if DEBUG
    /// <summary>Applies hot-reloaded search positioning resources without replacing the live TextBox.</summary>
    public void ApplyAXAMLResources(Thickness margin, double captionSpacing)
    {
        _positioner.Margin = margin;
        _captionSpacing = captionSpacing;
        UpdateCaptionAvoidance();
    }
#endif

    private void OnSearchBoxLayoutUpdated(object? sender, EventArgs eventArgs) =>
        UpdateCaptionAvoidance();

    private void UpdateCaptionAvoidance()
    {
        if (_disposed || _searchBox.Bounds.Width <= 0 || Bounds.Width <= 0) return;

        Point? translatedRight = _searchBox.TranslatePoint(
            new Point(_searchBox.Bounds.Width, y: 0),
            this);
        if (!translatedRight.HasValue) return;

        double currentOffset = _captionAvoidanceTransform.X;
        double unshiftedSearchRight = translatedRight.Value.X - currentOffset;
        double nextOffset = TaskManagerSearchOverlayGeometry.CalculateHorizontalOffset(
            Bounds.Width,
            unshiftedSearchRight,
            _captionButtonAreaWidth,
            _captionSpacing);
        if (Math.Abs(currentOffset - nextOffset) <= PositionTolerance) return;

        _captionAvoidanceTransform.X = nextOffset;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _searchBox.LayoutUpdated -= OnSearchBoxLayoutUpdated;
    }
}

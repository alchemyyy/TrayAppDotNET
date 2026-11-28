using Avalonia;
using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// Lays out the prominent statistics around the metadata column so a narrow window does not leave a tall empty
/// space beside them.
/// </summary>
/// <remarks>
/// The metadata sits at the top right at the width its longest pair needs. Prominent statistics flow left to right
/// in the width beside it, and once their rows pass its bottom they use the full width. When not even one prominent
/// statistic fits beside the metadata, the metadata moves below them instead.
/// </remarks>
internal sealed class PerformanceStatisticsPanel : Panel
{
    private readonly Control _metadata;
    private readonly double _columnSpacing;
    private readonly double _rowSpacing;
    private readonly double _itemWidthStep;
    private readonly List<Rect> _itemBounds = [];
    private Rect _metadataBounds;

    /// <param name="itemWidthStep">
    /// Every prominent statistic takes the width of the widest one, rounded up to this step, so the columns line up
    /// without a value's few-pixel changes moving them every sample.
    /// </param>
    public PerformanceStatisticsPanel(Control metadata, double columnSpacing, double rowSpacing, double itemWidthStep)
    {
        _metadata = metadata;
        _columnSpacing = columnSpacing;
        _rowSpacing = rowSpacing;
        _itemWidthStep = itemWidthStep;
        Children.Add(metadata);
    }

    /// <summary>Appends one prominent statistic after the ones already shown.</summary>
    public void AddPrimary(Control item) => Children.Add(item);

    /// <summary>Removes every prominent statistic, keeping the metadata.</summary>
    public void ClearPrimary()
    {
        for (int index = Children.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(Children[index], _metadata)) Children.RemoveAt(index);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _metadata.Measure(Size.Infinity);
        Size metadataSize = _metadata.DesiredSize;
        List<Size> itemSizes = new(Children.Count);
        double itemWidth = 0;
        foreach (Control child in Children)
        {
            if (ReferenceEquals(child, _metadata)) continue;
            child.Measure(Size.Infinity);
            itemSizes.Add(child.DesiredSize);
            itemWidth = Math.Max(itemWidth, child.DesiredSize.Width);
        }

        itemWidth = RoundUpItemWidth(itemWidth, _itemWidthStep);
        for (int index = 0; index < itemSizes.Count; index++)
            itemSizes[index] = itemSizes[index].WithWidth(itemWidth);

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : SumWidths(itemSizes);
        double height = Layout(
            itemSizes,
            metadataSize,
            width,
            _columnSpacing,
            _rowSpacing,
            _itemBounds,
            out _metadataBounds);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int itemIndex = 0;
        foreach (Control child in Children)
        {
            if (ReferenceEquals(child, _metadata))
                child.Arrange(_metadataBounds);
            else if (itemIndex < _itemBounds.Count)
                child.Arrange(_itemBounds[itemIndex++]);
        }

        return finalSize;
    }

    /// <summary>Places the prominent statistics and the metadata; returns the height they take.</summary>
    internal static double Layout(
        IReadOnlyList<Size> itemSizes,
        Size metadataSize,
        double width,
        double columnSpacing,
        double rowSpacing,
        List<Rect> itemBounds,
        out Rect metadataBounds)
    {
        itemBounds.Clear();
        double besideWidth = width - metadataSize.Width - columnSpacing;
        bool hasRoomBeside = itemSizes.Count > 0 && besideWidth >= itemSizes[0].Width;
        if (!hasRoomBeside)
        {
            // Nothing fits beside the metadata, so it goes below the prominent statistics
            double itemsHeight = Flow(itemSizes, width, obstacleBottom: 0, obstacleWidth: 0, itemBounds);
            double metadataTop = itemsHeight > 0 && metadataSize.Height > 0 ? itemsHeight + rowSpacing : itemsHeight;
            metadataBounds = new Rect(0, metadataTop, Math.Min(metadataSize.Width, width), metadataSize.Height);
            return metadataTop + metadataSize.Height;
        }

        metadataBounds = new Rect(width - metadataSize.Width, 0, metadataSize.Width, metadataSize.Height);
        double height = Flow(
            itemSizes,
            width,
            metadataSize.Height,
            metadataSize.Width + columnSpacing,
            itemBounds);
        return Math.Max(height, metadataSize.Height);
    }

    /// <summary>
    /// Flows items into rows left to right. Rows that start above <paramref name="obstacleBottom"/> end where the
    /// obstacle on the right begins.
    /// </summary>
    private static double Flow(
        IReadOnlyList<Size> itemSizes,
        double width,
        double obstacleBottom,
        double obstacleWidth,
        List<Rect> itemBounds)
    {
        double y = 0;
        int index = 0;
        while (index < itemSizes.Count)
        {
            double rowLimit = y < obstacleBottom ? width - obstacleWidth : width;
            double x = 0;
            double rowHeight = 0;
            int rowStart = index;
            while (index < itemSizes.Count)
            {
                Size size = itemSizes[index];
                if (index > rowStart && x + size.Width > rowLimit) break;

                itemBounds.Add(new Rect(x, y, size.Width, size.Height));
                x += size.Width;
                rowHeight = Math.Max(rowHeight, size.Height);
                index++;
            }

            y += rowHeight;
        }

        return y;
    }

    private static double SumWidths(IReadOnlyList<Size> sizes)
    {
        double width = 0;
        foreach (Size size in sizes)
            width += size.Width;
        return width;
    }

    /// <summary>Rounds a width up to the next multiple of <paramref name="step"/>; a step of 0 leaves it as is.</summary>
    internal static double RoundUpItemWidth(double width, double step) =>
        step > 0 ? Math.Ceiling(width / step) * step : width;
}

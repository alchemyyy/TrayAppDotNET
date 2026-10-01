namespace TaskManagerTrayAppDotNET.UI.ProcessesPage;

internal readonly record struct ProcessTableMetrics(
    double HeaderHeight,
    double RowHeight,
    double RowTextHeight,
    double CellPadding,
    double FontSize,
    double HeaderFontSize,
    double ProcessIconSize,
    double ProcessIconGap,
    double StatusGlyphSize);

internal readonly record struct ProcessTableColumn(
    ProcessTableColumnKind Kind,
    string Title,
    double Left,
    double Width,
    ProcessTableColumnAlignment Alignment)
{
    public double Right => Left + Width;
}

/// <summary>Provides Processes-specific column geometry.</summary>
internal static class ProcessTableLayout
{
    public static ProcessTableColumn[] CreateDefaultLayout()
    {
        List<ProcessTableColumn> columns = [];
        double left = 0;
        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
        {
            if (!definition.DefaultVisible) continue;

            columns.Add(new ProcessTableColumn(
                definition.Kind,
                definition.Title,
                left,
                definition.DefaultWidth,
                definition.Alignment));
            left += definition.DefaultWidth;
        }

        return columns.ToArray();
    }

    /// <summary>Scales process icons by the smaller text or row zoom factor.</summary>
    public static double ScaleProcessIconSize(
        double baseIconSize,
        double baseFontSize,
        double baseRowHeight,
        double fontSize,
        double rowHeight)
    {
        if (!double.IsFinite(baseIconSize) || baseIconSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseIconSize));
        if (!double.IsFinite(baseFontSize) || baseFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseFontSize));
        if (!double.IsFinite(baseRowHeight) || baseRowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseRowHeight));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowHeight));

        double zoomScale = Math.Min(fontSize / baseFontSize, rowHeight / baseRowHeight);
        return baseIconSize * zoomScale;
    }

    public static int HitTestColumn(double x, ProcessTableColumn[] columns)
    {
        if (!double.IsFinite(x) || x < 0) return -1;

        int lowerBound = 0;
        int upperBound = columns.Length - 1;
        while (lowerBound <= upperBound)
        {
            int columnIndex = lowerBound + (upperBound - lowerBound) / 2;
            ProcessTableColumn column = columns[columnIndex];
            if (x < column.Left)
            {
                upperBound = columnIndex - 1;
                continue;
            }

            if (x >= column.Right)
            {
                lowerBound = columnIndex + 1;
                continue;
            }

            return columnIndex;
        }

        return -1;
    }

    public static int HitTestColumnDivider(
        double x,
        ProcessTableColumn[] columns,
        double hitRadius)
    {
        if (!double.IsFinite(x)
            || !double.IsFinite(hitRadius)
            || x < 0
            || hitRadius < 0)
            return -1;

        int columnIndex = HitTestColumn(x, columns);
        if (columnIndex >= 0)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (columnIndex > 0 && Math.Abs(x - column.Left) <= hitRadius)
                return columnIndex - 1;
            return Math.Abs(x - column.Right) <= hitRadius ? columnIndex : -1;
        }

        int lastColumnIndex = columns.Length - 1;
        return lastColumnIndex >= 0
               && Math.Abs(x - columns[lastColumnIndex].Right) <= hitRadius
            ? lastColumnIndex
            : -1;
    }

    /// <summary>Writes resized display geometry without modifying the committed column layout.</summary>
    public static void WriteResizedColumns(
        ReadOnlySpan<ProcessTableColumn> columns,
        int resizedColumnIndex,
        double width,
        Span<ProcessTableColumn> destination)
    {
        if (columns.Length != destination.Length)
            throw new ArgumentException(message: "Source and destination column counts must match.",
                nameof(destination));
        if ((uint)resizedColumnIndex >= (uint)columns.Length)
            throw new ArgumentOutOfRangeException(nameof(resizedColumnIndex));
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double offset = width - columns[resizedColumnIndex].Width;
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (columnIndex < resizedColumnIndex)
            {
                destination[columnIndex] = column;
                continue;
            }

            destination[columnIndex] = columnIndex == resizedColumnIndex
                ? column with { Width = width }
                : column with { Left = column.Left + offset };
        }
    }

    /// <summary>Returns the final visible index after removing and reinserting the source column.</summary>
    public static int GetReorderInsertionIndex(
        double x,
        ProcessTableColumn[] columns,
        int sourceColumnIndex)
    {
        if (!double.IsFinite(x) || (uint)sourceColumnIndex >= (uint)columns.Length) return -1;

        int insertionIndex = 0;
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columnIndex == sourceColumnIndex) continue;
            ProcessTableColumn column = columns[columnIndex];
            if (x < column.Left + column.Width / 2) break;
            insertionIndex++;
        }

        return Math.Clamp(insertionIndex, min: 0, columns.Length - 1);
    }

    /// <summary>Returns the current-layout divider that represents a pending insertion.</summary>
    public static double GetReorderInsertionX(
        ProcessTableColumn[] columns,
        int sourceColumnIndex,
        int insertionIndex)
    {
        if ((uint)sourceColumnIndex >= (uint)columns.Length
            || (uint)insertionIndex >= (uint)columns.Length)
            return double.NaN;

        if (insertionIndex <= sourceColumnIndex)
            return columns[insertionIndex].Left;

        int rightNeighborIndex = insertionIndex + 1;
        return rightNeighborIndex < columns.Length
            ? columns[rightNeighborIndex].Left
            : columns[^1].Right;
    }
}

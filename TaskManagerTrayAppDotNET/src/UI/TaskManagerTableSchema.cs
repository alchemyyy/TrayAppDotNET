namespace TaskManagerTrayAppDotNET.UI;

internal enum TaskManagerTableAlignment : byte
{
    Left,
    Right
}

internal enum TaskManagerTableSortValueKind : byte
{
    Empty,
    Text,
    Signed,
    Unsigned,
    Decimal
}

/// <summary>Provides a typed value for deterministic table sorting without parsing display text.</summary>
internal readonly record struct TaskManagerTableSortValue : IComparable<TaskManagerTableSortValue>
{
    private TaskManagerTableSortValue(
        TaskManagerTableSortValueKind kind,
        string? text,
        long signed,
        ulong unsigned,
        double decimalValue)
    {
        Kind = kind;
        Text = text;
        Signed = signed;
        Unsigned = unsigned;
        Decimal = decimalValue;
    }

    public TaskManagerTableSortValueKind Kind { get; }
    public string? Text { get; }
    public long Signed { get; }
    public ulong Unsigned { get; }
    public double Decimal { get; }

    public static TaskManagerTableSortValue Empty => default;

    public static TaskManagerTableSortValue FromText(string? value) =>
        new(TaskManagerTableSortValueKind.Text, value ?? string.Empty, 0, 0, 0);

    public static TaskManagerTableSortValue FromSigned(long value) =>
        new(TaskManagerTableSortValueKind.Signed, null, value, 0, 0);

    public static TaskManagerTableSortValue FromUnsigned(ulong value) =>
        new(TaskManagerTableSortValueKind.Unsigned, null, 0, value, 0);

    public static TaskManagerTableSortValue FromDecimal(double value) =>
        double.IsFinite(value)
            ? new TaskManagerTableSortValue(TaskManagerTableSortValueKind.Decimal, null, 0, 0, value)
            : default;

    public int CompareTo(TaskManagerTableSortValue other)
    {
        if (Kind == TaskManagerTableSortValueKind.Empty)
            return other.Kind == TaskManagerTableSortValueKind.Empty ? 0 : 1;
        if (other.Kind == TaskManagerTableSortValueKind.Empty) return -1;

        if (Kind == other.Kind)
        {
            return Kind switch
            {
                TaskManagerTableSortValueKind.Text => StringComparer.CurrentCultureIgnoreCase.Compare(
                    Text,
                    other.Text),
                TaskManagerTableSortValueKind.Signed => Signed.CompareTo(other.Signed),
                TaskManagerTableSortValueKind.Unsigned => Unsigned.CompareTo(other.Unsigned),
                TaskManagerTableSortValueKind.Decimal => Decimal.CompareTo(other.Decimal),
                _ => 0
            };
        }

        if (TryGetDecimal(this, out double left) && TryGetDecimal(other, out double right))
            return left.CompareTo(right);

        return Kind.CompareTo(other.Kind);
    }

    private static bool TryGetDecimal(TaskManagerTableSortValue value, out double result)
    {
        switch (value.Kind)
        {
            case TaskManagerTableSortValueKind.Signed:
                result = value.Signed;
                return true;
            case TaskManagerTableSortValueKind.Unsigned:
                result = value.Unsigned;
                return true;
            case TaskManagerTableSortValueKind.Decimal:
                result = value.Decimal;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}

/// <summary>Contains display text and its independent typed sort value.</summary>
internal readonly record struct TaskManagerTableCell(
    string Text,
    TaskManagerTableSortValue SortValue)
{
    public static TaskManagerTableCell TextCell(string? value)
    {
        string display = value ?? string.Empty;
        return new TaskManagerTableCell(display, TaskManagerTableSortValue.FromText(display));
    }

    public static TaskManagerTableCell SignedCell(string display, long value) =>
        new(display, TaskManagerTableSortValue.FromSigned(value));

    public static TaskManagerTableCell UnsignedCell(string display, ulong value) =>
        new(display, TaskManagerTableSortValue.FromUnsigned(value));

    public static TaskManagerTableCell DecimalCell(string display, double value) =>
        new(display, TaskManagerTableSortValue.FromDecimal(value));

    public static TaskManagerTableCell Empty => new(string.Empty, TaskManagerTableSortValue.Empty);
}

/// <summary>Defines one generic Task Manager table column.</summary>
internal readonly record struct TaskManagerTableColumn(
    string Key,
    string Title,
    double Width,
    TaskManagerTableAlignment Alignment = TaskManagerTableAlignment.Left,
    bool SortDescendingByDefault = false,
    double MinimumWidth = 48);

/// <summary>Defines the stable row identity, hierarchy, cells, icon source, and caller-owned tag.</summary>
internal sealed class TaskManagerTableRow
{
    public required string Key { get; init; }
    public string? ParentKey { get; init; }
    public required TaskManagerTableCell[] Cells { get; init; }
    public ProcessIconSource IconSource { get; init; }
    public object? Tag { get; init; }
    public bool IsGroup { get; init; }
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Validates and owns the immutable column catalog used by one table page.</summary>
internal sealed class TaskManagerTableSchema
{
    public TaskManagerTableSchema(IReadOnlyList<TaskManagerTableColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("A table requires at least one column.", nameof(columns));

        Columns = new TaskManagerTableColumn[columns.Count];
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            TaskManagerTableColumn column = columns[columnIndex];
            if (string.IsNullOrWhiteSpace(column.Key))
                throw new ArgumentException("Column keys cannot be empty.", nameof(columns));
            if (string.IsNullOrWhiteSpace(column.Title))
                throw new ArgumentException("Column titles cannot be empty.", nameof(columns));
            if (!double.IsFinite(column.Width) || column.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns), "Column widths must be positive.");
            if (!double.IsFinite(column.MinimumWidth) || column.MinimumWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns), "Minimum widths must be positive.");
            if (!keys.Add(column.Key))
                throw new ArgumentException($"Duplicate table column key '{column.Key}'.", nameof(columns));

            Columns[columnIndex] = column;
        }
    }

    public TaskManagerTableColumn[] Columns { get; }
}

/// <summary>Builds the sorted, filtered, and expanded flat projection rendered by a table.</summary>
internal static class TaskManagerTableProjection
{
    public static List<TaskManagerTableRow> Build(
        IReadOnlyList<TaskManagerTableRow> sourceRows,
        int columnCount,
        int sortColumnIndex,
        bool sortDescending,
        IReadOnlySet<string> collapsedGroupKeys,
        string? filterText)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(collapsedGroupKeys);
        if (columnCount <= 0) throw new ArgumentOutOfRangeException(nameof(columnCount));
        if ((uint)sortColumnIndex >= (uint)columnCount)
            throw new ArgumentOutOfRangeException(nameof(sortColumnIndex));

        string filter = filterText?.Trim() ?? string.Empty;
        Dictionary<string, List<TaskManagerTableRow>> childrenByParent = new(
            StringComparer.Ordinal);
        List<TaskManagerTableRow> rootRows = [];
        HashSet<string> rowKeys = new(StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            TaskManagerTableRow row = sourceRows[rowIndex];
            ValidateRow(row, columnCount, rowKeys);
            if (row.ParentKey == null)
            {
                rootRows.Add(row);
                continue;
            }

            if (!childrenByParent.TryGetValue(row.ParentKey, out List<TaskManagerTableRow>? children))
            {
                children = [];
                childrenByParent.Add(row.ParentKey, children);
            }
            children.Add(row);
        }

        Comparison<TaskManagerTableRow> comparison = (left, right) =>
        {
            int valueComparison = left.Cells[sortColumnIndex].SortValue.CompareTo(
                right.Cells[sortColumnIndex].SortValue);
            if (valueComparison == 0)
                valueComparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.Key, right.Key);
            return sortDescending ? -valueComparison : valueComparison;
        };
        rootRows.Sort(comparison);
        foreach (List<TaskManagerTableRow> children in childrenByParent.Values)
            children.Sort(comparison);

        List<TaskManagerTableRow> projectedRows = new(sourceRows.Count);
        for (int rootIndex = 0; rootIndex < rootRows.Count; rootIndex++)
        {
            TaskManagerTableRow root = rootRows[rootIndex];
            childrenByParent.TryGetValue(root.Key, out List<TaskManagerTableRow>? children);
            bool rootMatches = MatchesFilter(root, filter);
            bool hasMatchingChild = false;
            if (!rootMatches && children != null)
            {
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    if (!MatchesFilter(children[childIndex], filter)) continue;
                    hasMatchingChild = true;
                    break;
                }
            }
            if (!rootMatches && !hasMatchingChild) continue;

            projectedRows.Add(root);
            if (children == null || collapsedGroupKeys.Contains(root.Key)) continue;
            for (int childIndex = 0; childIndex < children.Count; childIndex++)
            {
                TaskManagerTableRow child = children[childIndex];
                if (filter.Length > 0 && !rootMatches && !MatchesFilter(child, filter)) continue;
                projectedRows.Add(child);
            }
        }

        return projectedRows;
    }

    private static void ValidateRow(
        TaskManagerTableRow row,
        int columnCount,
        HashSet<string> rowKeys)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(row.Key))
            throw new ArgumentException("Table row keys cannot be empty.", nameof(row));
        if (!rowKeys.Add(row.Key))
            throw new ArgumentException($"Duplicate table row key '{row.Key}'.", nameof(row));
        if (row.Cells == null || row.Cells.Length != columnCount)
            throw new ArgumentException(
                $"Table row '{row.Key}' has {row.Cells?.Length ?? 0} cells; expected {columnCount}.",
                nameof(row));
    }

    private static bool MatchesFilter(TaskManagerTableRow row, string filter)
    {
        if (filter.Length == 0) return true;

        for (int cellIndex = 0; cellIndex < row.Cells.Length; cellIndex++)
        {
            if (row.Cells[cellIndex].Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                return true;
        }

        return false;
    }
}

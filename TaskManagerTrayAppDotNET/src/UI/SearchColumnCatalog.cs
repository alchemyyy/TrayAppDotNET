namespace TaskManagerTrayAppDotNET.UI;

/// <summary>How a search comparison reads a column's values, and the units its numbers are in.</summary>
internal enum SearchValueKind : byte
{
    /// <summary>Compared as text only.</summary>
    Text,

    /// <summary>A plain number; <c>k</c>, <c>m</c>, <c>g</c>, <c>t</c> step by 1000.</summary>
    Number,

    /// <summary>A percentage; an optional trailing <c>%</c>.</summary>
    Percentage,

    /// <summary>100-nanosecond ticks; written with <c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>, <c>d</c> or as displayed.</summary>
    Duration,

    /// <summary>Bytes; suffixes step by 1024.</summary>
    Bytes,

    /// <summary>Bytes per second; suffixes step by 1024, optionally followed by <c>/s</c> or <c>ps</c>.</summary>
    ByteRate,

    /// <summary>Bits per second; suffixes step by 1000, optionally followed by <c>/s</c> or <c>ps</c>.</summary>
    BitRate
}

/// <summary>One column a search expression can name.</summary>
/// <param name="ID">The value the page's resolver receives for this column.</param>
/// <param name="Title">The displayed title, which expressions and completions use.</param>
/// <param name="AlternateName">A second accepted name, such as the column's code identifier.</param>
internal readonly record struct SearchColumn(
    int ID,
    string Title,
    string AlternateName,
    SearchValueKind ValueKind)
{
    /// <summary>A user-chosen name accepted after titles and alternate names.</summary>
    public string Nickname { get; init; } = string.Empty;

    /// <summary>Page-defined bits a query using this column reports through <see cref="SearchQuery.RequiredColumnMask"/>.</summary>
    public ulong RequiredMask { get; init; }
}

/// <summary>The columns one page's search can name, and the columns plain text searches.</summary>
internal sealed class SearchColumnCatalog
{
    private readonly SearchColumn[] _columns;

    public SearchColumnCatalog(
        IReadOnlyList<SearchColumn> columns,
        IReadOnlyList<int> defaultColumnIDs)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(defaultColumnIDs);

        _columns = [.. columns];
        DefaultColumnIDs = [.. defaultColumnIDs];
        ulong defaultMask = 0;
        for (int defaultIndex = 0; defaultIndex < DefaultColumnIDs.Length; defaultIndex++)
        {
            int columnID = DefaultColumnIDs[defaultIndex];
            int columnIndex = Array.FindIndex(_columns, column => column.ID == columnID);
            if (columnIndex < 0)
            {
                throw new ArgumentException(
                    $"Default search column {columnID} is not in the catalog.",
                    nameof(defaultColumnIDs));
            }

            defaultMask |= _columns[columnIndex].RequiredMask;
        }

        DefaultMask = defaultMask;
    }

    /// <summary>Columns in the order completions list them when scores tie.</summary>
    public IReadOnlyList<SearchColumn> Columns => _columns;

    /// <summary>Columns a plain search term matches when any of them contains it.</summary>
    public int[] DefaultColumnIDs { get; }

    public ulong DefaultMask { get; }

    /// <summary>Resolves a braced column name; titles and alternate names win over nicknames.</summary>
    public bool TryResolve(string name, out SearchColumn column)
    {
        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            SearchColumn candidate = _columns[columnIndex];
            if (!string.Equals(candidate.Title, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate.AlternateName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            column = candidate;
            return true;
        }

        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            SearchColumn candidate = _columns[columnIndex];
            if (candidate.Nickname.Length == 0
                || !string.Equals(candidate.Nickname, name, StringComparison.OrdinalIgnoreCase))
                continue;

            column = candidate;
            return true;
        }

        column = default;
        return false;
    }
}

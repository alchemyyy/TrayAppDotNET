using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Paints the process table as one control. No control or view-model object is created per row.
/// </summary>
internal sealed class ProcessDetailsCanvas : Control, IDisposable
{
    private const double DefaultViewportHeight = 900;
    private const string RunningText = "Running";
    private const string SuspendedText = "Suspended";
    private const string SystemUserText = "SYSTEM";
    private const string UnavailableText = "Unavailable";
    private const string NotSampledText = "Not sampled";

    private static readonly Typeface TableTypeface = new(TADNFontResolver.SegoeUIFamilyName);
    private static readonly Typeface GlyphTypeface = new(TADNFontResolver.SegoeFluentIconsFamilyName);
    private static readonly CultureInfo TableCulture = CultureInfo.CurrentCulture;

    private readonly ProcessTableMetrics _metrics;
    private readonly ProcessTableColumn[] _columns;
    private readonly ProcessSnapshotRow[] _rows = new ProcessSnapshotRow[ProcessSnapshotService.MaximumProcessCount];
    private readonly int[] _visibleRowIndexes = new int[ProcessSnapshotService.MaximumProcessCount];
    private readonly ProcessRowTextCache[] _textCaches = new ProcessRowTextCache[ProcessSnapshotService.MaximumProcessCount];
    private readonly Dictionary<int, int> _cacheSlots = new(ProcessSnapshotService.MaximumProcessCount);
    private readonly int[] _freeCacheSlots = new int[ProcessSnapshotService.MaximumProcessCount];
    private readonly int[] _staleProcessIDs = new int[ProcessSnapshotService.MaximumProcessCount];
    private readonly ProcessRowIndexComparer _rowComparer;
    private readonly FormattedText[] _headerTexts;
    private readonly FormattedText _ascendingCaretText;
    private readonly FormattedText _descendingCaretText;
    private readonly FormattedText _runningText;
    private readonly FormattedText _suspendedText;
    private readonly FormattedText _currentUserText;
    private readonly FormattedText _systemUserText;
    private readonly FormattedText _unavailableText;
    private readonly FormattedText _notSampledText;
    private readonly IBrush _backgroundBrush;
    private readonly IBrush _foregroundBrush;
    private readonly IBrush _secondaryForegroundBrush;
    private readonly IBrush _hoverBrush;
    private readonly IBrush _selectedBrush;
    private readonly IBrush _accentBrush;
    private readonly Pen _gridPen;
    private readonly double _sortCaretRightMargin;
    private int _rowCount;
    private int _visibleRowCount;
    private int _selectedProcessID = -1;
    private int _hoveredProcessID = -1;
    private int _freeCacheSlotCount = ProcessSnapshotService.MaximumProcessCount;
    private int _cacheGeneration;
    private int _filterProcessID = -1;
    private long _snapshotVersion = -1;
    private string _filterText = string.Empty;
    private Rect _effectiveViewport;
    private ProcessTableColumnKind _sortColumn = ProcessTableColumnKind.Name;
    private bool _sortDescending;
    private bool _disposed;

    public ProcessDetailsCanvas(SettingsPalette palette, TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);

        _metrics = new ProcessTableMetrics(
            resources.AxamlProcessTable.HeaderHeight,
            resources.AxamlProcessTable.RowHeight,
            resources.AxamlProcessTable.CellPadding,
            resources.AxamlProcessTable.FontSize,
            resources.AxamlProcessTable.HeaderFontSize,
            resources.AxamlProcessTable.ProcessIconSize,
            resources.AxamlProcessTable.ProcessIconGap);
        _columns = CreateColumns(resources);
        _rowComparer = new ProcessRowIndexComparer(_rows);
        _sortCaretRightMargin = resources.AxamlProcessTable.SortCaretRightMargin;

        for (int cacheIndex = 0; cacheIndex < _freeCacheSlots.Length; cacheIndex++)
            _freeCacheSlots[cacheIndex] = _freeCacheSlots.Length - cacheIndex - 1;

        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(TaskManagerWindowResources.ProcessGridBackgroundColor);
        _foregroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _secondaryForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        _hoverBrush = TrayAppDotNETSettingsUI.Brush(palette.Hover);
        _selectedBrush = TrayAppDotNETSettingsUI.Brush(palette.SearchListItemSelected);
        _accentBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent);
        _gridPen = new Pen(TrayAppDotNETSettingsUI.Brush(palette.Border), 1);

        _headerTexts = new FormattedText[_columns.Length];
        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            _headerTexts[columnIndex] = CreateText(
                _columns[columnIndex].Title,
                _metrics.HeaderFontSize,
                _foregroundBrush,
                FontWeight.Normal);
        }

        _ascendingCaretText = CreateGlyphText(
            "\uE96D",
            resources.AxamlProcessTable.SortCaretFontSize,
            _secondaryForegroundBrush);
        _descendingCaretText = CreateGlyphText(
            "\uE96E",
            resources.AxamlProcessTable.SortCaretFontSize,
            _secondaryForegroundBrush);
        _runningText = CreateText(RunningText, _metrics.FontSize, _foregroundBrush);
        _suspendedText = CreateText(SuspendedText, _metrics.FontSize, _secondaryForegroundBrush);
        _currentUserText = CreateText(Environment.UserName, _metrics.FontSize, _foregroundBrush);
        _systemUserText = CreateText(SystemUserText, _metrics.FontSize, _foregroundBrush);
        _unavailableText = CreateText(UnavailableText, _metrics.FontSize, _secondaryForegroundBrush);
        _notSampledText = CreateText(NotSampledText, _metrics.FontSize, _secondaryForegroundBrush);

        ClipToBounds = true;
        Focusable = true;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public event Action<int?>? SelectedProcessChanged;

    public int? SelectedProcessID => _selectedProcessID >= 0 ? _selectedProcessID : null;

    /// <summary>Copies and applies the service's newest snapshot without allocating a row collection.</summary>
    public void RefreshFrom(ProcessSnapshotService snapshotService)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshotService);

        int previousCount = _rowCount;
        int count = snapshotService.CopyLatest(_rows, out long version);
        if (version == _snapshotVersion) return;

        if (count < previousCount)
            Array.Clear(_rows, count, previousCount - count);
        _snapshotVersion = version;
        _rowCount = count;
        SynchronizeTextCacheMembership();
        RebuildVisibleRows();
        EnsureSelectedProcessStillExists();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetFilter(string? filterText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string nextFilter = filterText?.Trim() ?? string.Empty;
        if (string.Equals(_filterText, nextFilter, StringComparison.Ordinal)) return;

        _filterText = nextFilter;
        _filterProcessID = int.TryParse(nextFilter, NumberStyles.None, CultureInfo.InvariantCulture, out int processID)
            ? processID
            : -1;
        RebuildVisibleRows();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : _columns[^1].Right;
        return new Size(width, ProcessTableLayout.GetContentHeight(_visibleRowCount, _metrics));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_disposed || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        Rect viewport = ResolveViewport();
        double stickyHeaderTop = Math.Clamp(viewport.Y, 0, Math.Max(0, Bounds.Height - _metrics.HeaderHeight));
        context.FillRectangle(_backgroundBrush, viewport);

        ProcessTableLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics,
            out int firstRow,
            out int lastRowExclusive);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
            DrawRow(context, visibleIndex);

        DrawColumnGrid(context, viewport);
        DrawHeader(context, stickyHeaderTop);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed) return;

        Point position = eventArgs.GetPosition(this);
        double stickyHeaderTop = Math.Max(0, _effectiveViewport.Y);
        if (position.Y >= stickyHeaderTop && position.Y < stickyHeaderTop + _metrics.HeaderHeight)
        {
            SortFromHeader(position.X);
            eventArgs.Handled = true;
            return;
        }

        int visibleIndex = ProcessTableLayout.HitTestRow(position.Y, _visibleRowCount, _metrics);
        SelectVisibleRow(visibleIndex);
        Focus();
        eventArgs.Handled = visibleIndex >= 0;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        Point position = eventArgs.GetPosition(this);
        double stickyHeaderTop = Math.Max(0, _effectiveViewport.Y);
        int nextHoveredProcessID = -1;
        if (position.Y < stickyHeaderTop || position.Y >= stickyHeaderTop + _metrics.HeaderHeight)
        {
            int visibleIndex = ProcessTableLayout.HitTestRow(position.Y, _visibleRowCount, _metrics);
            if (visibleIndex >= 0)
                nextHoveredProcessID = _rows[_visibleRowIndexes[visibleIndex]].ProcessID;
        }

        if (nextHoveredProcessID == _hoveredProcessID) return;
        _hoveredProcessID = nextHoveredProcessID;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        if (_hoveredProcessID < 0) return;

        _hoveredProcessID = -1;
        InvalidateVisual();
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs eventArgs)
    {
        _effectiveViewport = eventArgs.EffectiveViewport;
        InvalidateVisual();
    }

    private Rect ResolveViewport()
    {
        if (_effectiveViewport.Width > 0 && _effectiveViewport.Height > 0)
        {
            double top = Math.Clamp(_effectiveViewport.Y, 0, Bounds.Height);
            double bottom = Math.Clamp(_effectiveViewport.Bottom, top, Bounds.Height);
            return new Rect(0, top, Bounds.Width, bottom - top);
        }

        return new Rect(0, 0, Bounds.Width, Math.Min(Bounds.Height, DefaultViewportHeight));
    }

    private void DrawRow(DrawingContext context, int visibleIndex)
    {
        int rowIndex = _visibleRowIndexes[visibleIndex];
        ref ProcessSnapshotRow row = ref _rows[rowIndex];
        double top = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
        Rect rowBounds = new(0, top, Bounds.Width, _metrics.RowHeight);

        if (row.ProcessID == _selectedProcessID)
        {
            context.FillRectangle(_selectedBrush, rowBounds);
            context.FillRectangle(_accentBrush, new Rect(0, top, 3, _metrics.RowHeight));
        }
        else if (row.ProcessID == _hoveredProcessID)
            context.FillRectangle(_hoverBrush, rowBounds);

        if (!_cacheSlots.TryGetValue(row.ProcessID, out int cacheSlot)) return;
        ref ProcessRowTextCache cache = ref _textCaches[cacheSlot];
        double textTop = top + Math.Max(0, (_metrics.RowHeight - _metrics.FontSize * 1.35) / 2);

        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            ProcessTableColumn column = _columns[columnIndex];
            FormattedText text = ResolveCellText(ref cache, row, column.Kind);
            double leftInset = column.Kind == ProcessTableColumnKind.Name
                ? _metrics.CellPadding + _metrics.ProcessIconSize + _metrics.ProcessIconGap
                : _metrics.CellPadding;
            double textX = column.Alignment == ProcessTableColumnAlignment.Right
                ? column.Right - _metrics.CellPadding - text.Width
                : column.Left + leftInset;
            Rect clip = new(
                column.Left + leftInset,
                top,
                Math.Max(0, column.Width - leftInset - _metrics.CellPadding),
                _metrics.RowHeight);
            using (context.PushClip(clip))
                context.DrawText(text, new Point(textX, textTop));
        }

        ProcessTableColumn nameColumn = _columns[0];
        double iconTop = top + (_metrics.RowHeight - _metrics.ProcessIconSize) / 2;
        context.FillRectangle(
            _accentBrush,
            new Rect(
                nameColumn.Left + _metrics.CellPadding,
                iconTop,
                _metrics.ProcessIconSize,
                _metrics.ProcessIconSize),
            2);
    }

    private void DrawColumnGrid(DrawingContext context, Rect viewport)
    {
        for (int columnIndex = 1; columnIndex < _columns.Length; columnIndex++)
        {
            double left = _columns[columnIndex].Left;
            context.DrawLine(_gridPen, new Point(left, viewport.Y), new Point(left, viewport.Bottom));
        }
    }

    private void DrawHeader(DrawingContext context, double top)
    {
        Rect headerBounds = new(0, top, Bounds.Width, _metrics.HeaderHeight);
        context.FillRectangle(_backgroundBrush, headerBounds);
        context.DrawLine(
            _gridPen,
            new Point(0, top + _metrics.HeaderHeight),
            new Point(Bounds.Width, top + _metrics.HeaderHeight));

        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            ProcessTableColumn column = _columns[columnIndex];
            FormattedText headerText = _headerTexts[columnIndex];
            double textX = column.Left + _metrics.CellPadding;
            double textTop = top + Math.Max(0, (_metrics.HeaderHeight - headerText.Height) / 2);
            bool isSortedColumn = column.Kind == _sortColumn;
            FormattedText? caret = isSortedColumn
                ? _sortDescending ? _descendingCaretText : _ascendingCaretText
                : null;
            double caretX = caret == null
                ? column.Right
                : column.Right - _sortCaretRightMargin - caret.Width;
            double headerTextRight = caret == null
                ? column.Right - _metrics.CellPadding
                : caretX - _metrics.CellPadding;
            Rect headerClip = new(
                textX,
                top,
                Math.Max(0, headerTextRight - textX),
                _metrics.HeaderHeight);
            using (context.PushClip(headerClip))
                context.DrawText(headerText, new Point(textX, textTop));

            if (caret != null)
            {
                double caretTop = top + Math.Max(0, (_metrics.HeaderHeight - caret.Height) / 2);
                context.DrawText(caret, new Point(caretX, caretTop));
            }

            if (columnIndex == 0) continue;
            context.DrawLine(
                _gridPen,
                new Point(column.Left, top),
                new Point(column.Left, top + _metrics.HeaderHeight));
        }
    }

    private FormattedText ResolveCellText(
        ref ProcessRowTextCache cache,
        ProcessSnapshotRow row,
        ProcessTableColumnKind kind) =>
        kind switch
        {
            ProcessTableColumnKind.Name => GetStringText(ref cache.Name, row.Name, _foregroundBrush),
            ProcessTableColumnKind.ProcessID => GetIntegerText(ref cache.ProcessID, row.ProcessID),
            ProcessTableColumnKind.Status => row.State == ProcessExecutionState.Suspended
                ? _suspendedText
                : _runningText,
            ProcessTableColumnKind.UserName => row.Owner switch
            {
                ProcessOwnerKind.CurrentUser => _currentUserText,
                ProcessOwnerKind.System => _systemUserText,
                _ => _unavailableText
            },
            ProcessTableColumnKind.CPU => GetCPUText(ref cache.CPU, row.CPUPercent),
            ProcessTableColumnKind.PrivateMemory => GetMemoryText(ref cache.PrivateMemory, row.PrivateMemoryBytes),
            ProcessTableColumnKind.WorkingSet => GetMemoryText(ref cache.WorkingSet, row.WorkingSetBytes),
            ProcessTableColumnKind.CommandLine => row.CommandLine == null
                ? _notSampledText
                : GetStringText(ref cache.CommandLine, row.CommandLine, _foregroundBrush),
            _ => _unavailableText
        };

    private FormattedText GetStringText(ref CellTextCache cache, string value, IBrush brush)
    {
        if (cache.Text != null && string.Equals(cache.Source, value, StringComparison.Ordinal)) return cache.Text;

        cache.Source = value;
        cache.Text = CreateText(value, _metrics.FontSize, brush);
        return cache.Text;
    }

    private FormattedText GetIntegerText(ref CellTextCache cache, int value)
    {
        if (cache.Text != null && cache.NumericValue == value) return cache.Text;

        cache.NumericValue = value;
        cache.Source = null;
        cache.Text = CreateText(value.ToString(TableCulture), _metrics.FontSize, _foregroundBrush);
        return cache.Text;
    }

    private FormattedText GetCPUText(ref CellTextCache cache, double value)
    {
        long tenths = (long)Math.Round(value * 10, MidpointRounding.AwayFromZero);
        if (cache.Text != null && cache.NumericValue == tenths) return cache.Text;

        cache.NumericValue = tenths;
        cache.Source = null;
        string display = tenths == 0
            ? "0"
            : (tenths / 10.0).ToString("0.0", TableCulture);
        cache.Text = CreateText(display, _metrics.FontSize, _foregroundBrush);
        return cache.Text;
    }

    private FormattedText GetMemoryText(ref CellTextCache cache, long bytes)
    {
        long kibibytes = bytes <= 0 ? 0 : (bytes + 1023) / 1024;
        if (cache.Text != null && cache.NumericValue == kibibytes) return cache.Text;

        cache.NumericValue = kibibytes;
        cache.Source = null;
        string display = string.Concat(kibibytes.ToString("N0", TableCulture), " K");
        cache.Text = CreateText(display, _metrics.FontSize, _foregroundBrush);
        return cache.Text;
    }

    private void SortFromHeader(double x)
    {
        int columnIndex = ProcessTableLayout.HitTestColumn(x, _columns);
        if (columnIndex < 0) return;

        ProcessTableColumnKind nextColumn = _columns[columnIndex].Kind;
        if (nextColumn == _sortColumn)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = nextColumn;
            _sortDescending = false;
        }

        SortVisibleRows();
        InvalidateVisual();
    }

    private void SelectVisibleRow(int visibleIndex)
    {
        int nextProcessID = visibleIndex >= 0 && visibleIndex < _visibleRowCount
            ? _rows[_visibleRowIndexes[visibleIndex]].ProcessID
            : -1;
        if (nextProcessID == _selectedProcessID) return;

        _selectedProcessID = nextProcessID;
        SelectedProcessChanged?.Invoke(SelectedProcessID);
        InvalidateVisual();
    }

    private void RebuildVisibleRows()
    {
        int writeIndex = 0;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            if (!MatchesFilter(_rows[rowIndex])) continue;
            _visibleRowIndexes[writeIndex] = rowIndex;
            writeIndex++;
        }

        _visibleRowCount = writeIndex;
        SortVisibleRows();
    }

    private bool MatchesFilter(ProcessSnapshotRow row)
    {
        if (_filterText.Length == 0) return true;
        if (_filterProcessID >= 0 && row.ProcessID == _filterProcessID) return true;
        if (row.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) return true;

        return row.Owner switch
        {
            ProcessOwnerKind.CurrentUser => Environment.UserName.Contains(
                _filterText,
                StringComparison.OrdinalIgnoreCase),
            ProcessOwnerKind.System => SystemUserText.Contains(_filterText, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private void SortVisibleRows()
    {
        _rowComparer.Column = _sortColumn;
        _rowComparer.IsDescending = _sortDescending;
        Array.Sort(_visibleRowIndexes, 0, _visibleRowCount, _rowComparer);
    }

    private void SynchronizeTextCacheMembership()
    {
        int generation = unchecked(_cacheGeneration + 1);
        if (generation == 0)
        {
            ResetTextCaches();
            generation = 1;
        }

        _cacheGeneration = generation;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            int processID = _rows[rowIndex].ProcessID;
            if (_cacheSlots.TryGetValue(processID, out int slot))
                _textCaches[slot].LastSeenGeneration = generation;
        }

        int staleCount = 0;
        foreach (KeyValuePair<int, int> pair in _cacheSlots)
        {
            if (_textCaches[pair.Value].LastSeenGeneration == generation) continue;
            _staleProcessIDs[staleCount] = pair.Key;
            staleCount++;
        }

        for (int staleIndex = 0; staleIndex < staleCount; staleIndex++)
        {
            int processID = _staleProcessIDs[staleIndex];
            int slot = _cacheSlots[processID];
            _cacheSlots.Remove(processID);
            _textCaches[slot] = default;
            _freeCacheSlots[_freeCacheSlotCount] = slot;
            _freeCacheSlotCount++;
        }

        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            int processID = _rows[rowIndex].ProcessID;
            if (_cacheSlots.ContainsKey(processID)) continue;
            if (_freeCacheSlotCount <= 0) break;

            _freeCacheSlotCount--;
            int slot = _freeCacheSlots[_freeCacheSlotCount];
            _textCaches[slot] = new ProcessRowTextCache { LastSeenGeneration = generation };
            _cacheSlots.Add(processID, slot);
        }
    }

    private void EnsureSelectedProcessStillExists()
    {
        if (_selectedProcessID < 0) return;

        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            if (_rows[rowIndex].ProcessID == _selectedProcessID) return;
        }

        _selectedProcessID = -1;
        SelectedProcessChanged?.Invoke(null);
    }

    private void ResetTextCaches()
    {
        _cacheSlots.Clear();
        Array.Clear(_textCaches);
        _freeCacheSlotCount = _freeCacheSlots.Length;
        for (int cacheIndex = 0; cacheIndex < _freeCacheSlots.Length; cacheIndex++)
            _freeCacheSlots[cacheIndex] = _freeCacheSlots.Length - cacheIndex - 1;
    }

    private static ProcessTableColumn[] CreateColumns(TaskManagerWindowResources resources)
    {
        ProcessTableColumn[] columns = new ProcessTableColumn[8];
        double left = 0;
        AddColumn(0, ProcessTableColumnKind.Name, "Name", resources.AxamlProcessTable.NameColumnWidth,
            ProcessTableColumnAlignment.Left);
        AddColumn(1, ProcessTableColumnKind.ProcessID, "PID", resources.AxamlProcessTable.PIDColumnWidth,
            ProcessTableColumnAlignment.Right);
        AddColumn(2, ProcessTableColumnKind.Status, "Status", resources.AxamlProcessTable.StatusColumnWidth,
            ProcessTableColumnAlignment.Left);
        AddColumn(3, ProcessTableColumnKind.UserName, "User name", resources.AxamlProcessTable.UserNameColumnWidth,
            ProcessTableColumnAlignment.Left);
        AddColumn(4, ProcessTableColumnKind.CPU, "CPU", resources.AxamlProcessTable.CPUColumnWidth,
            ProcessTableColumnAlignment.Right);
        AddColumn(5, ProcessTableColumnKind.PrivateMemory, "Memory (private working set)",
            resources.AxamlProcessTable.PrivateMemoryColumnWidth, ProcessTableColumnAlignment.Right);
        AddColumn(6, ProcessTableColumnKind.WorkingSet, "Memory (shared working set)",
            resources.AxamlProcessTable.WorkingSetColumnWidth, ProcessTableColumnAlignment.Right);
        AddColumn(7, ProcessTableColumnKind.CommandLine, "Command line",
            resources.AxamlProcessTable.CommandLineColumnWidth, ProcessTableColumnAlignment.Left);
        return columns;

        void AddColumn(
            int index,
            ProcessTableColumnKind kind,
            string title,
            double width,
            ProcessTableColumnAlignment alignment)
        {
            columns[index] = new ProcessTableColumn(kind, title, left, width, alignment);
            left += width;
        }
    }

    private static FormattedText CreateText(
        string text,
        double fontSize,
        IBrush brush,
        FontWeight? fontWeight = null) =>
        new(
            text,
            TableCulture,
            FlowDirection.LeftToRight,
            new Typeface(TableTypeface.FontFamily, FontStyle.Normal, fontWeight ?? FontWeight.Normal),
            fontSize,
            brush);

    private static FormattedText CreateGlyphText(string text, double fontSize, IBrush brush) =>
        new(
            text,
            TableCulture,
            FlowDirection.LeftToRight,
            GlyphTypeface,
            fontSize,
            brush);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        SelectedProcessChanged = null;
        ResetTextCaches();
    }

    private struct CellTextCache
    {
        public string? Source;
        public long NumericValue;
        public FormattedText? Text;
    }

    private struct ProcessRowTextCache
    {
        public int LastSeenGeneration;
        public CellTextCache Name;
        public CellTextCache ProcessID;
        public CellTextCache CPU;
        public CellTextCache PrivateMemory;
        public CellTextCache WorkingSet;
        public CellTextCache CommandLine;
    }

    private sealed class ProcessRowIndexComparer(ProcessSnapshotRow[] rows) : IComparer<int>
    {
        public ProcessTableColumnKind Column { get; set; }
        public bool IsDescending { get; set; }

        public int Compare(int leftIndex, int rightIndex)
        {
            ProcessSnapshotRow left = rows[leftIndex];
            ProcessSnapshotRow right = rows[rightIndex];
            int comparison = Column switch
            {
                ProcessTableColumnKind.Name => string.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.OrdinalIgnoreCase),
                ProcessTableColumnKind.ProcessID => left.ProcessID.CompareTo(right.ProcessID),
                ProcessTableColumnKind.Status => left.State.CompareTo(right.State),
                ProcessTableColumnKind.UserName => left.Owner.CompareTo(right.Owner),
                ProcessTableColumnKind.CPU => left.CPUPercent.CompareTo(right.CPUPercent),
                ProcessTableColumnKind.PrivateMemory => left.PrivateMemoryBytes.CompareTo(right.PrivateMemoryBytes),
                ProcessTableColumnKind.WorkingSet => left.WorkingSetBytes.CompareTo(right.WorkingSetBytes),
                ProcessTableColumnKind.CommandLine => string.Compare(
                    left.CommandLine,
                    right.CommandLine,
                    StringComparison.OrdinalIgnoreCase),
                _ => 0
            };
            if (comparison == 0)
                comparison = left.ProcessID.CompareTo(right.ProcessID);
            if (!IsDescending) return comparison;
            return comparison switch
            {
                > 0 => -1,
                < 0 => 1,
                _ => 0
            };
        }
    }
}

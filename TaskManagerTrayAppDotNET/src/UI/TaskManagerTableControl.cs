using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Paints structured rows for the non-Processes Task Manager table pages.</summary>
internal sealed class TaskManagerTableControl : DetailsGridControl
{
    private const int MaximumTextLayoutCharacters = 2_048;
    private const string TextEllipsis = "\u2026";
    private const string MeasurementText = "Ag";

    private static readonly Typeface DefaultTypeface = new(TADNFontResolver.SegoeUIFamilyName);

    private readonly TaskManagerTableSchema _schema;
    private readonly ProcessIconService _processIconService;
    private readonly TaskManagerWindowResources _resources;
    private readonly DetailsGridFontWeight _baseFontWeight;
    private readonly double _rowTextHeightScale;
    private readonly IBrush _backgroundBrush;
    private readonly IBrush _foregroundBrush;
    private readonly IBrush _secondaryForegroundBrush;
    private readonly IBrush _headerHoverBrush;
    private readonly IBrush _rowHoverBrush;
    private readonly IBrush _selectionBrush;
    private readonly IBrush _accentBrush;
    private readonly IBrush _borderBrush;
    private readonly List<TaskManagerTableRow> _sourceRows = [];
    private readonly HashSet<string> _collapsedGroupKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _groupKeysWithChildren = new(StringComparer.Ordinal);
    private readonly string[] _columnTitles;
    private readonly double[] _columnWidths;
    private readonly double[] _columnLefts;
    private List<TaskManagerTableRow> _visibleRows = [];
    private Typeface _tableTypeface;
    private Typeface _groupTypeface;
    private Pen _gridPen;
    private Pen _selectionPen;
    private Pen _expanderPen;
    private IPointer? _capturedResizePointer;
    private string _filterText = string.Empty;
    private string? _selectedRowKey;
    private int _sortColumnIndex;
    private int _hoveredRowIndex = -1;
    private int _hoveredHeaderColumnIndex = -1;
    private int _resizingColumnIndex = -1;
    private double _resizeStartX;
    private double _resizeStartWidth;
    private double _fontSize;
    private double _rowHeight;
    private double _headerHeight;
    private double _headerFontSize;
    private double _cellPadding;
    private double _iconSize;
    private double _iconGap;
    private double _treeIndentWidth;
    private double _gridLineThickness;
    private bool _sortDescending;

    public TaskManagerTableControl(
        TaskManagerTableSchema schema,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(processIconService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);

        _schema = schema;
        _processIconService = processIconService;
        _resources = resources;
        _baseFontWeight = settings.GridFontWeight;
        _fontSize = settings.GridFontSize;
        _rowHeight = settings.GridRowHeight;
        _tableTypeface = CreateTypeface(CalculateFontWeight(_fontSize));
        _groupTypeface = CreateTypeface(Math.Max((int)FontWeight.SemiBold, CalculateFontWeight(_fontSize)));
        _rowTextHeightScale = MeasureRowTextHeightScale(CreateTypeface((int)settings.GridFontWeight));
        _columnTitles = new string[schema.Columns.Length];
        _columnWidths = new double[schema.Columns.Length];
        _columnLefts = new double[schema.Columns.Length];
        for (int columnIndex = 0; columnIndex < schema.Columns.Length; columnIndex++)
        {
            TaskManagerTableColumn column = schema.Columns[columnIndex];
            _columnTitles[columnIndex] = column.Title;
            _columnWidths[columnIndex] = column.Width;
        }
        RecalculateColumnLefts();

        _sortColumnIndex = 0;
        _sortDescending = schema.Columns[0].SortDescendingByDefault;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(
            TaskManagerWindowResources.ProcessGridBackgroundColor);
        _foregroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _secondaryForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        _headerHoverBrush = TrayAppDotNETSettingsUI.Brush(palette.Hover);
        _rowHoverBrush = TrayAppDotNETSettingsUI.Brush(palette.Hover);
        _selectionBrush = TrayAppDotNETSettingsUI.Brush(palette.SearchListItemSelected);
        _accentBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent);
        _borderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border);
        _gridPen = new Pen(_borderBrush, resources.AxamlProcessTable.GridLineThickness);
        _selectionPen = new Pen(_accentBrush, resources.AxamlProcessTable.SelectionBorderThickness.Left);
        _expanderPen = new Pen(_secondaryForegroundBrush, resources.AxamlProcessTable.TreeExpanderLineThickness);
        ApplyResources(resources);

        ClipToBounds = true;
        Focusable = true;
        _processIconService.IconsChanged += OnIconsChanged;
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
    }

    public event Action<TaskManagerTableRow?>? SelectedRowChanged;
    public event Action<TaskManagerTableRow>? RowActivated;
    public event Action<int, bool>? SortChanged;

    public IReadOnlyList<TaskManagerTableRow> VisibleRows => _visibleRows;
    public TaskManagerTableRow? SelectedRow => FindRowByKey(_selectedRowKey);
    public double RowHeight => _rowHeight;
    public int SortColumnIndex => _sortColumnIndex;
    public bool SortDescending => _sortDescending;

    protected override int DetailsGridRowCount => _visibleRows.Count;
    protected override double DetailsGridHeaderHeight => _headerHeight;
    protected override double DetailsGridRowHeight => _rowHeight;
    protected override double DetailsGridFontSize => _fontSize;
    protected override double DetailsGridDefaultViewportHeight =>
        _resources.AxamlProcessTable.DefaultViewportHeight;
    protected override bool CanResetDetailsGridZoom => _resizingColumnIndex < 0;

    /// <summary>Replaces all rows while preserving compatible sort, expansion, and selection state.</summary>
    public void SetRows(IReadOnlyList<TaskManagerTableRow> rows)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        ArgumentNullException.ThrowIfNull(rows);

        _sourceRows.Clear();
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            _sourceRows.Add(rows[rowIndex]);
        RebuildGroupIndex();
        RebuildProjection(notifySelectionRemoval: true);
    }

    /// <summary>Filters against all display cells while retaining matching parent groups.</summary>
    public void SetFilter(string? filterText)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        string nextFilter = filterText?.Trim() ?? string.Empty;
        if (string.Equals(_filterText, nextFilter, StringComparison.Ordinal)) return;

        _filterText = nextFilter;
        RebuildProjection(notifySelectionRemoval: false);
    }

    /// <summary>Updates live aggregate text in a column header without rebuilding rows.</summary>
    public void SetColumnTitle(int columnIndex, string title)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        if ((uint)columnIndex >= (uint)_columnTitles.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (string.Equals(_columnTitles[columnIndex], title, StringComparison.Ordinal)) return;

        _columnTitles[columnIndex] = title;
        InvalidateVisual();
    }

    /// <summary>Applies the shared grid font and visible row spacing settings.</summary>
    public void SetGridTypography(double fontSize, double rowSpacing)
    {
        double rowTextHeight = ProcessTableLayout.CalculateRowTextHeight(
            fontSize,
            _rowTextHeightScale);
        SetGridMetrics(
            fontSize,
            ProcessTableLayout.CalculateRowHeight(rowTextHeight, rowSpacing));
    }

    /// <summary>Selects a stable row key, or clears the selection when the key is absent.</summary>
    public void SelectRow(string? rowKey)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        string? nextKey = FindSourceRowByKey(rowKey)?.Key;
        SetSelectedRowKey(nextKey);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double contentWidth = GetContentWidth();
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(contentWidth, availableSize.Width)
            : contentWidth;
        return new Size(
            width,
            DetailsGridLayout.GetContentHeight(_visibleRows.Count, _headerHeight, _rowHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        InvalidateVisual();
        return arrangedSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (IsDetailsGridDisposed || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        Rect viewport = ResolveDetailsGridViewport();
        double stickyHeaderTop = ResolveStickyHeaderTop(viewport);
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            _visibleRows.Count,
            _headerHeight,
            _rowHeight,
            out int firstRow,
            out int lastRowExclusive);

        for (int rowIndex = firstRow; rowIndex < lastRowExclusive; rowIndex++)
            DrawRow(context, rowIndex);
        DrawColumnGrid(context, viewport);
        DrawHeader(context, stickyHeaderTop);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (eventArgs.Handled || IsDetailsGridDisposed) return;

        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        Point position = eventArgs.GetPosition(this);
        Rect viewport = ResolveDetailsGridViewport();
        double headerTop = ResolveStickyHeaderTop(viewport);
        bool isHeader = position.Y >= headerTop && position.Y < headerTop + _headerHeight;
        if (pointerPoint.Properties.IsLeftButtonPressed && isHeader)
        {
            int dividerIndex = HitTestColumnDivider(position.X);
            if (dividerIndex >= 0)
            {
                _capturedResizePointer = eventArgs.Pointer;
                _resizingColumnIndex = dividerIndex;
                _resizeStartX = position.X;
                _resizeStartWidth = _columnWidths[dividerIndex];
                eventArgs.Pointer.Capture(this);
                Cursor = TrayAppDotNETCursors.SizeWestEast;
                eventArgs.Handled = true;
                return;
            }

            int columnIndex = HitTestColumn(position.X);
            if (columnIndex >= 0)
            {
                SortBy(columnIndex);
                Focus();
                eventArgs.Handled = true;
            }
            return;
        }

        if (!pointerPoint.Properties.IsLeftButtonPressed
            && !pointerPoint.Properties.IsRightButtonPressed)
        {
            return;
        }

        int rowIndex = HitTestVisibleRow(position, viewport, headerTop);
        if (rowIndex < 0) return;

        TaskManagerTableRow row = _visibleRows[rowIndex];
        bool toggledGroup = pointerPoint.Properties.IsLeftButtonPressed
                            && row.IsGroup
                            && position.X < _columnLefts[0] + _cellPadding + _treeIndentWidth;
        if (toggledGroup)
            ToggleGroup(row.Key);
        SetSelectedRowKey(row.Key);
        Focus();
        if (eventArgs.ClickCount == 2 && pointerPoint.Properties.IsLeftButtonPressed)
        {
            if (row.IsGroup && !toggledGroup)
                ToggleGroup(row.Key);
            else if (!row.IsGroup)
                RowActivated?.Invoke(row);
        }
        eventArgs.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        Point position = eventArgs.GetPosition(this);
        if (_resizingColumnIndex >= 0
            && ReferenceEquals(_capturedResizePointer, eventArgs.Pointer))
        {
            ResizeColumn(_resizingColumnIndex, _resizeStartWidth + position.X - _resizeStartX);
            eventArgs.Handled = true;
            return;
        }

        Rect viewport = ResolveDetailsGridViewport();
        double headerTop = ResolveStickyHeaderTop(viewport);
        bool isHeader = position.Y >= headerTop && position.Y < headerTop + _headerHeight;
        int nextHeaderColumn = isHeader ? HitTestColumn(position.X) : -1;
        int nextRow = isHeader ? -1 : HitTestVisibleRow(position, viewport, headerTop);
        bool visualChanged = nextHeaderColumn != _hoveredHeaderColumnIndex
                             || nextRow != _hoveredRowIndex;
        _hoveredHeaderColumnIndex = nextHeaderColumn;
        _hoveredRowIndex = nextRow;
        Cursor = isHeader && HitTestColumnDivider(position.X) >= 0
            ? TrayAppDotNETCursors.SizeWestEast
            : TrayAppDotNETCursors.Arrow;
        if (visualChanged) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_resizingColumnIndex < 0
            || !ReferenceEquals(_capturedResizePointer, eventArgs.Pointer))
        {
            return;
        }

        eventArgs.Pointer.Capture(null);
        _capturedResizePointer = null;
        _resizingColumnIndex = -1;
        Cursor = TrayAppDotNETCursors.Arrow;
        eventArgs.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        _capturedResizePointer = null;
        _resizingColumnIndex = -1;
        Cursor = TrayAppDotNETCursors.Arrow;
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        if (_resizingColumnIndex >= 0) return;

        _hoveredRowIndex = -1;
        _hoveredHeaderColumnIndex = -1;
        Cursor = TrayAppDotNETCursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.Handled || _visibleRows.Count == 0) return;

        int selectedIndex = FindVisibleRowIndex(_selectedRowKey);
        switch (eventArgs.Key)
        {
            case Key.Up:
                SelectVisibleIndex(Math.Max(0, selectedIndex < 0 ? 0 : selectedIndex - 1));
                break;
            case Key.Down:
                SelectVisibleIndex(Math.Min(
                    _visibleRows.Count - 1,
                    selectedIndex < 0 ? 0 : selectedIndex + 1));
                break;
            case Key.Home:
                SelectVisibleIndex(0);
                break;
            case Key.End:
                SelectVisibleIndex(_visibleRows.Count - 1);
                break;
            case Key.Left:
                if (SelectedRow is not { IsGroup: true } leftGroup) return;
                _collapsedGroupKeys.Add(leftGroup.Key);
                RebuildProjection(notifySelectionRemoval: false);
                break;
            case Key.Right:
                if (SelectedRow is not { IsGroup: true } rightGroup) return;
                _collapsedGroupKeys.Remove(rightGroup.Key);
                RebuildProjection(notifySelectionRemoval: false);
                break;
            case Key.Enter:
                if (SelectedRow is not { } selectedRow) return;
                if (selectedRow.IsGroup)
                    ToggleGroup(selectedRow.Key);
                else
                    RowActivated?.Invoke(selectedRow);
                break;
            default:
                return;
        }

        eventArgs.Handled = true;
    }

    protected override void ApplyDetailsGridMetrics(double fontSize, double rowHeight)
    {
        _fontSize = fontSize;
        _rowHeight = rowHeight;
        int fontWeight = CalculateFontWeight(fontSize);
        _tableTypeface = CreateTypeface(fontWeight);
        _groupTypeface = CreateTypeface(Math.Max((int)FontWeight.SemiBold, fontWeight));
    }

    protected override void OnDetailsGridMetricsChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override bool RebuildDetailsGridZoomRow(int rowIndex) => false;

    protected override void CommitDetailsGridRetainedRange(int firstRow, int lastRowExclusive)
    {
    }

    protected override void InvalidateDetailsGridRows() => InvalidateVisual();

    protected override void OnDetailsGridViewportChanged() => InvalidateVisual();

    protected override void DisposeDetailsGridResources()
    {
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
        _processIconService.IconsChanged -= OnIconsChanged;
        _capturedResizePointer?.Capture(null);
        _capturedResizePointer = null;
        SelectedRowChanged = null;
        RowActivated = null;
        SortChanged = null;
    }

    private void RebuildProjection(bool notifySelectionRemoval)
    {
        _visibleRows = TaskManagerTableProjection.Build(
            _sourceRows,
            _schema.Columns.Length,
            _sortColumnIndex,
            _sortDescending,
            _collapsedGroupKeys,
            _filterText);

        TaskManagerTableRow? selectedSourceRow = FindSourceRowByKey(_selectedRowKey);
        if (_selectedRowKey != null && selectedSourceRow == null)
        {
            _selectedRowKey = null;
            if (notifySelectionRemoval) SelectedRowChanged?.Invoke(null);
        }
        _hoveredRowIndex = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RebuildGroupIndex()
    {
        _groupKeysWithChildren.Clear();
        HashSet<string> existingGroupKeys = new(StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < _sourceRows.Count; rowIndex++)
        {
            TaskManagerTableRow row = _sourceRows[rowIndex];
            if (row.IsGroup) existingGroupKeys.Add(row.Key);
            if (row.ParentKey != null) _groupKeysWithChildren.Add(row.ParentKey);
        }

        _collapsedGroupKeys.RemoveWhere(key => !existingGroupKeys.Contains(key));
    }

    private void SortBy(int columnIndex)
    {
        if (_sortColumnIndex == columnIndex)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumnIndex = columnIndex;
            _sortDescending = _schema.Columns[columnIndex].SortDescendingByDefault;
        }

        RebuildProjection(notifySelectionRemoval: false);
        SortChanged?.Invoke(_sortColumnIndex, _sortDescending);
    }

    private void ToggleGroup(string groupKey)
    {
        if (!_groupKeysWithChildren.Contains(groupKey)) return;

        if (!_collapsedGroupKeys.Add(groupKey)) _collapsedGroupKeys.Remove(groupKey);
        RebuildProjection(notifySelectionRemoval: false);
    }

    private void SelectVisibleIndex(int rowIndex)
    {
        if ((uint)rowIndex >= (uint)_visibleRows.Count) return;
        SetSelectedRowKey(_visibleRows[rowIndex].Key);
    }

    private void SetSelectedRowKey(string? rowKey)
    {
        if (string.Equals(_selectedRowKey, rowKey, StringComparison.Ordinal)) return;

        _selectedRowKey = rowKey;
        SelectedRowChanged?.Invoke(FindRowByKey(rowKey));
        InvalidateVisual();
    }

    private TaskManagerTableRow? FindSourceRowByKey(string? rowKey)
    {
        if (rowKey == null) return null;
        for (int rowIndex = 0; rowIndex < _sourceRows.Count; rowIndex++)
        {
            TaskManagerTableRow row = _sourceRows[rowIndex];
            if (string.Equals(row.Key, rowKey, StringComparison.Ordinal)) return row;
        }
        return null;
    }

    private TaskManagerTableRow? FindRowByKey(string? rowKey) => FindSourceRowByKey(rowKey);

    private int FindVisibleRowIndex(string? rowKey)
    {
        if (rowKey == null) return -1;
        for (int rowIndex = 0; rowIndex < _visibleRows.Count; rowIndex++)
        {
            if (string.Equals(_visibleRows[rowIndex].Key, rowKey, StringComparison.Ordinal))
                return rowIndex;
        }
        return -1;
    }

    private void DrawRow(DrawingContext context, int rowIndex)
    {
        TaskManagerTableRow row = _visibleRows[rowIndex];
        double rowTop = _headerHeight + rowIndex * _rowHeight;
        Rect rowBounds = new(0, rowTop, Bounds.Width, _rowHeight);
        if (rowIndex == _hoveredRowIndex) context.FillRectangle(_rowHoverBrush, rowBounds);
        if (string.Equals(row.Key, _selectedRowKey, StringComparison.Ordinal))
        {
            context.FillRectangle(_selectionBrush, rowBounds);
            context.DrawRectangle(null, _selectionPen, rowBounds.Deflate(_selectionPen.Thickness / 2));
        }

        for (int columnIndex = 0; columnIndex < _schema.Columns.Length; columnIndex++)
            DrawCell(context, row, rowTop, columnIndex);
    }

    private void DrawCell(
        DrawingContext context,
        TaskManagerTableRow row,
        double rowTop,
        int columnIndex)
    {
        TaskManagerTableColumn column = _schema.Columns[columnIndex];
        double columnLeft = _columnLefts[columnIndex];
        double columnWidth = _columnWidths[columnIndex];
        double leftInset = _cellPadding;
        if (columnIndex == 0)
        {
            if (row.ParentKey != null) leftInset += _treeIndentWidth;
            if (row.IsGroup)
            {
                DrawGroupExpander(context, row, columnLeft, rowTop);
                leftInset += _treeIndentWidth;
            }
            if (row.IconSource.IsAvailable)
            {
                IImage? icon = _processIconService.GetOrQueue(row.IconSource);
                if (icon != null)
                {
                    double iconTop = rowTop + Math.Max(0, (_rowHeight - _iconSize) / 2);
                    context.DrawImage(
                        icon,
                        new Rect(columnLeft + leftInset, iconTop, _iconSize, _iconSize));
                }
                leftInset += _iconSize + _iconGap;
            }
        }

        double maximumWidth = Math.Max(0, columnWidth - leftInset - _cellPadding);
        IBrush textBrush = row.IsEnabled ? _foregroundBrush : _secondaryForegroundBrush;
        Typeface typeface = row.IsGroup ? _groupTypeface : _tableTypeface;
        using TextLayout text = CreateTextLayout(
            row.Cells[columnIndex].Text,
            typeface,
            _fontSize,
            textBrush,
            maximumWidth,
            column.Alignment);
        // TextLayout performs right alignment within maximumWidth, so both alignments
        // must start at the content area's left edge
        double textX = columnLeft + leftInset;
        double textY = rowTop + Math.Max(0, (_rowHeight - text.Height) / 2);
        Rect textClip = new(columnLeft, rowTop, columnWidth, _rowHeight);
        using (context.PushClip(textClip))
            text.Draw(context, new Point(textX, textY));
    }

    private void DrawGroupExpander(
        DrawingContext context,
        TaskManagerTableRow row,
        double columnLeft,
        double rowTop)
    {
        if (!_groupKeysWithChildren.Contains(row.Key)) return;

        double centerX = columnLeft + _cellPadding + _treeIndentWidth / 2;
        double centerY = rowTop + _rowHeight / 2;
        double halfWidth = _resources.AxamlProcessTable.TreeExpanderChevronHalfWidth;
        double halfHeight = _resources.AxamlProcessTable.TreeExpanderChevronHalfHeight;
        if (_collapsedGroupKeys.Contains(row.Key))
        {
            context.DrawLine(
                _expanderPen,
                new Point(centerX - halfWidth, centerY - halfHeight),
                new Point(centerX + halfWidth, centerY));
            context.DrawLine(
                _expanderPen,
                new Point(centerX + halfWidth, centerY),
                new Point(centerX - halfWidth, centerY + halfHeight));
            return;
        }

        context.DrawLine(
            _expanderPen,
            new Point(centerX - halfHeight, centerY - halfWidth),
            new Point(centerX, centerY + halfWidth));
        context.DrawLine(
            _expanderPen,
            new Point(centerX, centerY + halfWidth),
            new Point(centerX + halfHeight, centerY - halfWidth));
    }

    private void DrawColumnGrid(DrawingContext context, Rect viewport)
    {
        double gridTop = Math.Max(0, viewport.Y);
        double gridBottom = Math.Min(Bounds.Height, viewport.Bottom);
        for (int columnIndex = 0; columnIndex < _schema.Columns.Length; columnIndex++)
        {
            double right = _columnLefts[columnIndex] + _columnWidths[columnIndex];
            context.DrawLine(_gridPen, new Point(right, gridTop), new Point(right, gridBottom));
        }
    }

    private void DrawHeader(DrawingContext context, double headerTop)
    {
        Rect headerBounds = new(0, headerTop, Bounds.Width, _headerHeight);
        context.FillRectangle(_backgroundBrush, headerBounds);
        if (_hoveredHeaderColumnIndex >= 0)
        {
            Rect hoverBounds = new(
                _columnLefts[_hoveredHeaderColumnIndex],
                headerTop,
                _columnWidths[_hoveredHeaderColumnIndex],
                _headerHeight);
            context.FillRectangle(_headerHoverBrush, hoverBounds);
        }

        for (int columnIndex = 0; columnIndex < _schema.Columns.Length; columnIndex++)
        {
            TaskManagerTableColumn column = _schema.Columns[columnIndex];
            double columnLeft = _columnLefts[columnIndex];
            double columnWidth = _columnWidths[columnIndex];
            double caretReserve = columnIndex == _sortColumnIndex
                ? _resources.AxamlProcessTable.SortCaretRightMargin * 2
                : 0;
            double maximumWidth = Math.Max(0, columnWidth - _cellPadding * 2 - caretReserve);
            using TextLayout text = CreateTextLayout(
                _columnTitles[columnIndex],
                _tableTypeface,
                _headerFontSize,
                _foregroundBrush,
                maximumWidth,
                column.Alignment);
            // TextLayout performs right alignment within maximumWidth
            double textX = columnLeft + _cellPadding;
            double textY = headerTop + Math.Max(0, (_headerHeight - text.Height) / 2);
            using (context.PushClip(new Rect(columnLeft, headerTop, columnWidth, _headerHeight)))
                text.Draw(context, new Point(textX, textY));

            if (columnIndex == _sortColumnIndex)
                DrawSortCaret(context, columnLeft + columnWidth, headerTop);
        }
        context.DrawLine(
            _gridPen,
            new Point(0, headerTop + _headerHeight),
            new Point(Bounds.Width, headerTop + _headerHeight));
    }

    private void DrawSortCaret(DrawingContext context, double columnRight, double headerTop)
    {
        double centerX = columnRight - _resources.AxamlProcessTable.SortCaretRightMargin;
        double centerY = headerTop + _headerHeight / 2;
        double halfWidth = 3;
        double halfHeight = 2;
        double direction = _sortDescending ? -1 : 1;
        Point left = new(centerX - halfWidth, centerY + direction * halfHeight);
        Point middle = new(centerX, centerY - direction * halfHeight);
        Point right = new(centerX + halfWidth, centerY + direction * halfHeight);
        context.DrawLine(_expanderPen, left, middle);
        context.DrawLine(_expanderPen, middle, right);
    }

    private int HitTestVisibleRow(Point position, Rect viewport, double headerTop)
    {
        if (position.X < viewport.X
            || position.X >= viewport.Right
            || position.Y < viewport.Y
            || position.Y >= viewport.Bottom
            || position.Y >= headerTop && position.Y < headerTop + _headerHeight)
        {
            return -1;
        }

        return DetailsGridLayout.HitTestRow(
            position.Y,
            _visibleRows.Count,
            _headerHeight,
            _rowHeight);
    }

    private int HitTestColumn(double x)
    {
        if (!double.IsFinite(x) || x < 0) return -1;

        for (int columnIndex = 0; columnIndex < _columnWidths.Length; columnIndex++)
        {
            double left = _columnLefts[columnIndex];
            if (x >= left && x < left + _columnWidths[columnIndex]) return columnIndex;
        }
        return -1;
    }

    private int HitTestColumnDivider(double x)
    {
        double hitRadius = _resources.AxamlProcessTable.ColumnResizeHitRadius;
        for (int columnIndex = 0; columnIndex < _columnWidths.Length; columnIndex++)
        {
            double right = _columnLefts[columnIndex] + _columnWidths[columnIndex];
            if (Math.Abs(x - right) <= hitRadius) return columnIndex;
        }
        return -1;
    }

    private void ResizeColumn(int columnIndex, double width)
    {
        TaskManagerTableColumn column = _schema.Columns[columnIndex];
        double nextWidth = Math.Max(column.MinimumWidth, width);
        if (Math.Abs(_columnWidths[columnIndex] - nextWidth) < 0.01) return;

        _columnWidths[columnIndex] = nextWidth;
        RecalculateColumnLefts();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RecalculateColumnLefts()
    {
        double left = 0;
        for (int columnIndex = 0; columnIndex < _columnWidths.Length; columnIndex++)
        {
            _columnLefts[columnIndex] = left;
            left += _columnWidths[columnIndex];
        }
    }

    private double GetContentWidth() =>
        _columnWidths.Length == 0
            ? 0
            : _columnLefts[^1] + _columnWidths[^1];

    private double ResolveStickyHeaderTop(Rect viewport) =>
        Math.Clamp(viewport.Y, 0, Math.Max(0, Bounds.Height - _headerHeight));

    private void ApplyResources(TaskManagerWindowResources resources)
    {
        _headerHeight = resources.AxamlProcessTable.HeaderHeight;
        _headerFontSize = resources.AxamlProcessTable.HeaderFontSize;
        _cellPadding = resources.AxamlProcessTable.CellPadding;
        _iconSize = resources.AxamlProcessTable.ProcessIconSize;
        _iconGap = resources.AxamlProcessTable.ProcessIconGap;
        _treeIndentWidth = resources.AxamlProcessTable.TreeIndentWidth;
        _gridLineThickness = resources.AxamlProcessTable.GridLineThickness;
        _gridPen = new Pen(_borderBrush, _gridLineThickness);
        _selectionPen = new Pen(
            _accentBrush,
            resources.AxamlProcessTable.SelectionBorderThickness.Left);
        _expanderPen = new Pen(
            _secondaryForegroundBrush,
            resources.AxamlProcessTable.TreeExpanderLineThickness);
    }

    private void OnAXAMLResourcesReloaded()
    {
        if (IsDetailsGridDisposed) return;
        ApplyResources(_resources);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnIconsChanged()
    {
        if (!IsDetailsGridDisposed) InvalidateVisual();
    }

    private int CalculateFontWeight(double fontSize) =>
        ProcessTableLayout.CalculateZoomFontWeight(
            _baseFontWeight,
            AppSettings.GridFontSizeDefault,
            fontSize);

    private static Typeface CreateTypeface(int fontWeight) =>
        new(DefaultTypeface.FontFamily, FontStyle.Normal, (FontWeight)fontWeight);

    private static double MeasureRowTextHeightScale(Typeface typeface)
    {
        using TextLayout measurement = new(
            MeasurementText,
            typeface,
            AppSettings.GridFontSizeDefault,
            Brushes.White,
            textWrapping: TextWrapping.NoWrap,
            maxLines: 1);
        return measurement.Height / AppSettings.GridFontSizeDefault;
    }

    private static TextLayout CreateTextLayout(
        string value,
        Typeface typeface,
        double fontSize,
        IBrush brush,
        double maximumWidth,
        TaskManagerTableAlignment alignment) =>
        new(
            LimitText(value),
            typeface,
            fontSize,
            brush,
            textAlignment: alignment == TaskManagerTableAlignment.Right
                ? TextAlignment.Right
                : TextAlignment.Left,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: Math.Max(0, maximumWidth),
            maxLines: 1);

    private static string LimitText(string value)
    {
        if (value.Length <= MaximumTextLayoutCharacters) return value;

        int prefixLength = MaximumTextLayoutCharacters - TextEllipsis.Length;
        return string.Concat(value.AsSpan(0, prefixLength), TextEllipsis.AsSpan());
    }
}

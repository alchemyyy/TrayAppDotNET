using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

internal enum ProcessCopyPreviewMode : byte
{
    None,
    Cell,
    Row
}

internal readonly record struct ProcessRowContextMenuRequest(
    ProcessTerminationTarget Target,
    PixelPoint ScreenPosition,
    string CellCopyText,
    string RowCopyText);

/// <summary>Composites two retained drawing roots per process from shared visible-column fragments.</summary>
internal sealed class ProcessDetailsCanvas : Control, IDisposable
{
    private const int DynamicRefreshBatchSize = 16;
    private const string ZeroText = "0";
    private const string ZeroMemoryText = "0 K";
    private const string ZeroCPUTimeText = "0:00:00";

    private static readonly Typeface TableTypeface = new(TADNFontResolver.SegoeUIFamilyName);
    private static readonly Typeface GlyphTypeface = new(TADNFontResolver.SegoeFluentIconsFamilyName);
    private static readonly CultureInfo TableCulture = CultureInfo.CurrentCulture;

    private readonly ProcessIconService _processIconService;
    private readonly ProcessDataSchema _schema;
    private readonly TaskManagerWindowResources _resources;
    private ProcessTableMetrics _metrics;
    private ProcessTableVisualMetrics _visualMetrics;
    private readonly bool _hasDynamicColumns;
    private readonly bool _enableLiveColumnResizing;
    private readonly ProcessSnapshotBuffer _snapshot = new();
    private readonly Dictionary<ProcessInstanceKey, ProcessRowRenderCache> _renderCaches = new(256);
    private readonly Dictionary<ProcessSharedCellKey, SharedCellDrawing> _sharedCellDrawings = new();
    private readonly List<SharedCellDrawing> _sharedCellBuffer = new(8);
    private readonly List<ProcessInstanceKey> _staleProcessKeys = new(256);
    private readonly HashSet<ProcessInstanceKey> _collapsedProcesses = [];
    private readonly Dictionary<int, int> _rowIndexByProcessID = new(1_024);
    private readonly ProcessRowIndexComparer _rowComparer;
    private FormattedText _ascendingCaretText;
    private FormattedText _descendingCaretText;
    private readonly IBrush _backgroundBrush;
    private readonly IBrush _foregroundBrush;
    private readonly IBrush _secondaryForegroundBrush;
    private readonly IBrush _accentBrush;
    private readonly IBrush _hoverBrush;
    private readonly IBrush _borderBrush;
    private Pen _gridPen;
    private Pen _columnInteractionPen;
    private Pen _textUnderlinePen;
    private Pen _treeExpanderPen;
    private double _sortCaretRightMargin;
    private readonly long _totalPhysicalMemoryBytes;
    private readonly Action _refreshWarmDynamicDrawings;
    private readonly ProcessTableColumn[]? _liveResizeColumns;
    private readonly TextUnderlineSegment[] _textUnderlineSegments;
    private readonly string?[] _contextCopyValuesByColumn;
    private List<ProcessColumnSetting> _columnSettings;
    private ProcessColumnSetting[] _settingsByColumn;
    private ProcessTableColumn[] _columns;
    private FormattedText[] _headerTexts;
    private int[] _visibleRowIndexes = [];
    private int[] _treeOrderBuffer = [];
    private int[] _treeParentIndexes = [];
    private int[] _treeChildCounts = [];
    private int[] _treeChildStarts = [];
    private int[] _treeChildWriteOffsets = [];
    private int[] _treeChildren = [];
    private int[] _treeStackRows = [];
    private byte[] _treeStackDepths = [];
    private bool[] _treeStackHidden = [];
    private byte[] _treeVisited = [];
    private byte[] _rowDepths = [];
    private bool[] _rowHasChildren = [];
    private int[] _warmProcessIDs = [];
    private int _rowCount;
    private int _visibleRowCount;
    private int _cacheGeneration;
    private int _filterProcessID = -1;
    private int _warmRefreshCursor;
    private int _warmRefreshEnd;
    private long _snapshotVersion = -1;
    private string _filterText = string.Empty;
    private string _unavailableText;
    private Rect _effectiveViewport;
    private ProcessTableColumnKind _sortColumn = ProcessTableColumnKind.Name;
    private ProcessInstanceKey? _selectedProcess;
    private IPointer? _capturedHeaderPointer;
    private HeaderInteractionMode _headerInteraction;
    private Point _headerPressPosition;
    private int _interactionColumnIndex = -1;
    private int _reorderInsertionIndex = -1;
    private int _hoveredVisibleIndex = -1;
    private int _hoveredHeaderColumnIndex = -1;
    private int _textPreviewVisibleIndex = -1;
    private int _textUnderlineSegmentCount;
    private double _resizeInitialWidth;
    private double _resizePreviewWidth;
    private double _headerDragX;
    private double _headerPointerOffsetX;
    private double _pointerViewportY;
    private ProcessInstanceKey? _contextCopyProcess;
    private ProcessTableColumnKind? _contextCopyColumn;
    private ProcessCopyPreviewMode _copyPreviewMode;
    private bool _sortDescending;
    private bool _pointerInside;
    private bool _isLiveColumnResizeActive;
    private bool _dynamicRefreshScheduled;
    private bool _groupProcesses;
    private bool _usesAXAMLFontSize;
    private bool _usesAXAMLRowHeight;
    private bool _disposed;
    private ProcessSnapshotService? _snapshotService;

    public ProcessDetailsCanvas(
        ProcessIconService processIconService,
        ProcessDataSchema schema,
        IReadOnlyList<ProcessColumnSetting> columnSettings,
        bool enableLiveColumnResizing,
        double gridFontSize,
        double gridRowHeight,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(processIconService);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(columnSettings);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);

        _processIconService = processIconService;
        _processIconService.IconsChanged += OnIconsChanged;
        _schema = schema;
        _resources = resources;
        _metrics = CreateTableMetrics(resources, gridFontSize, gridRowHeight);
        _visualMetrics = CreateVisualMetrics(resources);
        _usesAXAMLFontSize = Math.Abs(gridFontSize - resources.AxamlProcessTable.FontSize) < 0.01;
        _usesAXAMLRowHeight = Math.Abs(gridRowHeight - resources.AxamlProcessTable.RowHeight) < 0.01;
        _columnSettings = ProcessColumnSettings.Normalize(columnSettings);
        _settingsByColumn = CreateColumnSettingsIndex(_columnSettings);
        _columns = CreateColumns(_columnSettings);
        _hasDynamicColumns = ContainsLifetime(_columns, ProcessTableColumnLifetime.Dynamic);
        _enableLiveColumnResizing = enableLiveColumnResizing;
        _liveResizeColumns = enableLiveColumnResizing
            ? new ProcessTableColumn[_columns.Length]
            : null;
        _textUnderlineSegments = new TextUnderlineSegment[_columns.Length];
        _contextCopyValuesByColumn = new string?[ProcessTableColumnCatalog.Definitions.Length];
        _rowComparer = new ProcessRowIndexComparer(_snapshot, _schema);
        _sortCaretRightMargin = _visualMetrics.SortCaretRightMargin;
        _totalPhysicalMemoryBytes = NativeProcessInfo.ReadTotalPhysicalMemoryBytes();
        _refreshWarmDynamicDrawings = RefreshWarmDynamicDrawings;
        _unavailableText = ResolveUnavailableText();

        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(TaskManagerWindowResources.ProcessGridBackgroundColor);
        _foregroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _secondaryForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        _accentBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent);
        _hoverBrush = TrayAppDotNETSettingsUI.Brush(palette.Hover);
        _borderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border);
        _gridPen = new Pen(_borderBrush, _visualMetrics.GridLineThickness);
        _columnInteractionPen = new Pen(
            _accentBrush,
            _visualMetrics.ColumnInteractionLineThickness);
        _textUnderlinePen = new Pen(
            _foregroundBrush,
            _visualMetrics.TextUnderlineThickness);
        _treeExpanderPen = new Pen(
            _secondaryForegroundBrush,
            _visualMetrics.TreeExpanderLineThickness);

        _headerTexts = CreateHeaderTexts(_columns);

        _ascendingCaretText = CreateGlyphText(
            "\uE96D",
            _visualMetrics.SortCaretFontSize,
            _secondaryForegroundBrush);
        _descendingCaretText = CreateGlyphText(
            "\uE96E",
            _visualMetrics.SortCaretFontSize,
            _secondaryForegroundBrush);

        ClipToBounds = true;
        Focusable = true;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        LocalizationManager.Instance.CultureChanged += OnCultureChanged;
    }

    public event Action<ProcessTerminationTarget?>? SelectedProcessChanged;
    public event Action<double?>? HoverRowTopChanged;
    public event Action<double?>? SelectionRowTopChanged;
    public event Action<ProcessTableColumnKind>? ColumnPropertiesRequested;
    public event Action<List<ProcessColumnSetting>>? ColumnLayoutChanged;
    public event Action<double, double>? GridMetricsChanged;
    public event Action<int>? GridZoomRequested;
    public event Action? GridZoomResetRequested;
    public event Action<ProcessRowContextMenuRequest>? RowContextMenuRequested;

    private ProcessTableColumn[] DisplayColumns =>
        _isLiveColumnResizeActive ? _liveResizeColumns! : _columns;

    public int? SelectedProcessID => _selectedProcess?.ProcessID;

    public ProcessTerminationTarget? SelectedTerminationTarget => _selectedProcess is { } process
        ? new ProcessTerminationTarget(process.ProcessID, process.CreationTimeTicks)
        : null;

    /// <summary>Copies the newest compact snapshot and updates only changed retained row roots.</summary>
    public void RefreshFrom(ProcessSnapshotService snapshotService)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshotService);

        _snapshotService ??= snapshotService;
        int count = snapshotService.CopyLatest(_snapshot, _schema.VisibleMask, out long version);
        if (version == _snapshotVersion) return;

        _snapshotVersion = version;
        _rowCount = count;
        EnsureRowCapacity(count);
        SynchronizeRenderCacheMembership();
        RebuildVisibleRows();
        PublishWarmProcesses();
        EnsureSelectedProcessStillExists();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
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
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        double contentWidth = columns.Length == 0 ? 0 : columns[^1].Right;
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(contentWidth, availableSize.Width)
            : contentWidth;
        return new Size(width, ProcessTableLayout.GetContentHeight(_visibleRowCount, _metrics));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_disposed || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Keep every row and column position in Avalonia's render-data hit-test surface
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        Rect viewport = ResolveViewport();
        double stickyHeaderTop = Math.Clamp(viewport.Y, 0, Math.Max(0, Bounds.Height - _metrics.HeaderHeight));
        ProcessTableLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics,
            out int firstRow,
            out int lastRowExclusive);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
            DrawRetainedRow(context, viewport, visibleIndex);

        DrawCopyPreviewUnderline(context);
        DrawColumnGrid(context, viewport);
        DrawHeader(context, stickyHeaderTop);
        DrawHeaderInteraction(context, viewport, stickyHeaderTop);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (_headerInteraction != HeaderInteractionMode.None) return;

        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        Point position = eventArgs.GetPosition(this);
        bool isHeader = IsHeaderPosition(position.Y);
        if (pointerPoint.Properties.IsRightButtonPressed && isHeader)
        {
            int columnIndex = ProcessTableLayout.HitTestColumn(position.X, DisplayColumns);
            if (columnIndex >= 0)
            {
                ColumnPropertiesRequested?.Invoke(DisplayColumns[columnIndex].Kind);
                eventArgs.Handled = true;
            }
            return;
        }

        if (pointerPoint.Properties.IsRightButtonPressed)
        {
            int contextVisibleIndex = ProcessTableLayout.HitTestRow(position.Y, _visibleRowCount, _metrics);
            SelectVisibleRow(contextVisibleIndex);
            Focus();
            if (contextVisibleIndex >= 0 && SelectedTerminationTarget is { } target)
            {
                int contextColumnIndex = ProcessTableLayout.HitTestColumn(position.X, DisplayColumns);
                ProcessRowContextMenuRequest request = CreateRowContextMenuRequest(
                    target,
                    this.PointToScreen(position),
                    contextVisibleIndex,
                    contextColumnIndex);
                RowContextMenuRequested?.Invoke(request);
            }
            eventArgs.Handled = contextVisibleIndex >= 0;
            return;
        }

        if (pointerPoint.Properties.IsMiddleButtonPressed
            && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            GridZoomResetRequested?.Invoke();
            eventArgs.Handled = true;
            return;
        }

        if (!pointerPoint.Properties.IsLeftButtonPressed) return;
        if (isHeader)
        {
            int dividerColumnIndex = ProcessTableLayout.HitTestColumnDivider(
                position.X,
                _columns,
                _visualMetrics.ColumnResizeHitRadius);
            if (dividerColumnIndex >= 0)
            {
                BeginHeaderInteraction(
                    eventArgs.Pointer,
                    HeaderInteractionMode.Resizing,
                    dividerColumnIndex,
                    position);
                Cursor = TrayAppDotNETCursors.SizeWestEast;
                eventArgs.Handled = true;
                return;
            }

            int columnIndex = ProcessTableLayout.HitTestColumn(position.X, _columns);
            if (columnIndex >= 0)
            {
                BeginHeaderInteraction(
                    eventArgs.Pointer,
                    HeaderInteractionMode.PendingReorder,
                    columnIndex,
                    position);
                eventArgs.Handled = true;
            }

            return;
        }

        int visibleIndex = ProcessTableLayout.HitTestRow(position.Y, _visibleRowCount, _metrics);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && TryToggleTreeExpander(position, visibleIndex))
        {
            eventArgs.Handled = true;
            return;
        }

        SelectVisibleRow(visibleIndex);
        Focus();
        eventArgs.Handled = visibleIndex >= 0;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        Point position = eventArgs.GetPosition(this);
        if (_headerInteraction != HeaderInteractionMode.None
            && ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer))
        {
            MoveHeaderInteraction(position);
            eventArgs.Handled = true;
            return;
        }

        _pointerInside = true;
        _pointerViewportY = position.Y - Math.Max(0, _effectiveViewport.Y);
        UpdateHeaderCursor(position);
        UpdateHoveredHeader(position);
        UpdateHoveredRow(position.Y);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        base.OnPointerWheelChanged(eventArgs);
        if (!eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) || eventArgs.Delta.Y == 0) return;

        GridZoomRequested?.Invoke(eventArgs.Delta.Y > 0 ? 1 : -1);
        eventArgs.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_headerInteraction == HeaderInteractionMode.None
            || !ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer))
        {
            return;
        }

        Point position = eventArgs.GetPosition(this);
        HeaderInteractionMode completedInteraction = _headerInteraction;
        int columnIndex = _interactionColumnIndex;
        int insertionIndex = _reorderInsertionIndex;
        double width = _resizePreviewWidth;
        bool sortColumn = completedInteraction == HeaderInteractionMode.PendingReorder
                          && IsHeaderPosition(position.Y)
                          && ProcessTableLayout.HitTestColumn(position.X, _columns) == columnIndex;
        ResetHeaderInteraction();

        switch (completedInteraction)
        {
            case HeaderInteractionMode.PendingReorder when sortColumn:
                SortFromHeader(position.X);
                break;
            case HeaderInteractionMode.Resizing:
                CommitColumnResize(columnIndex, width);
                break;
            case HeaderInteractionMode.Reordering:
                CommitColumnReorder(columnIndex, insertionIndex);
                break;
        }

        _pointerInside = new Rect(Bounds.Size).Contains(position);
        _pointerViewportY = position.Y - Math.Max(0, _effectiveViewport.Y);
        UpdateHeaderCursor(position);
        UpdateHoveredHeader(position);
        UpdateHoverFromPointer();
        eventArgs.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        if (!ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer)) return;

        _capturedHeaderPointer = null;
        ClearHeaderInteractionState();
        Cursor = TrayAppDotNETCursors.Arrow;
        InvalidateVisual();
    }

    /// <summary>Switches between a flat sorted list and an allocation-free parent-process tree.</summary>
    public void SetGroupProcesses(bool groupProcesses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_groupProcesses == groupProcesses) return;

        _groupProcesses = groupProcesses;
        RebuildVisibleRows();
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Shows the copy target preview requested by the active row context menu.</summary>
    public void SetContextCopyPreview(ProcessCopyPreviewMode previewMode)
    {
        if (_disposed || _copyPreviewMode == previewMode) return;

        _copyPreviewMode = previewMode;
        RebuildCopyPreview();
        InvalidateVisual();
    }

    /// <summary>Rebuilds retained row text at a new font size and row height.</summary>
    public void SetGridMetrics(double fontSize, double rowHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(rowHeight) || rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        _usesAXAMLFontSize = Math.Abs(fontSize - _resources.AxamlProcessTable.FontSize) < 0.01;
        _usesAXAMLRowHeight = Math.Abs(rowHeight - _resources.AxamlProcessTable.RowHeight) < 0.01;
        if (Math.Abs(_metrics.FontSize - fontSize) < 0.01
            && Math.Abs(_metrics.RowHeight - rowHeight) < 0.01)
        {
            return;
        }

        _metrics = _metrics with { FontSize = fontSize, RowHeight = rowHeight };
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellDrawings.Clear();
        UpdateRetainedDrawings();
        GridMetricsChanged?.Invoke(fontSize, rowHeight);
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Applies one column's display properties without changing the visible schema.</summary>
    public void ApplyColumnProperties(ProcessColumnSetting replacement)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        ApplyColumnLayout(ProcessColumnSettings.WithProperties(_columnSettings, replacement));
    }

    /// <summary>Returns an independent copy of one current column setting.</summary>
    public ProcessColumnSetting GetColumnSetting(ProcessTableColumnKind column)
    {
        for (int settingIndex = 0; settingIndex < _columnSettings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = _columnSettings[settingIndex];
            if (setting.Column == column) return ProcessColumnSettings.Clone(setting);
        }

        throw new ArgumentOutOfRangeException(nameof(column));
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        _pointerInside = false;
        SetHoveredVisibleIndex(-1);
        SetHoveredHeaderColumnIndex(-1);
        if (_headerInteraction == HeaderInteractionMode.None)
            Cursor = TrayAppDotNETCursors.Arrow;
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.Key != Key.Escape || _headerInteraction == HeaderInteractionMode.None) return;

        ResetHeaderInteraction();
        eventArgs.Handled = true;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs eventArgs)
    {
        _effectiveViewport = eventArgs.EffectiveViewport;
        UpdateHoverFromPointer();
        PublishWarmProcesses();
        ScheduleWarmDynamicRefresh();
        InvalidateVisual();
    }

    private bool IsHeaderPosition(double positionY)
    {
        double stickyHeaderTop = Math.Max(0, _effectiveViewport.Y);
        return positionY >= stickyHeaderTop
               && positionY < stickyHeaderTop + _metrics.HeaderHeight;
    }

    private void BeginHeaderInteraction(
        IPointer pointer,
        HeaderInteractionMode interaction,
        int columnIndex,
        Point position)
    {
        _capturedHeaderPointer = pointer;
        _headerInteraction = interaction;
        _interactionColumnIndex = columnIndex;
        _headerPressPosition = position;
        _headerDragX = position.X;
        _headerPointerOffsetX = position.X - _columns[columnIndex].Left;
        _resizeInitialWidth = _columns[columnIndex].Width;
        _resizePreviewWidth = _resizeInitialWidth;
        _reorderInsertionIndex = columnIndex;
        SetHoveredVisibleIndex(-1);
        Focus();

        try
        {
            pointer.Capture(this);
        }
        catch (Exception exception)
        {
            _capturedHeaderPointer = null;
            ClearHeaderInteractionState();
            Cursor = TrayAppDotNETCursors.Arrow;
            TADNLog.Log($"ProcessDetailsCanvas header pointer capture failed: {exception.Message}");
        }
    }

    private void MoveHeaderInteraction(Point position)
    {
        switch (_headerInteraction)
        {
            case HeaderInteractionMode.PendingReorder:
            {
                double horizontalDistance = Math.Abs(position.X - _headerPressPosition.X);
                double verticalDistance = Math.Abs(position.Y - _headerPressPosition.Y);
                if (horizontalDistance < _visualMetrics.HeaderDragThreshold
                    && verticalDistance < _visualMetrics.HeaderDragThreshold)
                {
                    return;
                }

                _headerInteraction = HeaderInteractionMode.Reordering;
                Cursor = TrayAppDotNETCursors.SizeAll;
                goto case HeaderInteractionMode.Reordering;
            }
            case HeaderInteractionMode.Resizing:
            {
                double nextWidth = Math.Max(
                    ProcessColumnSettings.MinimumWidth,
                    _resizeInitialWidth + position.X - _headerPressPosition.X);
                if (Math.Abs(nextWidth - _resizePreviewWidth) < 0.01) return;

                _resizePreviewWidth = nextWidth;
                if (_enableLiveColumnResizing && _liveResizeColumns != null)
                {
                    ProcessTableLayout.WriteResizedColumns(
                        _columns,
                        _interactionColumnIndex,
                        nextWidth,
                        _liveResizeColumns);
                    _isLiveColumnResizeActive = true;
                    InvalidateMeasure();
                }

                InvalidateVisual();
                return;
            }
            case HeaderInteractionMode.Reordering:
            {
                int nextInsertionIndex = ProcessTableLayout.GetReorderInsertionIndex(
                    position.X,
                    _columns,
                    _interactionColumnIndex);
                bool changed = nextInsertionIndex != _reorderInsertionIndex
                               || Math.Abs(position.X - _headerDragX) >= 0.01;
                _reorderInsertionIndex = nextInsertionIndex;
                _headerDragX = position.X;
                if (changed) InvalidateVisual();
                return;
            }
        }
    }

    private void UpdateHeaderCursor(Point position)
    {
        Cursor = IsHeaderPosition(position.Y)
                 && ProcessTableLayout.HitTestColumnDivider(
                     position.X,
                     _columns,
                     _visualMetrics.ColumnResizeHitRadius) >= 0
            ? TrayAppDotNETCursors.SizeWestEast
            : TrayAppDotNETCursors.Arrow;
    }

    private void UpdateHoveredHeader(Point position)
    {
        int columnIndex = IsHeaderPosition(position.Y)
            ? ProcessTableLayout.HitTestColumn(position.X, DisplayColumns)
            : -1;
        SetHoveredHeaderColumnIndex(columnIndex);
    }

    private void SetHoveredHeaderColumnIndex(int columnIndex)
    {
        if (_hoveredHeaderColumnIndex == columnIndex) return;
        _hoveredHeaderColumnIndex = columnIndex;
        InvalidateVisual();
    }

    private void ResetHeaderInteraction()
    {
        IPointer? pointer = _capturedHeaderPointer;
        _capturedHeaderPointer = null;
        ClearHeaderInteractionState();
        Cursor = TrayAppDotNETCursors.Arrow;
        if (pointer != null)
        {
            try
            {
                pointer.Capture(null);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessDetailsCanvas header pointer release failed: {exception.Message}");
            }
        }

        InvalidateVisual();
    }

    private void ClearHeaderInteractionState()
    {
        if (_isLiveColumnResizeActive)
        {
            _isLiveColumnResizeActive = false;
            InvalidateMeasure();
        }

        _headerInteraction = HeaderInteractionMode.None;
        _interactionColumnIndex = -1;
        _reorderInsertionIndex = -1;
        _resizeInitialWidth = 0;
        _resizePreviewWidth = 0;
    }

    private void OnIconsChanged()
    {
        if (!_disposed) InvalidateVisual();
    }

    private void OnAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        ProcessTableMetrics nextMetrics = CreateTableMetrics(
            _resources,
            _usesAXAMLFontSize ? _resources.AxamlProcessTable.FontSize : _metrics.FontSize,
            _usesAXAMLRowHeight ? _resources.AxamlProcessTable.RowHeight : _metrics.RowHeight);
        ProcessTableVisualMetrics nextVisualMetrics = CreateVisualMetrics(_resources);
        if (nextMetrics == _metrics && nextVisualMetrics == _visualMetrics) return;

        bool rebuildRetainedRows = RetainedRowGeometryChanged(
            _metrics,
            nextMetrics,
            _visualMetrics,
            nextVisualMetrics);
        bool rebuildHeaderText = _metrics.HeaderFontSize != nextMetrics.HeaderFontSize
                                 || _metrics.CellPadding != nextMetrics.CellPadding;
        bool rebuildCaretText = _visualMetrics.SortCaretFontSize
                                != nextVisualMetrics.SortCaretFontSize;
        bool gridMetricsChanged = _metrics.FontSize != nextMetrics.FontSize
                                  || _metrics.RowHeight != nextMetrics.RowHeight;

        _metrics = nextMetrics;
        _visualMetrics = nextVisualMetrics;
        _sortCaretRightMargin = nextVisualMetrics.SortCaretRightMargin;
        RecreatePens();
        if (rebuildHeaderText)
            _headerTexts = CreateHeaderTexts(_columns);
        if (rebuildCaretText)
        {
            _ascendingCaretText = CreateGlyphText(
                "\uE96D",
                _visualMetrics.SortCaretFontSize,
                _secondaryForegroundBrush);
            _descendingCaretText = CreateGlyphText(
                "\uE96E",
                _visualMetrics.SortCaretFontSize,
                _secondaryForegroundBrush);
        }

        if (rebuildRetainedRows)
            RebuildRetainedRowDrawings();
        if (gridMetricsChanged)
            GridMetricsChanged?.Invoke(_metrics.FontSize, _metrics.RowHeight);
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        PublishWarmProcesses();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;

        string unavailableText = ResolveUnavailableText();
        if (string.Equals(_unavailableText, unavailableText, StringComparison.Ordinal)) return;

        _unavailableText = unavailableText;
        RebuildRetainedRowDrawings();
        RebuildCopyPreview();
        InvalidateVisual();
    }

    private void RecreatePens()
    {
        _gridPen = new Pen(_borderBrush, _visualMetrics.GridLineThickness);
        _columnInteractionPen = new Pen(
            _accentBrush,
            _visualMetrics.ColumnInteractionLineThickness);
        _textUnderlinePen = new Pen(
            _foregroundBrush,
            _visualMetrics.TextUnderlineThickness);
        _treeExpanderPen = new Pen(
            _secondaryForegroundBrush,
            _visualMetrics.TreeExpanderLineThickness);
    }

    private void RebuildRetainedRowDrawings()
    {
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellDrawings.Clear();
        UpdateRetainedDrawings();
    }

    private string LocalizeUnavailableText(string value) =>
        string.Equals(value, NativeProcessInfo.Unavailable, StringComparison.Ordinal)
            ? _unavailableText
            : value;

    private static string ResolveUnavailableText() =>
        LocalizationManager.Instance[nameof(CommonStrings.Common_Unavailable)];

    private static bool RetainedRowGeometryChanged(
        ProcessTableMetrics currentMetrics,
        ProcessTableMetrics nextMetrics,
        ProcessTableVisualMetrics currentVisualMetrics,
        ProcessTableVisualMetrics nextVisualMetrics) =>
        currentMetrics.RowHeight != nextMetrics.RowHeight
        || currentMetrics.CellPadding != nextMetrics.CellPadding
        || currentMetrics.FontSize != nextMetrics.FontSize
        || currentMetrics.ProcessIconSize != nextMetrics.ProcessIconSize
        || currentMetrics.ProcessIconGap != nextMetrics.ProcessIconGap
        || currentVisualMetrics.RowTextHeightMultiplier
        != nextVisualMetrics.RowTextHeightMultiplier
        || currentVisualMetrics.TreeIndentWidth != nextVisualMetrics.TreeIndentWidth
        || currentVisualMetrics.TreeExpanderWidth != nextVisualMetrics.TreeExpanderWidth;

    private Rect ResolveViewport()
    {
        if (_effectiveViewport.Width > 0 && _effectiveViewport.Height > 0)
        {
            double left = Math.Clamp(_effectiveViewport.X, 0, Bounds.Width);
            double right = Math.Clamp(_effectiveViewport.Right, left, Bounds.Width);
            double top = Math.Clamp(_effectiveViewport.Y, 0, Bounds.Height);
            double bottom = Math.Clamp(_effectiveViewport.Bottom, top, Bounds.Height);
            return new Rect(left, top, right - left, bottom - top);
        }

        return new Rect(
            0,
            0,
            Bounds.Width,
            Math.Min(Bounds.Height, _visualMetrics.DefaultViewportHeight));
    }

    private void DrawRetainedRow(DrawingContext context, Rect viewport, int visibleIndex)
    {
        int rowIndex = _visibleRowIndexes[visibleIndex];
        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
            return;

        double top = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
        using (context.PushTransform(Matrix.CreateTranslation(0, top)))
        {
            if (_isLiveColumnResizeActive && _liveResizeColumns != null)
                DrawLiveResizedRow(context, cache, _liveResizeColumns);
            else
                DrawRetainedRowDrawings(context, cache);
        }

        DrawProcessIcon(context, viewport, rowIndex, row, top);
    }

    private void DrawCopyPreviewUnderline(DrawingContext context)
    {
        if (_textUnderlineSegmentCount == 0
            || _textPreviewVisibleIndex < 0
            || _textPreviewVisibleIndex >= _visibleRowCount)
        {
            return;
        }

        double top = _metrics.HeaderHeight + _textPreviewVisibleIndex * _metrics.RowHeight;
        using (context.PushTransform(Matrix.CreateTranslation(0, top)))
        {
            for (int segmentIndex = 0; segmentIndex < _textUnderlineSegmentCount; segmentIndex++)
            {
                TextUnderlineSegment segment = _textUnderlineSegments[segmentIndex];
                context.DrawLine(
                    _textUnderlinePen,
                    new Point(segment.Left, segment.Y),
                    new Point(segment.Right, segment.Y));
            }
        }
    }

    private ProcessRowContextMenuRequest CreateRowContextMenuRequest(
        ProcessTerminationTarget target,
        PixelPoint screenPosition,
        int visibleIndex,
        int columnIndex)
    {
        int rowIndex = _visibleRowIndexes[visibleIndex];
        ProcessStaticData row = _snapshot.StaticRows[rowIndex]
            ?? throw new InvalidOperationException("A published process row is missing static data.");
        ProcessTableColumn[] columns = DisplayColumns;
        _contextCopyProcess = row.InstanceKey;
        _contextCopyColumn = (uint)columnIndex < (uint)columns.Length
            ? columns[columnIndex].Kind
            : null;
        _copyPreviewMode = ProcessCopyPreviewMode.None;
        _textPreviewVisibleIndex = -1;
        _textUnderlineSegmentCount = 0;
        InvalidateVisual();
        EnsureDynamicDrawingCurrent(rowIndex, row);

        Array.Clear(_contextCopyValuesByColumn);
        for (int visibleColumnIndex = 0; visibleColumnIndex < columns.Length; visibleColumnIndex++)
        {
            ProcessTableColumnKind kind = columns[visibleColumnIndex].Kind;
            _contextCopyValuesByColumn[(int)kind] = GetCellDisplayValue(rowIndex, kind);
        }

        string cellCopyText = _contextCopyColumn.HasValue
            ? _contextCopyValuesByColumn[(int)_contextCopyColumn.Value] ?? string.Empty
            : string.Empty;
        return new ProcessRowContextMenuRequest(
            target,
            screenPosition,
            cellCopyText,
            CreateRowCopyText(columns));
    }

    private string CreateRowCopyText(ProcessTableColumn[] columns)
    {
        StringBuilder copyText = new();
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columnIndex > 0) copyText.Append(',');
            string display = _contextCopyValuesByColumn[(int)columns[columnIndex].Kind] ?? string.Empty;
            AppendCSVField(copyText, display);
        }
        return copyText.ToString();
    }

    private void RebuildCopyPreview()
    {
        _textUnderlineSegmentCount = 0;
        _textPreviewVisibleIndex = -1;
        if (_copyPreviewMode == ProcessCopyPreviewMode.None || !_contextCopyProcess.HasValue) return;

        int visibleIndex = FindVisibleProcess(_contextCopyProcess.Value);
        if (visibleIndex < 0) return;

        int rowIndex = _visibleRowIndexes[visibleIndex];
        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null) return;
        _textPreviewVisibleIndex = visibleIndex;

        ProcessTableColumn[] columns = DisplayColumns;
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        if (_copyPreviewMode == ProcessCopyPreviewMode.Row)
        {
            for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                string display = _contextCopyValuesByColumn[(int)columns[columnIndex].Kind] ?? string.Empty;
                AddTextUnderlineSegment(columns[columnIndex], display, treeLayoutKey);
            }
            return;
        }

        if (!_contextCopyColumn.HasValue) return;
        int previewColumnIndex = FindColumn(columns, _contextCopyColumn.Value);
        if (previewColumnIndex < 0) return;
        ProcessTableColumn column = columns[previewColumnIndex];
        string cellDisplay = _contextCopyValuesByColumn[(int)column.Kind] ?? string.Empty;
        AddTextUnderlineSegment(column, cellDisplay, treeLayoutKey);
    }

    private void EnsureDynamicDrawingCurrent(int rowIndex, ProcessStaticData row)
    {
        if (!_hasDynamicColumns
            || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache)
            || cache.DynamicDrawing != null
            && cache.DynamicFingerprint == cache.PendingDynamicFingerprint)
        {
            return;
        }

        RebuildDynamicDrawing(cache, rowIndex);
    }

    private int FindVisibleProcess(ProcessInstanceKey process)
    {
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[_visibleRowIndexes[visibleIndex]];
            if (row?.InstanceKey == process) return visibleIndex;
        }
        return -1;
    }

    private void AddTextUnderlineSegment(
        ProcessTableColumn column,
        string display,
        int treeLayoutKey)
    {
        if (display.Length == 0 || _textUnderlineSegmentCount >= _textUnderlineSegments.Length) return;

        CellTextLayout layout = CreateCellTextLayout(column, display, treeLayoutKey);
        double width = Math.Min(layout.Text.Width, layout.AvailableWidth);
        if (width <= 0) return;

        double underlineY = Math.Min(
            _metrics.RowHeight - _visualMetrics.TextUnderlineThickness,
            layout.Top + layout.Text.Baseline + _visualMetrics.TextUnderlineThickness);
        _textUnderlineSegments[_textUnderlineSegmentCount] = new TextUnderlineSegment(
            layout.Left,
            layout.Left + width,
            underlineY);
        _textUnderlineSegmentCount++;
    }

    private static void AppendCSVField(StringBuilder destination, string value)
    {
        bool requiresQuotes = false;
        for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            char character = value[characterIndex];
            if (character is not (',' or '"' or '\r' or '\n')) continue;
            requiresQuotes = true;
            break;
        }

        if (!requiresQuotes)
        {
            destination.Append(value);
            return;
        }

        destination.Append('"');
        for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            char character = value[characterIndex];
            if (character == '"') destination.Append('"');
            destination.Append(character);
        }
        destination.Append('"');
    }

    /// <summary>Clips the resized cell and translates trailing retained cells without rebuilding the row DAG.</summary>
    private void DrawLiveResizedRow(
        DrawingContext context,
        ProcessRowRenderCache cache,
        ProcessTableColumn[] liveColumns)
    {
        int resizedColumnIndex = _interactionColumnIndex;
        if ((uint)resizedColumnIndex >= (uint)_columns.Length)
        {
            DrawRetainedRowDrawings(context, cache);
            return;
        }

        ProcessTableColumn committedColumn = _columns[resizedColumnIndex];
        ProcessTableColumn liveColumn = liveColumns[resizedColumnIndex];
        double offset = liveColumn.Width - committedColumn.Width;
        DrawRetainedRowSegment(
            context,
            cache,
            new Rect(0, 0, committedColumn.Left, _metrics.RowHeight),
            0);

        double sourceTranslation = committedColumn.Alignment == ProcessTableColumnAlignment.Right
            ? offset
            : 0;
        double sourceClipLeft = Math.Max(liveColumn.Left, committedColumn.Left + sourceTranslation);
        double sourceClipRight = Math.Min(liveColumn.Right, committedColumn.Right + sourceTranslation);
        DrawRetainedRowSegment(
            context,
            cache,
            new Rect(
                sourceClipLeft,
                0,
                Math.Max(0, sourceClipRight - sourceClipLeft),
                _metrics.RowHeight),
            sourceTranslation);

        DrawRetainedRowSegment(
            context,
            cache,
            new Rect(
                liveColumn.Right,
                0,
                Math.Max(0, Bounds.Width - liveColumn.Right),
                _metrics.RowHeight),
            offset);
    }

    private static void DrawRetainedRowSegment(
        DrawingContext context,
        ProcessRowRenderCache cache,
        Rect clip,
        double translationX)
    {
        if (clip.Width <= 0 || clip.Height <= 0) return;

        using (context.PushClip(clip))
        {
            if (Math.Abs(translationX) < 0.01)
            {
                DrawRetainedRowDrawings(context, cache);
                return;
            }

            using (context.PushTransform(Matrix.CreateTranslation(translationX, 0)))
                DrawRetainedRowDrawings(context, cache);
        }
    }

    private static void DrawRetainedRowDrawings(DrawingContext context, ProcessRowRenderCache cache)
    {
        cache.StaticDrawing?.Draw(context);
        cache.DynamicDrawing?.Draw(context);
    }

    private void DrawProcessIcon(
        DrawingContext context,
        Rect viewport,
        int rowIndex,
        ProcessStaticData row,
        double top)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        int nameColumnIndex = FindColumn(columns, ProcessTableColumnKind.Name);
        if (nameColumnIndex < 0) return;

        ProcessTableColumn nameColumn = columns[nameColumnIndex];
        if (nameColumn.Right <= viewport.Left || nameColumn.Left >= viewport.Right) return;

        double iconTop = top + (_metrics.RowHeight - _metrics.ProcessIconSize) / 2;
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        double hierarchyInset = GetHierarchyInset(treeLayoutKey);
        double expanderInset = HasTreeExpanderSlot(treeLayoutKey)
            ? _visualMetrics.TreeExpanderWidth
            : 0;
        Rect iconBounds = new(
            nameColumn.Left + _metrics.CellPadding + hierarchyInset + expanderInset,
            iconTop,
            _metrics.ProcessIconSize,
            _metrics.ProcessIconSize);
        IImage? icon = _processIconService.GetOrQueue(row.Image.IconSource);
        if (icon != null)
            context.DrawImage(icon, iconBounds);
        else
            context.FillRectangle(
                _accentBrush,
                iconBounds,
                (float)_visualMetrics.ProcessIconCornerRadius);

        if ((treeLayoutKey & 1) != 0)
            DrawTreeExpander(context, nameColumn, row, top, hierarchyInset);
    }

    private void DrawTreeExpander(
        DrawingContext context,
        ProcessTableColumn nameColumn,
        ProcessStaticData row,
        double top,
        double hierarchyInset)
    {
        double centerX = nameColumn.Left
                         + _metrics.CellPadding
                         + hierarchyInset
                         + _visualMetrics.TreeExpanderWidth / 2;
        double centerY = top + _metrics.RowHeight / 2;
        if (_collapsedProcesses.Contains(row.InstanceKey))
        {
            context.DrawLine(
                _treeExpanderPen,
                new Point(
                    centerX - _visualMetrics.TreeExpanderChevronHalfWidth,
                    centerY - _visualMetrics.TreeExpanderChevronHalfHeight),
                new Point(centerX + _visualMetrics.TreeExpanderChevronHalfWidth, centerY));
            context.DrawLine(
                _treeExpanderPen,
                new Point(centerX + _visualMetrics.TreeExpanderChevronHalfWidth, centerY),
                new Point(
                    centerX - _visualMetrics.TreeExpanderChevronHalfWidth,
                    centerY + _visualMetrics.TreeExpanderChevronHalfHeight));
            return;
        }

        context.DrawLine(
            _treeExpanderPen,
            new Point(
                centerX - _visualMetrics.TreeExpanderChevronHalfHeight,
                centerY - _visualMetrics.TreeExpanderChevronHalfWidth),
            new Point(centerX, centerY + _visualMetrics.TreeExpanderChevronHalfWidth));
        context.DrawLine(
            _treeExpanderPen,
            new Point(centerX, centerY + _visualMetrics.TreeExpanderChevronHalfWidth),
            new Point(
                centerX + _visualMetrics.TreeExpanderChevronHalfHeight,
                centerY - _visualMetrics.TreeExpanderChevronHalfWidth));
    }

    private void DrawColumnGrid(DrawingContext context, Rect viewport)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        for (int columnIndex = 1; columnIndex < columns.Length; columnIndex++)
        {
            double left = columns[columnIndex].Left;
            if (left < viewport.Left || left > viewport.Right) continue;
            context.DrawLine(_gridPen, new Point(left, viewport.Y), new Point(left, viewport.Bottom));
        }
    }

    private void DrawHeader(DrawingContext context, double top)
    {
        Rect headerBounds = new(0, top, Bounds.Width, _metrics.HeaderHeight);
        context.FillRectangle(_backgroundBrush, headerBounds);

        ProcessTableColumn[] columns = DisplayColumns;
        if ((uint)_hoveredHeaderColumnIndex < (uint)columns.Length)
        {
            ProcessTableColumn hoveredColumn = columns[_hoveredHeaderColumnIndex];
            context.FillRectangle(
                _hoverBrush,
                new Rect(hoveredColumn.Left, top, hoveredColumn.Width, _metrics.HeaderHeight));
        }

        context.DrawLine(
            _gridPen,
            new Point(0, top + _metrics.HeaderHeight),
            new Point(Bounds.Width, top + _metrics.HeaderHeight));

        Rect viewport = ResolveViewport();
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (column.Right <= viewport.Left || column.Left >= viewport.Right) continue;

            FormattedText headerText = _headerTexts[columnIndex];
            double textLeft = column.Left + _metrics.CellPadding;
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
            double headerTextWidth = Math.Max(0, headerTextRight - textLeft);
            if (column.Alignment == ProcessTableColumnAlignment.Right)
                headerText.MaxTextWidth = headerTextWidth;
            double textTop = top + Math.Max(0, (_metrics.HeaderHeight - headerText.Height) / 2);
            Rect headerClip = new(
                textLeft,
                top,
                headerTextWidth,
                _metrics.HeaderHeight);
            using (context.PushClip(headerClip))
                context.DrawText(headerText, new Point(textLeft, textTop));

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

    private void DrawHeaderInteraction(DrawingContext context, Rect viewport, double headerTop)
    {
        if (_headerInteraction == HeaderInteractionMode.Resizing && _enableLiveColumnResizing) return;

        ProcessTableColumn[] columns = DisplayColumns;
        if ((uint)_interactionColumnIndex >= (uint)columns.Length) return;

        switch (_headerInteraction)
        {
            case HeaderInteractionMode.Resizing:
            {
                double dividerX = columns[_interactionColumnIndex].Left + _resizePreviewWidth;
                if (dividerX >= viewport.Left && dividerX <= viewport.Right)
                {
                    context.DrawLine(
                        _columnInteractionPen,
                        new Point(dividerX, headerTop),
                        new Point(dividerX, viewport.Bottom));
                }

                return;
            }
            case HeaderInteractionMode.Reordering:
            {
                if (_reorderInsertionIndex != _interactionColumnIndex)
                {
                    double insertionX = ProcessTableLayout.GetReorderInsertionX(
                        columns,
                        _interactionColumnIndex,
                        _reorderInsertionIndex);
                    if (double.IsFinite(insertionX)
                        && insertionX >= viewport.Left
                        && insertionX <= viewport.Right)
                    {
                        context.DrawLine(
                            _columnInteractionPen,
                            new Point(insertionX, headerTop),
                            new Point(insertionX, viewport.Bottom));
                    }
                }

                DrawDraggedHeader(context, viewport, headerTop);
                return;
            }
        }
    }

    private void DrawDraggedHeader(DrawingContext context, Rect viewport, double headerTop)
    {
        ProcessTableColumn column = DisplayColumns[_interactionColumnIndex];
        double minimumLeft = viewport.Left;
        double maximumLeft = Math.Max(minimumLeft, viewport.Right - column.Width);
        double left = Math.Clamp(_headerDragX - _headerPointerOffsetX, minimumLeft, maximumLeft);
        Rect bounds = new(left, headerTop, column.Width, _metrics.HeaderHeight);
        context.FillRectangle(_backgroundBrush, bounds);
        context.DrawRectangle(null, _columnInteractionPen, bounds);

        FormattedText headerText = _headerTexts[_interactionColumnIndex];
        double textLeft = left + _metrics.CellPadding;
        Rect textClip = new(
            textLeft,
            headerTop,
            Math.Max(0, column.Width - _metrics.CellPadding * 2),
            _metrics.HeaderHeight);
        double textTop = headerTop + Math.Max(0, (_metrics.HeaderHeight - headerText.Height) / 2);
        using (context.PushClip(textClip))
            context.DrawText(headerText, new Point(textLeft, textTop));
    }

    private void UpdateRetainedDrawings()
    {
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
                continue;

            int treeLayoutKey = GetTreeLayoutKey(rowIndex);
            if (cache.StaticDrawing == null || cache.StaticTreeLayoutKey != treeLayoutKey)
            {
                ReleaseSharedCellDrawings(cache.StaticSharedCells);
                cache.StaticDrawing = BuildRowDrawing(
                    rowIndex,
                    ProcessTableColumnLifetime.Static,
                    out SharedCellDrawing[] sharedCells);
                cache.StaticSharedCells = sharedCells;
                cache.StaticTreeLayoutKey = treeLayoutKey;
            }
            if (!_hasDynamicColumns) continue;

            cache.PendingDynamicFingerprint = CalculateDynamicFingerprint(rowIndex);
            if (cache.DynamicDrawing == null)
                RebuildDynamicDrawing(cache, rowIndex);
        }

        ScheduleWarmDynamicRefresh();
    }

    private DrawingGroup BuildRowDrawing(
        int rowIndex,
        ProcessTableColumnLifetime lifetime,
        out SharedCellDrawing[] sharedCells)
    {
        _sharedCellBuffer.Clear();
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        DrawingGroup uniqueDrawing = new();
        DrawingCollection children = new() { Capacity = _columns.Length + 1 };
        bool hasUniqueDrawing = false;
        using (DrawingContext uniqueContext = uniqueDrawing.Open())
        {
            for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
            {
                ProcessTableColumn column = _columns[columnIndex];
                ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(column.Kind);
                if (definition.Lifetime != lifetime) continue;

                string display = GetCellDisplayValue(rowIndex, column.Kind);
                if (display.Length == 0) continue;

                if (ShouldShareCell(column.Kind, display))
                {
                    int cellTreeLayoutKey = column.Kind == ProcessTableColumnKind.Name ? treeLayoutKey : 0;
                    ProcessSharedCellKey key = new(column.Kind, display, cellTreeLayoutKey);
                    SharedCellDrawing sharedCell = AcquireSharedCellDrawing(column, key);
                    children.Add(sharedCell.Drawing);
                    _sharedCellBuffer.Add(sharedCell);
                    continue;
                }

                DrawCell(uniqueContext, column, display, treeLayoutKey);
                hasUniqueDrawing = true;
            }
        }

        if (hasUniqueDrawing) children.Insert(0, uniqueDrawing);
        sharedCells = _sharedCellBuffer.Count == 0 ? [] : _sharedCellBuffer.ToArray();
        return new DrawingGroup { Children = children };
    }

    private SharedCellDrawing AcquireSharedCellDrawing(ProcessTableColumn column, ProcessSharedCellKey key)
    {
        if (_sharedCellDrawings.TryGetValue(key, out SharedCellDrawing? existing))
        {
            existing.ReferenceCount++;
            return existing;
        }

        DrawingGroup drawing = new();
        using (DrawingContext context = drawing.Open())
            DrawCell(context, column, key.Value, key.TreeLayoutKey);
        SharedCellDrawing sharedCell = new(key, drawing);
        _sharedCellDrawings.Add(key, sharedCell);
        return sharedCell;
    }

    private void ReleaseSharedCellDrawings(SharedCellDrawing[] sharedCells)
    {
        for (int cellIndex = 0; cellIndex < sharedCells.Length; cellIndex++)
        {
            SharedCellDrawing entry = sharedCells[cellIndex];
            entry.ReferenceCount--;
            if (entry.ReferenceCount <= 0)
                _sharedCellDrawings.Remove(entry.Key);
        }
    }

    private void DrawCell(
        DrawingContext context,
        ProcessTableColumn column,
        string display,
        int treeLayoutKey)
    {
        CellTextLayout layout = CreateCellTextLayout(column, display, treeLayoutKey);
        context.DrawText(layout.Text, new Point(layout.Left, layout.Top));
    }

    private CellTextLayout CreateCellTextLayout(
        ProcessTableColumn column,
        string display,
        int treeLayoutKey)
    {
        double textTop = Math.Max(
            0,
            (_metrics.RowHeight - _metrics.FontSize * _visualMetrics.RowTextHeightMultiplier) / 2);
        double leftInset = column.Kind == ProcessTableColumnKind.Name
            ? _metrics.CellPadding
              + GetHierarchyInset(treeLayoutKey)
              + (HasTreeExpanderSlot(treeLayoutKey) ? _visualMetrics.TreeExpanderWidth : 0)
              + _metrics.ProcessIconSize
              + _metrics.ProcessIconGap
            : _metrics.CellPadding;
        double availableWidth = Math.Max(0, column.Width - leftInset - _metrics.CellPadding);
        FormattedText text = CreateBoundedText(display, availableWidth);
        double textX = column.Alignment == ProcessTableColumnAlignment.Right
            ? column.Right - _metrics.CellPadding - text.Width
            : column.Left + leftInset;
        return new CellTextLayout(text, textX, textTop, availableWidth);
    }

    private void RebuildDynamicDrawing(ProcessRowRenderCache cache, int rowIndex)
    {
        ReleaseSharedCellDrawings(cache.DynamicSharedCells);
        cache.DynamicDrawing = BuildRowDrawing(
            rowIndex,
            ProcessTableColumnLifetime.Dynamic,
            out SharedCellDrawing[] sharedCells);
        cache.DynamicSharedCells = sharedCells;
        cache.DynamicFingerprint = cache.PendingDynamicFingerprint;
    }

    private int CalculateDynamicFingerprint(int rowIndex)
    {
        HashCode hash = new();
        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            ProcessTableColumnKind kind = _columns[columnIndex].Kind;
            if (ProcessTableColumnCatalog.Get(kind).Lifetime != ProcessTableColumnLifetime.Dynamic) continue;

            if (ProcessDataSchema.StoresText(kind))
            {
                hash.Add(_snapshot.GetDynamicText(rowIndex, kind));
                continue;
            }

            long value = _snapshot.GetDynamicNumeric(rowIndex, kind);
            ProcessColumnSetting setting = _settingsByColumn[(int)kind];
            switch (kind)
            {
                case ProcessTableColumnKind.CPU:
                case ProcessTableColumnKind.GPU:
                case ProcessTableColumnKind.NPU:
                case ProcessTableColumnKind.CPUUtility:
                    hash.Add(QuantizePercent(
                        BitConverter.Int64BitsToDouble(value),
                        setting.ShowDecimalUsage));
                    break;
                case ProcessTableColumnKind.CPUTime:
                    hash.Add(value / TimeSpan.TicksPerSecond);
                    break;
                case ProcessTableColumnKind.WorkingSet:
                case ProcessTableColumnKind.PeakWorkingSet:
                case ProcessTableColumnKind.ActivePrivateWorkingSet:
                case ProcessTableColumnKind.PrivateMemory:
                case ProcessTableColumnKind.SharedWorkingSet:
                case ProcessTableColumnKind.CommitSize:
                case ProcessTableColumnKind.PagedPool:
                case ProcessTableColumnKind.NonPagedPool:
                case ProcessTableColumnKind.DedicatedGPUMemory:
                case ProcessTableColumnKind.SharedGPUMemory:
                case ProcessTableColumnKind.DedicatedNPUMemory:
                case ProcessTableColumnKind.SharedNPUMemory:
                    hash.Add(QuantizeMemory(value, setting.MemoryUnit, false));
                    break;
                case ProcessTableColumnKind.WorkingSetDelta:
                    hash.Add(QuantizeMemory(value, setting.MemoryUnit, true));
                    break;
                default:
                    hash.Add(value);
                    break;
            }
        }

        return hash.ToHashCode();
    }

    private string GetCellDisplayValue(int rowIndex, ProcessTableColumnKind kind) =>
        ProcessTableColumnCatalog.Get(kind).Lifetime == ProcessTableColumnLifetime.Static
            ? GetStaticDisplayValue(rowIndex, kind)
            : GetDynamicDisplayValue(rowIndex, kind);

    private string GetStaticDisplayValue(int rowIndex, ProcessTableColumnKind kind)
    {
        ProcessStaticData row = _snapshot.StaticRows[rowIndex]
            ?? throw new InvalidOperationException("A published process row is missing static data.");
        if (kind == ProcessTableColumnKind.ProcessID)
            return row.ProcessID.ToString(TableCulture);

        string? identityText = GetIdentityText(row, kind);
        if (kind == ProcessTableColumnKind.UserName
            && identityText != null
            && !_settingsByColumn[(int)kind].ShowUserNamePrefix)
        {
            int separatorIndex = identityText.LastIndexOf('\\');
            if (separatorIndex >= 0 && separatorIndex < identityText.Length - 1)
                identityText = identityText[(separatorIndex + 1)..];
        }
        if (identityText != null) return LocalizeUnavailableText(identityText);

        if (ProcessDataSchema.StoresText(kind))
        {
            int slot = _schema.GetStaticTextSlot(kind);
            return slot < 0
                ? string.Empty
                : LocalizeUnavailableText(row.TextValues[slot] ?? string.Empty);
        }

        int numericSlot = _schema.GetStaticNumericSlot(kind);
        if (numericSlot < 0) return string.Empty;
        long value = row.NumericValues[numericSlot];
        return kind switch
        {
            ProcessTableColumnKind.ProcessID => value.ToString(TableCulture),
            ProcessTableColumnKind.SessionID => value < 0 ? _unavailableText : value.ToString(TableCulture),
            _ => FormatDisplayCode(value)
        };
    }

    private static string? GetIdentityText(ProcessStaticData row, ProcessTableColumnKind kind) => kind switch
    {
        ProcessTableColumnKind.Name => row.Image.Name,
        ProcessTableColumnKind.UserName => row.UserName,
        ProcessTableColumnKind.ImagePath => row.Image.ImagePath,
        ProcessTableColumnKind.Description => row.Image.Description,
        _ => null
    };

    private string GetDynamicDisplayValue(int rowIndex, ProcessTableColumnKind kind)
    {
        if (ProcessDataSchema.StoresText(kind))
            return LocalizeUnavailableText(_snapshot.GetDynamicText(rowIndex, kind));

        long value = _snapshot.GetDynamicNumeric(rowIndex, kind);
        ProcessColumnSetting setting = _settingsByColumn[(int)kind];
        return kind switch
        {
            ProcessTableColumnKind.Status => FormatDisplayCode(value),
            ProcessTableColumnKind.JobObjectID => FormatJobObjectID(value),
            ProcessTableColumnKind.CPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.CPUTime => FormatCPUTime(value),
            ProcessTableColumnKind.Cycle => FormatUnsigned(value),
            ProcessTableColumnKind.WorkingSet => FormatMemory(value, setting, false),
            ProcessTableColumnKind.PeakWorkingSet => FormatMemory(value, setting, false),
            ProcessTableColumnKind.WorkingSetDelta => FormatMemory(value, setting, true),
            ProcessTableColumnKind.ActivePrivateWorkingSet => FormatMemory(value, setting, false),
            ProcessTableColumnKind.PrivateMemory => FormatMemory(value, setting, false),
            ProcessTableColumnKind.SharedWorkingSet => FormatMemory(value, setting, false),
            ProcessTableColumnKind.CommitSize => FormatMemory(value, setting, false),
            ProcessTableColumnKind.PagedPool => FormatMemory(value, setting, false),
            ProcessTableColumnKind.NonPagedPool => FormatMemory(value, setting, false),
            ProcessTableColumnKind.PageFaults => FormatSigned(value),
            ProcessTableColumnKind.PageFaultDelta => FormatSigned(value),
            ProcessTableColumnKind.BasePriority => value.ToString(TableCulture),
            ProcessTableColumnKind.Handles => value.ToString("N0", TableCulture),
            ProcessTableColumnKind.Threads => value.ToString("N0", TableCulture),
            ProcessTableColumnKind.UserObjects => value.ToString("N0", TableCulture),
            ProcessTableColumnKind.GDIObjects => value.ToString("N0", TableCulture),
            ProcessTableColumnKind.IOReads => FormatUnsigned(value),
            ProcessTableColumnKind.IOWrites => FormatUnsigned(value),
            ProcessTableColumnKind.IOOther => FormatUnsigned(value),
            ProcessTableColumnKind.IOReadBytes => FormatUnsigned(value),
            ProcessTableColumnKind.IOWriteBytes => FormatUnsigned(value),
            ProcessTableColumnKind.IOOtherBytes => FormatUnsigned(value),
            ProcessTableColumnKind.UACVirtualization => FormatDisplayCode(value),
            ProcessTableColumnKind.IOPriority => FormatDisplayCode(value),
            ProcessTableColumnKind.PowerThrottling => FormatDisplayCode(value),
            ProcessTableColumnKind.GPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.DedicatedGPUMemory => FormatMemory(value, setting, false),
            ProcessTableColumnKind.SharedGPUMemory => FormatMemory(value, setting, false),
            ProcessTableColumnKind.DPIAwareness => FormatDisplayCode(value),
            ProcessTableColumnKind.NPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.DedicatedNPUMemory => FormatMemory(value, setting, false),
            ProcessTableColumnKind.SharedNPUMemory => FormatMemory(value, setting, false),
            ProcessTableColumnKind.CPUUtility => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            _ => string.Empty
        };
    }

    private string FormatDisplayCode(long value)
    {
        ProcessDisplayCode code = (ProcessDisplayCode)value;
        return code == ProcessDisplayCode.Unavailable
            ? _unavailableText
            : ProcessDisplayCodeText.Get(code);
    }

    private string FormatJobObjectID(long value) => value switch
    {
        < 0 => _unavailableText,
        0 => string.Empty,
        _ => value.ToString(TableCulture)
    };

    private string FormatPercent(double value, ProcessColumnSetting setting)
    {
        long quantized = QuantizePercent(value, setting.ShowDecimalUsage);
        if (quantized < 0) return _unavailableText;

        string display = setting.ShowDecimalUsage
            ? (quantized / 10.0).ToString("0.0", TableCulture)
            : quantized.ToString("0", TableCulture);
        return setting.ShowPercentSuffix ? string.Concat(display, "%") : display;
    }

    private static string FormatCPUTime(long ticks)
    {
        long totalSeconds = Math.Max(0, ticks / TimeSpan.TicksPerSecond);
        if (totalSeconds == 0) return ZeroCPUTimeText;

        long hours = totalSeconds / 3_600;
        long minutes = totalSeconds / 60 % 60;
        long seconds = totalSeconds % 60;
        return string.Create(TableCulture, $"{hours}:{minutes:00}:{seconds:00}");
    }

    private string FormatMemory(long bytes, ProcessColumnSetting setting, bool isDelta)
    {
        if (!isDelta && bytes < 0) return _unavailableText;

        long quantized = QuantizeMemory(bytes, setting.MemoryUnit, isDelta);
        if (quantized == -1 && setting.MemoryUnit == ProcessMemoryUnit.PercentageOfSystem
            && _totalPhysicalMemoryBytes <= 0)
        {
            return _unavailableText;
        }

        string display = setting.MemoryUnit == ProcessMemoryUnit.Kilobytes
            ? quantized.ToString("N0", TableCulture)
            : (quantized / 10.0).ToString("N1", TableCulture);
        string suffix = setting.MemorySuffix ?? string.Empty;
        if (suffix.Length == 0) return display;
        return setting.MemoryUnit == ProcessMemoryUnit.PercentageOfSystem
            ? string.Concat(display, suffix)
            : string.Concat(display, " ", suffix);
    }

    private static string FormatSigned(long value) => value == 0
        ? ZeroText
        : value.ToString("N0", TableCulture);

    private static string FormatUnsigned(long value) => value == 0
        ? ZeroText
        : unchecked((ulong)value).ToString("N0", TableCulture);

    private static long QuantizePercent(double value, bool showDecimalUsage)
    {
        if (!double.IsFinite(value) || value < 0) return -1;
        double scale = showDecimalUsage ? 10 : 1;
        return (long)Math.Round(Math.Max(0, value) * scale, MidpointRounding.AwayFromZero);
    }

    private long QuantizeMemory(long bytes, ProcessMemoryUnit unit, bool isDelta)
    {
        if (!isDelta && bytes < 0) return -1;
        return unit switch
        {
            ProcessMemoryUnit.Kilobytes => isDelta ? ToSignedKibibytes(bytes) : ToKibibytes(bytes),
            ProcessMemoryUnit.Megabytes => QuantizeMemoryFraction(bytes, 1024.0 * 1024.0),
            ProcessMemoryUnit.Gigabytes => QuantizeMemoryFraction(bytes, 1024.0 * 1024.0 * 1024.0),
            ProcessMemoryUnit.PercentageOfSystem when _totalPhysicalMemoryBytes > 0 =>
                QuantizeMemoryFraction(bytes * 100.0, _totalPhysicalMemoryBytes),
            _ => -1
        };
    }

    private static long QuantizeMemoryFraction(double numerator, double divisor) =>
        (long)Math.Round(numerator / divisor * 10, MidpointRounding.AwayFromZero);

    private static long ToKibibytes(long bytes) => bytes switch
    {
        < 0 => -1,
        0 => 0,
        _ => (bytes + 1023) / 1024
    };

    private static long ToSignedKibibytes(long bytes) => bytes switch
    {
        > 0 => (bytes + 1023) / 1024,
        < 0 => -((-bytes + 1023) / 1024),
        _ => 0
    };

    private void ScheduleWarmDynamicRefresh()
    {
        if (_disposed || !_hasDynamicColumns) return;

        GetWarmVisibleRowRange(out _warmRefreshCursor, out _warmRefreshEnd);
        if (_warmRefreshCursor >= _warmRefreshEnd || _dynamicRefreshScheduled) return;

        _dynamicRefreshScheduled = true;
        Dispatcher.UIThread.Post(_refreshWarmDynamicDrawings, DispatcherPriority.Background);
    }

    private void RefreshWarmDynamicDrawings()
    {
        _dynamicRefreshScheduled = false;
        if (_disposed) return;

        bool changed = false;
        int processed = 0;
        long startTimestamp = Stopwatch.GetTimestamp();
        while (_warmRefreshCursor < _warmRefreshEnd && processed < DynamicRefreshBatchSize)
        {
            int rowIndex = _visibleRowIndexes[_warmRefreshCursor];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row != null
                && _renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache)
                && (cache.DynamicDrawing == null
                    || cache.DynamicFingerprint != cache.PendingDynamicFingerprint))
            {
                RebuildDynamicDrawing(cache, rowIndex);
                changed = true;
            }

            _warmRefreshCursor++;
            processed++;
            if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                >= TimeConstants.DynamicRefreshBudgetMilliseconds)
            {
                break;
            }
        }

        if (changed) InvalidateVisual();
        if (_warmRefreshCursor < _warmRefreshEnd)
        {
            _dynamicRefreshScheduled = true;
            Dispatcher.UIThread.Post(_refreshWarmDynamicDrawings, DispatcherPriority.Background);
        }
    }

    private void GetWarmVisibleRowRange(out int firstRow, out int lastRowExclusive)
    {
        Rect viewport = ResolveViewport();
        ProcessTableLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics,
            out int visibleFirst,
            out int visibleLastExclusive);
        firstRow = visibleFirst;
        lastRowExclusive = visibleLastExclusive;
    }

    private void PublishWarmProcesses()
    {
        if (_snapshotService == null) return;

        bool sampleEveryProcess = ProcessTableColumnCatalog.Get(_sortColumn).Lifetime
                                  == ProcessTableColumnLifetime.Dynamic;
        if (sampleEveryProcess)
        {
            _snapshotService.SetWarmProcesses(_schema.VisibleMask, _warmProcessIDs, 0, true);
            return;
        }

        GetWarmVisibleRowRange(out int firstRow, out int lastRowExclusive);
        int warmProcessCount = lastRowExclusive - firstRow;
        EnsureWarmCapacity(warmProcessCount);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            _warmProcessIDs[visibleIndex - firstRow] = row?.ProcessID ?? -1;
        }

        _snapshotService.SetWarmProcesses(
            _schema.VisibleMask,
            _warmProcessIDs,
            warmProcessCount,
            false);
    }

    private void CommitColumnResize(int columnIndex, double width)
    {
        if ((uint)columnIndex >= (uint)_columns.Length || !double.IsFinite(width)) return;
        if (Math.Abs(_columns[columnIndex].Width - width) < 0.01) return;

        List<ProcessColumnSetting> nextSettings = ProcessColumnSettings.WithWidth(
            _columnSettings,
            _columns[columnIndex].Kind,
            width);
        ApplyColumnLayout(nextSettings);
    }

    private void CommitColumnReorder(int columnIndex, int insertionIndex)
    {
        if ((uint)columnIndex >= (uint)_columns.Length
            || (uint)insertionIndex >= (uint)_columns.Length
            || columnIndex == insertionIndex)
        {
            return;
        }

        List<ProcessColumnSetting> nextSettings = ProcessColumnSettings.MoveVisible(
            _columnSettings,
            _columns[columnIndex].Kind,
            insertionIndex);
        ApplyColumnLayout(nextSettings);
    }

    private void ApplyColumnLayout(List<ProcessColumnSetting> settings)
    {
        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(settings);
        ProcessTableColumn[] columns = CreateColumns(normalized);
        if (columns.Length != _columns.Length)
        {
            TADNLog.Log("ProcessDetailsCanvas rejected a width/order update that changed column visibility.");
            return;
        }

        _columnSettings = normalized;
        _settingsByColumn = CreateColumnSettingsIndex(normalized);
        _columns = columns;
        _headerTexts = CreateHeaderTexts(columns);
        RebuildVisibleRows();

        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellDrawings.Clear();
        UpdateRetainedDrawings();
        PublishWarmProcesses();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateVisual();
        ColumnLayoutChanged?.Invoke(normalized);
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

        RebuildVisibleRows();
        PublishWarmProcesses();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        ScheduleWarmDynamicRefresh();
        InvalidateVisual();
    }

    private void SelectVisibleRow(int visibleIndex)
    {
        ProcessStaticData? row = visibleIndex >= 0 && visibleIndex < _visibleRowCount
            ? _snapshot.StaticRows[_visibleRowIndexes[visibleIndex]]
            : null;
        ProcessInstanceKey? nextProcess = row?.InstanceKey;
        if (_selectedProcess == nextProcess) return;

        _selectedProcess = nextProcess;
        SelectedProcessChanged?.Invoke(SelectedTerminationTarget);
        UpdateSelectionOverlay();
    }

    private bool TryToggleTreeExpander(Point position, int visibleIndex)
    {
        if (!_groupProcesses || visibleIndex < 0 || visibleIndex >= _visibleRowCount) return false;

        int rowIndex = _visibleRowIndexes[visibleIndex];
        if (!_rowHasChildren[rowIndex]) return false;

        ProcessTableColumn[] columns = DisplayColumns;
        int nameColumnIndex = FindColumn(columns, ProcessTableColumnKind.Name);
        if (nameColumnIndex < 0) return false;

        ProcessTableColumn nameColumn = columns[nameColumnIndex];
        double expanderLeft = nameColumn.Left
                              + _metrics.CellPadding
                              + _rowDepths[rowIndex] * _visualMetrics.TreeIndentWidth;
        if (position.X < expanderLeft
            || position.X >= expanderLeft + _visualMetrics.TreeExpanderWidth)
        {
            return false;
        }

        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null) return false;

        SelectVisibleRow(visibleIndex);
        if (!_collapsedProcesses.Add(row.InstanceKey))
            _collapsedProcesses.Remove(row.InstanceKey);
        RebuildVisibleRows();
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateVisual();
        return true;
    }

    private void UpdateHoveredRow(double positionY)
    {
        double stickyHeaderTop = Math.Max(0, _effectiveViewport.Y);
        int visibleIndex = -1;
        if (positionY < stickyHeaderTop || positionY >= stickyHeaderTop + _metrics.HeaderHeight)
            visibleIndex = ProcessTableLayout.HitTestRow(positionY, _visibleRowCount, _metrics);
        SetHoveredVisibleIndex(visibleIndex);
    }

    private void UpdateHoverFromPointer()
    {
        if (!_pointerInside)
        {
            SetHoveredVisibleIndex(-1);
            return;
        }

        UpdateHoveredRow(Math.Max(0, _effectiveViewport.Y) + _pointerViewportY);
    }

    private void SetHoveredVisibleIndex(int visibleIndex)
    {
        if (_hoveredVisibleIndex == visibleIndex) return;
        _hoveredVisibleIndex = visibleIndex;
        HoverRowTopChanged?.Invoke(visibleIndex < 0
            ? null
            : _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight);
    }

    private void UpdateSelectionOverlay()
    {
        if (!_selectedProcess.HasValue)
        {
            SelectionRowTopChanged?.Invoke(null);
            return;
        }

        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[_visibleRowIndexes[visibleIndex]];
            if (row?.InstanceKey != _selectedProcess.Value) continue;
            SelectionRowTopChanged?.Invoke(_metrics.HeaderHeight + visibleIndex * _metrics.RowHeight);
            return;
        }

        SelectionRowTopChanged?.Invoke(null);
    }

    private void RebuildVisibleRows()
    {
        int writeIndex = 0;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null || !MatchesFilter(row)) continue;
            _visibleRowIndexes[writeIndex] = rowIndex;
            writeIndex++;
        }

        _visibleRowCount = writeIndex;
        SortVisibleRows();
        if (_groupProcesses && _visibleRowCount > 1)
            BuildGroupedVisibleRows();
        else
            ClearTreeLayout();
    }

    private bool MatchesFilter(ProcessStaticData row)
    {
        if (_filterText.Length == 0) return true;
        if (_filterProcessID >= 0 && row.ProcessID == _filterProcessID) return true;
        if (row.Image.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) return true;
        return row.UserName.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    }

    private void SortVisibleRows()
    {
        _rowComparer.Column = _sortColumn;
        _rowComparer.IsDescending = _sortDescending;
        _rowComparer.ShowUserNamePrefix = _settingsByColumn[(int)ProcessTableColumnKind.UserName]
            .ShowUserNamePrefix;
        Array.Sort(_visibleRowIndexes, 0, _visibleRowCount, _rowComparer);
    }

    /// <summary>Builds a sorted parent/child traversal using reusable contiguous buffers.</summary>
    private void BuildGroupedVisibleRows()
    {
        Array.Fill(_treeParentIndexes, -1, 0, _rowCount);
        Array.Clear(_treeChildCounts, 0, _rowCount);
        Array.Clear(_rowDepths, 0, _rowCount);
        Array.Clear(_rowHasChildren, 0, _rowCount);
        Array.Clear(_treeVisited, 0, _rowCount);
        _rowIndexByProcessID.Clear();

        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row != null) _rowIndexByProcessID[row.ProcessID] = rowIndex;
        }

        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null
                || row.ParentProcessID < 0
                || row.ParentProcessID == row.ProcessID
                || !_rowIndexByProcessID.TryGetValue(row.ParentProcessID, out int parentRowIndex))
            {
                continue;
            }

            ProcessStaticData? parent = _snapshot.StaticRows[parentRowIndex];
            if (parent == null || parent.InstanceKey.CreationTimeTicks > row.InstanceKey.CreationTimeTicks)
                continue;

            _treeParentIndexes[rowIndex] = parentRowIndex;
            _treeChildCounts[parentRowIndex]++;
            _rowHasChildren[parentRowIndex] = true;
        }

        int childOffset = 0;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            _treeChildStarts[rowIndex] = childOffset;
            _treeChildWriteOffsets[rowIndex] = childOffset;
            childOffset += _treeChildCounts[rowIndex];
        }

        // Iterating the already-sorted candidates preserves the selected sort within each sibling set
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            int parentRowIndex = _treeParentIndexes[rowIndex];
            if (parentRowIndex < 0) continue;
            _treeChildren[_treeChildWriteOffsets[parentRowIndex]++] = rowIndex;
        }

        int outputCount = 0;
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            if (_treeParentIndexes[rowIndex] >= 0 || _treeVisited[rowIndex] != 0) continue;
            outputCount = AppendTree(rowIndex, outputCount);
        }

        // PID reuse or malformed native data can form a cycle; retain those rows as an extra root tree
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            if (_treeVisited[rowIndex] != 0) continue;
            outputCount = AppendTree(rowIndex, outputCount);
        }

        Array.Copy(_treeOrderBuffer, _visibleRowIndexes, outputCount);
        _visibleRowCount = outputCount;
    }

    private int AppendTree(int rootRowIndex, int outputCount)
    {
        int stackCount = 1;
        _treeStackRows[0] = rootRowIndex;
        _treeStackDepths[0] = 0;
        _treeStackHidden[0] = false;
        while (stackCount > 0)
        {
            stackCount--;
            int rowIndex = _treeStackRows[stackCount];
            byte depth = _treeStackDepths[stackCount];
            bool hidden = _treeStackHidden[stackCount];
            if (_treeVisited[rowIndex] != 0) continue;

            _treeVisited[rowIndex] = 1;
            _rowDepths[rowIndex] = depth;
            if (!hidden)
            {
                _treeOrderBuffer[outputCount] = rowIndex;
                outputCount++;
            }

            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null) continue;

            int childStart = _treeChildStarts[rowIndex];
            int childCount = _treeChildCounts[rowIndex];
            byte childDepth = depth == byte.MaxValue ? byte.MaxValue : (byte)(depth + 1);
            bool hideChildren = hidden || _collapsedProcesses.Contains(row.InstanceKey);
            for (int childOffset = childCount - 1; childOffset >= 0; childOffset--)
            {
                _treeStackRows[stackCount] = _treeChildren[childStart + childOffset];
                _treeStackDepths[stackCount] = childDepth;
                _treeStackHidden[stackCount] = hideChildren;
                stackCount++;
            }
        }

        return outputCount;
    }

    private void ClearTreeLayout()
    {
        Array.Clear(_rowDepths, 0, _rowCount);
        Array.Clear(_rowHasChildren, 0, _rowCount);
    }

    private void SynchronizeRenderCacheMembership()
    {
        int generation = unchecked(_cacheGeneration + 1);
        if (generation == 0)
        {
            foreach (ProcessRowRenderCache cache in _renderCaches.Values)
                ReleaseRenderCache(cache);
            _renderCaches.Clear();
            generation = 1;
        }

        _cacheGeneration = generation;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null) continue;
            if (_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
            {
                cache.LastSeenGeneration = generation;
                continue;
            }

            _renderCaches.Add(
                row.InstanceKey,
                new ProcessRowRenderCache
                {
                    LastSeenGeneration = generation
                });
        }

        _staleProcessKeys.Clear();
        foreach (KeyValuePair<ProcessInstanceKey, ProcessRowRenderCache> pair in _renderCaches)
        {
            if (pair.Value.LastSeenGeneration != generation)
                _staleProcessKeys.Add(pair.Key);
        }

        for (int staleIndex = 0; staleIndex < _staleProcessKeys.Count; staleIndex++)
        {
            ProcessInstanceKey key = _staleProcessKeys[staleIndex];
            if (!_renderCaches.Remove(key, out ProcessRowRenderCache? cache)) continue;
            _collapsedProcesses.Remove(key);
            ReleaseRenderCache(cache);
        }
    }

    private void ReleaseRenderCache(ProcessRowRenderCache cache)
    {
        ReleaseSharedCellDrawings(cache.StaticSharedCells);
        ReleaseSharedCellDrawings(cache.DynamicSharedCells);
        cache.StaticSharedCells = [];
        cache.DynamicSharedCells = [];
        cache.StaticDrawing = null;
        cache.DynamicDrawing = null;
    }

    private void EnsureSelectedProcessStillExists()
    {
        if (!_selectedProcess.HasValue) return;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            if (_snapshot.StaticRows[rowIndex]?.InstanceKey == _selectedProcess.Value) return;
        }

        _selectedProcess = null;
        SelectedProcessChanged?.Invoke(null);
    }

    private void EnsureRowCapacity(int count)
    {
        if (_visibleRowIndexes.Length >= count
            && _treeOrderBuffer.Length >= count
            && _treeParentIndexes.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(256, _visibleRowIndexes.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _visibleRowIndexes, capacity);
        Array.Resize(ref _treeOrderBuffer, capacity);
        Array.Resize(ref _treeParentIndexes, capacity);
        Array.Resize(ref _treeChildCounts, capacity);
        Array.Resize(ref _treeChildStarts, capacity);
        Array.Resize(ref _treeChildWriteOffsets, capacity);
        Array.Resize(ref _treeChildren, capacity);
        Array.Resize(ref _treeStackRows, capacity);
        Array.Resize(ref _treeStackDepths, capacity);
        Array.Resize(ref _treeStackHidden, capacity);
        Array.Resize(ref _treeVisited, capacity);
        Array.Resize(ref _rowDepths, capacity);
        Array.Resize(ref _rowHasChildren, capacity);
    }

    private void EnsureWarmCapacity(int count)
    {
        if (_warmProcessIDs.Length >= count) return;

        int capacity = Math.Max(256, _warmProcessIDs.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _warmProcessIDs, capacity);
    }

    private static int FindColumn(ProcessTableColumn[] columns, ProcessTableColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columns[columnIndex].Kind == kind) return columnIndex;
        }

        return -1;
    }

    private int GetTreeLayoutKey(int rowIndex)
    {
        if (!_groupProcesses || (uint)rowIndex >= (uint)_rowDepths.Length) return 0;
        return _rowDepths[rowIndex] * 2 + (_rowHasChildren[rowIndex] ? 1 : 0);
    }

    private double GetHierarchyInset(int treeLayoutKey) =>
        (treeLayoutKey >> 1) * _visualMetrics.TreeIndentWidth;

    private static bool HasTreeExpanderSlot(int treeLayoutKey) => treeLayoutKey != 0;

    private static ProcessTableMetrics CreateTableMetrics(
        TaskManagerWindowResources resources,
        double fontSize,
        double rowHeight) =>
        new(
            resources.AxamlProcessTable.HeaderHeight,
            rowHeight,
            resources.AxamlProcessTable.CellPadding,
            fontSize,
            resources.AxamlProcessTable.HeaderFontSize,
            resources.AxamlProcessTable.ProcessIconSize,
            resources.AxamlProcessTable.ProcessIconGap);

    private static ProcessTableVisualMetrics CreateVisualMetrics(
        TaskManagerWindowResources resources) =>
        new(
            resources.AxamlProcessTable.DefaultViewportHeight,
            resources.AxamlProcessTable.RowTextHeightMultiplier,
            resources.AxamlProcessTable.GridLineThickness,
            resources.AxamlProcessTable.ColumnResizeHitRadius,
            resources.AxamlProcessTable.HeaderDragThreshold,
            resources.AxamlProcessTable.ColumnInteractionLineThickness,
            resources.AxamlProcessTable.TextUnderlineThickness,
            resources.AxamlProcessTable.SortCaretFontSize,
            resources.AxamlProcessTable.SortCaretRightMargin,
            resources.AxamlProcessTable.ProcessIconCornerRadius,
            resources.AxamlProcessTable.TreeIndentWidth,
            resources.AxamlProcessTable.TreeExpanderWidth,
            resources.AxamlProcessTable.TreeExpanderChevronHalfWidth,
            resources.AxamlProcessTable.TreeExpanderChevronHalfHeight,
            resources.AxamlProcessTable.TreeExpanderLineThickness);

    private static ProcessTableColumn[] CreateColumns(IReadOnlyList<ProcessColumnSetting> source)
    {
        List<ProcessTableColumn> columns = new(source.Count);
        double left = 0;
        for (int settingIndex = 0; settingIndex < source.Count; settingIndex++)
        {
            ProcessColumnSetting setting = source[settingIndex];
            if (!setting.Visible) continue;

            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
            columns.Add(new ProcessTableColumn(
                setting.Column,
                ProcessColumnSettings.ResolveTitle(setting),
                left,
                setting.Width,
                definition.Alignment));
            left += setting.Width;
        }

        return columns.ToArray();
    }

    private static ProcessColumnSetting[] CreateColumnSettingsIndex(
        IReadOnlyList<ProcessColumnSetting> settings)
    {
        ProcessColumnSetting[] settingsByColumn =
            new ProcessColumnSetting[ProcessTableColumnCatalog.Definitions.Length];
        for (int settingIndex = 0; settingIndex < settings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = settings[settingIndex];
            settingsByColumn[(int)setting.Column] = setting;
        }

        return settingsByColumn;
    }

    private FormattedText[] CreateHeaderTexts(ProcessTableColumn[] columns)
    {
        FormattedText[] headerTexts = new FormattedText[columns.Length];
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            FormattedText headerText = CreateText(
                column.Title,
                _metrics.HeaderFontSize,
                _foregroundBrush,
                FontWeight.Normal);
            if (column.Alignment == ProcessTableColumnAlignment.Right)
            {
                headerText.TextAlignment = TextAlignment.Right;
                headerText.MaxLineCount = 1;
                headerText.Trimming = TextTrimming.CharacterEllipsis;
                headerText.MaxTextWidth = Math.Max(0, column.Width - _metrics.CellPadding * 2);
            }

            headerTexts[columnIndex] = headerText;
        }

        return headerTexts;
    }

    private static bool ContainsLifetime(
        ProcessTableColumn[] columns,
        ProcessTableColumnLifetime lifetime)
    {
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (ProcessTableColumnCatalog.Get(columns[columnIndex].Kind).Lifetime == lifetime)
                return true;
        }

        return false;
    }

    private bool ShouldShareCell(ProcessTableColumnKind column, string value)
    {
        if (value == ZeroText
            || value == ZeroMemoryText
            || value == ZeroCPUTimeText
            || value == _unavailableText)
        {
            return true;
        }
        if (ProcessRowIndexComparer.IsDisplayCodeColumn(column)) return true;

        return column switch
        {
            ProcessTableColumnKind.SessionID
                or ProcessTableColumnKind.BasePriority
                or ProcessTableColumnKind.Threads
                or ProcessTableColumnKind.UserObjects
                or ProcessTableColumnKind.GDIObjects
                or ProcessTableColumnKind.Name
                or ProcessTableColumnKind.UserName
                or ProcessTableColumnKind.JobObjectID
                or ProcessTableColumnKind.ImagePath
                or ProcessTableColumnKind.Description
                or ProcessTableColumnKind.PackageName
                or ProcessTableColumnKind.EnterpriseContext
                or ProcessTableColumnKind.GPUEngine
                or ProcessTableColumnKind.NPUEngine => true,
            _ => false
        };
    }

    private FormattedText CreateBoundedText(string value, double maximumWidth)
    {
        FormattedText text = CreateText(value, _metrics.FontSize, _foregroundBrush);
        text.MaxTextWidth = Math.Max(0, maximumWidth);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        return text;
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

        if (_capturedHeaderPointer != null) ResetHeaderInteraction();
        _disposed = true;
        _processIconService.IconsChanged -= OnIconsChanged;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
        LocalizationManager.Instance.CultureChanged -= OnCultureChanged;
        SelectedProcessChanged = null;
        HoverRowTopChanged = null;
        SelectionRowTopChanged = null;
        ColumnPropertiesRequested = null;
        ColumnLayoutChanged = null;
        GridMetricsChanged = null;
        GridZoomRequested = null;
        GridZoomResetRequested = null;
        RowContextMenuRequested = null;
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _renderCaches.Clear();
        _sharedCellDrawings.Clear();
        _sharedCellBuffer.Clear();
        _staleProcessKeys.Clear();
        _collapsedProcesses.Clear();
        _rowIndexByProcessID.Clear();
        Array.Clear(_contextCopyValuesByColumn);
        _snapshot.Reset();
    }

    private readonly record struct CellTextLayout(
        FormattedText Text,
        double Left,
        double Top,
        double AvailableWidth);

    private readonly record struct TextUnderlineSegment(double Left, double Right, double Y);

    private readonly record struct ProcessTableVisualMetrics(
        double DefaultViewportHeight,
        double RowTextHeightMultiplier,
        double GridLineThickness,
        double ColumnResizeHitRadius,
        double HeaderDragThreshold,
        double ColumnInteractionLineThickness,
        double TextUnderlineThickness,
        double SortCaretFontSize,
        double SortCaretRightMargin,
        double ProcessIconCornerRadius,
        double TreeIndentWidth,
        double TreeExpanderWidth,
        double TreeExpanderChevronHalfWidth,
        double TreeExpanderChevronHalfHeight,
        double TreeExpanderLineThickness);

    private enum HeaderInteractionMode : byte
    {
        None,
        PendingReorder,
        Resizing,
        Reordering
    }

    private readonly record struct ProcessSharedCellKey(
        ProcessTableColumnKind Column,
        string Value,
        int TreeLayoutKey);

    private sealed class ProcessRowRenderCache
    {
        public int LastSeenGeneration;
        public int DynamicFingerprint;
        public int PendingDynamicFingerprint;
        public int StaticTreeLayoutKey;
        public DrawingGroup? StaticDrawing;
        public DrawingGroup? DynamicDrawing;
        public SharedCellDrawing[] StaticSharedCells = [];
        public SharedCellDrawing[] DynamicSharedCells = [];
    }

    private sealed class SharedCellDrawing(ProcessSharedCellKey key, Drawing drawing)
    {
        public ProcessSharedCellKey Key { get; } = key;
        public Drawing Drawing { get; } = drawing;
        public int ReferenceCount { get; set; } = 1;
    }

    private sealed class ProcessRowIndexComparer(
        ProcessSnapshotBuffer snapshot,
        ProcessDataSchema schema) : IComparer<int>
    {
        public ProcessTableColumnKind Column { get; set; }
        public bool IsDescending { get; set; }
        public bool ShowUserNamePrefix { get; set; }

        public int Compare(int leftIndex, int rightIndex)
        {
            ProcessStaticData? left = snapshot.StaticRows[leftIndex];
            ProcessStaticData? right = snapshot.StaticRows[rightIndex];
            if (left == null || right == null) return left == null ? right == null ? 0 : 1 : -1;

            int comparison = CompareColumn(leftIndex, rightIndex, left, right, Column);
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

        private int CompareColumn(
            int leftIndex,
            int rightIndex,
            ProcessStaticData left,
            ProcessStaticData right,
            ProcessTableColumnKind column)
        {
            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(column);
            if (column == ProcessTableColumnKind.ProcessID)
                return left.ProcessID.CompareTo(right.ProcessID);
            if (column == ProcessTableColumnKind.UserName && !ShowUserNamePrefix)
            {
                ReadOnlySpan<char> leftUserName = GetUnqualifiedUserName(left.UserName);
                ReadOnlySpan<char> rightUserName = GetUnqualifiedUserName(right.UserName);
                return leftUserName.CompareTo(rightUserName, StringComparison.OrdinalIgnoreCase);
            }

            string? leftIdentityText = GetIdentityText(left, column);
            if (leftIdentityText != null)
            {
                string rightIdentityText = GetIdentityText(right, column) ?? string.Empty;
                return string.Compare(leftIdentityText, rightIdentityText, StringComparison.OrdinalIgnoreCase);
            }

            if (ProcessDataSchema.StoresText(column))
            {
                string leftText;
                string rightText;
                if (definition.Lifetime == ProcessTableColumnLifetime.Static)
                {
                    int slot = schema.GetStaticTextSlot(column);
                    leftText = left.TextValues[slot] ?? string.Empty;
                    rightText = right.TextValues[slot] ?? string.Empty;
                }
                else
                {
                    leftText = snapshot.GetDynamicText(leftIndex, column);
                    rightText = snapshot.GetDynamicText(rightIndex, column);
                }

                return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
            }

            long leftValue;
            long rightValue;
            if (definition.Lifetime == ProcessTableColumnLifetime.Static)
            {
                int slot = schema.GetStaticNumericSlot(column);
                leftValue = left.NumericValues[slot];
                rightValue = right.NumericValues[slot];
            }
            else
            {
                leftValue = snapshot.GetDynamicNumeric(leftIndex, column);
                rightValue = snapshot.GetDynamicNumeric(rightIndex, column);
            }

            if (IsPercentColumn(column))
            {
                return BitConverter.Int64BitsToDouble(leftValue)
                    .CompareTo(BitConverter.Int64BitsToDouble(rightValue));
            }
            if (IsUnsignedColumn(column))
                return unchecked((ulong)leftValue).CompareTo(unchecked((ulong)rightValue));
            if (IsDisplayCodeColumn(column))
            {
                string leftText = ProcessDisplayCodeText.Get((ProcessDisplayCode)leftValue);
                string rightText = ProcessDisplayCodeText.Get((ProcessDisplayCode)rightValue);
                return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
            }

            return leftValue.CompareTo(rightValue);
        }

        private static ReadOnlySpan<char> GetUnqualifiedUserName(string userName)
        {
            int separatorIndex = userName.LastIndexOf('\\');
            return separatorIndex >= 0 && separatorIndex < userName.Length - 1
                ? userName.AsSpan(separatorIndex + 1)
                : userName.AsSpan();
        }

        public static bool IsDisplayCodeColumn(ProcessTableColumnKind column) => column switch
        {
            ProcessTableColumnKind.Status
                or ProcessTableColumnKind.OperatingSystemContext
                or ProcessTableColumnKind.Platform
                or ProcessTableColumnKind.Elevated
                or ProcessTableColumnKind.UACVirtualization
                or ProcessTableColumnKind.DataExecutionPrevention
                or ProcessTableColumnKind.IOPriority
                or ProcessTableColumnKind.PowerThrottling
                or ProcessTableColumnKind.DPIAwareness
                or ProcessTableColumnKind.Architecture
                or ProcessTableColumnKind.HardwareStackProtection
                or ProcessTableColumnKind.ExtendedControlFlowGuard
                or ProcessTableColumnKind.Isolation => true,
            _ => false
        };

        private static bool IsPercentColumn(ProcessTableColumnKind column) => column switch
        {
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.GPU
                or ProcessTableColumnKind.NPU
                or ProcessTableColumnKind.CPUUtility => true,
            _ => false
        };

        private static bool IsUnsignedColumn(ProcessTableColumnKind column) => column switch
        {
            ProcessTableColumnKind.Cycle
                or ProcessTableColumnKind.IOReads
                or ProcessTableColumnKind.IOWrites
                or ProcessTableColumnKind.IOOther
                or ProcessTableColumnKind.IOReadBytes
                or ProcessTableColumnKind.IOWriteBytes
                or ProcessTableColumnKind.IOOtherBytes => true,
            _ => false
        };
    }
}

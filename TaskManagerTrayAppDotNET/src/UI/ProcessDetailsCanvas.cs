using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Composites two retained drawing roots per process from shared visible-column fragments.</summary>
internal sealed class ProcessDetailsCanvas : Control, IDisposable
{
    private const double DefaultViewportHeight = 900;
    private const int DynamicRefreshBatchSize = 16;
    private const double DynamicRefreshBudgetMilliseconds = 1.25;
    private const string UnavailableText = "Unavailable";
    private const string ZeroText = "0";
    private const string ZeroMemoryText = "0 K";
    private const string ZeroCPUTimeText = "0:00:00";

    private static readonly Typeface TableTypeface = new(TADNFontResolver.SegoeUIFamilyName);
    private static readonly Typeface GlyphTypeface = new(TADNFontResolver.SegoeFluentIconsFamilyName);
    private static readonly CultureInfo TableCulture = CultureInfo.CurrentCulture;

    private readonly ProcessIconService _processIconService;
    private readonly ProcessDataSchema _schema;
    private readonly ProcessTableMetrics _metrics;
    private readonly ProcessTableColumn[] _columns;
    private readonly bool _hasDynamicColumns;
    private readonly ProcessSnapshotBuffer _snapshot = new();
    private readonly Dictionary<ProcessInstanceKey, ProcessRowRenderCache> _renderCaches = new(256);
    private readonly Dictionary<ProcessSharedCellKey, SharedCellDrawing> _sharedCellDrawings = new();
    private readonly List<SharedCellDrawing> _sharedCellBuffer = new(8);
    private readonly List<ProcessInstanceKey> _staleProcessKeys = new(256);
    private readonly ProcessRowIndexComparer _rowComparer;
    private readonly FormattedText[] _headerTexts;
    private readonly FormattedText _ascendingCaretText;
    private readonly FormattedText _descendingCaretText;
    private readonly IBrush _backgroundBrush;
    private readonly IBrush _foregroundBrush;
    private readonly IBrush _secondaryForegroundBrush;
    private readonly IBrush _accentBrush;
    private readonly Pen _gridPen;
    private readonly double _sortCaretRightMargin;
    private readonly Action _refreshWarmDynamicDrawings;
    private int[] _visibleRowIndexes = [];
    private int[] _warmProcessIDs = [];
    private int _rowCount;
    private int _visibleRowCount;
    private int _cacheGeneration;
    private int _filterProcessID = -1;
    private int _warmRefreshCursor;
    private int _warmRefreshEnd;
    private long _snapshotVersion = -1;
    private string _filterText = string.Empty;
    private Rect _effectiveViewport;
    private ProcessTableColumnKind _sortColumn = ProcessTableColumnKind.Name;
    private ProcessInstanceKey? _selectedProcess;
    private int _hoveredVisibleIndex = -1;
    private double _pointerViewportY;
    private bool _sortDescending;
    private bool _pointerInside;
    private bool _dynamicRefreshScheduled;
    private bool _disposed;
    private ProcessSnapshotService? _snapshotService;

    public ProcessDetailsCanvas(
        ProcessIconService processIconService,
        ProcessDataSchema schema,
        IReadOnlyList<ProcessColumnSetting> columnSettings,
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
        _metrics = new ProcessTableMetrics(
            resources.AxamlProcessTable.HeaderHeight,
            resources.AxamlProcessTable.RowHeight,
            resources.AxamlProcessTable.CellPadding,
            resources.AxamlProcessTable.FontSize,
            resources.AxamlProcessTable.HeaderFontSize,
            resources.AxamlProcessTable.ProcessIconSize,
            resources.AxamlProcessTable.ProcessIconGap);
        _columns = CreateColumns(columnSettings);
        _hasDynamicColumns = ContainsLifetime(_columns, ProcessTableColumnLifetime.Dynamic);
        _rowComparer = new ProcessRowIndexComparer(_snapshot, _schema);
        _sortCaretRightMargin = resources.AxamlProcessTable.SortCaretRightMargin;
        _refreshWarmDynamicDrawings = RefreshWarmDynamicDrawings;

        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(TaskManagerWindowResources.ProcessGridBackgroundColor);
        _foregroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _secondaryForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
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

        ClipToBounds = true;
        Focusable = true;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public event Action<ProcessTerminationTarget?>? SelectedProcessChanged;
    public event Action<double?>? HoverRowTopChanged;
    public event Action<double?>? SelectionRowTopChanged;
    public event Action? ColumnsRequested;

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
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double contentWidth = _columns.Length == 0 ? 0 : _columns[^1].Right;
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

        DrawColumnGrid(context, viewport);
        DrawHeader(context, stickyHeaderTop);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        Point position = eventArgs.GetPosition(this);
        double stickyHeaderTop = Math.Max(0, _effectiveViewport.Y);
        bool isHeader = position.Y >= stickyHeaderTop
                        && position.Y < stickyHeaderTop + _metrics.HeaderHeight;
        if (pointerPoint.Properties.IsRightButtonPressed && isHeader)
        {
            ColumnsRequested?.Invoke();
            eventArgs.Handled = true;
            return;
        }

        if (!pointerPoint.Properties.IsLeftButtonPressed) return;
        if (isHeader)
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
        _pointerInside = true;
        _pointerViewportY = position.Y - Math.Max(0, _effectiveViewport.Y);
        UpdateHoveredRow(position.Y);
    }

    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        _pointerInside = false;
        SetHoveredVisibleIndex(-1);
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs eventArgs)
    {
        _effectiveViewport = eventArgs.EffectiveViewport;
        UpdateHoverFromPointer();
        PublishWarmProcesses();
        ScheduleWarmDynamicRefresh();
        InvalidateVisual();
    }

    private void OnIconsChanged()
    {
        if (!_disposed) InvalidateVisual();
    }

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

        return new Rect(0, 0, Bounds.Width, Math.Min(Bounds.Height, DefaultViewportHeight));
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
            cache.StaticDrawing?.Draw(context);
            cache.DynamicDrawing?.Draw(context);
        }

        DrawProcessIcon(context, viewport, row, top);
    }

    private void DrawProcessIcon(
        DrawingContext context,
        Rect viewport,
        ProcessStaticData row,
        double top)
    {
        int nameColumnIndex = FindColumn(ProcessTableColumnKind.Name);
        if (nameColumnIndex < 0) return;

        ProcessTableColumn nameColumn = _columns[nameColumnIndex];
        if (nameColumn.Right <= viewport.Left || nameColumn.Left >= viewport.Right) return;

        double iconTop = top + (_metrics.RowHeight - _metrics.ProcessIconSize) / 2;
        Rect iconBounds = new(
            nameColumn.Left + _metrics.CellPadding,
            iconTop,
            _metrics.ProcessIconSize,
            _metrics.ProcessIconSize);
        IImage? icon = _processIconService.GetOrQueue(row.Image.IconSource);
        if (icon != null)
            context.DrawImage(icon, iconBounds);
        else
            context.FillRectangle(_accentBrush, iconBounds, 2);
    }

    private void DrawColumnGrid(DrawingContext context, Rect viewport)
    {
        for (int columnIndex = 1; columnIndex < _columns.Length; columnIndex++)
        {
            double left = _columns[columnIndex].Left;
            if (left < viewport.Left || left > viewport.Right) continue;
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

        Rect viewport = ResolveViewport();
        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            ProcessTableColumn column = _columns[columnIndex];
            if (column.Right <= viewport.Left || column.Left >= viewport.Right) continue;

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

    private void UpdateRetainedDrawings()
    {
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
                continue;

            if (cache.StaticDrawing == null)
            {
                cache.StaticDrawing = BuildRowDrawing(
                    rowIndex,
                    ProcessTableColumnLifetime.Static,
                    out SharedCellDrawing[] sharedCells);
                cache.StaticSharedCells = sharedCells;
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

                string display = lifetime == ProcessTableColumnLifetime.Static
                    ? GetStaticDisplayValue(rowIndex, column.Kind)
                    : GetDynamicDisplayValue(rowIndex, column.Kind);
                if (display.Length == 0) continue;

                if (ShouldShareCell(column.Kind, display))
                {
                    ProcessSharedCellKey key = new(column.Kind, display);
                    SharedCellDrawing sharedCell = AcquireSharedCellDrawing(column, key);
                    children.Add(sharedCell.Drawing);
                    _sharedCellBuffer.Add(sharedCell);
                    continue;
                }

                DrawCell(uniqueContext, column, display);
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
            DrawCell(context, column, key.Value);
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

    private void DrawCell(DrawingContext context, ProcessTableColumn column, string display)
    {
        double textTop = Math.Max(0, (_metrics.RowHeight - _metrics.FontSize * 1.35) / 2);
        double leftInset = column.Kind == ProcessTableColumnKind.Name
            ? _metrics.CellPadding + _metrics.ProcessIconSize + _metrics.ProcessIconGap
            : _metrics.CellPadding;
        double availableWidth = Math.Max(0, column.Width - leftInset - _metrics.CellPadding);
        FormattedText text = CreateBoundedText(display, availableWidth);
        double textX = column.Alignment == ProcessTableColumnAlignment.Right
            ? column.Right - _metrics.CellPadding - text.Width
            : column.Left + leftInset;
        context.DrawText(text, new Point(textX, textTop));
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
            switch (kind)
            {
                case ProcessTableColumnKind.CPU:
                case ProcessTableColumnKind.GPU:
                case ProcessTableColumnKind.NPU:
                case ProcessTableColumnKind.CPUUtility:
                    hash.Add(QuantizePercent(BitConverter.Int64BitsToDouble(value)));
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
                    hash.Add(ToKibibytes(value));
                    break;
                case ProcessTableColumnKind.WorkingSetDelta:
                    hash.Add(ToSignedKibibytes(value));
                    break;
                default:
                    hash.Add(value);
                    break;
            }
        }

        return hash.ToHashCode();
    }

    private string GetStaticDisplayValue(int rowIndex, ProcessTableColumnKind kind)
    {
        ProcessStaticData row = _snapshot.StaticRows[rowIndex]
            ?? throw new InvalidOperationException("A published process row is missing static data.");
        if (kind == ProcessTableColumnKind.ProcessID)
            return row.ProcessID.ToString(TableCulture);

        string? identityText = GetIdentityText(row, kind);
        if (identityText != null) return identityText;

        if (ProcessDataSchema.StoresText(kind))
        {
            int slot = _schema.GetStaticTextSlot(kind);
            return slot < 0 ? string.Empty : row.TextValues[slot] ?? string.Empty;
        }

        int numericSlot = _schema.GetStaticNumericSlot(kind);
        if (numericSlot < 0) return string.Empty;
        long value = row.NumericValues[numericSlot];
        return kind switch
        {
            ProcessTableColumnKind.ProcessID => value.ToString(TableCulture),
            ProcessTableColumnKind.SessionID => value < 0 ? UnavailableText : value.ToString(TableCulture),
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
        if (ProcessDataSchema.StoresText(kind)) return _snapshot.GetDynamicText(rowIndex, kind);

        long value = _snapshot.GetDynamicNumeric(rowIndex, kind);
        return kind switch
        {
            ProcessTableColumnKind.Status => FormatDisplayCode(value),
            ProcessTableColumnKind.JobObjectID => FormatJobObjectID(value),
            ProcessTableColumnKind.CPU => FormatPercent(BitConverter.Int64BitsToDouble(value)),
            ProcessTableColumnKind.CPUTime => FormatCPUTime(value),
            ProcessTableColumnKind.Cycle => FormatUnsigned(value),
            ProcessTableColumnKind.WorkingSet => FormatMemory(value),
            ProcessTableColumnKind.PeakWorkingSet => FormatMemory(value),
            ProcessTableColumnKind.WorkingSetDelta => FormatMemoryDelta(value),
            ProcessTableColumnKind.ActivePrivateWorkingSet => FormatMemory(value),
            ProcessTableColumnKind.PrivateMemory => FormatMemory(value),
            ProcessTableColumnKind.SharedWorkingSet => FormatMemory(value),
            ProcessTableColumnKind.CommitSize => FormatMemory(value),
            ProcessTableColumnKind.PagedPool => FormatMemory(value),
            ProcessTableColumnKind.NonPagedPool => FormatMemory(value),
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
            ProcessTableColumnKind.GPU => FormatPercent(BitConverter.Int64BitsToDouble(value)),
            ProcessTableColumnKind.DedicatedGPUMemory => FormatMemory(value),
            ProcessTableColumnKind.SharedGPUMemory => FormatMemory(value),
            ProcessTableColumnKind.DPIAwareness => FormatDisplayCode(value),
            ProcessTableColumnKind.NPU => FormatPercent(BitConverter.Int64BitsToDouble(value)),
            ProcessTableColumnKind.DedicatedNPUMemory => FormatMemory(value),
            ProcessTableColumnKind.SharedNPUMemory => FormatMemory(value),
            ProcessTableColumnKind.CPUUtility => FormatPercent(BitConverter.Int64BitsToDouble(value)),
            _ => string.Empty
        };
    }

    private static string FormatDisplayCode(long value) =>
        ProcessDisplayCodeText.Get((ProcessDisplayCode)value);

    private static string FormatJobObjectID(long value) => value switch
    {
        < 0 => UnavailableText,
        0 => string.Empty,
        _ => value.ToString(TableCulture)
    };

    private static string FormatPercent(double value)
    {
        if (value < 0) return UnavailableText;
        long tenths = QuantizePercent(value);
        return tenths == 0 ? ZeroText : (tenths / 10.0).ToString("0.0", TableCulture);
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

    private static string FormatMemory(long bytes) => bytes switch
    {
        < 0 => UnavailableText,
        0 => ZeroMemoryText,
        _ => string.Concat(ToKibibytes(bytes).ToString("N0", TableCulture), " K")
    };

    private static string FormatMemoryDelta(long bytes) => bytes == 0
        ? ZeroMemoryText
        : string.Concat(ToSignedKibibytes(bytes).ToString("N0", TableCulture), " K");

    private static string FormatSigned(long value) => value == 0
        ? ZeroText
        : value.ToString("N0", TableCulture);

    private static string FormatUnsigned(long value) => value == 0
        ? ZeroText
        : unchecked((ulong)value).ToString("N0", TableCulture);

    private static long QuantizePercent(double value) => value < 0
        ? -1
        : (long)Math.Round(Math.Max(0, value) * 10, MidpointRounding.AwayFromZero);

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
                >= DynamicRefreshBudgetMilliseconds)
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
        PublishWarmProcesses();
        UpdateSelectionOverlay();
        UpdateHoverFromPointer();
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
        Array.Sort(_visibleRowIndexes, 0, _visibleRowCount, _rowComparer);
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
        if (_visibleRowIndexes.Length >= count) return;

        int capacity = Math.Max(256, _visibleRowIndexes.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _visibleRowIndexes, capacity);
    }

    private void EnsureWarmCapacity(int count)
    {
        if (_warmProcessIDs.Length >= count) return;

        int capacity = Math.Max(256, _warmProcessIDs.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _warmProcessIDs, capacity);
    }

    private int FindColumn(ProcessTableColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            if (_columns[columnIndex].Kind == kind) return columnIndex;
        }

        return -1;
    }

    private static ProcessTableColumn[] CreateColumns(IReadOnlyList<ProcessColumnSetting> source)
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.Normalize(source);
        List<ProcessTableColumn> columns = new(settings.Count);
        double left = 0;
        foreach (ProcessColumnSetting setting in settings)
        {
            if (!setting.Visible) continue;

            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
            columns.Add(new ProcessTableColumn(
                setting.Column,
                definition.Title,
                left,
                setting.Width,
                definition.Alignment));
            left += setting.Width;
        }

        return columns.ToArray();
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

    private static bool ShouldShareCell(ProcessTableColumnKind column, string value)
    {
        if (value is ZeroText or ZeroMemoryText or ZeroCPUTimeText or UnavailableText) return true;
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

        _disposed = true;
        _processIconService.IconsChanged -= OnIconsChanged;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        SelectedProcessChanged = null;
        HoverRowTopChanged = null;
        SelectionRowTopChanged = null;
        ColumnsRequested = null;
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _renderCaches.Clear();
        _sharedCellDrawings.Clear();
        _sharedCellBuffer.Clear();
        _staleProcessKeys.Clear();
        _snapshot.Reset();
    }

    private readonly record struct ProcessSharedCellKey(ProcessTableColumnKind Column, string Value);

    private sealed class ProcessRowRenderCache
    {
        public int LastSeenGeneration;
        public int DynamicFingerprint;
        public int PendingDynamicFingerprint;
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

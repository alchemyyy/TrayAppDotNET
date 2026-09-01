using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

internal enum ProcessCopyPreviewMode : byte
{
    None,
    Cell,
    Row
}

internal readonly record struct ProcessRowContextMenuRequest(
    ProcessEndTaskRequest EndTaskRequest,
    PixelPoint ScreenPosition,
    string CellCopyText,
    string RowCopyText)
{
    public ProcessTerminationTarget Target => EndTaskRequest.Processes[0].Target;

    public bool IsMultiple => EndTaskRequest.Count > 1;
}

/// <summary>Composites bounded viewport rows from shared visible-column text layouts.</summary>
internal sealed class ProcessDetailsCanvas : DetailsGridControl
{
    [Flags]
    private enum RenderLayerMask : byte
    {
        None = 0,
        StaticRows = 1 << 0,
        DynamicRows = 1 << 1,
        Icons = 1 << 2,
        CopyPreview = 1 << 3,
        Chrome = 1 << 4,
        Header = 1 << 5,
        HeaderInteraction = 1 << 6,
        Selection = 1 << 7,
        Rows = StaticRows | DynamicRows,
        All = StaticRows | DynamicRows | Icons | CopyPreview | Chrome | Header | HeaderInteraction | Selection
    }

    private enum RenderLayerKind : byte
    {
        Selection,
        StaticRows,
        DynamicRows,
        Icons,
        CopyPreview,
        Chrome,
        Header,
        HeaderInteraction
    }

    private const int DynamicRefreshBatchSize = 16;
    private const int MaximumTextLayoutCharacters = 2_048;
    private const string RowTextMeasurementText = "Ag";
    private const string TextEllipsis = "\u2026";
    private const string ZeroText = "0";
    private const string ZeroMemoryText = "0 K";
    private const string ZeroCPUTimeText = "0:00:00";
    private const string UnavailableText = ProcessTableValuePresentation.UnavailableText;
    private const double BytesPerMebibyte = 1_048_576;
    private const double BytesPerMegabit = 1_000_000.0 / 8;
    private const int TreeLayoutValueMask = 0x1FF;
    private const int SemanticSectionLayoutFlag = 1 << 9;
    private const int SemanticSectionHeaderLayoutFlag = 1 << 10;

    private static readonly Typeface DefaultTableTypeface = new(TADNFontResolver.SegoeUIFamilyName);
    private static readonly CultureInfo TableCulture = CultureInfo.CurrentCulture;

    private readonly ProcessIconService _processIconService;
    private ProcessDataSchema _schema;
    private readonly TaskManagerWindowResources _resources;
    private readonly DetailsGridFontWeight _baseTableFontWeight;
    private readonly double _rowTextHeightScale;
    private Typeface _tableTypeface;
    private Typeface _liveTotalTypeface;
    private LiveTotalTypography _liveTotalTypography;
    private int _tableFontWeight;
    private readonly ProcessTableRenderLayer _selectionLayer;
    private readonly ProcessTableRenderLayer _staticRowsLayer;
    private readonly ProcessTableRenderLayer _dynamicRowsLayer;
    private readonly ProcessTableRenderLayer _iconsLayer;
    private readonly ProcessTableRenderLayer _copyPreviewLayer;
    private readonly ProcessTableRenderLayer _chromeLayer;
    private readonly ProcessTableRenderLayer _headerLayer;
    private readonly ProcessTableRenderLayer _headerInteractionLayer;
    private readonly ProcessHeaderHoverVisual _headerHoverLayer;
    private readonly Control[] _renderLayers;
    private ProcessTableMetrics _metrics;
    private ProcessTableVisualMetrics _visualMetrics;
#if DEBUG
    private ProcessTableAXAMLColumnWidths _axamlColumnWidths;
#endif
    private bool _hasDynamicColumns;
    private readonly bool _enableLiveColumnResizing;
    private readonly ProcessSnapshotBuffer _sourceSnapshot = new();
    private readonly ProcessSnapshotBuffer _snapshot = new();
    private readonly Dictionary<ProcessInstanceKey, ProcessRowRenderCache> _renderCaches = new(256);
    private readonly Dictionary<ProcessSharedCellKey, SharedCellLayout> _sharedCellLayouts = new();
    private readonly List<SharedCellLayout> _sharedCellBuffer = new(8);
    private readonly List<CellTextLayout> _cellTextLayoutBuffer = new(8);
    private readonly List<ProcessInstanceKey> _staleProcessKeys = new(256);
    private readonly HashSet<ProcessInstanceKey> _collapsedProcesses = [];
    private readonly Dictionary<int, int> _rowIndexByProcessID = new(1_024);
    private readonly Dictionary<ProcessInstanceKey, int> _sourceRowIndexByInstance = new(1_024);
    private readonly Dictionary<ProcessInstanceKey, int> _rowIndexByInstance = new(1_024);
    private readonly Dictionary<SemanticProcessGroupKey, ProcessInstanceKey> _syntheticKeyByGroup = [];
    private readonly Dictionary<ProcessInstanceKey, ProcessInstanceKey[]> _membersBySyntheticKey = [];
    private readonly Dictionary<ProcessInstanceKey, ProcessInstanceKey?> _semanticParentByInstance = [];
    private readonly Dictionary<ProcessInstanceKey, SemanticProcessGroupClassification>
        _semanticClassificationByInstance = [];
    private readonly HashSet<ProcessInstanceKey> _warmProcessKeySet = [];
    private readonly HashSet<SemanticProcessGroupKey> _liveSemanticGroupKeys = [];
    private readonly List<SemanticProcessGroupKey> _staleSemanticGroupKeys = [];
    private readonly ProcessRowIndexComparer _rowComparer;
    private TextLayout _ascendingCaretText;
    private TextLayout _descendingCaretText;
#if DEBUG
    private Color _backgroundColor;
    private IBrush _backgroundBrush;
#else
    private readonly IBrush _backgroundBrush;
#endif
    private readonly IBrush _foregroundBrush;
    private readonly IBrush _secondaryForegroundBrush;
    private readonly IBrush _selectionBackgroundBrush;
    private readonly IBrush _accentBrush;
    private readonly IBrush _borderBrush;
    private Pen _gridPen;
    private Pen _columnInteractionPen;
    private Pen _textUnderlinePen;
    private Pen _treeExpanderPen;
    private Thickness _selectionBorderThickness;
    private double _sortCaretRightMargin;
    private readonly long _totalPhysicalMemoryBytes;
    private readonly Action _refreshWarmDynamicDrawings;
    private readonly ProcessSearchValueResolver _resolveSearchValue;
    private ProcessTableColumn[]? _liveResizeColumns;
    private TextUnderlineSegment[] _textUnderlineSegments;
    private List<ProcessColumnSetting> _columnSettings;
    private ProcessColumnSetting[] _settingsByColumn;
    private readonly string?[] _liveTotalTextsByColumn =
        new string?[ProcessTableColumnCatalog.Definitions.Length];
    private ProcessTableColumn[] _columns;
    private HeaderTextLayouts[] _headerTexts = [];
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
    private bool[] _filterIncludedRows = [];
    private byte[] _rowDepths = [];
    private bool[] _rowHasChildren = [];
    private SemanticProcessSectionRowKind[] _semanticSectionRowKinds = [];
    private byte[] _semanticRowClassifications = [];
    private readonly int[] _semanticSectionSpacerRowIndexes = new int[SemanticProcessSections.Count];
    private readonly int[] _semanticSectionHeaderRowIndexes = new int[SemanticProcessSections.Count];
    private readonly int[] _semanticSectionEntryCounts = new int[SemanticProcessSections.Count];
    private readonly int[] _semanticSectionVisibleStarts = new int[SemanticProcessSections.Count];
    private int[] _warmProcessIDs = [];
    private int _rowCount;
    private int _visibleRowCount;
    private int _cacheGeneration;
    private int _gridMetricsGeneration = 1;
    private int _retainedFirstVisibleIndex = -1;
    private int _retainedLastVisibleIndexExclusive = -1;
    private int _warmRefreshCursor;
    private int _warmRefreshEnd;
    private long _snapshotVersion = -1;
    private string _filterText = string.Empty;
    private ProcessSearchQuery _filterQuery;
    private ProcessTableColumnKind _sortColumn = ProcessTableColumnKind.Name;
    private readonly HashSet<ProcessInstanceKey> _selectedProcesses = [];
    private ProcessInstanceKey? _selectedProcess;
    private ProcessInstanceKey? _selectionAnchorProcess;
    private IPointer? _capturedHeaderPointer;
    private HeaderInteractionMode _headerInteraction;
    private Point _headerPressPosition;
    private int _interactionColumnIndex = -1;
    private int _reorderInsertionIndex = -1;
    private int _hoveredHeaderColumnIndex = -1;
    private int _textUnderlineSegmentCount;
    private double _resizeInitialWidth;
    private double _resizePreviewWidth;
    private double _headerDragX;
    private double _headerPointerOffsetX;
    private ContextCopyRow[] _contextCopyRows = [];
    private ProcessTableColumnKind? _contextCopyColumn;
    private ProcessCopyPreviewMode _copyPreviewMode;
    private bool _sortDescending;
    private bool _isLiveColumnResizeActive;
    private bool _hasVisibleLiveTotals;
    private bool _dynamicRefreshScheduled;
    private bool _usesSemanticSections;
    private ProcessGroupingStyle _processGroupingStyle;
#if DEBUG
    private double _axamlFontSize;
    private double _axamlRowSpacing;
#endif
    private PendingColumnLayout? _pendingColumnLayout;
    private ProcessViewportAnchor? _pendingViewportAnchor;
    private ProcessRowHoverGeometry _publishedRowHoverGeometry;
    private bool _hasPublishedRowHoverGeometry;
    private bool _externalSubscriptionsAttached;
    private ProcessSnapshotService? _snapshotService;
    private bool _samplingActive;
    private SemanticProcessTreeState _semanticTreeState = new();
    private int _nextSyntheticProcessID = SemanticProcessSections.FirstGroupSyntheticProcessID;

    public ProcessDetailsCanvas(
        ProcessIconService processIconService,
        ProcessDataSchema schema,
        IReadOnlyList<ProcessColumnSetting> columnSettings,
        bool enableLiveColumnResizing,
        double gridFontSize,
        DetailsGridFontWeight gridFontWeight,
        double gridRowSpacing,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(processIconService);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(columnSettings);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);

        Array.Fill(_semanticSectionVisibleStarts, value: -1);
        _processIconService = processIconService;
        _schema = schema;
        _resources = resources;
        _baseTableFontWeight = gridFontWeight;
        _tableFontWeight = CalculateTableFontWeight(gridFontSize);
        _tableTypeface = CreateTableTypeface(_tableFontWeight);
        _liveTotalTypography = CreateLiveTotalTypography(resources);
        _liveTotalTypeface = CreateLiveTotalTypeface(_liveTotalTypography);
        Typeface referenceTypeface = CreateTableTypeface((int)gridFontWeight);
        _rowTextHeightScale = MeasureRowTextHeightScale(referenceTypeface);
        double gridRowHeight = CalculateRowHeight(gridFontSize, gridRowSpacing);
        _metrics = CreateTableMetrics(
            resources,
            gridFontSize,
            gridRowHeight,
            _rowTextHeightScale);
        _visualMetrics = CreateVisualMetrics(resources);
#if DEBUG
        _axamlColumnWidths = CreateAXAMLColumnWidths(resources);
        _axamlFontSize = resources.AxamlProcessTable.FontSize;
        _axamlRowSpacing = resources.AxamlProcessTable.RowSpacing;
#endif
        _columnSettings = ProcessColumnSettings.Normalize(columnSettings);
        _settingsByColumn = CreateColumnSettingsIndex(_columnSettings);
        _hasVisibleLiveTotals = ProcessColumnSettings.HasVisibleLiveTotals(_columnSettings);
        _filterQuery = ProcessSearchQuery.Parse(filterText: null, _columnSettings);
        _resolveSearchValue = GetSearchColumnValue;
        _columns = CreateColumns(_columnSettings);
        _hasDynamicColumns = ContainsLifetime(_columns, ProcessTableColumnLifetime.Dynamic);
        _enableLiveColumnResizing = enableLiveColumnResizing;
        _liveResizeColumns = enableLiveColumnResizing
            ? new ProcessTableColumn[_columns.Length]
            : null;
        _textUnderlineSegments = new TextUnderlineSegment[_columns.Length];
        _rowComparer = new ProcessRowIndexComparer(_snapshot, _schema);
        _sortCaretRightMargin = _visualMetrics.SortCaretRightMargin;
        _totalPhysicalMemoryBytes = NativeProcessInfo.ReadTotalPhysicalMemoryBytes();
        _refreshWarmDynamicDrawings = RefreshWarmDynamicDrawings;

#if DEBUG
        _backgroundColor = resources.AxamlProcessTable.GridBackgroundColor;
#endif
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(
            resources.AxamlProcessTable.GridBackgroundColor);
        _foregroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _secondaryForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        _selectionBackgroundBrush = TrayAppDotNETSettingsUI.Brush(palette.SearchListItemSelected);
        _accentBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent);
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
        _selectionBorderThickness = resources.AxamlProcessTable.SelectionBorderThickness;

        (_ascendingCaretText, _descendingCaretText) = CreateSortCaretTexts(
            _visualMetrics.SortCaretFontSize,
            _secondaryForegroundBrush);
        _headerTexts = CreateHeaderTexts(_columns);

        _selectionLayer = new ProcessTableRenderLayer(this, RenderLayerKind.Selection);
        _staticRowsLayer = new ProcessTableRenderLayer(this, RenderLayerKind.StaticRows);
        _dynamicRowsLayer = new ProcessTableRenderLayer(this, RenderLayerKind.DynamicRows);
        _iconsLayer = new ProcessTableRenderLayer(this, RenderLayerKind.Icons);
        _copyPreviewLayer = new ProcessTableRenderLayer(this, RenderLayerKind.CopyPreview);
        _chromeLayer = new ProcessTableRenderLayer(this, RenderLayerKind.Chrome);
        _headerLayer = new ProcessTableRenderLayer(this, RenderLayerKind.Header);
        _headerInteractionLayer = new ProcessTableRenderLayer(this, RenderLayerKind.HeaderInteraction);
        _headerHoverLayer = new ProcessHeaderHoverVisual(palette.Hover);
        _renderLayers =
        [
            _selectionLayer,
            _staticRowsLayer,
            _dynamicRowsLayer,
            _iconsLayer,
            _copyPreviewLayer,
            _chromeLayer,
            _headerHoverLayer,
            _headerLayer,
            _headerInteractionLayer
        ];

        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Attaches external notifications after the owning page is fully constructed.</summary>
    internal void AttachExternalSubscriptions()
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        if (_externalSubscriptionsAttached) return;

        _processIconService.IconsChanged += OnIconsChanged;
#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphResourcesReloaded;
#endif
        _externalSubscriptionsAttached = true;
    }

    public event Action<ProcessTerminationTarget?>? SelectedProcessChanged;
    public event Action<ProcessRowHoverGeometry>? RowHoverGeometryChanged;
    public event Action<ProcessViewportAnchorAdjustment>? ViewportAnchorAdjustmentRequested;
    public event Action<ProcessTableColumnKind>? ColumnPropertiesRequested;
    public event Action<List<ProcessColumnSetting>>? ColumnLayoutChanged;
    public event Action<ProcessEndTaskRequest>? EndTaskRequested;
    public event Action<ProcessRowContextMenuRequest>? RowContextMenuRequested;

    private ProcessTableColumn[] DisplayColumns =>
        _isLiveColumnResizeActive ? _liveResizeColumns! : _columns;

    /// <summary>Returns the fixed retained visual stack rendered beneath the input canvas.</summary>
    public IReadOnlyList<Control> RenderLayers => _renderLayers;

    /// <summary>Returns the effective row height derived from rendered text and spacing.</summary>
    public double RowHeight => _metrics.RowHeight;

#if DEBUG
    /// <summary>Returns the currently rendered font size rather than the persisted startup value.</summary>
    public double GridFontSize => _metrics.FontSize;

    /// <summary>Returns the currently rendered gap between adjacent row text.</summary>
    public double GridRowSpacing => _metrics.RowHeight - _metrics.RowTextHeight;
#endif

    /// <summary>Applies font size and the requested gap between rendered text lines.</summary>
    public void SetGridTypography(double fontSize, double rowSpacing)
    {
        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        SetGridMetrics(fontSize, CalculateRowHeight(fontSize, rowSpacing));
        RestoreViewportAnchor(viewportAnchor);
    }

    protected override int DetailsGridRowCount => _visibleRowCount;
    protected override double DetailsGridHeaderHeight => _metrics.HeaderHeight;
    protected override double DetailsGridRowHeight => _metrics.RowHeight;
    protected override double DetailsGridFontSize => _metrics.FontSize;
    protected override double DetailsGridDefaultViewportHeight => _visualMetrics.DefaultViewportHeight;
    protected override bool CanResetDetailsGridZoom => _headerInteraction == HeaderInteractionMode.None;

    /// <summary>Returns the structural state consumed by the render-thread row-hover sampler.</summary>
    public ProcessRowHoverGeometry RowHoverGeometry => CreateRowHoverGeometry();

    public int? SelectedProcessID => _selectedProcess is { } process
                                     && _sourceRowIndexByInstance.ContainsKey(process)
        ? process.ProcessID
        : null;

    public int SelectedProcessCount => CountSelectedProcessInstances();

    public ProcessTerminationTarget? SelectedTerminationTarget => _selectedProcess is { } process
        && _sourceRowIndexByInstance.ContainsKey(process)
        ? new ProcessTerminationTarget(process.ProcessID, process.CreationTimeTicks)
        : null;

    public ProcessEndTaskRequest? SelectedEndTaskRequest
    {
        get
        {
            ProcessEndTaskItem[] selectedProcesses = CreateSelectedEndTaskItems();
            return selectedProcesses.Length == 0
                ? null
                : new ProcessEndTaskRequest(selectedProcesses);
        }
    }

#if DEBUG
    public ProcessTerminationTarget[] SelectedTerminationTargets => CreateSelectedTerminationTargets();
#endif

    /// <summary>Clears every selected process and its keyboard-driven action target.</summary>
    internal void ClearSelection() => ApplyPointerSelection(
        visibleIndex: -1,
        KeyModifiers.None);

#if DEBUG
    /// <summary>Restores identity-checked selections after a shared shell rebuild.</summary>
    internal void RestoreSelectedProcesses(
        IReadOnlyList<ProcessTerminationTarget> targets,
        ProcessTerminationTarget? activeTarget)
    {
        if (IsDetailsGridDisposed) return;
        ArgumentNullException.ThrowIfNull(targets);

        HashSet<ProcessInstanceKey> availableProcesses = new(_rowCount);
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row != null) availableProcesses.Add(row.InstanceKey);
        }

        _selectedProcesses.Clear();
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            ProcessTerminationTarget target = targets[targetIndex];
            ProcessInstanceKey candidate = new(target.ProcessID, target.CreationTimeFileTime);
            if (availableProcesses.Contains(candidate)) _selectedProcesses.Add(candidate);
        }

        ProcessInstanceKey? activeProcess = activeTarget is { } selectedTarget
            ? new ProcessInstanceKey(selectedTarget.ProcessID, selectedTarget.CreationTimeFileTime)
            : null;
        _selectedProcess = activeProcess.HasValue && _selectedProcesses.Contains(activeProcess.Value)
            ? activeProcess
            : FindFirstSelectedProcess();
        _selectionAnchorProcess = _selectedProcess;
        NotifySelectionChanged();
        UpdateSelectionOverlay();
        InvalidateLayers(RenderLayerMask.All);
    }
#endif

    /// <summary>Copies the newest compact snapshot and updates only changed retained row roots.</summary>
    public void RefreshFrom(ProcessSnapshotService snapshotService)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        ArgumentNullException.ThrowIfNull(snapshotService);

        _snapshotService ??= snapshotService;
        ProcessViewportAnchor? viewportAnchor = _visibleRowCount > 0
            ? CaptureViewportAnchor()
            : _pendingViewportAnchor;
        if (_pendingColumnLayout is { } pendingColumnLayout)
        {
            if (!snapshotService.TryCopyLatest(
                    _sourceSnapshot,
                    pendingColumnLayout.Schema.VisibleMask,
                    out int pendingCount,
                    out long pendingVersion))
                return;

            CommitPendingColumnLayout(
                pendingColumnLayout,
                pendingCount,
                pendingVersion,
                viewportAnchor);
            return;
        }

        if (!snapshotService.TryCopyLatest(
                _sourceSnapshot,
                _schema.VisibleMask,
                out int count,
                out long version))
            return;
        if (version == _snapshotVersion) return;

        RebuildFromCopiedSnapshot(count, version, viewportAnchor);
    }

    private void RebuildFromCopiedSnapshot(
        int count,
        long version,
        ProcessViewportAnchor? viewportAnchor)
    {
        _snapshotVersion = version;
        BuildPresentationSnapshot(count);
        EnsureRowCapacity(_rowCount);
        BuildLogicalParentIndexes();
        SynchronizeRenderCacheMembership();
        RebuildVisibleRows();
        EnsureSelectedProcessesStillExist();
        RefreshLiveTotalHeaders();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);
        _pendingViewportAnchor = null;
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateLayers(RenderLayerMask.All);
    }

    private void BuildPresentationSnapshot(int sourceCount)
    {
        if (sourceCount != _sourceSnapshot.Count)
            throw new InvalidOperationException("The copied process count does not match its snapshot.");

        RebuildSourceRowIndex();
        ResetSemanticSectionPresentation();
        if (_processGroupingStyle != ProcessGroupingStyle.Semantic)
        {
            _snapshot.CopyFrom(_sourceSnapshot);
            _rowCount = sourceCount;
            _membersBySyntheticKey.Clear();
            _semanticParentByInstance.Clear();
            _semanticClassificationByInstance.Clear();
            return;
        }

        ProcessGroupingFacts[] groupingFacts = new ProcessGroupingFacts[sourceCount];
        for (int rowIndex = 0; rowIndex < sourceCount; rowIndex++)
        {
            ProcessStaticData row = _sourceSnapshot.StaticRows[rowIndex]
                                    ?? throw new InvalidOperationException(
                                        "A source process row is missing static data.");
            ProcessGroupingFacts facts = _sourceSnapshot.GroupingFacts[rowIndex];
            groupingFacts[rowIndex] = facts.InstanceKey == row.InstanceKey
                ? facts
                : CreateFallbackGroupingFacts(row);
        }

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build(
            groupingFacts,
            _semanticTreeState);
        _semanticTreeState = forest.RetainedState;

        int syntheticCount = 0;
        for (int groupIndex = 0; groupIndex < forest.Groups.Length; groupIndex++)
        {
            SemanticProcessGroup group = forest.Groups[groupIndex];
            if (group.Nodes.Length > 1) syntheticCount++;
            _semanticSectionEntryCounts[(int)group.Classification]++;
        }

        int sectionRowCount = 0;
        for (int sectionIndex = 0; sectionIndex < SemanticProcessSections.Count; sectionIndex++)
        {
            SemanticProcessGroupClassification classification =
                SemanticProcessSections.GetClassification(sectionIndex);
            if (_semanticSectionEntryCounts[(int)classification] > 0)
                sectionRowCount += SemanticProcessSections.RowsPerSection;
        }

        ProcessDataSchema schema = _sourceSnapshot.Schema
                                   ?? throw new InvalidOperationException(
                                       "The source process snapshot has no schema.");
        _snapshot.BeginWrite(
            schema,
            checked(sourceCount + syntheticCount + sectionRowCount));
        Array.Copy(_sourceSnapshot.StaticRows, _snapshot.StaticRows, sourceCount);
        Array.Copy(_sourceSnapshot.GroupingFacts, _snapshot.GroupingFacts, sourceCount);
        Array.Copy(
            _sourceSnapshot.DynamicNumericValues,
            _snapshot.DynamicNumericValues,
            checked(sourceCount * schema.DynamicNumericCount));
        Array.Copy(
            _sourceSnapshot.DynamicTextValues,
            _snapshot.DynamicTextValues,
            checked(sourceCount * schema.DynamicTextCount));

        _membersBySyntheticKey.Clear();
        _semanticParentByInstance.Clear();
        _semanticClassificationByInstance.Clear();
        _liveSemanticGroupKeys.Clear();
        int presentationRowIndex = sourceCount;
        for (int groupIndex = 0; groupIndex < forest.Groups.Length; groupIndex++)
        {
            SemanticProcessGroup group = forest.Groups[groupIndex];
            ProcessInstanceKey? syntheticInstanceKey = null;
            if (group.Nodes.Length > 1)
            {
                ProcessInstanceKey groupInstanceKey = GetOrCreateSyntheticInstanceKey(group.Key);
                syntheticInstanceKey = groupInstanceKey;
                _liveSemanticGroupKeys.Add(group.Key);

                int[] memberRowIndexes = new int[group.Nodes.Length];
                ProcessInstanceKey[] memberInstanceKeys = new ProcessInstanceKey[group.Nodes.Length];
                int memberWriteIndex = 0;
                AddSemanticGroupMember(
                    group.RepresentativeInstanceKey,
                    memberRowIndexes,
                    memberInstanceKeys,
                    ref memberWriteIndex);
                for (int memberIndex = 0; memberIndex < group.Nodes.Length; memberIndex++)
                {
                    ProcessInstanceKey memberInstanceKey = group.Nodes[memberIndex].Facts.InstanceKey;
                    if (memberInstanceKey == group.RepresentativeInstanceKey) continue;
                    AddSemanticGroupMember(
                        memberInstanceKey,
                        memberRowIndexes,
                        memberInstanceKeys,
                        ref memberWriteIndex);
                }

                if (!_sourceRowIndexByInstance.TryGetValue(
                        group.RepresentativeInstanceKey,
                        out int representativeRowIndex))
                {
                    throw new InvalidOperationException(
                        "A semantic process group has no display representative.");
                }

                ProcessStaticData syntheticStaticData = CreateSyntheticStaticData(
                    group,
                    groupInstanceKey,
                    representativeRowIndex,
                    schema);
                long[] dynamicNumericValues = CreateSyntheticDynamicNumericValues(
                    memberRowIndexes,
                    representativeRowIndex,
                    schema);
                string?[] dynamicTextValues = CreateSyntheticDynamicTextValues(
                    representativeRowIndex,
                    schema);
                _snapshot.SetRow(
                    presentationRowIndex,
                    syntheticStaticData,
                    dynamicNumericValues,
                    dynamicTextValues);
                _membersBySyntheticKey.Add(groupInstanceKey, memberInstanceKeys);
                _semanticParentByInstance.Add(groupInstanceKey, value: null);
                _semanticClassificationByInstance.Add(groupInstanceKey, group.Classification);
                presentationRowIndex++;
            }

            for (int memberIndex = 0; memberIndex < group.Nodes.Length; memberIndex++)
            {
                SemanticProcessNode node = group.Nodes[memberIndex];
                ProcessInstanceKey? parentInstanceKey = node.ParentInstanceKey;
                if (!parentInstanceKey.HasValue && syntheticInstanceKey.HasValue)
                    parentInstanceKey = syntheticInstanceKey;
                _semanticParentByInstance[node.Facts.InstanceKey] = parentInstanceKey;
                _semanticClassificationByInstance[node.Facts.InstanceKey] = group.Classification;
            }
        }

        AppendSemanticSectionRows(schema, ref presentationRowIndex);

        _snapshot.CompleteWrite(presentationRowIndex);
        _rowCount = presentationRowIndex;
        PruneSyntheticGroupKeys();
    }

    private void ResetSemanticSectionPresentation()
    {
        Array.Fill(_semanticSectionSpacerRowIndexes, value: -1);
        Array.Fill(_semanticSectionHeaderRowIndexes, value: -1);
        Array.Clear(_semanticSectionEntryCounts);
    }

    private void AppendSemanticSectionRows(
        ProcessDataSchema schema,
        ref int presentationRowIndex)
    {
        for (int sectionIndex = 0; sectionIndex < SemanticProcessSections.Count; sectionIndex++)
        {
            SemanticProcessGroupClassification classification =
                SemanticProcessSections.GetClassification(sectionIndex);
            int classificationIndex = (int)classification;
            int entryCount = _semanticSectionEntryCounts[classificationIndex];
            if (entryCount == 0) continue;

            _semanticSectionSpacerRowIndexes[classificationIndex] = presentationRowIndex;
            AppendSemanticSectionRow(
                schema,
                classification,
                SemanticProcessSectionRowKind.Spacer,
                entryCount,
                ref presentationRowIndex);

            _semanticSectionHeaderRowIndexes[classificationIndex] = presentationRowIndex;
            AppendSemanticSectionRow(
                schema,
                classification,
                SemanticProcessSectionRowKind.Header,
                entryCount,
                ref presentationRowIndex);
        }
    }

    private void AppendSemanticSectionRow(
        ProcessDataSchema schema,
        SemanticProcessGroupClassification classification,
        SemanticProcessSectionRowKind rowKind,
        int entryCount,
        ref int presentationRowIndex)
    {
        ProcessInstanceKey instanceKey = SemanticProcessSections.GetInstanceKey(
            classification,
            rowKind);
        string title = rowKind == SemanticProcessSectionRowKind.Header
            ? SemanticProcessSections.GetTitle(classification, entryCount)
            : string.Empty;
        ProcessImageIdentity image = new(
            key: $"semantic-section:{instanceKey.ProcessID.ToString(TableCulture)}",
            title,
            imagePath: string.Empty,
            description: string.Empty,
            iconSource: default);
        ProcessStaticData staticData = new()
        {
            InstanceKey = instanceKey,
            IsCreationTimeKnown = true,
            Image = image,
            UserName = string.Empty,
            NumericValues = new long[schema.StaticNumericCount],
            TextValues = new string?[schema.StaticTextCount]
        };
        _snapshot.SetRow(
            presentationRowIndex,
            staticData,
            new long[schema.DynamicNumericCount],
            new string?[schema.DynamicTextCount]);
        presentationRowIndex++;
    }

    private void AddSemanticGroupMember(
        ProcessInstanceKey memberInstanceKey,
        int[] memberRowIndexes,
        ProcessInstanceKey[] memberInstanceKeys,
        ref int memberWriteIndex)
    {
        if (!_sourceRowIndexByInstance.TryGetValue(
                memberInstanceKey,
                out int memberRowIndex))
        {
            throw new InvalidOperationException(
                "A semantic process group references a missing source row.");
        }

        memberRowIndexes[memberWriteIndex] = memberRowIndex;
        memberInstanceKeys[memberWriteIndex] = memberInstanceKey;
        memberWriteIndex++;
    }

    private void RebuildSourceRowIndex()
    {
        _sourceRowIndexByInstance.Clear();
        for (int rowIndex = 0; rowIndex < _sourceSnapshot.Count; rowIndex++)
        {
            ProcessStaticData? row = _sourceSnapshot.StaticRows[rowIndex];
            if (row != null) _sourceRowIndexByInstance[row.InstanceKey] = rowIndex;
        }
    }

    private static ProcessGroupingFacts CreateFallbackGroupingFacts(ProcessStaticData row)
    {
        string executablePath = row.Image.ImagePath;
        string executableName = executablePath.Length > 0
            ? Path.GetFileName(executablePath)
            : row.Image.Name;
        return new ProcessGroupingFacts(
            row.InstanceKey,
            row.IsCreationTimeKnown,
            row.ParentProcessID,
            executableName,
            executablePath.Length > 0 ? executablePath : null,
            row.UserSID,
            row.SessionID,
            row.PackageFullName,
            row.ProcessApplicationUserModelID,
            IsApplicationUserModelIDAmbiguous: false,
            ProcessIndependentWindowState.Unknown,
            row.IsCriticalOrProtected);
    }

    private ProcessInstanceKey GetOrCreateSyntheticInstanceKey(SemanticProcessGroupKey groupKey)
    {
        if (_syntheticKeyByGroup.TryGetValue(groupKey, out ProcessInstanceKey instanceKey))
            return instanceKey;

        if (_nextSyntheticProcessID == int.MinValue)
            throw new InvalidOperationException("Semantic process group identity space is exhausted.");
        instanceKey = new ProcessInstanceKey(_nextSyntheticProcessID, CreationTimeTicks: 0);
        _nextSyntheticProcessID--;
        _syntheticKeyByGroup.Add(groupKey, instanceKey);
        return instanceKey;
    }

    private ProcessStaticData CreateSyntheticStaticData(
        SemanticProcessGroup group,
        ProcessInstanceKey instanceKey,
        int representativeRowIndex,
        ProcessDataSchema schema)
    {
        ProcessStaticData representative = _sourceSnapshot.StaticRows[representativeRowIndex]
                                           ?? throw new InvalidOperationException(
                                               "A semantic group representative row is missing.");
        string representativeDescription = representative.Image.Description;
        string displayName = representativeDescription.Length > 0
                             && !string.Equals(
                                 representativeDescription,
                                 UnavailableText,
                                 StringComparison.OrdinalIgnoreCase)
            ? representativeDescription
            : representative.Image.Name;
        string aggregateName = $"{displayName} ({group.Nodes.Length.ToString(TableCulture)})";
        ProcessImageIdentity aggregateImage = new(
            key: $"semantic-group:{instanceKey.ProcessID.ToString(TableCulture)}",
            aggregateName,
            representative.Image.ImagePath,
            displayName,
            representative.Image.IconSource);

        long[] numericValues = new long[schema.StaticNumericCount];
        int sessionSlot = schema.GetStaticNumericSlot(ProcessTableColumnKind.SessionID);
        if (sessionSlot >= 0) numericValues[sessionSlot] = representative.NumericValues[sessionSlot];

        string?[] textValues = new string?[schema.StaticTextCount];
        int packageSlot = schema.GetStaticTextSlot(ProcessTableColumnKind.PackageName);
        if (packageSlot >= 0) textValues[packageSlot] = representative.TextValues[packageSlot];

        return new ProcessStaticData
        {
            InstanceKey = instanceKey,
            IsCreationTimeKnown = true,
            ParentProcessID = -1,
            Image = aggregateImage,
            UserName = representative.UserName,
            UserSID = representative.UserSID,
            SessionID = representative.SessionID,
            PackageFullName = representative.PackageFullName,
            ProcessApplicationUserModelID = representative.ProcessApplicationUserModelID,
            NumericValues = numericValues,
            TextValues = textValues
        };
    }

    private long[] CreateSyntheticDynamicNumericValues(
        int[] memberRowIndexes,
        int representativeRowIndex,
        ProcessDataSchema schema)
    {
        long[] values = new long[schema.DynamicNumericCount];
        for (int definitionIndex = 0;
             definitionIndex < ProcessTableColumnCatalog.Definitions.Length;
             definitionIndex++)
        {
            ProcessTableColumnKind column = (ProcessTableColumnKind)definitionIndex;
            int slot = schema.GetDynamicNumericSlot(column);
            if (slot < 0) continue;
            values[slot] = SemanticProcessAggregation.AggregateDynamicNumeric(
                _sourceSnapshot,
                memberRowIndexes,
                column,
                representativeRowIndex);
        }

        return values;
    }

    private string?[] CreateSyntheticDynamicTextValues(
        int representativeRowIndex,
        ProcessDataSchema schema)
    {
        string?[] values = new string?[schema.DynamicTextCount];
        if (schema.DynamicTextCount == 0) return values;

        Array.Copy(
            _sourceSnapshot.DynamicTextValues,
            checked(representativeRowIndex * schema.DynamicTextCount),
            values,
            destinationIndex: 0,
            schema.DynamicTextCount);
        return values;
    }

    private void PruneSyntheticGroupKeys()
    {
        _staleSemanticGroupKeys.Clear();
        foreach (KeyValuePair<SemanticProcessGroupKey, ProcessInstanceKey> pair in _syntheticKeyByGroup)
        {
            if (!_liveSemanticGroupKeys.Contains(pair.Key))
                _staleSemanticGroupKeys.Add(pair.Key);
        }

        for (int staleIndex = 0; staleIndex < _staleSemanticGroupKeys.Count; staleIndex++)
        {
            SemanticProcessGroupKey groupKey = _staleSemanticGroupKeys[staleIndex];
            if (!_syntheticKeyByGroup.Remove(groupKey, out ProcessInstanceKey instanceKey)) continue;
            _collapsedProcesses.Remove(instanceKey);
        }
    }

    private void BuildLogicalParentIndexes()
    {
        Array.Fill(_treeParentIndexes, value: -1, startIndex: 0, _rowCount);
        Array.Clear(_semanticSectionRowKinds, index: 0, _rowCount);
        Array.Clear(_semanticRowClassifications, index: 0, _rowCount);
        _rowIndexByInstance.Clear();
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row != null) _rowIndexByInstance[row.InstanceKey] = rowIndex;
        }

        switch (_processGroupingStyle)
        {
            case ProcessGroupingStyle.None:
                return;
            case ProcessGroupingStyle.ParentProcess:
                BuildParentProcessIndexes();
                return;
            case ProcessGroupingStyle.Semantic:
                foreach (KeyValuePair<ProcessInstanceKey, ProcessInstanceKey?> pair in
                         _semanticParentByInstance)
                {
                    if (!pair.Value.HasValue
                        || !_rowIndexByInstance.TryGetValue(pair.Key, out int childRowIndex)
                        || !_rowIndexByInstance.TryGetValue(pair.Value.Value, out int parentRowIndex))
                        continue;
                    _treeParentIndexes[childRowIndex] = parentRowIndex;
                }

                BuildSemanticPresentationIndexes();

                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(_processGroupingStyle));
        }
    }

    private void BuildSemanticPresentationIndexes()
    {
        foreach (KeyValuePair<ProcessInstanceKey, SemanticProcessGroupClassification> pair in
                 _semanticClassificationByInstance)
        {
            if (!_rowIndexByInstance.TryGetValue(pair.Key, out int rowIndex)) continue;
            _semanticRowClassifications[rowIndex] = checked((byte)((int)pair.Value + 1));
        }

        for (int sectionIndex = 0; sectionIndex < SemanticProcessSections.Count; sectionIndex++)
        {
            SemanticProcessGroupClassification classification =
                SemanticProcessSections.GetClassification(sectionIndex);
            int classificationIndex = (int)classification;
            int spacerRowIndex = _semanticSectionSpacerRowIndexes[classificationIndex];
            int headerRowIndex = _semanticSectionHeaderRowIndexes[classificationIndex];
            if (spacerRowIndex >= 0)
            {
                _semanticSectionRowKinds[spacerRowIndex] =
                    SemanticProcessSectionRowKind.Spacer;
            }

            if (headerRowIndex >= 0)
            {
                _semanticSectionRowKinds[headerRowIndex] =
                    SemanticProcessSectionRowKind.Header;
            }
        }
    }

    private void BuildParentProcessIndexes()
    {
        _rowIndexByProcessID.Clear();
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row != null && row.ProcessID >= 0)
                _rowIndexByProcessID[row.ProcessID] = rowIndex;
        }

        for (int childRowIndex = 0; childRowIndex < _rowCount; childRowIndex++)
        {
            ProcessStaticData? child = _snapshot.StaticRows[childRowIndex];
            if (child == null
                || !child.IsCreationTimeKnown
                || child.ParentProcessID < 0
                || child.ParentProcessID == child.ProcessID
                || !_rowIndexByProcessID.TryGetValue(
                    child.ParentProcessID,
                    out int parentRowIndex))
                continue;

            ProcessStaticData? parent = _snapshot.StaticRows[parentRowIndex];
            if (parent == null
                || !parent.IsCreationTimeKnown
                || parent.InstanceKey.CreationTimeTicks >= child.InstanceKey.CreationTimeTicks)
                continue;
            _treeParentIndexes[childRowIndex] = parentRowIndex;
        }
    }

    /// <summary>Publishes this table's schema and viewport sampling policy.</summary>
    public void ActivateSampling(ProcessSnapshotService snapshotService)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        ArgumentNullException.ThrowIfNull(snapshotService);
        if (_snapshotService != null && !ReferenceEquals(_snapshotService, snapshotService))
            throw new InvalidOperationException("The process table cannot change snapshot services.");

        _snapshotService = snapshotService;
        _samplingActive = true;
        snapshotService.SetSemanticGroupingEnabled(
            _processGroupingStyle == ProcessGroupingStyle.Semantic);
        ProcessDataSchema schema = _pendingColumnLayout?.Schema ?? _schema;
        snapshotService.SetActiveSchema(schema);
        PublishWarmProcesses();
        snapshotService.RequestRefresh();
    }

    /// <summary>Stops this table from changing the shared process sampling policy.</summary>
    public void DeactivateSampling()
    {
        _samplingActive = false;
        _snapshotService?.SetSemanticGroupingEnabled(isEnabled: false);
    }

    public void SetFilter(string? filterText)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        string nextFilter = filterText?.Trim() ?? string.Empty;
        if (string.Equals(_filterText, nextFilter, StringComparison.Ordinal)) return;

        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        _filterText = nextFilter;
        if (_pendingColumnLayout is { } pendingColumnLayout)
        {
            ProcessSearchQuery pendingFilterQuery = ProcessSearchQuery.Parse(
                nextFilter,
                pendingColumnLayout.Settings);
            ProcessDataSchema pendingSchema = ProcessDataSchema.Create(
                pendingColumnLayout.Settings,
                pendingFilterQuery.RequiredColumnMask);
            PendingColumnLayout nextPendingColumnLayout = pendingColumnLayout with
            {
                FilterQuery = pendingFilterQuery, Schema = pendingSchema
            };
            _pendingColumnLayout = nextPendingColumnLayout;
            ApplySamplingSchemaIfActive(pendingSchema);
            if (pendingSchema.VisibleMask == _schema.VisibleMask)
            {
                CommitPendingColumnLayout(
                    nextPendingColumnLayout,
                    _sourceSnapshot.Count,
                    _snapshotVersion,
                    viewportAnchor);
            }

            return;
        }

        _filterQuery = ProcessSearchQuery.Parse(nextFilter, _columnSettings);
        ProcessDataSchema nextSchema = ProcessDataSchema.Create(
            _columnSettings,
            _filterQuery.RequiredColumnMask);
        if (ApplySearchSchema(nextSchema, viewportAnchor)) return;

        RebuildVisibleRows();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateLayers(RenderLayerMask.All);
    }

    private bool ApplySearchSchema(
        ProcessDataSchema schema,
        ProcessViewportAnchor? viewportAnchor)
    {
        if (_schema.VisibleMask == schema.VisibleMask) return false;

        _pendingViewportAnchor = viewportAnchor;
        ClearSnapshotPresentationState();
        _schema = schema;
        _rowComparer.SetSchema(schema);
        _sourceSnapshot.Reset();
        _snapshot.Reset();
        _snapshotVersion = -1;
        ClearLiveTotalHeaders();

        ApplySamplingSchemaIfActive(schema);
        PublishWarmProcesses();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateMeasure();
        PublishRowHoverGeometry();
        InvalidateLayers(RenderLayerMask.All);
        return true;
    }

    private void ClearSnapshotPresentationState()
    {
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _renderCaches.Clear();
        foreach (SharedCellLayout sharedCell in _sharedCellLayouts.Values)
            sharedCell.Dispose();
        _sharedCellLayouts.Clear();

        _rowCount = 0;
        _visibleRowCount = 0;
        _cacheGeneration = 0;
        _retainedFirstVisibleIndex = -1;
        _retainedLastVisibleIndexExclusive = -1;
        _warmRefreshCursor = 0;
        _warmRefreshEnd = 0;
        _rowIndexByProcessID.Clear();
        _sourceRowIndexByInstance.Clear();
        _rowIndexByInstance.Clear();
        _membersBySyntheticKey.Clear();
        _semanticParentByInstance.Clear();
        _semanticClassificationByInstance.Clear();
        ResetSemanticSectionPresentation();
        Array.Fill(_semanticSectionVisibleStarts, value: -1);
        _usesSemanticSections = false;
        _contextCopyRows = [];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        double contentWidth = columns.Length == 0 ? 0 : columns[^1].Right;
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(contentWidth, availableSize.Width)
            : contentWidth;
        return new Size(
            width,
            DetailsGridLayout.GetContentHeight(
                _visibleRowCount,
                _metrics.HeaderHeight,
                _metrics.RowHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arrangedSize = base.ArrangeOverride(finalSize);
        EnsureRetainedDrawingsForViewport();
        PublishRowHoverGeometry();
        return arrangedSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (IsDetailsGridDisposed || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Keep every row and column position in Avalonia's render-data hit-test surface
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    private void RenderLayer(DrawingContext context, RenderLayerKind layerKind)
    {
        if (IsDetailsGridDisposed || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        switch (layerKind)
        {
            case RenderLayerKind.Selection:
                DrawSelections(context);
                return;
            case RenderLayerKind.StaticRows:
                DrawRetainedRows(context, ProcessTableColumnLifetime.Static);
                return;
            case RenderLayerKind.DynamicRows:
                DrawRetainedRows(context, ProcessTableColumnLifetime.Dynamic);
                return;
            case RenderLayerKind.Icons:
                DrawProcessIcons(context);
                return;
            case RenderLayerKind.CopyPreview:
                DrawCopyPreviewUnderline(context);
                return;
            case RenderLayerKind.Chrome:
            {
                Rect viewport = ResolveViewport();
                double headerTop = ResolveStickyHeaderTop(viewport);
                DrawColumnGrid(context, viewport);
                DrawHeaderBackground(context, headerTop);
                return;
            }
            case RenderLayerKind.Header:
                DrawHeaderContent(context, ResolveStickyHeaderTop(ResolveViewport()));
                return;
            case RenderLayerKind.HeaderInteraction:
            {
                Rect viewport = ResolveViewport();
                DrawHeaderInteraction(context, viewport, ResolveStickyHeaderTop(viewport));
                return;
            }
        }
    }

    private void InvalidateLayers(RenderLayerMask layers)
    {
        if ((layers & RenderLayerMask.Selection) != 0) _selectionLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.StaticRows) != 0) _staticRowsLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.DynamicRows) != 0) _dynamicRowsLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.Icons) != 0) _iconsLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.CopyPreview) != 0) _copyPreviewLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.Chrome) != 0) _chromeLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.Header) != 0) _headerLayer.InvalidateVisual();
        if ((layers & RenderLayerMask.HeaderInteraction) != 0)
            _headerInteractionLayer.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (eventArgs.Handled) return;
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
            int contextVisibleIndex = DetailsGridLayout.HitTestRow(
                position.Y,
                _visibleRowCount,
                _metrics.HeaderHeight,
                _metrics.RowHeight);
            if (contextVisibleIndex >= 0)
            {
                int contextRowIndex = _visibleRowIndexes[contextVisibleIndex];
                if (IsSemanticSectionRow(contextRowIndex))
                {
                    Focus();
                    eventArgs.Handled = true;
                    return;
                }

                ProcessStaticData? contextRow = _snapshot.StaticRows[contextRowIndex];
                if (contextRow != null)
                {
                    if (_selectedProcesses.Contains(contextRow.InstanceKey))
                        SetActiveSelectedProcess(contextRow.InstanceKey);
                    else
                        ApplyPointerSelection(contextVisibleIndex, KeyModifiers.None);
                }
            }
            else
                ApplyPointerSelection(contextVisibleIndex, KeyModifiers.None);
            Focus();
            if (contextVisibleIndex >= 0 && _selectedProcesses.Count > 0)
            {
                int contextColumnIndex = ProcessTableLayout.HitTestColumn(position.X, DisplayColumns);
                ProcessRowContextMenuRequest request = CreateRowContextMenuRequest(
                    this.PointToScreen(position),
                    contextColumnIndex);
                RowContextMenuRequested?.Invoke(request);
            }

            eventArgs.Handled = contextVisibleIndex >= 0;
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

        int visibleIndex = DetailsGridLayout.HitTestRow(
            position.Y,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight);
        if (pointerPoint.Properties.IsLeftButtonPressed
            && TryToggleTreeExpander(position, visibleIndex))
        {
            eventArgs.Handled = true;
            return;
        }

        ApplyPointerSelection(visibleIndex, eventArgs.KeyModifiers);
        Focus();
        eventArgs.Handled = visibleIndex >= 0;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        Point position = GetLatestPointerPosition(eventArgs);
        if (_headerInteraction != HeaderInteractionMode.None
            && ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer))
        {
            MoveHeaderInteraction(position);
            eventArgs.Handled = true;
            return;
        }

        UpdateHeaderCursor(position);
        UpdateHoveredHeader(position);
    }

    private Point GetLatestPointerPosition(PointerEventArgs eventArgs)
    {
        // Read the live OS cursor so queued PointerMoved events cannot replay obsolete rows
        if (eventArgs.Pointer.Type == PointerType.Mouse
            && OperatingSystem.IsWindows()
            && User32.GetCursorPos(out User32.POINT cursorPosition))
            return this.PointToClient(new PixelPoint(cursorPosition.X, cursorPosition.Y));

        return eventArgs.GetPosition(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_headerInteraction == HeaderInteractionMode.None
            || !ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer))
            return;

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

        UpdateHeaderCursor(position);
        UpdateHoveredHeader(position);
        eventArgs.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        if (!ReferenceEquals(_capturedHeaderPointer, eventArgs.Pointer)) return;

        _capturedHeaderPointer = null;
        ClearHeaderInteractionState();
        Cursor = TrayAppDotNETCursors.Arrow;
    }

    /// <summary>Switches between flat, parent-process, and semantic application layouts.</summary>
    public void SetProcessGroupingStyle(ProcessGroupingStyle groupingStyle)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        if (!Enum.IsDefined(groupingStyle)) groupingStyle = ProcessGroupingStyle.None;
        if (_processGroupingStyle == groupingStyle) return;

        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        _processGroupingStyle = groupingStyle;
        if (_samplingActive && _snapshotService != null)
        {
            _snapshotService.SetSemanticGroupingEnabled(
                groupingStyle == ProcessGroupingStyle.Semantic);
            _snapshotService.RequestRefresh();
        }

        if (_sourceSnapshot.Schema != null)
        {
            BuildPresentationSnapshot(_sourceSnapshot.Count);
            EnsureRowCapacity(_rowCount);
            BuildLogicalParentIndexes();
            SynchronizeRenderCacheMembership();
            EnsureSelectedProcessesStillExist();
            RefreshLiveTotalHeaders();
        }
        RebuildVisibleRows();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateLayers(RenderLayerMask.All);
    }

    /// <summary>Shows the copy target preview requested by the active row context menu.</summary>
    public void SetContextCopyPreview(ProcessCopyPreviewMode previewMode)
    {
        if (IsDetailsGridDisposed || _copyPreviewMode == previewMode) return;

        _copyPreviewMode = previewMode;
        RebuildCopyPreview();
        InvalidateLayers(RenderLayerMask.CopyPreview);
    }

    protected override void ApplyDetailsGridMetrics(double fontSize, double rowHeight)
    {
        int nextTableFontWeight = CalculateTableFontWeight(fontSize);
        bool tableTypefaceChanged = nextTableFontWeight != _tableFontWeight;
        AdvanceGridMetricsGeneration();
        _metrics = CreateTableMetrics(
            _resources,
            fontSize,
            rowHeight,
            _rowTextHeightScale);
        if (!tableTypefaceChanged) return;

        _tableFontWeight = nextTableFontWeight;
        _tableTypeface = CreateTableTypeface(nextTableFontWeight);
        ReplaceHeaderTexts(_columns);
    }

    protected override void OnDetailsGridMetricsChanged()
    {
        UpdateSelectionOverlay();
        PublishRowHoverGeometry();
        RebuildCopyPreview();
        InvalidateMeasure();
        InvalidateLayers(RenderLayerMask.All);
    }

    private void AdvanceGridMetricsGeneration()
    {
        int nextGeneration = unchecked(_gridMetricsGeneration + 1);
        if (nextGeneration != 0)
        {
            _gridMetricsGeneration = nextGeneration;
            return;
        }

        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellLayouts.Clear();
        _gridMetricsGeneration = 1;
    }

    /// <summary>Applies one column's display properties without changing the visible schema.</summary>
    public void ApplyColumnProperties(ProcessColumnSetting replacement)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        ApplyColumnLayout(ProcessColumnSettings.WithProperties(_columnSettings, replacement));
    }

    /// <summary>Stages column visibility and order, then swaps after compatible data is ready.</summary>
    public void ApplyColumnSettings(IReadOnlyList<ProcessColumnSetting> settings)
    {
        ObjectDisposedException.ThrowIf(IsDetailsGridDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        ApplyColumnLayout(ProcessColumnSettings.CloneList(settings));
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
        SetHoveredHeaderColumnIndex(-1);
        if (_headerInteraction == HeaderInteractionMode.None)
            Cursor = TrayAppDotNETCursors.Arrow;
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        switch (eventArgs.Key)
        {
            case Key.Delete when SelectedEndTaskRequest is { } request:
                EndTaskRequested?.Invoke(request);
                eventArgs.Handled = true;
                return;
            case Key.Escape when _headerInteraction != HeaderInteractionMode.None:
                ResetHeaderInteraction();
                eventArgs.Handled = true;
                return;
        }
    }

    protected override void OnDetailsGridViewportChanged()
    {
        PublishRowHoverGeometry();
        PublishWarmProcesses();
        EnsureRetainedDrawingsForViewport();
        UpdateHeaderHoverVisual();
        InvalidateLayers(RenderLayerMask.All);
    }

    private bool IsHeaderPosition(double positionY)
    {
        double stickyHeaderTop = ResolveStickyHeaderTop(ResolveViewport());
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
        PublishRowHoverGeometry();
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
                    return;

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
                    UpdateHeaderHoverVisual();
                    InvalidateLayers(
                        RenderLayerMask.Rows
                        | RenderLayerMask.Icons
                        | RenderLayerMask.Chrome
                        | RenderLayerMask.Header
                        | RenderLayerMask.HeaderInteraction);
                    return;
                }

                InvalidateLayers(RenderLayerMask.HeaderInteraction);
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
                if (changed) InvalidateLayers(RenderLayerMask.HeaderInteraction);
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
        UpdateHeaderHoverVisual();
    }

    private void UpdateHeaderHoverVisual()
    {
        ProcessTableColumn[] columns = DisplayColumns;
        if ((uint)_hoveredHeaderColumnIndex >= (uint)columns.Length)
        {
            _headerHoverLayer.SetHighlightBounds(null);
            return;
        }

        ProcessTableColumn column = columns[_hoveredHeaderColumnIndex];
        double headerTop = ResolveStickyHeaderTop(ResolveViewport());
        _headerHoverLayer.SetHighlightBounds(
            new Rect(column.Left, headerTop, column.Width, _metrics.HeaderHeight));
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
    }

    private void ClearHeaderInteractionState()
    {
        bool wasLiveColumnResizeActive = _isLiveColumnResizeActive;
        if (wasLiveColumnResizeActive)
        {
            _isLiveColumnResizeActive = false;
            InvalidateMeasure();
        }

        _headerInteraction = HeaderInteractionMode.None;
        _interactionColumnIndex = -1;
        _reorderInsertionIndex = -1;
        _resizeInitialWidth = 0;
        _resizePreviewWidth = 0;
        PublishRowHoverGeometry();
        UpdateHeaderHoverVisual();
        InvalidateLayers(wasLiveColumnResizeActive
            ? RenderLayerMask.Rows
              | RenderLayerMask.Icons
              | RenderLayerMask.Chrome
              | RenderLayerMask.Header
              | RenderLayerMask.HeaderInteraction
            : RenderLayerMask.HeaderInteraction);
    }

    private void OnIconsChanged()
    {
        if (!IsDetailsGridDisposed) InvalidateLayers(RenderLayerMask.Icons);
    }

#if DEBUG
    private void OnGlyphResourcesReloaded()
    {
        if (IsDetailsGridDisposed) return;

        RecreateSortCaretTexts();
        ReplaceHeaderTexts(_columns);
        InvalidateLayers(RenderLayerMask.Header);
    }

    /// <summary>Applies the current ProcessTable AXAML values without replacing runtime table state.</summary>
    internal List<ProcessColumnSetting>? ApplyAXAMLResources()
    {
        if (IsDetailsGridDisposed) return null;

        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        double nextAXAMLFontSize = _resources.AxamlProcessTable.FontSize;
        double nextAXAMLRowSpacing = _resources.AxamlProcessTable.RowSpacing;
        double nextFontSize = Math.Abs(nextAXAMLFontSize - _axamlFontSize) >= 0.01
            ? nextAXAMLFontSize
            : _metrics.FontSize;
        double currentRowSpacing = _metrics.RowHeight - _metrics.RowTextHeight;
        double nextRowSpacing = Math.Abs(nextAXAMLRowSpacing - _axamlRowSpacing) >= 0.01
            ? nextAXAMLRowSpacing
            : currentRowSpacing;
        double nextRowHeight = CalculateRowHeight(nextFontSize, nextRowSpacing);
        ProcessTableMetrics nextMetrics = CreateTableMetrics(
            _resources,
            nextFontSize,
            nextRowHeight,
            _rowTextHeightScale);
        ProcessTableVisualMetrics nextVisualMetrics = CreateVisualMetrics(_resources);
        ProcessTableAXAMLColumnWidths nextColumnWidths = CreateAXAMLColumnWidths(_resources);
        LiveTotalTypography nextLiveTotalTypography = CreateLiveTotalTypography(_resources);
        Thickness nextSelectionBorderThickness =
            _resources.AxamlProcessTable.SelectionBorderThickness;
        Color nextBackgroundColor = _resources.AxamlProcessTable.GridBackgroundColor;
        bool backgroundColorChanged = nextBackgroundColor != _backgroundColor;
        bool selectionBorderChanged = nextSelectionBorderThickness != _selectionBorderThickness;
        bool liveTotalTypographyChanged = nextLiveTotalTypography != _liveTotalTypography;
        _axamlFontSize = nextAXAMLFontSize;
        _axamlRowSpacing = nextAXAMLRowSpacing;
        if (nextMetrics == _metrics
            && nextVisualMetrics == _visualMetrics
            && nextColumnWidths == _axamlColumnWidths
            && !backgroundColorChanged
            && !selectionBorderChanged
            && !liveTotalTypographyChanged)
            return null;

        bool rebuildRetainedRows = RetainedRowGeometryChanged(
            _metrics,
            nextMetrics,
            _visualMetrics,
            nextVisualMetrics);
        bool rebuildCaretText = _visualMetrics.SortCaretFontSize
                                != nextVisualMetrics.SortCaretFontSize;
        int nextTableFontWeight = CalculateTableFontWeight(nextFontSize);
        bool rebuildTableTypeface = _tableFontWeight != nextTableFontWeight;
        bool rebuildHeaderText = _metrics.HeaderFontSize != nextMetrics.HeaderFontSize
                                 || _metrics.CellPadding != nextMetrics.CellPadding
                                 || _visualMetrics.SortCaretRightMargin
                                 != nextVisualMetrics.SortCaretRightMargin
                                 || rebuildCaretText
                                 || rebuildTableTypeface
                                 || liveTotalTypographyChanged;
        bool gridMetricsChanged = _metrics.FontSize != nextMetrics.FontSize
                                  || _metrics.RowHeight != nextMetrics.RowHeight;

        _metrics = nextMetrics;
        _visualMetrics = nextVisualMetrics;
        _backgroundColor = nextBackgroundColor;
        _backgroundBrush = TrayAppDotNETSettingsUI.Brush(nextBackgroundColor);
        _selectionBorderThickness = nextSelectionBorderThickness;
        _sortCaretRightMargin = nextVisualMetrics.SortCaretRightMargin;
        if (liveTotalTypographyChanged)
        {
            _liveTotalTypography = nextLiveTotalTypography;
            _liveTotalTypeface = CreateLiveTotalTypeface(nextLiveTotalTypography);
        }
        if (rebuildTableTypeface)
        {
            _tableFontWeight = nextTableFontWeight;
            _tableTypeface = CreateTableTypeface(nextTableFontWeight);
        }

        RecreatePens();
        if (rebuildCaretText) RecreateSortCaretTexts();
        List<ProcessColumnSetting>? hotReloadedColumnSettings = ApplyHotReloadedColumnWidths(
            _axamlColumnWidths,
            nextColumnWidths);
        bool rebuiltForColumnWidths = hotReloadedColumnSettings != null;
        _axamlColumnWidths = nextColumnWidths;
        if (rebuildHeaderText && !rebuiltForColumnWidths)
            ReplaceHeaderTexts(_columns);

        if (rebuildRetainedRows && !rebuiltForColumnWidths)
            RebuildRetainedRowDrawings();
        if (gridMetricsChanged)
            NotifyDetailsGridMetricsChanged(_metrics.FontSize, _metrics.RowHeight);
        UpdateSelectionOverlay();
        PublishRowHoverGeometry();
        RebuildCopyPreview();
        PublishWarmProcesses();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);
        UpdateHeaderHoverVisual();
        InvalidateLayers(RenderLayerMask.All);
        return hotReloadedColumnSettings;
    }
#endif

#if DEBUG
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
#endif

    private void RebuildRetainedRowDrawings()
    {
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellLayouts.Clear();
        UpdateRetainedDrawings();
    }

#if DEBUG
    private List<ProcessColumnSetting>? ApplyHotReloadedColumnWidths(
        ProcessTableAXAMLColumnWidths currentWidths,
        ProcessTableAXAMLColumnWidths nextWidths)
    {
        if (!ProcessTableAXAMLHotReload.TryApplyColumnWidths(
                _columnSettings,
                currentWidths,
                nextWidths,
                out List<ProcessColumnSetting> nextSettings))
            return null;

        ProcessTableColumn[] columns = CreateColumns(nextSettings);
        if (columns.Length != _columns.Length) return null;
        _columnSettings = nextSettings;
        _settingsByColumn = CreateColumnSettingsIndex(nextSettings);
        ApplyDisplayColumnLayout(columns, viewportAnchor: null);

        List<ProcessColumnSetting> authoritativeSettings = nextSettings;
        if (_pendingColumnLayout is { } pendingColumnLayout
            && ProcessTableAXAMLHotReload.TryApplyColumnWidths(
                pendingColumnLayout.Settings,
                currentWidths,
                nextWidths,
                out List<ProcessColumnSetting> nextPendingSettings))
        {
            ProcessTableColumn[] nextPendingColumns = CreateColumns(nextPendingSettings);
            ProcessSearchQuery nextPendingFilterQuery = ProcessSearchQuery.Parse(
                _filterText,
                nextPendingSettings);
            _pendingColumnLayout = pendingColumnLayout with
            {
                Settings = nextPendingSettings, Columns = nextPendingColumns, FilterQuery = nextPendingFilterQuery
            };
            authoritativeSettings = nextPendingSettings;
        }

        return ProcessColumnSettings.CloneList(authoritativeSettings);
    }
#endif

    private string LocalizeUnavailableText(string value) =>
        string.Equals(value, NativeProcessInfo.Unavailable, StringComparison.Ordinal)
            ? UnavailableText
            : value;

#if DEBUG
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
        || currentVisualMetrics.TreeIndentWidth != nextVisualMetrics.TreeIndentWidth
        || currentVisualMetrics.SemanticSectionChildIndent
        != nextVisualMetrics.SemanticSectionChildIndent
        || currentVisualMetrics.SemanticSectionHeaderSizeOffset
        != nextVisualMetrics.SemanticSectionHeaderSizeOffset
        || currentVisualMetrics.SemanticSectionHeaderUpwardShift
        != nextVisualMetrics.SemanticSectionHeaderUpwardShift
        || currentVisualMetrics.SemanticSectionHeaderTextGap
        != nextVisualMetrics.SemanticSectionHeaderTextGap
        || currentVisualMetrics.TreeExpanderWidth != nextVisualMetrics.TreeExpanderWidth;
#endif

    private Rect ResolveViewport() => ResolveDetailsGridViewport();

    private double ResolveStickyHeaderTop(Rect viewport) =>
        Math.Clamp(viewport.Y, min: 0, Math.Max(val1: 0, Bounds.Height - _metrics.HeaderHeight));

    private void DrawRetainedRows(DrawingContext context, ProcessTableColumnLifetime lifetime)
    {
        Rect viewport = ResolveViewport();
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int firstRow,
            out int lastRowExclusive);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
            DrawRetainedRow(context, visibleIndex, lifetime);
    }

    private void DrawRetainedRow(
        DrawingContext context,
        int visibleIndex,
        ProcessTableColumnLifetime lifetime)
    {
        int rowIndex = _visibleRowIndexes[visibleIndex];
        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
            return;

        double top = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
        using (context.PushTransform(Matrix.CreateTranslation(xPosition: 0, top)))
        {
            if (_isLiveColumnResizeActive && _liveResizeColumns != null)
                DrawLiveResizedRow(context, cache, rowIndex, _liveResizeColumns, lifetime);
            else
                DrawRetainedRowDrawing(context, cache, lifetime);
        }
    }

    private void DrawProcessIcons(DrawingContext context)
    {
        Rect viewport = ResolveViewport();
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int firstRow,
            out int lastRowExclusive);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null) continue;

            double top = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
            DrawProcessIcon(context, viewport, rowIndex, row, top);
        }
    }

    private void DrawSelections(DrawingContext context)
    {
        if (_selectedProcesses.Count == 0) return;

        Rect viewport = ResolveViewport();
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int firstRow,
            out int lastRowExclusive);
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[_visibleRowIndexes[visibleIndex]];
            if (row == null || !_selectedProcesses.Contains(row.InstanceKey)) continue;

            double top = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
            Rect bounds = new(x: 0, top, Bounds.Width, _metrics.RowHeight);
            context.FillRectangle(_selectionBackgroundBrush, bounds);
            DrawSelectionBorder(context, bounds);
        }
    }

    private void DrawSelectionBorder(DrawingContext context, Rect bounds)
    {
        if (_selectionBorderThickness.Left > 0)
        {
            context.FillRectangle(
                _accentBrush,
                new Rect(bounds.Left, bounds.Top, _selectionBorderThickness.Left, bounds.Height));
        }

        if (_selectionBorderThickness.Top > 0)
        {
            context.FillRectangle(
                _accentBrush,
                new Rect(bounds.Left, bounds.Top, bounds.Width, _selectionBorderThickness.Top));
        }

        if (_selectionBorderThickness.Right > 0)
        {
            context.FillRectangle(
                _accentBrush,
                new Rect(
                    bounds.Right - _selectionBorderThickness.Right,
                    bounds.Top,
                    _selectionBorderThickness.Right,
                    bounds.Height));
        }

        if (_selectionBorderThickness.Bottom > 0)
        {
            context.FillRectangle(
                _accentBrush,
                new Rect(
                    bounds.Left,
                    bounds.Bottom - _selectionBorderThickness.Bottom,
                    bounds.Width,
                    _selectionBorderThickness.Bottom));
        }
    }

    private void DrawCopyPreviewUnderline(DrawingContext context)
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

    private ProcessRowContextMenuRequest CreateRowContextMenuRequest(
        PixelPoint screenPosition,
        int columnIndex)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        _contextCopyColumn = (uint)columnIndex < (uint)columns.Length
            ? columns[columnIndex].Kind
            : null;
        _copyPreviewMode = ProcessCopyPreviewMode.None;
        _textUnderlineSegmentCount = 0;
        int[] selectedRowIndexes = CreateSelectedRowIndexes();
        ProcessEndTaskItem[] selectedProcesses = CreateSelectedEndTaskItems();
        ContextCopyRow[] contextCopyRows = new ContextCopyRow[selectedRowIndexes.Length];
        StringBuilder cellCopyText = new();
        StringBuilder rowCopyText = new();
        for (int selectedIndex = 0; selectedIndex < selectedRowIndexes.Length; selectedIndex++)
        {
            int rowIndex = selectedRowIndexes[selectedIndex];
            ProcessStaticData row = _snapshot.StaticRows[rowIndex]
                                    ?? throw new InvalidOperationException(
                                        "A published process row is missing static data.");
            EnsureDynamicDrawingCurrent(rowIndex, row);

            string?[] valuesByColumn = new string?[ProcessTableColumnCatalog.Definitions.Length];
            for (int visibleColumnIndex = 0; visibleColumnIndex < columns.Length; visibleColumnIndex++)
            {
                ProcessTableColumnKind kind = columns[visibleColumnIndex].Kind;
                valuesByColumn[(int)kind] = GetCellDisplayValue(rowIndex, kind);
            }

            contextCopyRows[selectedIndex] = new ContextCopyRow(row.InstanceKey, valuesByColumn);
            if (selectedIndex > 0)
            {
                cellCopyText.AppendLine();
                rowCopyText.AppendLine();
            }

            if (_contextCopyColumn.HasValue)
                cellCopyText.Append(valuesByColumn[(int)_contextCopyColumn.Value] ?? string.Empty);
            AppendRowCopyText(rowCopyText, valuesByColumn, columns);
        }

        _contextCopyRows = contextCopyRows;
        InvalidateLayers(
            RenderLayerMask.DynamicRows
            | RenderLayerMask.CopyPreview);
        return new ProcessRowContextMenuRequest(
            new ProcessEndTaskRequest(selectedProcesses),
            screenPosition,
            cellCopyText.ToString(),
            rowCopyText.ToString());
    }

    private static ProcessEndTaskItem CreateEndTaskItem(ProcessStaticData row) =>
        new(
            new ProcessTerminationTarget(row.ProcessID, row.InstanceKey.CreationTimeTicks),
            row.Image.Name);

    private ProcessEndTaskItem[] CreateSelectedEndTaskItems()
    {
        int[] selectedRowIndexes = CreateSelectedRowIndexes();
        List<ProcessEndTaskItem> selectedProcesses = [];
        HashSet<ProcessInstanceKey> addedProcesses = [];
        for (int selectedIndex = 0; selectedIndex < selectedRowIndexes.Length; selectedIndex++)
        {
            ProcessStaticData row = _snapshot.StaticRows[selectedRowIndexes[selectedIndex]]
                                    ?? throw new InvalidOperationException(
                                        "A published process row is missing static data.");
            if (_membersBySyntheticKey.TryGetValue(
                    row.InstanceKey,
                    out ProcessInstanceKey[]? memberInstanceKeys))
            {
                for (int memberIndex = 0; memberIndex < memberInstanceKeys.Length; memberIndex++)
                    AddEndTaskItem(memberInstanceKeys[memberIndex], addedProcesses, selectedProcesses);
                continue;
            }

            AddEndTaskItem(row.InstanceKey, addedProcesses, selectedProcesses);
        }

        return [.. selectedProcesses];
    }

    private void AddEndTaskItem(
        ProcessInstanceKey instanceKey,
        HashSet<ProcessInstanceKey> addedProcesses,
        List<ProcessEndTaskItem> selectedProcesses)
    {
        if (!addedProcesses.Add(instanceKey)
            || !_sourceRowIndexByInstance.TryGetValue(instanceKey, out int sourceRowIndex))
            return;
        ProcessStaticData? sourceRow = _sourceSnapshot.StaticRows[sourceRowIndex];
        if (sourceRow != null) selectedProcesses.Add(CreateEndTaskItem(sourceRow));
    }

    private int CountSelectedProcessInstances()
    {
        if (_selectedProcesses.Count == 0) return 0;

        HashSet<ProcessInstanceKey> selectedProcessInstances = [];
        foreach (ProcessInstanceKey selectedRowKey in _selectedProcesses)
        {
            if (_membersBySyntheticKey.TryGetValue(
                    selectedRowKey,
                    out ProcessInstanceKey[]? memberInstanceKeys))
            {
                for (int memberIndex = 0; memberIndex < memberInstanceKeys.Length; memberIndex++)
                    selectedProcessInstances.Add(memberInstanceKeys[memberIndex]);
                continue;
            }

            if (_sourceRowIndexByInstance.ContainsKey(selectedRowKey))
                selectedProcessInstances.Add(selectedRowKey);
        }

        return selectedProcessInstances.Count;
    }

    private int[] CreateSelectedRowIndexes()
    {
        if (_selectedProcesses.Count == 0) return [];

        int[] selectedRowIndexes = new int[_selectedProcesses.Count];
        HashSet<ProcessInstanceKey> addedProcesses = new(_selectedProcesses.Count);
        int selectedCount = 0;
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null || !_selectedProcesses.Contains(row.InstanceKey)) continue;

            selectedRowIndexes[selectedCount] = rowIndex;
            selectedCount++;
            addedProcesses.Add(row.InstanceKey);
        }

        if (selectedCount < selectedRowIndexes.Length)
        {
            for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
            {
                ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
                if (row == null
                    || !_selectedProcesses.Contains(row.InstanceKey)
                    || !addedProcesses.Add(row.InstanceKey))
                    continue;

                selectedRowIndexes[selectedCount] = rowIndex;
                selectedCount++;
            }
        }

        if (selectedCount != selectedRowIndexes.Length)
            Array.Resize(ref selectedRowIndexes, selectedCount);
        return selectedRowIndexes;
    }

#if DEBUG
    private ProcessTerminationTarget[] CreateSelectedTerminationTargets()
    {
        ProcessEndTaskItem[] selectedProcesses = CreateSelectedEndTaskItems();
        ProcessTerminationTarget[] targets = new ProcessTerminationTarget[selectedProcesses.Length];
        for (int processIndex = 0; processIndex < selectedProcesses.Length; processIndex++)
            targets[processIndex] = selectedProcesses[processIndex].Target;
        return targets;
    }
#endif

    private static void AppendRowCopyText(
        StringBuilder copyText,
        string?[] valuesByColumn,
        ProcessTableColumn[] columns)
    {
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columnIndex > 0) copyText.Append(',');
            string display = valuesByColumn[(int)columns[columnIndex].Kind] ?? string.Empty;
            AppendCSVField(copyText, display);
        }
    }

    private void RebuildCopyPreview()
    {
        _textUnderlineSegmentCount = 0;
        if (_copyPreviewMode == ProcessCopyPreviewMode.None || _contextCopyRows.Length == 0) return;

        ProcessTableColumn[] columns = DisplayColumns;
        EnsureTextUnderlineSegmentCapacity(checked(_contextCopyRows.Length * columns.Length));
        for (int copyRowIndex = 0; copyRowIndex < _contextCopyRows.Length; copyRowIndex++)
        {
            ContextCopyRow copyRow = _contextCopyRows[copyRowIndex];
            int visibleIndex = FindVisibleProcess(copyRow.Process);
            if (visibleIndex < 0) continue;

            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null) continue;
            double rowTop = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
            int treeLayoutKey = GetTreeLayoutKey(rowIndex);
            if (_copyPreviewMode == ProcessCopyPreviewMode.Row)
            {
                for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    string display = copyRow.ValuesByColumn[(int)columns[columnIndex].Kind]
                                     ?? string.Empty;
                    AddTextUnderlineSegment(columns[columnIndex], display, treeLayoutKey, rowTop);
                }

                continue;
            }

            if (!_contextCopyColumn.HasValue) continue;
            int previewColumnIndex = FindColumn(columns, _contextCopyColumn.Value);
            if (previewColumnIndex < 0) continue;
            ProcessTableColumn column = columns[previewColumnIndex];
            string cellDisplay = copyRow.ValuesByColumn[(int)column.Kind] ?? string.Empty;
            AddTextUnderlineSegment(column, cellDisplay, treeLayoutKey, rowTop);
        }
    }

    private void EnsureDynamicDrawingCurrent(int rowIndex, ProcessStaticData row)
    {
        if (!_hasDynamicColumns
            || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
            return;

        cache.PendingDynamicFingerprint = CalculateDynamicFingerprint(rowIndex);
        if (cache.DynamicDrawing != null
            && cache.DynamicMetricsGeneration == _gridMetricsGeneration
            && cache.DynamicFingerprint == cache.PendingDynamicFingerprint)
            return;

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

    private ProcessViewportAnchor? CaptureViewportAnchor()
    {
        if (IsDetailsGridDisposed
            || _visibleRowCount <= 0
            || _selectedProcess is not { } selectedProcess)
            return null;

        ProcessRowHoverGeometry geometry = CreateRowHoverGeometry();
        int visibleIndex = FindVisibleProcess(selectedProcess);
        if (!geometry.IsRowVisible(visibleIndex)) return null;

        return new ProcessViewportAnchor(
            selectedProcess,
            _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight,
            DetailsGridLayout.GetContentHeight(
                _visibleRowCount,
                _metrics.HeaderHeight,
                _metrics.RowHeight));
    }

    private void RestoreViewportAnchor(ProcessViewportAnchor? viewportAnchor)
    {
        if (IsDetailsGridDisposed || viewportAnchor is not { } anchor) return;

        int visibleIndex = FindVisibleProcess(anchor.Process);
        if (visibleIndex < 0) return;

        double nextRowTop = _metrics.HeaderHeight + visibleIndex * _metrics.RowHeight;
        double nextContentHeight = DetailsGridLayout.GetContentHeight(
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight);
        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop,
            nextContentHeight);
        if (adjustment is not { } requestedAdjustment) return;

        if (requestedAdjustment.ContentHeightChanged) InvalidateMeasure();
        ViewportAnchorAdjustmentRequested?.Invoke(requestedAdjustment);
    }

    private void AddTextUnderlineSegment(
        ProcessTableColumn column,
        string display,
        int treeLayoutKey,
        double rowTop)
    {
        if (display.Length == 0 || _textUnderlineSegmentCount >= _textUnderlineSegments.Length) return;

        using CellTextLayout layout = CreateCellTextLayout(column, display, treeLayoutKey);
        double width = Math.Min(layout.Text.Width, layout.AvailableWidth);
        if (width <= 0) return;

        double underlineY = Math.Min(
            _metrics.RowHeight - _visualMetrics.TextUnderlineThickness,
            layout.Top + layout.Text.Baseline + _visualMetrics.TextUnderlineThickness);
        _textUnderlineSegments[_textUnderlineSegmentCount] = new TextUnderlineSegment(
            layout.Left,
            layout.Left + width,
            rowTop + underlineY);
        _textUnderlineSegmentCount++;
    }

    private void EnsureTextUnderlineSegmentCapacity(int count)
    {
        if (_textUnderlineSegments.Length >= count) return;

        int capacity = Math.Max(val1: 1, Math.Max(_columns.Length, _textUnderlineSegments.Length));
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _textUnderlineSegments, capacity);
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

    /// <summary>Relayouts the resized cell and translates trailing retained cells without rebuilding the row DAG.</summary>
    private void DrawLiveResizedRow(
        DrawingContext context,
        ProcessRowRenderCache cache,
        int rowIndex,
        ProcessTableColumn[] liveColumns,
        ProcessTableColumnLifetime lifetime)
    {
        int resizedColumnIndex = _interactionColumnIndex;
        if ((uint)resizedColumnIndex >= (uint)_columns.Length)
        {
            DrawRetainedRowDrawing(context, cache, lifetime);
            return;
        }

        ProcessTableColumn committedColumn = _columns[resizedColumnIndex];
        ProcessTableColumn liveColumn = liveColumns[resizedColumnIndex];
        double offset = liveColumn.Width - committedColumn.Width;
        DrawRetainedRowSegment(
            context,
            cache,
            new Rect(x: 0, y: 0, committedColumn.Left, _metrics.RowHeight),
            translationX: 0,
            lifetime);

        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(liveColumn.Kind);
        if (definition.Lifetime == lifetime)
        {
            string display = GetCellDisplayValue(rowIndex, liveColumn.Kind);
            if (display.Length > 0)
            {
                using CellTextLayout layout = CreateCellTextLayout(
                    liveColumn,
                    display,
                    GetTreeLayoutKey(rowIndex));
                using (context.PushClip(new Rect(
                           liveColumn.Left,
                           y: 0,
                           liveColumn.Width,
                           _metrics.RowHeight)))
                    layout.Draw(context);
            }
        }

        DrawRetainedRowSegment(
            context,
            cache,
            new Rect(
                liveColumn.Right,
                y: 0,
                Math.Max(val1: 0, Bounds.Width - liveColumn.Right),
                _metrics.RowHeight),
            offset,
            lifetime);
    }

    private void DrawRetainedRowSegment(
        DrawingContext context,
        ProcessRowRenderCache cache,
        Rect clip,
        double translationX,
        ProcessTableColumnLifetime lifetime)
    {
        if (clip.Width <= 0 || clip.Height <= 0) return;

        using (context.PushClip(clip))
        {
            if (Math.Abs(translationX) < 0.01)
            {
                DrawRetainedRowDrawing(context, cache, lifetime);
                return;
            }

            using (context.PushTransform(Matrix.CreateTranslation(translationX, yPosition: 0)))
                DrawRetainedRowDrawing(context, cache, lifetime);
        }
    }

    private void DrawRetainedRowDrawing(
        DrawingContext context,
        ProcessRowRenderCache cache,
        ProcessTableColumnLifetime lifetime)
    {
        ProcessRowDrawing? rowDrawing;
        int metricsGeneration;
        switch (lifetime)
        {
            case ProcessTableColumnLifetime.Static:
                rowDrawing = cache.StaticDrawing;
                metricsGeneration = cache.StaticMetricsGeneration;
                break;
            case ProcessTableColumnLifetime.Dynamic:
                rowDrawing = cache.DynamicDrawing;
                metricsGeneration = cache.DynamicMetricsGeneration;
                break;
            default:
                return;
        }

        if (rowDrawing == null) return;
        if (metricsGeneration == _gridMetricsGeneration)
        {
            rowDrawing.Draw(context);
            return;
        }

        // Keep the previous generation readable while the throttled visible-row rebuild catches up
        Rect rowClip = new(x: 0, y: 0, Bounds.Width, _metrics.RowHeight);
        double verticalOffset = (_metrics.RowHeight - rowDrawing.RowHeight) / 2;
        using (context.PushClip(rowClip))
        using (context.PushTransform(Matrix.CreateTranslation(xPosition: 0, verticalOffset)))
            rowDrawing.Draw(context);
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

        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        double hierarchyInset = GetHierarchyInset(treeLayoutKey);
        if (IsSemanticSectionRow(rowIndex))
        {
            if (IsSemanticSectionHeaderLayout(treeLayoutKey)
                && HasTreeExpanderSlot(treeLayoutKey))
                DrawTreeExpander(
                    context,
                    nameColumn,
                    row,
                    top,
                    hierarchyInset,
                    isSemanticSectionHeader: true);
            return;
        }

        double iconTop = top + (_metrics.RowHeight - _metrics.ProcessIconSize) / 2;
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
        {
            context.FillRectangle(
                _accentBrush,
                iconBounds,
                (float)_visualMetrics.ProcessIconCornerRadius);
        }

        if (HasTreeExpanderSlot(treeLayoutKey))
        {
            DrawTreeExpander(
                context,
                nameColumn,
                row,
                top,
                hierarchyInset,
                isSemanticSectionHeader: false);
        }
    }

    private void DrawTreeExpander(
        DrawingContext context,
        ProcessTableColumn nameColumn,
        ProcessStaticData row,
        double top,
        double hierarchyInset,
        bool isSemanticSectionHeader)
    {
        double caretSizeOffset = isSemanticSectionHeader
            ? _visualMetrics.SemanticSectionCaretSizeOffset / 2
            : 0;
        double chevronHalfWidth = _visualMetrics.TreeExpanderChevronHalfWidth
                                  + caretSizeOffset;
        double chevronHalfHeight = _visualMetrics.TreeExpanderChevronHalfHeight
                                   + caretSizeOffset;
        double centerX = nameColumn.Left
                         + _metrics.CellPadding
                         + hierarchyInset
                         + _visualMetrics.TreeExpanderWidth / 2;
        double centerY = top + _metrics.RowHeight / 2;
        if (isSemanticSectionHeader)
            centerY -= _visualMetrics.SemanticSectionCaretUpwardShift;
        if (_filterQuery.IsEmpty && _collapsedProcesses.Contains(row.InstanceKey))
        {
            context.DrawLine(
                _treeExpanderPen,
                new Point(
                    centerX - chevronHalfWidth,
                    centerY - chevronHalfHeight),
                new Point(centerX + chevronHalfWidth, centerY));
            context.DrawLine(
                _treeExpanderPen,
                new Point(centerX + chevronHalfWidth, centerY),
                new Point(
                    centerX - chevronHalfWidth,
                    centerY + chevronHalfHeight));
            return;
        }

        context.DrawLine(
            _treeExpanderPen,
            new Point(
                centerX - chevronHalfHeight,
                centerY - chevronHalfWidth),
            new Point(centerX, centerY + chevronHalfWidth));
        context.DrawLine(
            _treeExpanderPen,
            new Point(centerX, centerY + chevronHalfWidth),
            new Point(
                centerX + chevronHalfHeight,
                centerY - chevronHalfWidth));
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

    private void DrawHeaderBackground(DrawingContext context, double top)
    {
        Rect headerBounds = new(x: 0, top, Bounds.Width, _metrics.HeaderHeight);
        context.FillRectangle(_backgroundBrush, headerBounds);
    }

    private void DrawHeaderContent(DrawingContext context, double top)
    {
        ProcessTableColumn[] columns = DisplayColumns;
        Rect viewport = ResolveViewport();
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (column.Right <= viewport.Left || column.Left >= viewport.Right) continue;

            double textLeft = column.Left + _metrics.CellPadding;
            bool isSortedColumn = column.Kind == _sortColumn;
            bool useDescendingCaret = isSortedColumn
                                      && _sortDescending
                                      != ProcessTableColumnCatalog.SortsDescendingByDefault(column.Kind);
            TextLayout? caret = isSortedColumn
                ? useDescendingCaret ? _descendingCaretText : _ascendingCaretText
                : null;
            double caretX = caret == null
                ? column.Right
                : column.Right - _sortCaretRightMargin - caret.Width;
            double headerTextRight = caret == null
                ? column.Right - _metrics.CellPadding
                : caretX - _metrics.CellPadding;
            double headerTextWidth = Math.Max(val1: 0, headerTextRight - textLeft);
            if (headerTextWidth > 0 && _metrics.HeaderHeight > 0)
            {
                bool needsLiveResizeLayout = column.Alignment == ProcessTableColumnAlignment.Right
                                             && Math.Abs(column.Width - _columns[columnIndex].Width) >= 0.01;
                using HeaderContentLayout? liveResizeText = needsLiveResizeLayout
                    ? CreateHeaderText(column, headerTextWidth)
                    : null;
                HeaderContentLayout headerText = liveResizeText
                                                 ?? _headerTexts[columnIndex].Get(
                                                     isSortedColumn,
                                                     useDescendingCaret);
                Rect headerClip = new(
                    textLeft,
                    top,
                    headerTextWidth,
                    _metrics.HeaderHeight);
                using (context.PushClip(headerClip))
                    headerText.Draw(context, textLeft, top, _metrics.HeaderHeight);
            }

            if (caret != null)
            {
                double caretTop = top + Math.Max(val1: 0, (_metrics.HeaderHeight - caret.Height) / 2);
                caret.Draw(context, new Point(caretX, caretTop));
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
        context.DrawRectangle(brush: null, _columnInteractionPen, bounds);

        HeaderContentLayout headerText = _headerTexts[_interactionColumnIndex].Normal;
        double textLeft = left + _metrics.CellPadding;
        double textWidth = Math.Max(val1: 0, column.Width - _metrics.CellPadding * 2);
        if (textWidth <= 0 || _metrics.HeaderHeight <= 0) return;
        Rect textClip = new(
            textLeft,
            headerTop,
            textWidth,
            _metrics.HeaderHeight);
        using (context.PushClip(textClip))
            headerText.Draw(context, textLeft, headerTop, _metrics.HeaderHeight);
    }

    private void UpdateRetainedDrawings()
    {
        if (IsDetailsGridZoomActive)
        {
            QueueDetailsGridZoomWork();
            return;
        }

        DetailsGridLayout.GetRetainedRowRange(
            ResolveViewport(),
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int firstVisibleIndex,
            out int lastVisibleIndexExclusive);
        UpdateRetainedDrawings(firstVisibleIndex, lastVisibleIndexExclusive);
    }

    private void EnsureRetainedDrawingsForViewport()
    {
        if (IsDetailsGridDisposed) return;
        if (IsDetailsGridZoomActive)
        {
            QueueDetailsGridZoomWork();
            return;
        }

        DetailsGridLayout.GetRetainedRowRange(
            ResolveViewport(),
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int firstVisibleIndex,
            out int lastVisibleIndexExclusive);
        if (firstVisibleIndex == _retainedFirstVisibleIndex
            && lastVisibleIndexExclusive == _retainedLastVisibleIndexExclusive)
        {
            ScheduleWarmDynamicRefresh();
            return;
        }

        UpdateRetainedDrawings(firstVisibleIndex, lastVisibleIndexExclusive);
    }

    private void UpdateRetainedDrawings(
        int firstVisibleIndex,
        int lastVisibleIndexExclusive)
    {
        CommitRetainedDrawingRange(firstVisibleIndex, lastVisibleIndexExclusive);

        for (int visibleIndex = firstVisibleIndex;
             visibleIndex < lastVisibleIndexExclusive;
             visibleIndex++)
        {
            EnsureRowDrawingsCurrent(
                visibleIndex,
                rebuildChangedDynamicDrawingImmediately: false);
        }

        ScheduleWarmDynamicRefresh();
    }

    private void CommitRetainedDrawingRange(
        int firstVisibleIndex,
        int lastVisibleIndexExclusive)
    {
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            cache.IsDrawingRetained = false;

        for (int visibleIndex = firstVisibleIndex;
             visibleIndex < lastVisibleIndexExclusive;
             visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
                continue;

            cache.IsDrawingRetained = true;
        }

        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
        {
            if (!cache.IsDrawingRetained
                && (cache.StaticDrawing != null || cache.DynamicDrawing != null))
                ReleaseRenderCache(cache);
        }

        _retainedFirstVisibleIndex = firstVisibleIndex;
        _retainedLastVisibleIndexExclusive = lastVisibleIndexExclusive;
    }

    protected override void CommitDetailsGridRetainedRange(int firstRow, int lastRowExclusive) =>
        CommitRetainedDrawingRange(firstRow, lastRowExclusive);

    protected override bool RebuildDetailsGridZoomRow(int rowIndex) =>
        EnsureRowDrawingsCurrent(
            rowIndex,
            rebuildChangedDynamicDrawingImmediately: true);

    protected override void InvalidateDetailsGridRows() =>
        InvalidateLayers(RenderLayerMask.Rows);

    protected override void OnDetailsGridZoomCompleted() =>
        ScheduleWarmDynamicRefresh();

    private bool EnsureRowDrawingsCurrent(
        int visibleIndex,
        bool rebuildChangedDynamicDrawingImmediately)
    {
        if ((uint)visibleIndex >= (uint)_visibleRowCount) return false;

        int rowIndex = _visibleRowIndexes[visibleIndex];
        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null || !_renderCaches.TryGetValue(row.InstanceKey, out ProcessRowRenderCache? cache))
            return false;

        bool rebuiltDrawing = false;
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        if (cache.StaticDrawing == null
            || cache.StaticMetricsGeneration != _gridMetricsGeneration
            || cache.StaticTreeLayoutKey != treeLayoutKey)
        {
            ProcessRowDrawing replacementDrawing = BuildRowDrawing(
                rowIndex,
                ProcessTableColumnLifetime.Static);
            ProcessRowDrawing? previousDrawing = cache.StaticDrawing;
            cache.StaticDrawing = replacementDrawing;
            cache.StaticMetricsGeneration = _gridMetricsGeneration;
            cache.StaticTreeLayoutKey = treeLayoutKey;
            ReleaseRowDrawing(previousDrawing);
            rebuiltDrawing = true;
        }

        if (!_hasDynamicColumns) return rebuiltDrawing;

        cache.PendingDynamicFingerprint = CalculateDynamicFingerprint(rowIndex);
        if (cache.DynamicDrawing == null
            || cache.DynamicMetricsGeneration != _gridMetricsGeneration
            || (rebuildChangedDynamicDrawingImmediately
                && cache.DynamicFingerprint != cache.PendingDynamicFingerprint))
        {
            RebuildDynamicDrawing(cache, rowIndex);
            rebuiltDrawing = true;
        }

        return rebuiltDrawing;
    }

    private ProcessRowDrawing BuildRowDrawing(
        int rowIndex,
        ProcessTableColumnLifetime lifetime)
    {
        _sharedCellBuffer.Clear();
        _cellTextLayoutBuffer.Clear();
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        try
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
                    ProcessSharedCellKey key = new(
                        _gridMetricsGeneration,
                        column.Kind,
                        display,
                        cellTreeLayoutKey);
                    SharedCellLayout sharedCell = AcquireSharedCellLayout(column, key);
                    try
                    {
                        _sharedCellBuffer.Add(sharedCell);
                    }
                    catch
                    {
                        ReleaseSharedCellLayout(sharedCell);
                        throw;
                    }

                    continue;
                }

                CellTextLayout cellTextLayout = CreateCellTextLayout(column, display, treeLayoutKey);
                try
                {
                    _cellTextLayoutBuffer.Add(cellTextLayout);
                }
                catch
                {
                    cellTextLayout.Dispose();
                    throw;
                }
            }

            SharedCellLayout[] sharedCells = _sharedCellBuffer.Count == 0
                ? []
                : _sharedCellBuffer.ToArray();
            CellTextLayout[] cellTextLayouts = _cellTextLayoutBuffer.Count == 0
                ? []
                : _cellTextLayoutBuffer.ToArray();
            return new ProcessRowDrawing(sharedCells, cellTextLayouts, _metrics.RowHeight);
        }
        catch
        {
            for (int cellIndex = 0; cellIndex < _sharedCellBuffer.Count; cellIndex++)
                ReleaseSharedCellLayout(_sharedCellBuffer[cellIndex]);
            for (int layoutIndex = 0; layoutIndex < _cellTextLayoutBuffer.Count; layoutIndex++)
                _cellTextLayoutBuffer[layoutIndex].Dispose();
            throw;
        }
        finally
        {
            _sharedCellBuffer.Clear();
            _cellTextLayoutBuffer.Clear();
        }
    }

    private SharedCellLayout AcquireSharedCellLayout(ProcessTableColumn column, ProcessSharedCellKey key)
    {
        if (_sharedCellLayouts.TryGetValue(key, out SharedCellLayout? existing))
        {
            existing.ReferenceCount++;
            return existing;
        }

        CellTextLayout cellTextLayout = CreateCellTextLayout(column, key.Value, key.TreeLayoutKey);
        SharedCellLayout sharedCell = new(key, cellTextLayout);
        try
        {
            _sharedCellLayouts.Add(key, sharedCell);
        }
        catch
        {
            sharedCell.Dispose();
            throw;
        }

        return sharedCell;
    }

    private void ReleaseSharedCellLayouts(SharedCellLayout[] sharedCells)
    {
        for (int cellIndex = 0; cellIndex < sharedCells.Length; cellIndex++)
            ReleaseSharedCellLayout(sharedCells[cellIndex]);
    }

    private void ReleaseRowDrawing(ProcessRowDrawing? rowDrawing)
    {
        if (rowDrawing == null) return;

        ReleaseSharedCellLayouts(rowDrawing.SharedCells);
        DisposeCellTextLayouts(rowDrawing.CellTextLayouts);
    }

    private void ReleaseSharedCellLayout(SharedCellLayout sharedCell)
    {
        sharedCell.ReferenceCount--;
        if (sharedCell.ReferenceCount > 0) return;

        _sharedCellLayouts.Remove(sharedCell.Key);
        sharedCell.Dispose();
    }

    private CellTextLayout CreateCellTextLayout(
        ProcessTableColumn column,
        string display,
        int treeLayoutKey)
    {
        bool isSemanticSectionHeader = IsSemanticSectionHeaderLayout(treeLayoutKey);
        double leftInset = _metrics.CellPadding;
        if (column.Kind == ProcessTableColumnKind.Name)
        {
            double leadingContentWidth = isSemanticSectionHeader
                ? _visualMetrics.SemanticSectionHeaderTextGap
                : _metrics.ProcessIconSize + _metrics.ProcessIconGap;
            leftInset += GetHierarchyInset(treeLayoutKey)
                         + (HasTreeExpanderSlot(treeLayoutKey)
                             ? _visualMetrics.TreeExpanderWidth
                             : 0)
                         + leadingContentWidth;
        }
        double availableWidth = Math.Max(val1: 0, column.Width - leftInset - _metrics.CellPadding);
        double fontSize = isSemanticSectionHeader
            ? _metrics.FontSize + _visualMetrics.SemanticSectionHeaderSizeOffset
            : _metrics.FontSize;
        TextLayout text = CreateBoundedText(display, availableWidth, fontSize);
        double textHeight = isSemanticSectionHeader ? text.Height : _metrics.RowTextHeight;
        double textTop = Math.Max(val1: 0, (_metrics.RowHeight - textHeight) / 2);
        if (isSemanticSectionHeader)
            textTop -= _visualMetrics.SemanticSectionHeaderUpwardShift;
        double textX = column.Alignment == ProcessTableColumnAlignment.Right
            ? column.Right - _metrics.CellPadding - text.Width
            : column.Left + leftInset;
        return new CellTextLayout(text, textX, textTop, availableWidth);
    }

    private void RebuildDynamicDrawing(ProcessRowRenderCache cache, int rowIndex)
    {
        ProcessRowDrawing replacementDrawing = BuildRowDrawing(
            rowIndex,
            ProcessTableColumnLifetime.Dynamic);
        ProcessRowDrawing? previousDrawing = cache.DynamicDrawing;
        cache.DynamicDrawing = replacementDrawing;
        cache.DynamicMetricsGeneration = _gridMetricsGeneration;
        cache.DynamicFingerprint = cache.PendingDynamicFingerprint;
        ReleaseRowDrawing(previousDrawing);
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
                case ProcessTableColumnKind.CPUSingle:
                case ProcessTableColumnKind.GPU:
                case ProcessTableColumnKind.NPU:
                case ProcessTableColumnKind.CPUUtility:
                    hash.Add(QuantizePercent(
                        BitConverter.Int64BitsToDouble(value),
                        setting.ShowDecimalUsage));
                    break;
                case ProcessTableColumnKind.Disk:
                    hash.Add(QuantizeTransferRate(
                        BitConverter.Int64BitsToDouble(value),
                        BytesPerMebibyte));
                    break;
                case ProcessTableColumnKind.Network:
                    hash.Add(QuantizeTransferRate(
                        BitConverter.Int64BitsToDouble(value),
                        BytesPerMegabit));
                    break;
                case ProcessTableColumnKind.CPUTime:
                    hash.Add(value / TimeSpan.TicksPerSecond);
                    break;
                case ProcessTableColumnKind.Lifetime:
                    hash.Add(value < 0 ? ProcessLifetime.UnavailableTicks : value / TimeSpan.TicksPerSecond);
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
                    hash.Add(QuantizeMemory(value, setting.MemoryUnit, isDelta: false));
                    break;
                case ProcessTableColumnKind.WorkingSetDelta:
                    hash.Add(QuantizeMemory(value, setting.MemoryUnit, isDelta: true));
                    break;
                default:
                    hash.Add(value);
                    break;
            }
        }

        return hash.ToHashCode();
    }

    private string GetCellDisplayValue(int rowIndex, ProcessTableColumnKind kind)
    {
        if (IsSemanticSectionRow(rowIndex) && kind != ProcessTableColumnKind.Name)
            return string.Empty;

        return ProcessTableColumnCatalog.Get(kind).Lifetime == ProcessTableColumnLifetime.Static
            ? GetStaticDisplayValue(rowIndex, kind)
            : GetDynamicDisplayValue(rowIndex, kind);
    }

    private ProcessSearchColumnValue GetSearchColumnValue(
        int rowIndex,
        ProcessTableColumnKind kind)
    {
        string displayText = GetCellDisplayValue(rowIndex, kind);
        if (ProcessTableColumnCatalog.Get(kind).Lifetime == ProcessTableColumnLifetime.Static)
        {
            ProcessStaticData row = _snapshot.StaticRows[rowIndex]
                                    ?? throw new InvalidOperationException(
                                        "A published process row is missing static data.");
            return kind switch
            {
                ProcessTableColumnKind.ProcessID when
                    _membersBySyntheticKey.ContainsKey(row.InstanceKey) =>
                    ProcessSearchColumnValue.TextOnly(displayText),
                ProcessTableColumnKind.ProcessID => ProcessSearchColumnValue.Numeric(displayText, row.ProcessID),
                ProcessTableColumnKind.SessionID => CreateSignedSearchValue(
                    displayText,
                    row.NumericValues[_schema.GetStaticNumericSlot(kind)],
                    allowsNegative: false),
                _ => ProcessSearchColumnValue.TextOnly(displayText)
            };
        }

        if (ProcessDataSchema.StoresText(kind))
            return ProcessSearchColumnValue.TextOnly(displayText);

        long numericValue = _snapshot.GetDynamicNumeric(rowIndex, kind);
        return kind switch
        {
            ProcessTableColumnKind.Status
                or ProcessTableColumnKind.UACVirtualization
                or ProcessTableColumnKind.IOPriority
                or ProcessTableColumnKind.PowerThrottling
                or ProcessTableColumnKind.DPIAwareness => ProcessSearchColumnValue.TextOnly(displayText),
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.GPU
                or ProcessTableColumnKind.NPU
                or ProcessTableColumnKind.CPUUtility => CreatePercentageSearchValue(displayText, numericValue),
            ProcessTableColumnKind.Disk => CreateTransferRateSearchValue(
                displayText,
                numericValue,
                convertToBits: false),
            ProcessTableColumnKind.Network => CreateTransferRateSearchValue(
                displayText,
                numericValue,
                convertToBits: true),
            ProcessTableColumnKind.CPUTime
                or ProcessTableColumnKind.Lifetime
                or ProcessTableColumnKind.JobObjectID => CreateSignedSearchValue(
                    displayText,
                    numericValue,
                    allowsNegative: false),
            ProcessTableColumnKind.Cycle
                or ProcessTableColumnKind.PageFaults
                or ProcessTableColumnKind.Handles
                or ProcessTableColumnKind.Threads
                or ProcessTableColumnKind.UserObjects
                or ProcessTableColumnKind.GDIObjects
                or ProcessTableColumnKind.IOReads
                or ProcessTableColumnKind.IOWrites
                or ProcessTableColumnKind.IOOther
                or ProcessTableColumnKind.IOReadBytes
                or ProcessTableColumnKind.IOWriteBytes
                or ProcessTableColumnKind.IOOtherBytes => ProcessSearchColumnValue.Numeric(
                    displayText,
                    unchecked((ulong)numericValue)),
            ProcessTableColumnKind.WorkingSetDelta
                or ProcessTableColumnKind.PageFaultDelta
                or ProcessTableColumnKind.BasePriority => CreateSignedSearchValue(
                    displayText,
                    numericValue,
                    allowsNegative: true),
            _ => CreateSignedSearchValue(displayText, numericValue, allowsNegative: false)
        };
    }

    private static ProcessSearchColumnValue CreatePercentageSearchValue(string displayText, long value)
    {
        double percentage = BitConverter.Int64BitsToDouble(value);
        return double.IsFinite(percentage) && percentage >= 0
            ? ProcessSearchColumnValue.Numeric(displayText, percentage)
            : ProcessSearchColumnValue.TextOnly(displayText);
    }

    private static ProcessSearchColumnValue CreateTransferRateSearchValue(
        string displayText,
        long value,
        bool convertToBits)
    {
        double bytesPerSecond = BitConverter.Int64BitsToDouble(value);
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0)
            return ProcessSearchColumnValue.TextOnly(displayText);

        double numericValue = convertToBits ? bytesPerSecond * 8 : bytesPerSecond;
        return ProcessSearchColumnValue.Numeric(displayText, numericValue);
    }

    private static ProcessSearchColumnValue CreateSignedSearchValue(
        string displayText,
        long value,
        bool allowsNegative) =>
        allowsNegative || value >= 0
            ? ProcessSearchColumnValue.Numeric(displayText, value)
            : ProcessSearchColumnValue.TextOnly(displayText);

    private string GetStaticDisplayValue(int rowIndex, ProcessTableColumnKind kind)
    {
        ProcessStaticData row = _snapshot.StaticRows[rowIndex]
                                ?? throw new InvalidOperationException(
                                    "A published process row is missing static data.");
        if (kind == ProcessTableColumnKind.ProcessID)
        {
            return _membersBySyntheticKey.ContainsKey(row.InstanceKey)
                ? string.Empty
                : row.ProcessID.ToString(TableCulture);
        }

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
        if (ProcessDataSchema.StoresText(kind))
            return LocalizeUnavailableText(_snapshot.GetDynamicText(rowIndex, kind));

        long value = _snapshot.GetDynamicNumeric(rowIndex, kind);
        ProcessColumnSetting setting = _settingsByColumn[(int)kind];
        return kind switch
        {
            ProcessTableColumnKind.Status => FormatDisplayCode(value),
            ProcessTableColumnKind.JobObjectID => FormatJobObjectID(value),
            ProcessTableColumnKind.CPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.CPUSingle => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.CPUTime => FormatCPUTime(value),
            ProcessTableColumnKind.Lifetime => value < 0
                ? UnavailableText
                : ProcessLifetime.Format(value),
            ProcessTableColumnKind.Cycle => FormatUnsigned(value),
            ProcessTableColumnKind.WorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PeakWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.WorkingSetDelta => FormatMemory(value, setting, isDelta: true),
            ProcessTableColumnKind.ActivePrivateWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PrivateMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.Disk => FormatTransferRate(
                BitConverter.Int64BitsToDouble(value),
                BytesPerMebibyte,
                suffix: "MB/s"),
            ProcessTableColumnKind.Network => FormatTransferRate(
                BitConverter.Int64BitsToDouble(value),
                BytesPerMegabit,
                suffix: "Mbps"),
            ProcessTableColumnKind.CommitSize => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PagedPool => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.NonPagedPool => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PageFaults => FormatSigned(value),
            ProcessTableColumnKind.PageFaultDelta => FormatSigned(value),
            ProcessTableColumnKind.BasePriority => value.ToString(TableCulture),
            ProcessTableColumnKind.Handles => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.Threads => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.UserObjects => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.GDIObjects => value.ToString(format: "N0", TableCulture),
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
            ProcessTableColumnKind.DedicatedGPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedGPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.DPIAwareness => FormatDisplayCode(value),
            ProcessTableColumnKind.NPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.DedicatedNPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedNPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.CPUUtility => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            _ => string.Empty
        };
    }

    private void RefreshLiveTotalHeaders()
    {
        ulong changedColumns = UpdateLiveTotalTexts();
        if (changedColumns == 0 || _headerTexts.Length != _columns.Length) return;

        for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
        {
            ProcessTableColumn column = _columns[columnIndex];
            if (!ProcessTableColumnCatalog.Contains(changedColumns, column.Kind)) continue;

            HeaderTextLayouts replacement = CreateHeaderTextLayouts(column);
            HeaderTextLayouts previous = _headerTexts[columnIndex];
            _headerTexts[columnIndex] = replacement;
            previous.Dispose();
        }
    }

    private ulong UpdateLiveTotalTexts()
    {
        ulong changedColumns = 0;
        for (int definitionIndex = 0;
             definitionIndex < ProcessTableColumnCatalog.Definitions.Length;
             definitionIndex++)
        {
            ProcessTableColumnKind column = (ProcessTableColumnKind)definitionIndex;
            ProcessColumnSetting setting = _settingsByColumn[definitionIndex];
            string nextText = string.Empty;
            if (_hasVisibleLiveTotals
                && setting.Visible
                && setting.ShowLiveTotal
                && ProcessColumnSettings.SupportsLiveTotal(column))
            {
                ProcessLiveTotalValue total = ProcessLiveTotalFunctions.Calculate(
                    _sourceSnapshot,
                    _sourceSnapshot.Count,
                    column);
                nextText = FormatLiveTotal(column, total, setting);
            }

            string previousText = _liveTotalTextsByColumn[definitionIndex] ?? string.Empty;
            if (string.Equals(previousText, nextText, StringComparison.Ordinal)) continue;

            _liveTotalTextsByColumn[definitionIndex] = nextText;
            changedColumns |= ProcessTableColumnCatalog.GetMask(column);
        }

        return changedColumns;
    }

    private void ClearLiveTotalHeaders()
    {
        bool changed = false;
        for (int columnIndex = 0; columnIndex < _liveTotalTextsByColumn.Length; columnIndex++)
        {
            if (string.IsNullOrEmpty(_liveTotalTextsByColumn[columnIndex])) continue;
            _liveTotalTextsByColumn[columnIndex] = string.Empty;
            changed = true;
        }

        if (changed && _headerTexts.Length == _columns.Length)
            ReplaceHeaderTexts(_columns);
    }

    private string FormatLiveTotal(
        ProcessTableColumnKind column,
        ProcessLiveTotalValue total,
        ProcessColumnSetting setting)
    {
        if (!total.HasValue) return UnavailableText;

        long value = total.EncodedValue;
        return column switch
        {
            ProcessTableColumnKind.CPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.CPUSingle => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.CPUTime => FormatCPUTime(value),
            ProcessTableColumnKind.Cycle => FormatUnsigned(value),
            ProcessTableColumnKind.WorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PeakWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.WorkingSetDelta => FormatMemory(value, setting, isDelta: true),
            ProcessTableColumnKind.ActivePrivateWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PrivateMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedWorkingSet => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.Disk => FormatTransferRate(
                BitConverter.Int64BitsToDouble(value),
                BytesPerMebibyte,
                suffix: "MB/s"),
            ProcessTableColumnKind.Network => FormatTransferRate(
                BitConverter.Int64BitsToDouble(value),
                BytesPerMegabit,
                suffix: "Mbps"),
            ProcessTableColumnKind.CommitSize => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PagedPool => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.NonPagedPool => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.PageFaults => FormatSigned(value),
            ProcessTableColumnKind.PageFaultDelta => FormatSigned(value),
            ProcessTableColumnKind.Handles => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.Threads => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.UserObjects => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.GDIObjects => value.ToString(format: "N0", TableCulture),
            ProcessTableColumnKind.IOReads => FormatUnsigned(value),
            ProcessTableColumnKind.IOWrites => FormatUnsigned(value),
            ProcessTableColumnKind.IOOther => FormatUnsigned(value),
            ProcessTableColumnKind.IOReadBytes => FormatUnsigned(value),
            ProcessTableColumnKind.IOWriteBytes => FormatUnsigned(value),
            ProcessTableColumnKind.IOOtherBytes => FormatUnsigned(value),
            ProcessTableColumnKind.GPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.DedicatedGPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedGPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.NPU => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            ProcessTableColumnKind.DedicatedNPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.SharedNPUMemory => FormatMemory(value, setting, isDelta: false),
            ProcessTableColumnKind.CPUUtility => FormatPercent(BitConverter.Int64BitsToDouble(value), setting),
            _ => string.Empty
        };
    }

    private string FormatDisplayCode(long value)
    {
        ProcessDisplayCode code = (ProcessDisplayCode)value;
        return code == ProcessDisplayCode.Unavailable
            ? UnavailableText
            : ProcessDisplayCodeText.Get(code);
    }

    private string FormatJobObjectID(long value) => value switch
    {
        < 0 => UnavailableText,
        0 => string.Empty,
        _ => value.ToString(TableCulture)
    };

    private string FormatPercent(double value, ProcessColumnSetting setting)
    {
        long quantized = QuantizePercent(value, setting.ShowDecimalUsage);
        if (quantized < 0) return UnavailableText;

        string display = setting.ShowDecimalUsage
            ? (quantized / 10.0).ToString(format: "0.0", TableCulture)
            : quantized.ToString(format: "0", TableCulture);
        return setting.ShowPercentSuffix ? string.Concat(display, str1: "%") : display;
    }

    private static string FormatCPUTime(long ticks)
    {
        long totalSeconds = Math.Max(val1: 0, ticks / TimeSpan.TicksPerSecond);
        if (totalSeconds == 0) return ZeroCPUTimeText;

        long hours = totalSeconds / 3_600;
        long minutes = totalSeconds / 60 % 60;
        long seconds = totalSeconds % 60;
        return string.Create(TableCulture, $"{hours}:{minutes:00}:{seconds:00}");
    }

    private string FormatMemory(long bytes, ProcessColumnSetting setting, bool isDelta)
    {
        if (!isDelta && bytes < 0) return UnavailableText;

        long quantized = QuantizeMemory(bytes, setting.MemoryUnit, isDelta);
        if (quantized == -1 && setting.MemoryUnit == ProcessMemoryUnit.PercentageOfSystem
                            && _totalPhysicalMemoryBytes <= 0)
            return UnavailableText;

        string display = setting.MemoryUnit == ProcessMemoryUnit.Kilobytes
            ? quantized.ToString(format: "N0", TableCulture)
            : (quantized / 10.0).ToString(format: "N1", TableCulture);
        string suffix = setting.MemorySuffix;
        if (suffix.Length == 0) return display;
        return setting.MemoryUnit == ProcessMemoryUnit.PercentageOfSystem
            ? string.Concat(display, suffix)
            : string.Concat(display, str1: " ", suffix);
    }

    private static string FormatSigned(long value) => value == 0
        ? ZeroText
        : value.ToString(format: "N0", TableCulture);

    private static string FormatUnsigned(long value) => value == 0
        ? ZeroText
        : unchecked((ulong)value).ToString(format: "N0", TableCulture);

    private string FormatTransferRate(
        double bytesPerSecond,
        double bytesPerDisplayUnit,
        string suffix)
    {
        long quantized = QuantizeTransferRate(bytesPerSecond, bytesPerDisplayUnit);
        if (quantized < 0) return UnavailableText;
        if (quantized == 0) return string.Concat(ZeroText, str1: " ", suffix);
        return string.Concat((quantized / 10.0).ToString(format: "N1", TableCulture), str1: " ", suffix);
    }

    private static long QuantizePercent(double value, bool showDecimalUsage)
    {
        if (!double.IsFinite(value) || value < 0) return -1;
        double scale = showDecimalUsage ? 10 : 1;
        return (long)Math.Round(Math.Max(val1: 0, value) * scale, MidpointRounding.AwayFromZero);
    }

    private static long QuantizeTransferRate(double bytesPerSecond, double bytesPerDisplayUnit)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) return -1;
        if (bytesPerSecond == 0) return 0;

        double scaledTenths = bytesPerSecond / bytesPerDisplayUnit * 10;
        if (scaledTenths >= long.MaxValue) return long.MaxValue;
        long quantized = (long)Math.Round(scaledTenths, MidpointRounding.AwayFromZero);
        return Math.Max(val1: 1, quantized);
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

    private static long ToKibibytes(long bytes)
    {
        if (bytes < 0) return -1;
        long quotient = bytes / 1024;
        return bytes % 1024 == 0 ? quotient : quotient + 1;
    }

    private static long ToSignedKibibytes(long bytes)
    {
        long quotient = bytes / 1024;
        long remainder = bytes % 1024;
        return remainder switch
        {
            > 0 => quotient + 1,
            < 0 => quotient - 1,
            _ => quotient
        };
    }

    private void ScheduleWarmDynamicRefresh()
    {
        if (IsDetailsGridDisposed || IsDetailsGridZoomActive || !_hasDynamicColumns) return;

        GetWarmVisibleRowRange(out _warmRefreshCursor, out _warmRefreshEnd);
        if (_warmRefreshCursor >= _warmRefreshEnd || _dynamicRefreshScheduled) return;

        _dynamicRefreshScheduled = true;
        Dispatcher.UIThread.Post(_refreshWarmDynamicDrawings, DispatcherPriority.Background);
    }

    private void RefreshWarmDynamicDrawings()
    {
        _dynamicRefreshScheduled = false;
        if (IsDetailsGridDisposed) return;

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
                    || cache.DynamicMetricsGeneration != _gridMetricsGeneration
                    || cache.DynamicFingerprint != cache.PendingDynamicFingerprint))
            {
                RebuildDynamicDrawing(cache, rowIndex);
                changed = true;
            }

            _warmRefreshCursor++;
            processed++;
            if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                >= TimeConstants.DynamicRefreshBudgetMilliseconds)
                break;
        }

        if (changed) InvalidateLayers(RenderLayerMask.DynamicRows);
        if (_warmRefreshCursor < _warmRefreshEnd)
        {
            _dynamicRefreshScheduled = true;
            Dispatcher.UIThread.Post(_refreshWarmDynamicDrawings, DispatcherPriority.Background);
        }
    }

    private void GetWarmVisibleRowRange(out int firstRow, out int lastRowExclusive)
    {
        Rect viewport = ResolveViewport();
        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            out int visibleFirst,
            out int visibleLastExclusive);
        firstRow = visibleFirst;
        lastRowExclusive = visibleLastExclusive;
    }

    private void PublishWarmProcesses()
    {
        if (!_samplingActive || _snapshotService == null) return;

        bool sampleEveryProcess = ProcessTableColumnCatalog.Get(_sortColumn).Lifetime
                                  == ProcessTableColumnLifetime.Dynamic
                                  || _filterQuery.RequiresAllProcessSamples
                                  || _hasVisibleLiveTotals;
        if (sampleEveryProcess)
        {
            _snapshotService.SetWarmProcesses(_schema.VisibleMask, _warmProcessIDs, count: 0, sampleEveryProcess: true);
            return;
        }

        GetWarmVisibleRowRange(out int firstRow, out int lastRowExclusive);
        EnsureWarmCapacity(_sourceSnapshot.Count);
        _warmProcessKeySet.Clear();
        int warmProcessCount = 0;
        for (int visibleIndex = firstRow; visibleIndex < lastRowExclusive; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null) continue;

            if (_membersBySyntheticKey.TryGetValue(
                    row.InstanceKey,
                    out ProcessInstanceKey[]? memberInstanceKeys))
            {
                for (int memberIndex = 0; memberIndex < memberInstanceKeys.Length; memberIndex++)
                {
                    ProcessInstanceKey memberInstanceKey = memberInstanceKeys[memberIndex];
                    if (!_warmProcessKeySet.Add(memberInstanceKey)) continue;
                    _warmProcessIDs[warmProcessCount] = memberInstanceKey.ProcessID;
                    warmProcessCount++;
                }

                continue;
            }

            if (!_sourceRowIndexByInstance.ContainsKey(row.InstanceKey)
                || !_warmProcessKeySet.Add(row.InstanceKey))
                continue;
            _warmProcessIDs[warmProcessCount] = row.ProcessID;
            warmProcessCount++;
        }

        _snapshotService.SetWarmProcesses(
            _schema.VisibleMask,
            _warmProcessIDs,
            warmProcessCount,
            sampleEveryProcess: false);
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
            return;

        List<ProcessColumnSetting> nextSettings = ProcessColumnSettings.MoveVisible(
            _columnSettings,
            _columns[columnIndex].Kind,
            insertionIndex);
        ApplyColumnLayout(nextSettings);
    }

    private void ApplyColumnLayout(List<ProcessColumnSetting> settings)
    {
        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(settings);
        ProcessTableColumn[] columns = CreateColumns(normalized);
        ProcessSearchQuery filterQuery = ProcessSearchQuery.Parse(_filterText, normalized);
        ProcessDataSchema schema = ProcessDataSchema.Create(
            normalized,
            filterQuery.RequiredColumnMask);
        if (schema.VisibleMask != _schema.VisibleMask && _samplingActive && _snapshotService != null)
        {
            _pendingColumnLayout = new PendingColumnLayout(
                normalized,
                columns,
                filterQuery,
                schema);
            ApplySamplingSchemaIfActive(schema);
            ColumnLayoutChanged?.Invoke(normalized);
            return;
        }

        _pendingColumnLayout = null;
        ApplySamplingSchemaIfActive(schema);
        PrepareColumnLayout(columns);
        _columnSettings = normalized;
        _settingsByColumn = CreateColumnSettingsIndex(normalized);
        _hasVisibleLiveTotals = ProcessColumnSettings.HasVisibleLiveTotals(normalized);
        _filterQuery = filterQuery;
        if (_schema.VisibleMask != schema.VisibleMask)
            ApplySearchSchema(schema, viewportAnchor);
        else
        {
            _schema = schema;
            _rowComparer.SetSchema(schema);
        }

        ApplyDisplayColumnLayout(columns, viewportAnchor);
        ColumnLayoutChanged?.Invoke(normalized);
    }

    private void CommitPendingColumnLayout(
        PendingColumnLayout pendingColumnLayout,
        int count,
        long version,
        ProcessViewportAnchor? viewportAnchor)
    {
        _pendingColumnLayout = null;
        PrepareColumnLayout(pendingColumnLayout.Columns);
        ClearSnapshotPresentationState();
        _schema = pendingColumnLayout.Schema;
        _rowComparer.SetSchema(pendingColumnLayout.Schema);
        _columnSettings = pendingColumnLayout.Settings;
        _settingsByColumn = CreateColumnSettingsIndex(pendingColumnLayout.Settings);
        _hasVisibleLiveTotals =
            ProcessColumnSettings.HasVisibleLiveTotals(pendingColumnLayout.Settings);
        _filterQuery = pendingColumnLayout.FilterQuery;
        _columns = pendingColumnLayout.Columns;
        UpdateLiveTotalTexts();
        ReplaceHeaderTexts(pendingColumnLayout.Columns);
        UpdateHeaderHoverVisual();
        RebuildFromCopiedSnapshot(count, version, viewportAnchor);
    }

    private void PrepareColumnLayout(ProcessTableColumn[] columns)
    {
        ResetHeaderInteraction();
        _hasDynamicColumns = ContainsLifetime(columns, ProcessTableColumnLifetime.Dynamic);
        if (_enableLiveColumnResizing
            && (_liveResizeColumns == null || _liveResizeColumns.Length != columns.Length))
            _liveResizeColumns = new ProcessTableColumn[columns.Length];
        if (_textUnderlineSegments.Length != columns.Length)
            _textUnderlineSegments = new TextUnderlineSegment[columns.Length];

        _textUnderlineSegmentCount = 0;
        _contextCopyRows = [];
        _hoveredHeaderColumnIndex = -1;
        if (FindColumn(columns, _sortColumn) >= 0) return;

        _sortColumn = columns[0].Kind;
        _sortDescending = ProcessTableColumnCatalog.SortsDescendingByDefault(_sortColumn);
    }

    private void ApplyDisplayColumnLayout(
        ProcessTableColumn[] columns,
        ProcessViewportAnchor? viewportAnchor)
    {
        _columns = columns;
        UpdateLiveTotalTexts();
        ReplaceHeaderTexts(columns);
        RebuildVisibleRows();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);

        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _sharedCellLayouts.Clear();
        UpdateRetainedDrawings();
        PublishWarmProcesses();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        UpdateHeaderHoverVisual();
        InvalidateLayers(RenderLayerMask.All);
    }

    private void ApplySamplingSchemaIfActive(ProcessDataSchema schema)
    {
        if (_samplingActive) _snapshotService?.SetActiveSchema(schema);
    }

    private void SortFromHeader(double x)
    {
        int columnIndex = ProcessTableLayout.HitTestColumn(x, _columns);
        if (columnIndex < 0) return;

        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        ProcessTableColumnKind nextColumn = _columns[columnIndex].Kind;
        if (nextColumn == _sortColumn)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = nextColumn;
            _sortDescending = ProcessTableColumnCatalog.SortsDescendingByDefault(nextColumn);
        }

        RebuildVisibleRows();
        RestoreViewportAnchor(viewportAnchor);
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateLayers(
            RenderLayerMask.Rows
            | RenderLayerMask.Icons
            | RenderLayerMask.CopyPreview
            | RenderLayerMask.Header);
    }

    private void ApplyPointerSelection(int visibleIndex, KeyModifiers modifiers)
    {
        if ((uint)visibleIndex < (uint)_visibleRowCount
            && IsSemanticSectionRow(_visibleRowIndexes[visibleIndex]))
            return;

        ProcessInstanceKey[] visibleProcesses = CreateVisibleProcessKeys(
            visibleIndex,
            out int selectableVisibleIndex);
        ProcessSelectionResult result = ProcessSelectionFunctions.ApplyPointerSelection(
            _selectedProcesses,
            visibleProcesses,
            selectableVisibleIndex,
            _selectedProcess,
            _selectionAnchorProcess,
            isControlPressed: (modifiers & KeyModifiers.Control) != 0,
            isShiftPressed: (modifiers & KeyModifiers.Shift) != 0);
        if (!result.Changed) return;

        _selectedProcess = result.ActiveProcess;
        _selectionAnchorProcess = result.AnchorProcess;
        NotifySelectionChanged();
        UpdateSelectionOverlay();
    }

    private void SetActiveSelectedProcess(ProcessInstanceKey process)
    {
        if (!_selectedProcesses.Contains(process) || _selectedProcess == process) return;

        _selectedProcess = process;
        NotifySelectionChanged();
    }

    private ProcessInstanceKey[] CreateVisibleProcessKeys(
        int requestedVisibleIndex,
        out int selectableVisibleIndex)
    {
        ProcessInstanceKey[] visibleProcesses = new ProcessInstanceKey[_visibleRowCount];
        selectableVisibleIndex = -1;
        int writeIndex = 0;
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            if (IsSemanticSectionRow(rowIndex)) continue;

            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null)
                throw new InvalidOperationException("A published process row is missing static data.");
            if (visibleIndex == requestedVisibleIndex) selectableVisibleIndex = writeIndex;
            visibleProcesses[writeIndex] = row.InstanceKey;
            writeIndex++;
        }

        if (writeIndex != visibleProcesses.Length)
            Array.Resize(ref visibleProcesses, writeIndex);
        return visibleProcesses;
    }

    private ProcessInstanceKey? FindFirstSelectedProcess()
    {
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[_visibleRowIndexes[visibleIndex]];
            if (row != null && _selectedProcesses.Contains(row.InstanceKey)) return row.InstanceKey;
        }

        foreach (ProcessInstanceKey process in _selectedProcesses)
            return process;
        return null;
    }

    private void NotifySelectionChanged()
    {
        SelectedProcessChanged?.Invoke(SelectedTerminationTarget);
        InvalidateLayers(RenderLayerMask.Selection);
    }

    private bool TryToggleTreeExpander(Point position, int visibleIndex)
    {
        if (_processGroupingStyle == ProcessGroupingStyle.None
            || visibleIndex < 0
            || visibleIndex >= _visibleRowCount)
            return false;

        int rowIndex = _visibleRowIndexes[visibleIndex];
        if (_semanticSectionRowKinds[rowIndex] == SemanticProcessSectionRowKind.Spacer)
        {
            int headerVisibleIndex = visibleIndex + 1;
            if (headerVisibleIndex >= _visibleRowCount) return false;

            int headerRowIndex = _visibleRowIndexes[headerVisibleIndex];
            if (_semanticSectionRowKinds[headerRowIndex]
                != SemanticProcessSectionRowKind.Header)
                return false;
            rowIndex = headerRowIndex;
        }

        if (!_rowHasChildren[rowIndex]) return false;

        ProcessTableColumn[] columns = DisplayColumns;
        int nameColumnIndex = FindColumn(columns, ProcessTableColumnKind.Name);
        if (nameColumnIndex < 0) return false;

        ProcessTableColumn nameColumn = columns[nameColumnIndex];
        int treeLayoutKey = GetTreeLayoutKey(rowIndex);
        double expanderLeft = nameColumn.Left
                              + _metrics.CellPadding
                              + GetHierarchyInset(treeLayoutKey);
        if (position.X < expanderLeft
            || position.X >= expanderLeft + _visualMetrics.TreeExpanderWidth)
            return false;

        ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
        if (row == null) return false;

        ApplyPointerSelection(visibleIndex, KeyModifiers.None);
        ProcessViewportAnchor? viewportAnchor = CaptureViewportAnchor();
        if (!_collapsedProcesses.Add(row.InstanceKey))
            _collapsedProcesses.Remove(row.InstanceKey);
        RebuildVisibleRows();
        InvalidateMeasure();
        RestoreViewportAnchor(viewportAnchor);
        PublishWarmProcesses();
        UpdateRetainedDrawings();
        UpdateSelectionOverlay();
        RebuildCopyPreview();
        InvalidateLayers(RenderLayerMask.All);
        return true;
    }

    private ProcessRowHoverGeometry CreateRowHoverGeometry()
    {
        Rect viewport = ResolveViewport();
        return new ProcessRowHoverGeometry(
            viewport,
            _visibleRowCount,
            _metrics.HeaderHeight,
            _metrics.RowHeight,
            ResolveStickyHeaderTop(viewport),
            _headerInteraction == HeaderInteractionMode.None && !IsDetailsGridDisposed,
            _semanticSectionVisibleStarts[0],
            _semanticSectionVisibleStarts[1],
            _semanticSectionVisibleStarts[2]);
    }

    private void PublishRowHoverGeometry()
    {
        if (IsDetailsGridDisposed) return;

        ProcessRowHoverGeometry geometry = CreateRowHoverGeometry();
        if (_hasPublishedRowHoverGeometry && geometry == _publishedRowHoverGeometry) return;

        _hasPublishedRowHoverGeometry = true;
        _publishedRowHoverGeometry = geometry;
        RowHoverGeometryChanged?.Invoke(geometry);
    }

    private void UpdateSelectionOverlay()
    {
        InvalidateLayers(RenderLayerMask.Selection);
    }

    private void RebuildVisibleRows()
    {
        Array.Fill(_semanticSectionVisibleStarts, value: -1);
        _usesSemanticSections = SemanticProcessSections.IsEnabled(
            _processGroupingStyle,
            _sortColumn);
        Array.Clear(_filterIncludedRows, index: 0, _rowCount);
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            ProcessStaticData? row = _snapshot.StaticRows[rowIndex];
            if (row == null
                || IsSemanticSectionRow(rowIndex)
                || !MatchesFilter(rowIndex))
                continue;
            _filterIncludedRows[rowIndex] = true;

            if (_processGroupingStyle == ProcessGroupingStyle.None) continue;
            int ancestorRowIndex = _treeParentIndexes[rowIndex];
            int remainingEdges = _rowCount;
            while (ancestorRowIndex >= 0 && remainingEdges > 0)
            {
                _filterIncludedRows[ancestorRowIndex] = true;
                ancestorRowIndex = _treeParentIndexes[ancestorRowIndex];
                remainingEdges--;
            }
        }

        int writeIndex = 0;
        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            if (!_filterIncludedRows[rowIndex]) continue;
            _visibleRowIndexes[writeIndex] = rowIndex;
            writeIndex++;
        }
        _visibleRowCount = writeIndex;
        SortVisibleRows();
        if (_usesSemanticSections && _visibleRowCount > 0)
            BuildSemanticSectionedVisibleRows();
        else if (_processGroupingStyle != ProcessGroupingStyle.None && _visibleRowCount > 1)
            BuildGroupedVisibleRows();
        else
            ClearTreeLayout();
        PublishRowHoverGeometry();
    }

    private bool MatchesFilter(int rowIndex)
    {
        if (_filterQuery.IsEmpty) return true;
        return _filterQuery.Matches(rowIndex, _resolveSearchValue);
    }

    private void SortVisibleRows()
    {
        _rowComparer.Column = _sortColumn;
        _rowComparer.IsDescending = _sortDescending;
        _rowComparer.ShowUserNamePrefix = _settingsByColumn[(int)ProcessTableColumnKind.UserName]
            .ShowUserNamePrefix;
        Array.Sort(_visibleRowIndexes, index: 0, _visibleRowCount, _rowComparer);
    }

    /// <summary>Builds a sorted parent/child traversal using reusable contiguous buffers.</summary>
    private void BuildGroupedVisibleRows()
    {
        BuildTreeChildIndexes();

        int outputCount = 0;
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            if (_treeParentIndexes[rowIndex] >= 0 || _treeVisited[rowIndex] != 0) continue;
            outputCount = AppendTree(rowIndex, outputCount, initialDepth: 0);
        }

        // PID reuse or malformed native data can form a cycle; retain those rows as an extra root tree
        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            if (_treeVisited[rowIndex] != 0) continue;
            outputCount = AppendTree(rowIndex, outputCount, initialDepth: 0);
        }

        Array.Copy(_treeOrderBuffer, _visibleRowIndexes, outputCount);
        _visibleRowCount = outputCount;
    }

    private void BuildTreeChildIndexes()
    {
        Array.Clear(_treeChildCounts, index: 0, _rowCount);
        Array.Clear(_rowDepths, index: 0, _rowCount);
        Array.Clear(_rowHasChildren, index: 0, _rowCount);
        Array.Clear(_treeVisited, index: 0, _rowCount);

        for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
        {
            int rowIndex = _visibleRowIndexes[visibleIndex];
            int parentRowIndex = _treeParentIndexes[rowIndex];
            if (parentRowIndex < 0 || !_filterIncludedRows[parentRowIndex]) continue;
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
    }

    /// <summary>Adds fixed two-row category blocks around name-sorted semantic trees.</summary>
    private void BuildSemanticSectionedVisibleRows()
    {
        BuildTreeChildIndexes();
        int outputCount = 0;
        for (int sectionIndex = 0; sectionIndex < SemanticProcessSections.Count; sectionIndex++)
        {
            SemanticProcessGroupClassification classification =
                SemanticProcessSections.GetClassification(sectionIndex);
            bool hasIncludedRows = false;
            for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
            {
                int rowIndex = _visibleRowIndexes[visibleIndex];
                if (IsSemanticClassification(rowIndex, classification))
                {
                    hasIncludedRows = true;
                    break;
                }
            }

            if (!hasIncludedRows) continue;

            int classificationIndex = (int)classification;
            int spacerRowIndex = _semanticSectionSpacerRowIndexes[classificationIndex];
            int headerRowIndex = _semanticSectionHeaderRowIndexes[classificationIndex];
            if (spacerRowIndex < 0 || headerRowIndex < 0)
                throw new InvalidOperationException("A semantic process section is missing its row pair.");

            _semanticSectionVisibleStarts[sectionIndex] = outputCount;
            _treeOrderBuffer[outputCount] = spacerRowIndex;
            outputCount++;
            _treeOrderBuffer[outputCount] = headerRowIndex;
            outputCount++;
            _rowHasChildren[headerRowIndex] = true;

            ProcessStaticData header = _snapshot.StaticRows[headerRowIndex]
                                       ?? throw new InvalidOperationException(
                                           "A semantic process section header is missing static data.");
            bool hideSection = _filterQuery.IsEmpty
                               && _collapsedProcesses.Contains(header.InstanceKey);
            if (hideSection) continue;

            for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
            {
                int rowIndex = _visibleRowIndexes[visibleIndex];
                if (!IsSemanticClassification(rowIndex, classification)
                    || _treeParentIndexes[rowIndex] >= 0
                    || _treeVisited[rowIndex] != 0)
                    continue;
                outputCount = AppendTree(rowIndex, outputCount, initialDepth: 1);
            }

            // Preserve malformed cyclic rows inside their classified section
            for (int visibleIndex = 0; visibleIndex < _visibleRowCount; visibleIndex++)
            {
                int rowIndex = _visibleRowIndexes[visibleIndex];
                if (!IsSemanticClassification(rowIndex, classification)
                    || _treeVisited[rowIndex] != 0)
                    continue;
                outputCount = AppendTree(rowIndex, outputCount, initialDepth: 1);
            }
        }

        Array.Copy(_treeOrderBuffer, _visibleRowIndexes, outputCount);
        _visibleRowCount = outputCount;
    }

    private bool IsSemanticClassification(
        int rowIndex,
        SemanticProcessGroupClassification classification) =>
        _semanticRowClassifications[rowIndex] == checked((byte)((int)classification + 1));

    private bool IsSemanticSectionRow(int rowIndex) =>
        (uint)rowIndex < (uint)_semanticSectionRowKinds.Length
        && _semanticSectionRowKinds[rowIndex] != SemanticProcessSectionRowKind.None;

    private int AppendTree(int rootRowIndex, int outputCount, byte initialDepth)
    {
        int stackCount = 1;
        _treeStackRows[0] = rootRowIndex;
        _treeStackDepths[0] = initialDepth;
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
            bool hideChildren = hidden
                                || (_filterQuery.IsEmpty
                                    && _collapsedProcesses.Contains(row.InstanceKey));
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
        Array.Clear(_rowDepths, index: 0, _rowCount);
        Array.Clear(_rowHasChildren, index: 0, _rowCount);
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
                if (!ReferenceEquals(cache.StaticData, row))
                {
                    ReleaseRenderCache(cache);
                    cache.StaticData = row;
                }
                cache.LastSeenGeneration = generation;
                continue;
            }

            _renderCaches.Add(
                row.InstanceKey,
                new ProcessRowRenderCache
                {
                    LastSeenGeneration = generation,
                    StaticData = row
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
        ProcessRowDrawing? staticDrawing = cache.StaticDrawing;
        ProcessRowDrawing? dynamicDrawing = cache.DynamicDrawing;
        cache.StaticDrawing = null;
        cache.DynamicDrawing = null;
        cache.StaticMetricsGeneration = 0;
        cache.DynamicMetricsGeneration = 0;
        ReleaseRowDrawing(staticDrawing);
        ReleaseRowDrawing(dynamicDrawing);
    }

    private void EnsureSelectedProcessesStillExist()
    {
        if (_selectedProcesses.Count == 0) return;

        _staleProcessKeys.Clear();
        foreach (ProcessInstanceKey process in _selectedProcesses)
        {
            if (!_renderCaches.ContainsKey(process)) _staleProcessKeys.Add(process);
        }

        if (_staleProcessKeys.Count == 0) return;
        for (int staleIndex = 0; staleIndex < _staleProcessKeys.Count; staleIndex++)
            _selectedProcesses.Remove(_staleProcessKeys[staleIndex]);

        if (_selectedProcess.HasValue && !_selectedProcesses.Contains(_selectedProcess.Value))
            _selectedProcess = FindFirstSelectedProcess();
        if (_selectionAnchorProcess.HasValue
            && !_renderCaches.ContainsKey(_selectionAnchorProcess.Value))
            _selectionAnchorProcess = _selectedProcess;
        if (_selectedProcesses.Count == 0)
        {
            _selectedProcess = null;
            _selectionAnchorProcess = null;
        }

        NotifySelectionChanged();
    }

    private void EnsureRowCapacity(int count)
    {
        if (_visibleRowIndexes.Length >= count
            && _treeOrderBuffer.Length >= count
            && _treeParentIndexes.Length >= count
            && _filterIncludedRows.Length >= count)
            return;

        int capacity = Math.Max(val1: 256, _visibleRowIndexes.Length);
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
        Array.Resize(ref _filterIncludedRows, capacity);
        Array.Resize(ref _rowDepths, capacity);
        Array.Resize(ref _rowHasChildren, capacity);
        Array.Resize(ref _semanticSectionRowKinds, capacity);
        Array.Resize(ref _semanticRowClassifications, capacity);
    }

    private void EnsureWarmCapacity(int count)
    {
        if (_warmProcessIDs.Length >= count) return;

        int capacity = Math.Max(val1: 256, _warmProcessIDs.Length);
        while (capacity < count)
            capacity = checked(capacity * 2);
        Array.Resize(ref _warmProcessIDs, capacity);
    }

    private static int FindColumn(ProcessTableColumn[] columns, ProcessTableColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            if (columns[columnIndex].Kind == kind)
                return columnIndex;
        }

        return -1;
    }

    private int GetTreeLayoutKey(int rowIndex)
    {
        if (_processGroupingStyle == ProcessGroupingStyle.None
            || (uint)rowIndex >= (uint)_rowDepths.Length)
            return 0;

        int treeLayoutKey = _rowDepths[rowIndex] * 2 + (_rowHasChildren[rowIndex] ? 1 : 0);
        if (!_usesSemanticSections) return treeLayoutKey;

        treeLayoutKey |= SemanticSectionLayoutFlag;
        if (_semanticSectionRowKinds[rowIndex] == SemanticProcessSectionRowKind.Header)
            treeLayoutKey |= SemanticSectionHeaderLayoutFlag;
        return treeLayoutKey;
    }

    private double GetHierarchyInset(int treeLayoutKey)
    {
        int depth = (treeLayoutKey & TreeLayoutValueMask) >> 1;
        if ((treeLayoutKey & SemanticSectionLayoutFlag) == 0 || depth == 0)
            return depth * _visualMetrics.TreeIndentWidth;

        return _visualMetrics.SemanticSectionChildIndent
               + (depth - 1) * _visualMetrics.TreeIndentWidth;
    }

    private static bool HasTreeExpanderSlot(int treeLayoutKey) => (treeLayoutKey & 1) != 0;

    private static bool IsSemanticSectionHeaderLayout(int treeLayoutKey) =>
        (treeLayoutKey & SemanticSectionHeaderLayoutFlag) != 0;

    private double CalculateRowHeight(double fontSize, double rowSpacing) =>
        ProcessTableLayout.CalculateRowHeight(
            ProcessTableLayout.CalculateRowTextHeight(fontSize, _rowTextHeightScale),
            rowSpacing);

    private static ProcessTableMetrics CreateTableMetrics(
        TaskManagerWindowResources resources,
        double fontSize,
        double rowHeight,
        double rowTextHeightScale)
    {
        double rowTextHeight = ProcessTableLayout.CalculateRowTextHeight(
            fontSize,
            rowTextHeightScale);
        double effectiveRowHeight = Math.Max(rowHeight, rowTextHeight);
        double baseRowHeight = ProcessTableLayout.CalculateRowHeight(
            ProcessTableLayout.CalculateRowTextHeight(
                resources.AxamlProcessTable.FontSize,
                rowTextHeightScale),
            resources.AxamlProcessTable.RowSpacing);
        double processIconSize = ProcessTableLayout.ScaleProcessIconSize(
            resources.AxamlProcessTable.ProcessIconSize,
            resources.AxamlProcessTable.FontSize,
            baseRowHeight,
            fontSize,
            effectiveRowHeight);
        return new ProcessTableMetrics(
            resources.AxamlProcessTable.HeaderHeight,
            effectiveRowHeight,
            rowTextHeight,
            resources.AxamlProcessTable.CellPadding,
            fontSize,
            resources.AxamlProcessTable.HeaderFontSize,
            processIconSize,
            resources.AxamlProcessTable.ProcessIconGap);
    }

    private int CalculateTableFontWeight(double fontSize) =>
        ProcessTableLayout.CalculateZoomFontWeight(
            _baseTableFontWeight,
#if DEBUG
            ResolveReferenceTableFontSize(),
#else
            AppSettings.GridFontSizeDefault,
#endif
            fontSize);

#if DEBUG
    private double ResolveReferenceTableFontSize()
    {
        double referenceFontSize = _resources.AxamlProcessTable.FontSize;
        return double.IsFinite(referenceFontSize) && referenceFontSize > 0
            ? referenceFontSize
            : AppSettings.GridFontSizeDefault;
    }
#endif

    private static Typeface CreateTableTypeface(int fontWeight) =>
        new(
            DefaultTableTypeface.FontFamily,
            FontStyle.Normal,
            (FontWeight)fontWeight);

    private static LiveTotalTypography CreateLiveTotalTypography(
        TaskManagerWindowResources resources) =>
        new(
            resources.AxamlProcessTable.LiveTotalFontSize,
            resources.AxamlProcessTable.LiveTotalFontWeight,
            Math.Clamp(
                resources.AxamlProcessTable.LiveTotalHorizontalScale,
                min: 0.25,
                max: 1),
            Math.Max(val1: 0, resources.AxamlProcessTable.LiveTotalTextGap));

    private static Typeface CreateLiveTotalTypeface(LiveTotalTypography typography) =>
        new(
            DefaultTableTypeface.FontFamily,
            FontStyle.Normal,
            (FontWeight)typography.FontWeight);

    private static double MeasureRowTextHeightScale(Typeface typeface)
    {
        using TextLayout measurement = new(
            RowTextMeasurementText,
            typeface,
            AppSettings.GridFontSizeDefault,
            Brushes.White,
            textWrapping: TextWrapping.NoWrap,
            maxLines: 1);
        return measurement.Height / AppSettings.GridFontSizeDefault;
    }

    private static ProcessTableVisualMetrics CreateVisualMetrics(
        TaskManagerWindowResources resources) =>
        new(
            resources.AxamlProcessTable.DefaultViewportHeight,
            resources.AxamlProcessTable.GridLineThickness,
            resources.AxamlProcessTable.ColumnResizeHitRadius,
            resources.AxamlProcessTable.HeaderDragThreshold,
            resources.AxamlProcessTable.ColumnInteractionLineThickness,
            resources.AxamlProcessTable.TextUnderlineThickness,
            resources.AxamlProcessTable.SortCaretFontSize,
            resources.AxamlProcessTable.SortCaretRightMargin,
            resources.AxamlProcessTable.ProcessIconCornerRadius,
            resources.AxamlProcessTable.TreeIndentWidth,
            resources.AxamlProcessTable.SemanticSectionChildIndent,
            Math.Max(
                val1: 0,
                resources.AxamlProcessTable.SemanticSectionHeaderSizeOffset),
            Math.Max(
                val1: 0,
                resources.AxamlProcessTable.SemanticSectionCaretSizeOffset),
            Math.Max(
                val1: 0,
                resources.AxamlProcessTable.SemanticSectionHeaderUpwardShift),
            Math.Max(
                val1: 0,
                resources.AxamlProcessTable.SemanticSectionCaretUpwardShift),
            resources.AxamlProcessTable.SemanticSectionHeaderTextGap,
            resources.AxamlProcessTable.TreeExpanderWidth,
            resources.AxamlProcessTable.TreeExpanderChevronHalfWidth,
            resources.AxamlProcessTable.TreeExpanderChevronHalfHeight,
            resources.AxamlProcessTable.TreeExpanderLineThickness);

#if DEBUG
    private static ProcessTableAXAMLColumnWidths CreateAXAMLColumnWidths(
        TaskManagerWindowResources resources) =>
        new(
            resources.AxamlProcessTable.NameColumnWidth,
            resources.AxamlProcessTable.PIDColumnWidth,
            resources.AxamlProcessTable.StatusColumnWidth,
            resources.AxamlProcessTable.UserNameColumnWidth,
            resources.AxamlProcessTable.CPUColumnWidth,
            resources.AxamlProcessTable.LifetimeColumnWidth,
            resources.AxamlProcessTable.PrivateMemoryColumnWidth,
            resources.AxamlProcessTable.WorkingSetColumnWidth,
            resources.AxamlProcessTable.CommandLineColumnWidth);
#endif

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

    private HeaderTextLayouts[] CreateHeaderTexts(ProcessTableColumn[] columns)
    {
        List<HeaderTextLayouts> headerTexts = new(columns.Length);
        try
        {
            for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                headerTexts.Add(CreateHeaderTextLayouts(columns[columnIndex]));
            return headerTexts.ToArray();
        }
        catch
        {
            for (int headerIndex = 0; headerIndex < headerTexts.Count; headerIndex++)
                headerTexts[headerIndex].Dispose();
            throw;
        }
    }

    private HeaderTextLayouts CreateHeaderTextLayouts(ProcessTableColumn column)
    {
        double normalWidth = Math.Max(val1: 0, column.Width - _metrics.CellPadding * 2);
        double ascendingSortWidth = Math.Max(
            val1: 0,
            normalWidth - _sortCaretRightMargin - _ascendingCaretText.Width);
        double descendingSortWidth = Math.Max(
            val1: 0,
            normalWidth - _sortCaretRightMargin - _descendingCaretText.Width);
        List<HeaderContentLayout> layouts = new(3);
        try
        {
            layouts.Add(CreateHeaderText(column, normalWidth));
            layouts.Add(CreateHeaderText(column, ascendingSortWidth));
            layouts.Add(CreateHeaderText(column, descendingSortWidth));
            return new HeaderTextLayouts(layouts[0], layouts[1], layouts[2]);
        }
        catch
        {
            for (int layoutIndex = 0; layoutIndex < layouts.Count; layoutIndex++)
                layouts[layoutIndex].Dispose();
            throw;
        }
    }

    private void ReplaceHeaderTexts(ProcessTableColumn[] columns)
    {
        HeaderTextLayouts[] nextHeaderTexts = CreateHeaderTexts(columns);
        HeaderTextLayouts[] previousHeaderTexts = _headerTexts;
        _headerTexts = nextHeaderTexts;
        DisposeHeaderTexts(previousHeaderTexts);
    }

#if DEBUG
    private void RecreateSortCaretTexts()
    {
        (TextLayout nextAscendingCaretText, TextLayout nextDescendingCaretText) =
            CreateSortCaretTexts(
                _visualMetrics.SortCaretFontSize,
                _secondaryForegroundBrush);

        TextLayout previousAscendingCaretText = _ascendingCaretText;
        TextLayout previousDescendingCaretText = _descendingCaretText;
        _ascendingCaretText = nextAscendingCaretText;
        _descendingCaretText = nextDescendingCaretText;
        previousAscendingCaretText.Dispose();
        previousDescendingCaretText.Dispose();
    }
#endif

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
            || value == UnavailableText)
            return true;
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

    private HeaderContentLayout CreateHeaderText(
        ProcessTableColumn column,
        double maximumWidth)
    {
        string liveTotal = _liveTotalTextsByColumn[(int)column.Kind] ?? string.Empty;
        if (liveTotal.Length == 0)
        {
            bool rightAligned = column.Alignment == ProcessTableColumnAlignment.Right;
            TextLayout title = CreateHeaderTitleText(
                column.Title,
                rightAligned ? maximumWidth : double.PositiveInfinity,
                rightAligned ? TextAlignment.Right : TextAlignment.Left,
                trim: rightAligned);
            return new HeaderContentLayout(title);
        }

        double availableWidth = Math.Max(val1: 0, maximumWidth);
        double horizontalScale = _liveTotalTypography.HorizontalScale;
        TextLayout? totalLayout = CreateLiveTotalText(liveTotal, double.PositiveInfinity);
        TextLayout? titleLayout = null;
        try
        {
            double totalVisualWidth = totalLayout.Width * horizontalScale;
            if (totalVisualWidth >= availableWidth)
            {
                totalLayout.Dispose();
                totalLayout = null;
                totalLayout = CreateLiveTotalText(liveTotal, availableWidth / horizontalScale);
                totalVisualWidth = Math.Min(availableWidth, totalLayout.Width * horizontalScale);
                double totalLeft = column.Alignment == ProcessTableColumnAlignment.Right
                    ? availableWidth - totalVisualWidth
                    : 0;
                return new HeaderContentLayout(
                    title: null,
                    totalLayout,
                    horizontalScale,
                    totalLeft,
                    titleLeft: 0);
            }

            double remainingWidth = availableWidth - totalVisualWidth;
            double textGap = remainingWidth > _liveTotalTypography.TextGap
                ? _liveTotalTypography.TextGap
                : 0;
            double titleAvailableWidth = Math.Max(val1: 0, remainingWidth - textGap);
            if (titleAvailableWidth <= 0)
            {
                double totalLeft = column.Alignment == ProcessTableColumnAlignment.Right
                    ? availableWidth - totalVisualWidth
                    : 0;
                return new HeaderContentLayout(
                    title: null,
                    totalLayout,
                    horizontalScale,
                    totalLeft,
                    titleLeft: 0);
            }

            titleLayout = CreateHeaderTitleText(
                column.Title,
                double.PositiveInfinity,
                TextAlignment.Left,
                trim: false);
            double titleWidth = Math.Min(titleLayout.Width, titleAvailableWidth);
            if (titleLayout.Width - titleWidth >= 0.01)
            {
                titleLayout.Dispose();
                titleLayout = null;
                titleLayout = CreateHeaderTitleText(
                    column.Title,
                    titleWidth,
                    TextAlignment.Left,
                    trim: true);
            }

            double contentWidth = totalVisualWidth + textGap + titleWidth;
            double contentLeft = column.Alignment == ProcessTableColumnAlignment.Right
                ? availableWidth - contentWidth
                : 0;
            return new HeaderContentLayout(
                titleLayout,
                totalLayout,
                horizontalScale,
                totalLeft: contentLeft,
                titleLeft: contentLeft + totalVisualWidth + textGap);
        }
        catch
        {
            titleLayout?.Dispose();
            totalLayout?.Dispose();
            throw;
        }
    }

    private TextLayout CreateHeaderTitleText(
        string text,
        double maximumWidth,
        TextAlignment alignment,
        bool trim) =>
        new(
            LimitTextLayoutInput(text),
            _tableTypeface,
            _metrics.HeaderFontSize,
            _foregroundBrush,
            alignment,
            TextWrapping.NoWrap,
            trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            maxWidth: double.IsPositiveInfinity(maximumWidth)
                ? maximumWidth
                : Math.Max(val1: 0, maximumWidth),
            maxLines: 1);

    private TextLayout CreateLiveTotalText(string text, double maximumWidth) =>
        new(
            LimitTextLayoutInput(text),
            _liveTotalTypeface,
            _liveTotalTypography.FontSize,
            _foregroundBrush,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            double.IsPositiveInfinity(maximumWidth)
                ? TextTrimming.None
                : TextTrimming.CharacterEllipsis,
            maxWidth: maximumWidth,
            maxLines: 1);

    private TextLayout CreateBoundedText(
        string value,
        double maximumWidth,
        double fontSize) =>
        new(
            LimitTextLayoutInput(value),
            _tableTypeface,
            fontSize,
            _foregroundBrush,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: Math.Max(val1: 0, maximumWidth),
            maxLines: 1);

    private static string LimitTextLayoutInput(string value)
    {
        if (value.Length <= MaximumTextLayoutCharacters) return value;

        int prefixLength = MaximumTextLayoutCharacters;
        if (char.IsHighSurrogate(value[prefixLength - 1])
            && char.IsLowSurrogate(value[prefixLength]))
            prefixLength--;

        return string.Concat(
            value.AsSpan(start: 0, prefixLength),
            TextEllipsis.AsSpan());
    }

    // AXAML hot-reload exception: Painted sort TextLayouts cannot apply optional Glyph scale or
    // translation metadata without replacing their drawing geometry and hit placement
    private static TextLayout CreateGlyphText(Glyph glyph, double fontSize, IBrush brush) =>
        new(
            glyph.Text,
            new Typeface(
                TADNFontResolver.ResolveFontFamily(glyph.Font),
                FontStyle.Normal,
                glyph.FontWeight ?? FontWeight.Normal),
            fontSize,
            brush,
            textWrapping: TextWrapping.NoWrap,
            maxLines: 1);

    private static (TextLayout ascending, TextLayout descending) CreateSortCaretTexts(
        double fontSize,
        IBrush brush)
    {
        TextLayout ascending = CreateGlyphText(TaskManagerGlyphCatalog.SORT_ASCENDING, fontSize, brush);
        try
        {
            TextLayout descending = CreateGlyphText(TaskManagerGlyphCatalog.SORT_DESCENDING, fontSize, brush);
            return (ascending, descending);
        }
        catch
        {
            ascending.Dispose();
            throw;
        }
    }

    private static void DisposeHeaderTexts(ReadOnlySpan<HeaderTextLayouts> headerTexts)
    {
        for (int headerIndex = 0; headerIndex < headerTexts.Length; headerIndex++)
            headerTexts[headerIndex].Dispose();
    }

    private static void DisposeCellTextLayouts(ReadOnlySpan<CellTextLayout> cellTextLayouts)
    {
        for (int layoutIndex = 0; layoutIndex < cellTextLayouts.Length; layoutIndex++)
            cellTextLayouts[layoutIndex].Dispose();
    }

    protected override void DisposeDetailsGridResources()
    {
        if (_capturedHeaderPointer != null) ResetHeaderInteraction();
        _processIconService.IconsChanged -= OnIconsChanged;
#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphResourcesReloaded;
#endif
        _externalSubscriptionsAttached = false;
        SelectedProcessChanged = null;
        RowHoverGeometryChanged = null;
        ViewportAnchorAdjustmentRequested = null;
        ColumnPropertiesRequested = null;
        ColumnLayoutChanged = null;
        EndTaskRequested = null;
        RowContextMenuRequested = null;
        _pendingColumnLayout = null;
        _pendingViewportAnchor = null;
        foreach (ProcessRowRenderCache cache in _renderCaches.Values)
            ReleaseRenderCache(cache);
        _renderCaches.Clear();
        foreach (SharedCellLayout sharedCell in _sharedCellLayouts.Values)
            sharedCell.Dispose();
        _sharedCellLayouts.Clear();
        _sharedCellBuffer.Clear();
        _cellTextLayoutBuffer.Clear();
        _staleProcessKeys.Clear();
        _collapsedProcesses.Clear();
        _selectedProcesses.Clear();
        _rowIndexByProcessID.Clear();
        _sourceRowIndexByInstance.Clear();
        _rowIndexByInstance.Clear();
        _syntheticKeyByGroup.Clear();
        _membersBySyntheticKey.Clear();
        _semanticParentByInstance.Clear();
        _semanticClassificationByInstance.Clear();
        _warmProcessKeySet.Clear();
        _liveSemanticGroupKeys.Clear();
        _staleSemanticGroupKeys.Clear();
        _contextCopyRows = [];
        DisposeHeaderTexts(_headerTexts);
        _headerTexts = [];
        _ascendingCaretText.Dispose();
        _descendingCaretText.Dispose();
        _headerHoverLayer.Dispose();
        _sourceSnapshot.Reset();
        _snapshot.Reset();
    }

    private sealed record PendingColumnLayout(
        List<ProcessColumnSetting> Settings,
        ProcessTableColumn[] Columns,
        ProcessSearchQuery FilterQuery,
        ProcessDataSchema Schema);

    private sealed class ProcessTableRenderLayer : Control
    {
        private readonly ProcessDetailsCanvas _owner;
        private readonly RenderLayerKind _layerKind;

        public ProcessTableRenderLayer(ProcessDetailsCanvas owner, RenderLayerKind layerKind)
        {
            _owner = owner;
            _layerKind = layerKind;
            ClipToBounds = true;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            _owner.RenderLayer(context, _layerKind);
        }
    }

    private readonly record struct CellTextLayout(
        TextLayout Text,
        double Left,
        double Top,
        double AvailableWidth) : IDisposable
    {
        public void Draw(DrawingContext context) => Text.Draw(context, new Point(Left, Top));

        public void Dispose() => Text.Dispose();
    }

    private sealed class HeaderContentLayout : IDisposable
    {
        private readonly TextLayout? _title;
        private readonly TextLayout? _total;
        private readonly double _horizontalScale;
        private readonly double _totalLeft;
        private readonly double _titleLeft;
        private readonly double _baseline;
        private readonly double _height;
        private bool _disposed;

        public HeaderContentLayout(TextLayout title)
            : this(title, total: null, horizontalScale: 1, totalLeft: 0, titleLeft: 0)
        {
        }

        public HeaderContentLayout(
            TextLayout? title,
            TextLayout? total,
            double horizontalScale,
            double totalLeft,
            double titleLeft)
        {
            if (title == null && total == null)
                throw new ArgumentException("A header content layout needs text.");

            _title = title;
            _total = total;
            _horizontalScale = horizontalScale;
            _totalLeft = totalLeft;
            _titleLeft = titleLeft;

            double titleBaseline = title?.Baseline ?? 0;
            double totalBaseline = total?.Baseline ?? 0;
            _baseline = Math.Max(titleBaseline, totalBaseline);
            double titleBelowBaseline = title == null ? 0 : title.Height - titleBaseline;
            double totalBelowBaseline = total == null ? 0 : total.Height - totalBaseline;
            _height = _baseline + Math.Max(titleBelowBaseline, totalBelowBaseline);
        }

        public void Draw(DrawingContext context, double left, double top, double availableHeight)
        {
            double contentTop = top + Math.Max(val1: 0, (availableHeight - _height) / 2);
            if (_total != null)
            {
                double totalTop = contentTop + _baseline - _total.Baseline;
                using (context.PushTransform(Matrix.CreateScale(_horizontalScale, 1)))
                {
                    _total.Draw(
                        context,
                        new Point((left + _totalLeft) / _horizontalScale, totalTop));
                }
            }

            if (_title != null)
            {
                double titleTop = contentTop + _baseline - _title.Baseline;
                _title.Draw(context, new Point(left + _titleLeft, titleTop));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _title?.Dispose();
            _total?.Dispose();
        }
    }

    private sealed class HeaderTextLayouts(
        HeaderContentLayout normal,
        HeaderContentLayout ascendingSort,
        HeaderContentLayout descendingSort) : IDisposable
    {
        private bool _disposed;

        public HeaderContentLayout Normal { get; } = normal;
        private HeaderContentLayout AscendingSort { get; } = ascendingSort;
        private HeaderContentLayout DescendingSort { get; } = descendingSort;

        public HeaderContentLayout Get(bool isSorted, bool useDescendingCaret)
        {
            if (!isSorted) return Normal;
            return useDescendingCaret ? DescendingSort : AscendingSort;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Normal.Dispose();
            AscendingSort.Dispose();
            DescendingSort.Dispose();
        }
    }

    private readonly record struct LiveTotalTypography(
        double FontSize,
        int FontWeight,
        double HorizontalScale,
        double TextGap);

    private readonly record struct ContextCopyRow(
        ProcessInstanceKey Process,
        string?[] ValuesByColumn);

    private readonly record struct TextUnderlineSegment(double Left, double Right, double Y);

    private readonly record struct ProcessTableVisualMetrics(
        double DefaultViewportHeight,
        double GridLineThickness,
        double ColumnResizeHitRadius,
        double HeaderDragThreshold,
        double ColumnInteractionLineThickness,
        double TextUnderlineThickness,
        double SortCaretFontSize,
        double SortCaretRightMargin,
        double ProcessIconCornerRadius,
        double TreeIndentWidth,
        double SemanticSectionChildIndent,
        double SemanticSectionHeaderSizeOffset,
        double SemanticSectionCaretSizeOffset,
        double SemanticSectionHeaderUpwardShift,
        double SemanticSectionCaretUpwardShift,
        double SemanticSectionHeaderTextGap,
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
        int MetricsGeneration,
        ProcessTableColumnKind Column,
        string Value,
        int TreeLayoutKey);

    private sealed class ProcessRowRenderCache
    {
        public ProcessStaticData? StaticData;
        public int LastSeenGeneration;
        public int DynamicFingerprint;
        public int PendingDynamicFingerprint;
        public int StaticMetricsGeneration;
        public int DynamicMetricsGeneration;
        public int StaticTreeLayoutKey;
        public bool IsDrawingRetained;
        public ProcessRowDrawing? StaticDrawing;
        public ProcessRowDrawing? DynamicDrawing;
    }

    private sealed class ProcessRowDrawing(
        SharedCellLayout[] sharedCells,
        CellTextLayout[] cellTextLayouts,
        double rowHeight)
    {
        public SharedCellLayout[] SharedCells { get; } = sharedCells;
        public CellTextLayout[] CellTextLayouts { get; } = cellTextLayouts;
        public double RowHeight { get; } = rowHeight;

        public void Draw(DrawingContext context)
        {
            // Draw into Avalonia's render context so it owns immutable glyph references
            // Intermediate DrawingGroups retain GlyphRun objects across TextLayout disposal
            for (int layoutIndex = 0; layoutIndex < CellTextLayouts.Length; layoutIndex++)
                CellTextLayouts[layoutIndex].Draw(context);
            for (int sharedCellIndex = 0; sharedCellIndex < SharedCells.Length; sharedCellIndex++)
                SharedCells[sharedCellIndex].Draw(context);
        }
    }

    private sealed class SharedCellLayout(
        ProcessSharedCellKey key,
        CellTextLayout cellTextLayout) : IDisposable
    {
        private bool _disposed;

        public ProcessSharedCellKey Key { get; } = key;
        public int ReferenceCount { get; set; } = 1;

        public void Draw(DrawingContext context) => cellTextLayout.Draw(context);

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            cellTextLayout.Dispose();
        }
    }

    private sealed class ProcessRowIndexComparer(
        ProcessSnapshotBuffer snapshot,
        ProcessDataSchema schema) : IComparer<int>
    {
        private ProcessDataSchema _schema = schema;

        public ProcessTableColumnKind Column { get; set; }
        public bool IsDescending { get; set; }
        public bool ShowUserNamePrefix { get; set; }

        public void SetSchema(ProcessDataSchema nextSchema)
        {
            ArgumentNullException.ThrowIfNull(nextSchema);
            _schema = nextSchema;
        }

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
                    int slot = _schema.GetStaticTextSlot(column);
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
                int slot = _schema.GetStaticNumericSlot(column);
                leftValue = left.NumericValues[slot];
                rightValue = right.NumericValues[slot];
            }
            else
            {
                leftValue = snapshot.GetDynamicNumeric(leftIndex, column);
                rightValue = snapshot.GetDynamicNumeric(rightIndex, column);
            }

            if (IsDoubleColumn(column))
            {
                return ProcessTableValuePresentation.CompareNonnegativeDouble(
                    BitConverter.Int64BitsToDouble(leftValue),
                    BitConverter.Int64BitsToDouble(rightValue));
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

        private static bool IsDoubleColumn(ProcessTableColumnKind column) => column switch
        {
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.GPU
                or ProcessTableColumnKind.NPU
                or ProcessTableColumnKind.CPUUtility
                or ProcessTableColumnKind.Disk
                or ProcessTableColumnKind.Network => true,
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

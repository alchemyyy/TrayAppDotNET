using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Builds the Processes toolbar around the allocation-light painted process table.</summary>
internal sealed class ProcessDetailsPage : TaskManagerPageLayout, ITaskManagerSearchOverlayPage, IDisposable
{
    private const double GridFontZoomStep = 0.5;
    private const double GridRowSpacingStep = 1;

    private readonly ProcessSnapshotService _snapshotService;
    private readonly Action<ProcessTerminationTarget?> _armTerminationTarget;
    private readonly TryTerminateProcessAction _terminateProcess;
    private readonly Func<ElevatedHelperStatus> _getElevatedHelperStatus;
    private readonly Action _requestElevatedTermination;
    private readonly Func<ProcessEndTaskRequest, Task<bool>> _confirmEndTask;
    private readonly Func<Task<bool>> _confirmRestartExplorer;
    private readonly Func<Task<ExplorerRestartResult>> _restartExplorer;
    private readonly Action<string, string> _reportMessage;
    private readonly Func<string, bool> _startProcess;
    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly ProcessDetailsCanvas _processCanvas;
    private readonly ProcessRowContextMenuController _rowContextMenuController;
    private readonly TextBox _searchBox;
    private readonly Grid _searchControls;
    private readonly TaskManagerSearchOverlay _searchOverlay;
    private readonly ProcessSearchAutocompleteController _searchAutocomplete;
    private readonly ProcessSavedSearchController _savedSearches;
    private readonly TranslateTransform _searchControlsTransform = new();
    private readonly TextBox _runInput;
    private readonly Border _runPanel;
    private readonly SettingsButton _runTaskButton;
    private readonly SettingsButton _restartExplorerButton;
    private readonly SettingsButton _columnsButton;
    private readonly SettingsButton _endTaskButton;
    private readonly SettingsButton _moreActionsButton;
    private readonly SettingsButton _submitRunButton;
    private readonly SettingsButton _cancelRunButton;
    private readonly SettingsToggle _groupProcessesToggle;
    private readonly StackPanel _groupProcessesHeaderControl;
    private readonly SettingsScrollViewport _tableScrollViewport;
    private readonly TaskManagerResizeGrip _resizeGrip;
    private readonly Border _columnHeaderBorder;
    private readonly ProcessRowHoverVisual _hoverHighlight;
    private readonly Dictionary<ProcessTableColumnKind, ProcessColumnPropertiesWindow> _columnPropertyWindows = [];
    private ProcessColumnChooserWindow? _columnChooserWindow;
    private ProcessHeaderButtonArrangementWindow? _headerButtonArrangementWindow;
    private TaskManagerContextMenuWindow? _headerActionsMenuWindow;
    private bool _isEndTaskConfirmationPending;
    private bool _isRestartExplorerPending;
    private bool _isPageActive;
    private bool _disposed;

    public ProcessDetailsPage(
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<ProcessTerminationTarget?> armTerminationTarget,
        TryTerminateProcessAction terminateProcess,
        Func<ElevatedHelperStatus> getElevatedHelperStatus,
        Action requestElevatedTermination,
        Func<ProcessEndTaskRequest, Task<bool>> confirmEndTask,
        Func<Task<bool>> confirmRestartExplorer,
        Func<ProcessSavedSearch, Task<bool>> confirmDeleteSavedSearch,
        Func<Task<ExplorerRestartResult>> restartExplorer,
        Action<string, string> reportMessage,
        Func<string, bool> startProcess)
        : base(title: "Processes", palette, resources)
    {
        _snapshotService = snapshotService;
        _settings = settings;
        _palette = palette;
        _resources = resources;
        _armTerminationTarget = armTerminationTarget;
        _terminateProcess = terminateProcess;
        _getElevatedHelperStatus = getElevatedHelperStatus;
        _requestElevatedTermination = requestElevatedTermination;
        _confirmEndTask = confirmEndTask;
        _confirmRestartExplorer = confirmRestartExplorer;
        _restartExplorer = restartExplorer;
        _reportMessage = reportMessage;
        _startProcess = startProcess;
        ProcessDataSchema schema = ProcessDataSchema.Create(
            settings.DetailsColumns,
            ProcessTableColumnKind.Name);
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _processCanvas = new ProcessDetailsCanvas(
            processIconService,
            schema,
            settings.DetailsColumns,
            settings.EnableLiveDetailsColumnResizing,
            settings.ProcessTreeDefaultState,
            settings.ExpandSemanticSectionsByDefault,
            settings.GridFontSize,
            settings.GridFontWeight,
            settings.GridRowSpacing,
            palette,
            resources);
        _processCanvas.SelectedProcessChanged += OnSelectedProcessChanged;
        _processCanvas.RowHoverGeometryChanged += OnRowHoverGeometryChanged;
        _processCanvas.ViewportAnchorAdjustmentRequested += OnViewportAnchorAdjustmentRequested;
        _processCanvas.ColumnPropertiesRequested += OnColumnPropertiesRequested;
        _processCanvas.ColumnLayoutChanged += OnColumnLayoutChanged;
        _processCanvas.GridZoomRequested += OnGridZoomRequested;
        _processCanvas.GridZoomResetRequested += OnGridZoomResetRequested;
        _processCanvas.GridRowSpacingRequested += OnGridRowSpacingRequested;
        _processCanvas.GridRowSpacingResetRequested += OnGridRowSpacingResetRequested;
        _processCanvas.EndTaskRequested += RequestEndTask;
        _processCanvas.RowContextMenuRequested += OnRowContextMenuRequested;

        _rowContextMenuController = new ProcessRowContextMenuController(
            palette,
            settings.EnableRoundedCorners,
            settings,
            terminateProcess,
            RequestEndTask,
            _snapshotService.RequestRefresh,
            reportMessage,
            _processCanvas.SetContextCopyPreview,
            reportMessage);

        _runTaskButton = TrayAppDotNETSettingsUI.Button(text: "Run new task", palette);
        _runTaskButton.Click += OnRunTaskClick;
        _restartExplorerButton = TrayAppDotNETSettingsUI.Button(text: "Restart explorer", palette);
        _restartExplorerButton.Click += OnRestartExplorerClick;
        _columnsButton = TrayAppDotNETSettingsUI.Button(text: "Columns..", palette);
        _columnsButton.Click += OnColumnsClick;
        _endTaskButton = TrayAppDotNETSettingsUI.Button(text: "End task", palette);
        _endTaskButton.IsEnabled = false;
        _endTaskButton.Click += OnEndTaskClick;
        _groupProcessesToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            settings.GroupProcesses,
            OnGroupProcessesChanged);
        _moreActionsButton = TrayAppDotNETSettingsUI.Button(TaskManagerGlyphCatalog.MORE, palette);
        _moreActionsButton.Width = resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        _moreActionsButton.Height = resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        _moreActionsButton.MinHeight = resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        _moreActionsButton.Padding = resources.AxamlTaskManagerReorderDialog.MoreButtonPadding;
        _moreActionsButton.Label.FontSize = resources.AxamlTaskManagerReorderDialog.MoreGlyphFontSize;
        _moreActionsButton.Click += OnMoreActionsClick;
        TrayAppDotNETToolTip.SetTip(_moreActionsButton, tip: "More");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_moreActionsButton);
        _groupProcessesHeaderControl = BuildGroupProcessesHeaderControl(palette, resources);
        PopulateHeaderActions();

        _searchBox = TrayAppDotNETSettingsUI.SearchTextBox(
            palette,
            resources.AxamlTaskManagerDetails.SearchWidth);
        _searchBox.PlaceholderText = "Search by name, PID, or enter an expression";
        _searchBox.VerticalAlignment = VerticalAlignment.Top;
        _searchBox.TextChanged += OnSearchTextChanged;
        TrayAppDotNETToolTip.SetTip(
            _searchBox,
            "Name/PID contains search is the default. Expressions support =, !=, <, <=, >, >=, &&, and ||.\n"
            + "Regex uses =~ or !~. Example: {Name}=~\"^(chrome|firefox)\\.exe$\".\n"
            + "Lifetime example: {Lifetime}>=1h&&{Lifetime}<2h. Type { and press Tab to complete a column.");
        _searchAutocomplete = new ProcessSearchAutocompleteController(
            _searchBox,
            settings.DetailsColumns,
            palette,
            settings.EnableRoundedCorners);
        _savedSearches = new ProcessSavedSearchController(
            _searchBox,
            settings.ProcessSavedSearches,
            palette,
            resources,
            settings.EnableRoundedCorners,
            settings,
            settings.UpdateProcessSavedSearches,
            confirmDeleteSavedSearch);
        _savedSearches.ClearButton.Margin = default;
        _savedSearches.SaveButton.Margin = new Thickness(
            left: 0,
            top: 0,
            resources.AxamlTaskManagerDetails.SearchActionSpacing,
            bottom: 0);
        _searchControls = new Grid
        {
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _searchControlsTransform,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _searchControls.Children.Add(_savedSearches.ClearButton);
        SetColumn(_savedSearches.SaveButton, value: 1);
        _searchControls.Children.Add(_savedSearches.SaveButton);
        SetColumn(_searchBox, value: 2);
        _searchControls.Children.Add(_searchBox);
        UpdateSearchControlsPosition();
        _searchOverlay = new TaskManagerSearchOverlay(
            _searchControls,
            _searchBox,
            settings.LeftAlignProcessSearchBar,
            resources.AxamlTaskManagerDetails.SearchMargin,
            resources.AxamlTaskManagerDetails.SearchCaptionSpacing);
        _searchOverlay.AddOverlay(_searchAutocomplete.Popup);

        _runInput = TrayAppDotNETSettingsUI.TextBox(
            palette,
            resources.AxamlTaskManagerDetails.RunInputWidth);
        _runInput.Width = double.NaN;
        _runInput.HorizontalAlignment = HorizontalAlignment.Stretch;
        _runInput.PlaceholderText = "Executable, document, or URI";
        _runInput.KeyDown += OnRunInputKeyDown;
        _submitRunButton = TrayAppDotNETSettingsUI.Button(text: "Run", palette);
        _submitRunButton.Click += OnSubmitRunClick;
        _cancelRunButton = TrayAppDotNETSettingsUI.Button(text: "Cancel", palette);
        _cancelRunButton.Click += OnCancelRunClick;
        _runPanel = BuildRunPanel(palette, resources);
        _runPanel.IsVisible = false;
        _runPanel.Margin = resources.AxamlTaskManagerDetails.RunPanelMargin;
        MainContent.Children.Add(_runPanel);

        SettingsScrollBarStyle scrollBarStyle = TaskManagerScrollBarStyles.CreateProcessGrid(resources);
        ContextMenuWindowOptions scrollBarContextMenuOptions = TaskManagerContextMenuWindow.CreateOptions(
            palette,
            settings.EnableRoundedCorners,
            settings);
        _hoverHighlight = new ProcessRowHoverVisual(palette.Hover, _processCanvas.RowHoverGeometry);
        Grid tableSurface = new();
        tableSurface.Children.Add(_hoverHighlight);
        foreach (Control renderLayer in _processCanvas.RenderLayers)
            tableSurface.Children.Add(renderLayer);
        tableSurface.Children.Add(_processCanvas);

        _resizeGrip = new TaskManagerResizeGrip(resources);
        // Preserve the fractional anchor correction instead of pixel-rounding -Offset after each reorder
        _tableScrollViewport = new SettingsScrollViewport(
            tableSurface,
            padding: default,
            resources.AxamlProcessTable.GridBackgroundColor,
            scrollBarStyle,
            scrollBarContextMenuOptions,
            _resizeGrip,
            overlayVerticalScrollBar: true)
        {
            Margin = resources.AxamlTaskManagerDetails.TableMargin
        };
        _tableScrollViewport.SetScrollContentLayoutRounding(isEnabled: false);
        _tableScrollViewport.SetVerticalScrollBarTopInset(
            GetProcessTableVerticalScrollBarTopInset(resources));
        _columnHeaderBorder = new Border
        {
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        ApplyColumnHeaderBorderResources(_columnHeaderBorder, resources);
        SetColumnSpan(_columnHeaderBorder, value: 2);
        _tableScrollViewport.Children.Add(_columnHeaderBorder);
        SetRow(_tableScrollViewport, value: 1);
        MainContent.Children.Add(_tableScrollViewport);

        _processCanvas.SetProcessGroupingStyle(
            settings.GroupProcesses
                ? settings.ProcessGroupingStyle
                : ProcessGroupingStyle.None);
        _processCanvas.AttachExternalSubscriptions();
#if DEBUG
        _searchAutocomplete.AttachAXAMLHotReload();
        _savedSearches.AttachAXAMLHotReload();
        TaskManagerContextMenuResources.ResourcesReloaded += OnContextMenuAXAMLResourcesReloaded;
#endif
    }

    /// <summary>Gets the search controls rendered by the shell-level page overlay.</summary>
    internal override Control? PageOverlay => _searchOverlay;

    /// <summary>Gets the search box and visible leading action widths for restored-window drag avoidance.</summary>
    internal bool TryGetSearchDragRegionPixelWidths(
        out int searchWidth,
        out int leadingActionWidth)
    {
        searchWidth = 0;
        leadingActionWidth = 0;
        TopLevel? topLevel = TopLevel.GetTopLevel(_searchBox);
        if (!_searchBox.IsEffectivelyVisible
            || _searchBox.Bounds.Width <= 0
            || topLevel == null)
            return false;

        PixelPoint screenLeft = _searchBox.PointToScreen(default);
        PixelPoint screenRight = _searchBox.PointToScreen(new Point(_searchBox.Bounds.Width, y: 0));
        int searchLeft = Math.Min(screenLeft.X, screenRight.X);
        searchWidth = Math.Abs(screenRight.X - screenLeft.X);
        if (searchWidth <= 0) return false;

        Control leftmostActionButton = _savedSearches.ClearButton;
        if (!leftmostActionButton.IsEffectivelyVisible) return true;
        if (leftmostActionButton.Bounds.Width <= 0
            || TopLevel.GetTopLevel(leftmostActionButton) == null)
        {
            leadingActionWidth = (int)Math.Ceiling(
                GetLeadingSearchActionWidth() * topLevel.RenderScaling);
            return true;
        }

        PixelPoint actionLeft = leftmostActionButton.PointToScreen(default);
        PixelPoint actionRight = leftmostActionButton.PointToScreen(
            new Point(leftmostActionButton.Bounds.Width, y: 0));
        int actionLeftX = Math.Min(actionLeft.X, actionRight.X);
        leadingActionWidth = Math.Max(val1: 0, searchLeft - actionLeftX);
        return true;
    }

    bool ITaskManagerSearchOverlayPage.TryGetSearchDragRegionPixelWidths(
        out int searchWidth,
        out int leadingActionWidth) =>
        TryGetSearchDragRegionPixelWidths(out searchWidth, out leadingActionWidth);

    void ITaskManagerSearchOverlayPage.SetSearchCaptionButtonAreaWidth(double width) =>
        _searchOverlay.SetCaptionButtonAreaWidth(width);

    private void UpdateSearchControlsPosition()
    {
        bool hasLeadingAction = !string.IsNullOrWhiteSpace(_searchBox.Text);
        if (!hasLeadingAction)
        {
            _searchControlsTransform.X = 0;
            return;
        }

        double leadingActionWidth = GetLeadingSearchActionWidth();
        _searchControlsTransform.X = _settings.LeftAlignProcessSearchBar
            ? -leadingActionWidth
            : -leadingActionWidth / 2;
    }

    private double GetLeadingSearchActionWidth() =>
        _savedSearches.ClearButton.Width
        + _savedSearches.SaveButton.Width
        + _resources.AxamlTaskManagerDetails.SearchActionSpacing;

    private StackPanel BuildGroupProcessesHeaderControl(
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(
            text: "Group processes",
            palette,
            resources.AxamlTaskManagerDetails.ToolbarFontSize,
            (FontWeight)resources.AxamlTaskManagerDetails.ToolbarFontWeight);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = resources.AxamlTaskManagerDetails.ToolbarSpacing,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { label, _groupProcessesToggle }
        };
    }

    private void PopulateHeaderActions()
    {
        HeaderActions.Children.Clear();
        HeaderActions.Children.Add(_groupProcessesHeaderControl);
        foreach (ProcessHeaderButtonKind buttonKind in
                 ProcessHeaderButtonSettings.Normalize(_settings.ProcessHeaderButtonOrder))
        {
            SettingsButton button = buttonKind switch
            {
                ProcessHeaderButtonKind.RunNewTask => _runTaskButton,
                ProcessHeaderButtonKind.Columns => _columnsButton,
                ProcessHeaderButtonKind.EndTask => _endTaskButton,
                ProcessHeaderButtonKind.RestartExplorer => _restartExplorerButton,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(buttonKind),
                    buttonKind,
                    message: "Unknown header button kind.")
            };
            HeaderActions.Children.Add(button);
        }

        HeaderActions.Children.Add(_moreActionsButton);
    }

    private Border BuildRunPanel(SettingsPalette palette, TaskManagerWindowResources resources)
    {
        ColumnDefinition inputColumn = new(GridLength.Star)
        {
            MaxWidth = resources.AxamlTaskManagerDetails.RunInputWidth
        };
        Grid actions = new()
        {
            ColumnSpacing = resources.AxamlTaskManagerDetails.ToolbarSpacing,
            ColumnDefinitions =
            {
                inputColumn, new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)
            }
        };
        actions.Children.Add(_runInput);
        SetColumn(_submitRunButton, value: 1);
        actions.Children.Add(_submitRunButton);
        SetColumn(_cancelRunButton, value: 2);
        actions.Children.Add(_cancelRunButton);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            CornerRadius = resources.AxamlTaskManagerDetails.PanelCornerRadius,
            Padding = resources.AxamlTaskManagerDetails.RunPanelPadding,
            Child = actions
        };
    }

    private void OnSnapshotAvailable()
    {
        if (_disposed || !_isPageActive) return;
        _processCanvas.RefreshFrom(_snapshotService);
    }

    internal override void SetPageActive(bool isActive)
    {
        if (_disposed || _isPageActive == isActive) return;

        _isPageActive = isActive;
        if (isActive)
        {
            _snapshotService.SnapshotAvailable += OnSnapshotAvailable;
            _processCanvas.ActivateSampling(_snapshotService);
            _processCanvas.RefreshFrom(_snapshotService);
            return;
        }

        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _processCanvas.DeactivateSampling();
    }

    /// <summary>Clears the selected row when a left click begins outside the process grid.</summary>
    internal void ClearSelectionForExternalPointerSource(object? source)
    {
        if (_disposed || IsSelfOrDescendant(_tableScrollViewport, source as Visual)) return;

        // End task still needs the selected row until its Click handler captures the request
        if (IsSelfOrDescendant(_endTaskButton, source as Visual)) return;

        _processCanvas.ClearSelection();
        if (_processCanvas.IsKeyboardFocusWithin)
            TopLevel.GetTopLevel(_processCanvas)?.FocusManager.Focus(null);
    }

    /// <summary>Stops compositor-owned row hover while a same-window modal overlay owns input.</summary>
    internal void SetConfirmationOverlayVisible(bool isVisible)
    {
        if (_disposed) return;

        bool isProcessPointerInputEnabled = !isVisible;
        _hoverHighlight.SetSamplingEnabled(isProcessPointerInputEnabled);
    }

#if DEBUG
    /// <summary>Applies Task Manager AXAML values without replacing Processes runtime state.</summary>
    internal override void ApplyAXAMLResources(TaskManagerWindowResources resources)
    {
        if (_disposed) return;
        ArgumentNullException.ThrowIfNull(resources);

        base.ApplyAXAMLResources(resources);

        List<ProcessColumnSetting>? hotReloadedColumnSettings = _processCanvas.ApplyAXAMLResources();
        IReadOnlyList<ProcessColumnSetting> currentColumnSettings =
            hotReloadedColumnSettings ?? _settings.DetailsColumns;
        if (hotReloadedColumnSettings != null)
        {
            _settings.ApplyHotReloadedDetailsColumnLayout(hotReloadedColumnSettings);
            _searchAutocomplete.SetColumnSettings(hotReloadedColumnSettings);
        }

        double moreButtonSize = resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        _moreActionsButton.Width = moreButtonSize;
        _moreActionsButton.Height = moreButtonSize;
        _moreActionsButton.MinHeight = moreButtonSize;
        _moreActionsButton.Padding = resources.AxamlTaskManagerReorderDialog.MoreButtonPadding;
        _moreActionsButton.Label.FontSize = resources.AxamlTaskManagerReorderDialog.MoreGlyphFontSize;

        TextBlock groupProcessesLabel = (TextBlock)_groupProcessesHeaderControl.Children[0];
        groupProcessesLabel.FontSize = resources.AxamlTaskManagerDetails.ToolbarFontSize;
        groupProcessesLabel.FontWeight = (FontWeight)resources.AxamlTaskManagerDetails.ToolbarFontWeight;
        _groupProcessesHeaderControl.Spacing = resources.AxamlTaskManagerDetails.ToolbarSpacing;

        _searchBox.Width = resources.AxamlTaskManagerDetails.SearchWidth;
        _savedSearches.ClearButton.Margin = default;
        _savedSearches.SaveButton.Margin = new Thickness(
            left: 0,
            top: 0,
            resources.AxamlTaskManagerDetails.SearchActionSpacing,
            bottom: 0);
        _savedSearches.ApplyAXAMLResources(resources);
        _searchOverlay.ApplyAXAMLResources(
            resources.AxamlTaskManagerDetails.SearchMargin,
            resources.AxamlTaskManagerDetails.SearchCaptionSpacing);
        UpdateSearchControlsPosition();

        Grid runActions = (Grid)_runPanel.Child!;
        runActions.ColumnSpacing = resources.AxamlTaskManagerDetails.ToolbarSpacing;
        runActions.ColumnDefinitions[0].MaxWidth = resources.AxamlTaskManagerDetails.RunInputWidth;
        _runPanel.CornerRadius = resources.AxamlTaskManagerDetails.PanelCornerRadius;
        _runPanel.Padding = resources.AxamlTaskManagerDetails.RunPanelPadding;
        _runPanel.Margin = resources.AxamlTaskManagerDetails.RunPanelMargin;

        _tableScrollViewport.Margin = resources.AxamlTaskManagerDetails.TableMargin;
        _tableScrollViewport.Background = TrayAppDotNETSettingsUI.Brush(
            resources.AxamlProcessTable.GridBackgroundColor);

        _tableScrollViewport.SetScrollBarStyle(TaskManagerScrollBarStyles.CreateProcessGrid(resources));
        _tableScrollViewport.SetVerticalScrollBarTopInset(
            GetProcessTableVerticalScrollBarTopInset(resources));
        ApplyColumnHeaderBorderResources(_columnHeaderBorder, resources);
        _resizeGrip.ApplyResources(resources);

        foreach (ProcessColumnPropertiesWindow propertiesWindow in _columnPropertyWindows.Values)
            propertiesWindow.ApplyAXAMLResources(currentColumnSettings);
        _columnChooserWindow?.ApplyAXAMLResources(resources, currentColumnSettings);
        _headerButtonArrangementWindow?.ApplyAXAMLResources(resources);
        _rowContextMenuController.ApplyAXAMLResources(resources);
    }

    /// <summary>Captures user-editable Processes state before a shared shell rebuild.</summary>
    internal ProcessDetailsHotReloadState CaptureHotReloadState() =>
        new(
            _searchBox.Text ?? string.Empty,
            _tableScrollViewport.HorizontalOffset,
            _tableScrollViewport.VerticalOffset,
            _processCanvas.SelectedTerminationTargets,
            _processCanvas.SelectedTerminationTarget,
            _runPanel.IsVisible,
            _runInput.Text ?? string.Empty,
            _processCanvas.GridFontSize,
            _processCanvas.GridRowSpacing);

    /// <summary>Restores user-editable Processes state after a shared shell rebuild.</summary>
    internal void RestoreHotReloadState(ProcessDetailsHotReloadState state)
    {
        if (_disposed) return;

        _searchBox.Text = state.SearchText;
        _runInput.Text = state.RunInputText;
        _runPanel.IsVisible = state.IsRunPanelVisible;
        _processCanvas.SetGridTypography(state.GridFontSize, state.GridRowSpacing);
        _processCanvas.RestoreSelectedProcesses(state.SelectedProcesses, state.ActiveProcess);
        UpdateLayout();
        _tableScrollViewport.UpdateLayout();
        _tableScrollViewport.SetOffsets(state.HorizontalOffset, state.VerticalOffset);
    }
#endif

    private static double GetProcessTableVerticalScrollBarTopInset(
        TaskManagerWindowResources resources) =>
        resources.AxamlProcessTable.HeaderHeight;

    private static bool IsSelfOrDescendant(Visual boundary, Visual? source) =>
        source != null
        && (ReferenceEquals(source, boundary)
            || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, boundary)));

    private static void ApplyColumnHeaderBorderResources(
        Border columnHeaderBorder,
        TaskManagerWindowResources resources)
    {
        double borderThickness = resources.AxamlProcessTable.GridLineThickness;
        columnHeaderBorder.BorderThickness = new Thickness(left: 0, top: 0, right: 0, borderThickness);
        columnHeaderBorder.Height = resources.AxamlProcessTable.HeaderHeight + borderThickness / 2;
    }

    private void OnSelectedProcessChanged(ProcessTerminationTarget? target)
    {
        int selectedCount = _processCanvas.SelectedProcessCount;
        _endTaskButton.IsEnabled = selectedCount > 0;
        _endTaskButton.Text = selectedCount > 1 ? "End tasks" : "End task";
        _armTerminationTarget(target);
    }

    private void OnRowHoverGeometryChanged(ProcessRowHoverGeometry geometry) => _hoverHighlight.SetGeometry(geometry);

    private void OnViewportAnchorAdjustmentRequested(ProcessViewportAnchorAdjustment adjustment)
    {
        if (_disposed) return;

        if (adjustment.ContentHeightChanged) _tableScrollViewport.UpdateLayout();
        _tableScrollViewport.AdjustVerticalOffset(adjustment.VerticalOffsetDelta);
    }

    private void OnGridZoomRequested(int direction)
    {
        if (direction == 0) return;

#if DEBUG
        double currentFontSize = _processCanvas.GridFontSize;
        double currentRowSpacing = _processCanvas.GridRowSpacing;
#else
        double currentFontSize = _settings.GridFontSize;
        double currentRowSpacing = _settings.GridRowSpacing;
#endif
        double fontSize = Math.Clamp(
            currentFontSize + Math.Sign(direction) * GridFontZoomStep,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum);
        ApplyGridTypography(fontSize, currentRowSpacing);
    }

    private void OnGridZoomResetRequested()
    {
#if DEBUG
        ApplyGridTypography(
            _resources.AxamlProcessTable.FontSize,
            _processCanvas.GridRowSpacing);
#else
        ApplyGridTypography(
            AppSettings.GridFontSizeDefault,
            _settings.GridRowSpacing);
#endif
    }

    private void OnGridRowSpacingRequested(int direction)
    {
        if (direction == 0) return;

#if DEBUG
        double currentRowSpacing = _processCanvas.GridRowSpacing;
        double currentFontSize = _processCanvas.GridFontSize;
#else
        double currentRowSpacing = _settings.GridRowSpacing;
        double currentFontSize = _settings.GridFontSize;
#endif
        double rowSpacing = Math.Clamp(
            currentRowSpacing + Math.Sign(direction) * GridRowSpacingStep,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum);
        ApplyGridTypography(currentFontSize, rowSpacing);
    }

    private void OnGridRowSpacingResetRequested()
    {
#if DEBUG
        ApplyGridTypography(
            _processCanvas.GridFontSize,
            _resources.AxamlProcessTable.RowSpacing);
#else
        ApplyGridTypography(
            _settings.GridFontSize,
            AppSettings.GridRowSpacingDefault);
#endif
    }

    private void ApplyGridTypography(double fontSize, double rowSpacing)
    {
        _processCanvas.SetGridTypography(fontSize, rowSpacing);
        _settings.UpdateGridMetrics(fontSize, _processCanvas.RowHeight, rowSpacing);
    }

    private void OnGroupProcessesChanged(object? sender, bool groupProcesses)
    {
        _processCanvas.SetProcessGroupingStyle(
            groupProcesses
                ? _settings.ProcessGroupingStyle
                : ProcessGroupingStyle.None);
        _settings.UpdateGroupProcesses(groupProcesses);
    }

    private void OnRowContextMenuRequested(ProcessRowContextMenuRequest request)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _rowContextMenuController.Show(owner, request);
        });
    }

    private void OnColumnPropertiesRequested(ProcessTableColumnKind column)
    {
        if (!_disposed) ShowColumnProperties(column);
    }

    private void ShowColumnProperties(ProcessTableColumnKind column)
    {
        if (_columnPropertyWindows.TryGetValue(column, out ProcessColumnPropertiesWindow? existing))
        {
            existing.Activate();
            return;
        }

        ProcessColumnPropertiesWindow propertiesWindow = ProcessColumnPropertiesWindow.Create(
            _processCanvas.GetColumnSetting(column),
            _palette,
            _settings.EnableRoundedCorners,
            _processCanvas.ApplyColumnProperties);
        _columnPropertyWindows.Add(column, propertiesWindow);
        propertiesWindow.Closed += OnColumnPropertiesWindowClosed;
        if (TopLevel.GetTopLevel(this) is Window owner)
            propertiesWindow.Show(owner);
        else
            propertiesWindow.Show();
    }

    private void OnColumnPropertiesWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not ProcessColumnPropertiesWindow propertiesWindow) return;

        propertiesWindow.Closed -= OnColumnPropertiesWindowClosed;
        ProcessTableColumnKind? closedColumn = null;
        foreach (KeyValuePair<ProcessTableColumnKind, ProcessColumnPropertiesWindow> pair in _columnPropertyWindows)
        {
            if (!ReferenceEquals(pair.Value, propertiesWindow)) continue;
            closedColumn = pair.Key;
            break;
        }

        if (closedColumn.HasValue)
            _columnPropertyWindows.Remove(closedColumn.Value);
    }

    private void OnColumnLayoutChanged(List<ProcessColumnSetting> settings)
    {
        _settings.UpdateDetailsColumnLayout(settings);
        _searchAutocomplete.SetColumnSettings(settings);
    }

    private void OnMoreActionsClick(object? sender, EventArgs eventArgs) => ShowHeaderActionsMenu();

    private void ShowHeaderActionsMenu()
    {
        if (_disposed || TopLevel.GetTopLevel(this) is not Window owner) return;

        CloseHeaderActionsMenu();
        ContextMenuEntryBuilder entries = new();
        entries.Add(text: "Arrange buttons", ShowHeaderButtonArrangement);
        entries.AddSeparator();
        AddElevatedHelperMenuEntry(entries);
        TaskManagerContextMenuWindow menuWindow = new(
            entries.ToList(),
            _palette,
            _settings.EnableRoundedCorners,
            _settings);
        _headerActionsMenuWindow = menuWindow;
        menuWindow.Closed += OnHeaderActionsMenuClosed;
        menuWindow.ShowOver(_moreActionsButton, _moreActionsButton, owner);
    }

    private void AddElevatedHelperMenuEntry(ContextMenuEntryBuilder entries)
    {
        ElevatedHelperStatus status = _getElevatedHelperStatus();
        switch (status.State)
        {
            case ElevatedHelperState.NotRequested:
                entries.Add(text: "Enable elevated termination...", _requestElevatedTermination);
                break;
            case ElevatedHelperState.Declined:
            case ElevatedHelperState.Failed:
                entries.Add(text: "Retry elevated termination...", _requestElevatedTermination);
                break;
            case ElevatedHelperState.Starting:
                entries.Add(text: "Waiting for Windows approval", static () => { });
                break;
            case ElevatedHelperState.Ready:
                entries.Add(text: "Elevated termination enabled", static () => { });
                break;
            case ElevatedHelperState.Disposed:
                entries.Add(text: "Elevated termination unavailable", static () => { });
                break;
        }
    }

    private void CloseHeaderActionsMenu()
    {
        TaskManagerContextMenuWindow? menuWindow = _headerActionsMenuWindow;
        if (menuWindow == null) return;

        _headerActionsMenuWindow = null;
        menuWindow.Closed -= OnHeaderActionsMenuClosed;
        menuWindow.Close();
    }

    private void OnHeaderActionsMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerContextMenuWindow menuWindow)
            menuWindow.Closed -= OnHeaderActionsMenuClosed;
        if (ReferenceEquals(sender, _headerActionsMenuWindow))
            _headerActionsMenuWindow = null;
    }

    private void ShowHeaderButtonArrangement()
    {
        if (_disposed) return;
        if (_headerButtonArrangementWindow != null)
        {
            _headerButtonArrangementWindow.Activate();
            return;
        }

        ProcessHeaderButtonArrangementWindow arrangementWindow = new(
            _settings,
            _palette,
            _resources,
            UpdateHeaderButtonOrder);
        _headerButtonArrangementWindow = arrangementWindow;
        arrangementWindow.Closed += OnHeaderButtonArrangementClosed;
        if (TopLevel.GetTopLevel(this) is Window owner)
            _ = arrangementWindow.ShowDialog(owner);
        else
            arrangementWindow.Show();
    }

    private void OnHeaderButtonArrangementClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ProcessHeaderButtonArrangementWindow arrangementWindow)
            arrangementWindow.Closed -= OnHeaderButtonArrangementClosed;
        if (ReferenceEquals(sender, _headerButtonArrangementWindow))
            _headerButtonArrangementWindow = null;
    }

    private void UpdateHeaderButtonOrder(IReadOnlyList<ProcessHeaderButtonKind> buttonOrder)
    {
        if (_disposed) return;

        _settings.UpdateProcessHeaderButtonOrder(buttonOrder);
        PopulateHeaderActions();
    }

    private void OnColumnsClick(object? sender, EventArgs eventArgs) => ShowColumnChooser();

    private void ShowColumnChooser()
    {
        if (_columnChooserWindow != null)
        {
            _columnChooserWindow.Activate();
            return;
        }

        _columnChooserWindow = new ProcessColumnChooserWindow(
            _settings,
            _palette,
            _resources,
            UpdateColumnSettings);
        _columnChooserWindow.Closed += OnColumnChooserClosed;
        if (TopLevel.GetTopLevel(this) is Window owner)
            _ = _columnChooserWindow.ShowDialog(owner);
        else
            _columnChooserWindow.Show();
    }

    private void OnColumnChooserClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ProcessColumnChooserWindow window)
            window.Closed -= OnColumnChooserClosed;
        if (ReferenceEquals(sender, _columnChooserWindow))
            _columnChooserWindow = null;
    }

    private void UpdateColumnSettings(IReadOnlyList<ProcessColumnSetting> settings)
    {
        if (_disposed) return;
        _processCanvas.ApplyColumnSettings(settings);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        _processCanvas.SetFilter(_searchBox.Text);
        UpdateSearchControlsPosition();
    }

#if DEBUG
    /// <summary>Replaces cached scrollbar menu options without rebuilding Processes state.</summary>
    private void OnContextMenuAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        ContextMenuWindowOptions contextMenuOptions = TaskManagerContextMenuWindow.CreateOptions(
            _palette,
            _settings.EnableRoundedCorners,
            _settings);
        _tableScrollViewport.SetContextMenuOptions(contextMenuOptions);
        _columnChooserWindow?.SetScrollBarContextMenuOptions(contextMenuOptions);
    }
#endif

    /// <summary>Gets the process-grid top edge in the requested control's coordinate space.</summary>
    internal bool TryGetTableTop(Control relativeTo, out double tableTop)
    {
        Point? tableOrigin = _tableScrollViewport.TranslatePoint(point: default, relativeTo);
        if (!tableOrigin.HasValue)
        {
            tableTop = 0;
            return false;
        }

        tableTop = tableOrigin.Value.Y;
        return true;
    }

    private void OnRunTaskClick(object? sender, EventArgs eventArgs)
    {
        _runPanel.IsVisible = true;
        _runInput.Focus();
        _runInput.SelectAll();
    }

    private void OnEndTaskClick(object? sender, EventArgs eventArgs)
    {
        ProcessEndTaskRequest? request = _processCanvas.SelectedEndTaskRequest;
        _processCanvas.ClearSelection();
        if (request is { } selectedRequest)
            RequestEndTask(selectedRequest);
    }

    private void OnRestartExplorerClick(object? sender, EventArgs eventArgs) =>
        _ = RestartExplorerAsync();

    private async Task RestartExplorerAsync()
    {
        if (_disposed || _isRestartExplorerPending) return;

        _isRestartExplorerPending = true;
        _restartExplorerButton.IsEnabled = false;
        try
        {
            bool confirmed = await _confirmRestartExplorer();
            if (_disposed || !confirmed) return;

            ExplorerRestartResult result = await _restartExplorer();
            if (_disposed) return;
            if (!result.Succeeded)
            {
                _reportMessage(arg1: "Restart explorer failed", result.ErrorMessage);
                return;
            }

            _snapshotService.RequestRefresh();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Restart Explorer failed: {exception}");
            if (!_disposed) _reportMessage(arg1: "Restart explorer failed", exception.Message);
        }
        finally
        {
            _isRestartExplorerPending = false;
            if (!_disposed)
            {
                _armTerminationTarget(_processCanvas.SelectedTerminationTarget);
                _restartExplorerButton.IsEnabled = true;
            }
        }
    }

    private void RequestEndTask(ProcessEndTaskRequest request) => _ = EndTaskAsync(request);

    private async Task EndTaskAsync(ProcessEndTaskRequest request)
    {
        if (_disposed || _isEndTaskConfirmationPending) return;

        _isEndTaskConfirmationPending = true;
        try
        {
            bool confirmed = await _confirmEndTask(request);
            if (_disposed || !confirmed) return;

            ProcessTerminationBatchResult result = await Task.Run(() =>
                ProcessTerminationBatchFunctions.Execute(
                    request.Processes,
                    _terminateProcess,
                    CriticalProcessActions.IsTargetGone));
            if (_disposed) return;
            if (result.RefreshNeeded) _snapshotService.RequestRefresh();
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _reportMessage(
                    request.Count > 1 ? "End tasks failed" : "End task failed",
                    result.ErrorMessage);
                return;
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"End task confirmation failed: {exception}");
            if (!_disposed)
            {
                _reportMessage(
                    request.Count > 1 ? "End tasks failed" : "End task failed",
                    exception.Message);
            }
        }
        finally
        {
            _isEndTaskConfirmationPending = false;
        }
    }

    private void OnSubmitRunClick(object? sender, EventArgs eventArgs) => SubmitRunTask();

    private void OnCancelRunClick(object? sender, EventArgs eventArgs) => HideRunPanel();

    private void OnRunInputKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case Key.Enter:
                SubmitRunTask();
                eventArgs.Handled = true;
                return;
            case Key.Escape:
                HideRunPanel();
                eventArgs.Handled = true;
                return;
        }
    }

    private void SubmitRunTask()
    {
        string command = _runInput.Text ?? string.Empty;
        if (!_startProcess(command)) return;

        _runInput.Text = string.Empty;
        HideRunPanel();
        _snapshotService.RequestRefresh();
    }

    private void HideRunPanel()
    {
        _runPanel.IsVisible = false;
        _runTaskButton.Focus();
    }

    public void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        _armTerminationTarget(null);
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded -= OnContextMenuAXAMLResourcesReloaded;
#endif
        _processCanvas.SelectedProcessChanged -= OnSelectedProcessChanged;
        _processCanvas.RowHoverGeometryChanged -= OnRowHoverGeometryChanged;
        _processCanvas.ViewportAnchorAdjustmentRequested -= OnViewportAnchorAdjustmentRequested;
        _processCanvas.ColumnPropertiesRequested -= OnColumnPropertiesRequested;
        _processCanvas.ColumnLayoutChanged -= OnColumnLayoutChanged;
        _processCanvas.GridZoomRequested -= OnGridZoomRequested;
        _processCanvas.GridZoomResetRequested -= OnGridZoomResetRequested;
        _processCanvas.GridRowSpacingRequested -= OnGridRowSpacingRequested;
        _processCanvas.GridRowSpacingResetRequested -= OnGridRowSpacingResetRequested;
        _processCanvas.EndTaskRequested -= RequestEndTask;
        _processCanvas.RowContextMenuRequested -= OnRowContextMenuRequested;
        _groupProcessesToggle.CheckedChanged -= OnGroupProcessesChanged;
        _searchBox.TextChanged -= OnSearchTextChanged;
        _searchOverlay.Dispose();
        _savedSearches.Dispose();
        _searchAutocomplete.Dispose();
        _runInput.KeyDown -= OnRunInputKeyDown;
        _runTaskButton.Click -= OnRunTaskClick;
        _restartExplorerButton.Click -= OnRestartExplorerClick;
        _columnsButton.Click -= OnColumnsClick;
        _endTaskButton.Click -= OnEndTaskClick;
        _moreActionsButton.Click -= OnMoreActionsClick;
        _submitRunButton.Click -= OnSubmitRunClick;
        _cancelRunButton.Click -= OnCancelRunClick;
        CloseHeaderActionsMenu();
        if (_headerButtonArrangementWindow != null)
        {
            _headerButtonArrangementWindow.Closed -= OnHeaderButtonArrangementClosed;
            _headerButtonArrangementWindow.Close();
            _headerButtonArrangementWindow = null;
        }

        if (_columnChooserWindow != null)
        {
            _columnChooserWindow.Closed -= OnColumnChooserClosed;
            _columnChooserWindow.Close();
            _columnChooserWindow = null;
        }

        foreach (ProcessColumnPropertiesWindow propertiesWindow in _columnPropertyWindows.Values)
        {
            propertiesWindow.Closed -= OnColumnPropertiesWindowClosed;
            propertiesWindow.Close();
        }

        _columnPropertyWindows.Clear();
        _rowContextMenuController.Dispose();
        _tableScrollViewport.Dispose();
        _hoverHighlight.Dispose();
        _processCanvas.Dispose();
    }
}

#if DEBUG
/// <summary>Processes state that survives Common or glyph-triggered shell reconstruction.</summary>
internal readonly record struct ProcessDetailsHotReloadState(
    string SearchText,
    double HorizontalOffset,
    double VerticalOffset,
    ProcessTerminationTarget[] SelectedProcesses,
    ProcessTerminationTarget? ActiveProcess,
    bool IsRunPanelVisible,
    string RunInputText,
    double GridFontSize,
    double GridRowSpacing);
#endif

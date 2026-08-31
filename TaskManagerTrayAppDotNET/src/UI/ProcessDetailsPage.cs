using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Builds the Processes toolbar around the allocation-light painted process table.</summary>
internal sealed class ProcessDetailsPage : TaskManagerPageLayout, IDisposable
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
    private readonly Grid _searchOverlay;
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
    private readonly Border _selectionHighlight;
    private readonly TranslateTransform _selectionTransform = new();
    private readonly Dictionary<ProcessTableColumnKind, ProcessColumnPropertiesWindow> _columnPropertyWindows = [];
    private ProcessColumnChooserWindow? _columnChooserWindow;
    private ProcessHeaderButtonArrangementWindow? _headerButtonArrangementWindow;
    private TaskManagerContextMenuWindow? _headerActionsMenuWindow;
    private bool _isEndTaskConfirmationPending;
    private bool _isRestartExplorerPending;
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
        : base("Processes", palette, resources)
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
        _snapshotService.SetActiveSchema(schema);
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _processCanvas = new ProcessDetailsCanvas(
            processIconService,
            schema,
            settings.DetailsColumns,
            settings.EnableLiveDetailsColumnResizing,
            settings.GridFontSize,
            settings.GridFontWeight,
            settings.GridRowSpacing,
            palette,
            resources);
        _processCanvas.SelectedProcessChanged += OnSelectedProcessChanged;
        _processCanvas.RowHoverGeometryChanged += OnRowHoverGeometryChanged;
        _processCanvas.SelectionRowTopChanged += OnSelectionRowTopChanged;
        _processCanvas.ColumnPropertiesRequested += OnColumnPropertiesRequested;
        _processCanvas.ColumnLayoutChanged += OnColumnLayoutChanged;
        _processCanvas.GridMetricsChanged += OnGridMetricsChanged;
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

        _runTaskButton = TrayAppDotNETSettingsUI.Button("Run new task", palette);
        _runTaskButton.Click += OnRunTaskClick;
        _restartExplorerButton = TrayAppDotNETSettingsUI.Button("Restart explorer", palette);
        _restartExplorerButton.Click += OnRestartExplorerClick;
        _columnsButton = TrayAppDotNETSettingsUI.Button("Columns", palette);
        _columnsButton.Click += OnColumnsClick;
        _endTaskButton = TrayAppDotNETSettingsUI.Button("End task", palette);
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
        TrayAppDotNETToolTip.SetTip(_moreActionsButton, "More");
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
        Thickness searchActionMargin = new(
            0,
            0,
            resources.AxamlTaskManagerDetails.SearchActionSpacing,
            0);
        _savedSearches.ClearButton.Margin = searchActionMargin;
        _savedSearches.SaveButton.Margin = searchActionMargin;
        _searchControls = new Grid
        {
            HorizontalAlignment = settings.LeftAlignProcessSearchBar
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = resources.AxamlTaskManagerDetails.SearchMargin,
            RenderTransform = _searchControlsTransform,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _searchControls.Children.Add(_savedSearches.ClearButton);
        Grid.SetColumn(_savedSearches.SaveButton, 1);
        _searchControls.Children.Add(_savedSearches.SaveButton);
        Grid.SetColumn(_searchBox, 2);
        _searchControls.Children.Add(_searchBox);
        UpdateSearchControlsPosition();
        _searchOverlay = new Grid();
        _searchOverlay.Children.Add(_searchControls);
        _searchOverlay.Children.Add(_searchAutocomplete.Popup);

        _runInput = TrayAppDotNETSettingsUI.TextBox(
            palette,
            resources.AxamlTaskManagerDetails.RunInputWidth);
        _runInput.Width = double.NaN;
        _runInput.HorizontalAlignment = HorizontalAlignment.Stretch;
        _runInput.PlaceholderText = "Executable, document, or URI";
        _runInput.KeyDown += OnRunInputKeyDown;
        _submitRunButton = TrayAppDotNETSettingsUI.Button("Run", palette);
        _submitRunButton.Click += OnSubmitRunClick;
        _cancelRunButton = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        _cancelRunButton.Click += OnCancelRunClick;
        _runPanel = BuildRunPanel(palette, resources);
        _runPanel.IsVisible = false;
        _runPanel.Margin = resources.AxamlTaskManagerDetails.RunPanelMargin;
        MainContent.Children.Add(_runPanel);

        SettingsScrollBarStyle scrollBarStyle = CreateProcessTableScrollBarStyle(resources);
        ContextMenuWindowOptions scrollBarContextMenuOptions = TaskManagerContextMenuWindow.CreateOptions(
            palette,
            settings.EnableRoundedCorners,
            settings);
        _hoverHighlight = new ProcessRowHoverVisual(palette.Hover, _processCanvas.RowHoverGeometry);
        _selectionHighlight = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.SearchListItemSelected),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent),
            BorderThickness = resources.AxamlProcessTable.SelectionBorderThickness,
            Height = _processCanvas.RowHeight,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransform = _selectionTransform
        };
        Grid tableSurface = new();
        tableSurface.Children.Add(_hoverHighlight);
        tableSurface.Children.Add(_selectionHighlight);
        foreach (Control renderLayer in _processCanvas.RenderLayers)
            tableSurface.Children.Add(renderLayer);
        tableSurface.Children.Add(_processCanvas);

        _resizeGrip = new TaskManagerResizeGrip(resources);
        _tableScrollViewport = new SettingsScrollViewport(
            tableSurface,
            default,
            TaskManagerWindowResources.ProcessGridBackgroundColor,
            scrollBarStyle,
            scrollBarContextMenuOptions,
            _resizeGrip,
            overlayVerticalScrollBar: true)
        {
            Margin = resources.AxamlTaskManagerDetails.TableMargin
        };
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
        Grid.SetColumnSpan(_columnHeaderBorder, 2);
        _tableScrollViewport.Children.Add(_columnHeaderBorder);
        Grid.SetRow(_tableScrollViewport, 1);
        MainContent.Children.Add(_tableScrollViewport);

        _processCanvas.SetGroupProcesses(settings.GroupProcesses);
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        _snapshotService.SnapshotAvailable += OnSnapshotAvailable;
        _processCanvas.RefreshFrom(_snapshotService);
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
        {
            return false;
        }

        PixelPoint screenLeft = _searchBox.PointToScreen(default);
        PixelPoint screenRight = _searchBox.PointToScreen(new Point(_searchBox.Bounds.Width, 0));
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
            new Point(leftmostActionButton.Bounds.Width, 0));
        int actionLeftX = Math.Min(actionLeft.X, actionRight.X);
        leadingActionWidth = Math.Max(0, searchLeft - actionLeftX);
        return true;
    }

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
        + (2 * _resources.AxamlTaskManagerDetails.SearchActionSpacing);

    private StackPanel BuildGroupProcessesHeaderControl(
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(
            "Group processes",
            palette,
            resources.AxamlTaskManagerDetails.ToolbarFontSize,
            FontWeight.Normal);
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
                    "Unknown header button kind.")
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
                inputColumn,
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        actions.Children.Add(_runInput);
        Grid.SetColumn(_submitRunButton, 1);
        actions.Children.Add(_submitRunButton);
        Grid.SetColumn(_cancelRunButton, 2);
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
        if (_disposed) return;
        _processCanvas.RefreshFrom(_snapshotService);
    }

    private void OnAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        _tableScrollViewport.SetScrollBarStyle(CreateProcessTableScrollBarStyle(_resources));
        _tableScrollViewport.SetVerticalScrollBarTopInset(
            GetProcessTableVerticalScrollBarTopInset(_resources));
        ApplyColumnHeaderBorderResources(_columnHeaderBorder, _resources);
        _resizeGrip.ApplyResources(_resources);
        _selectionHighlight.BorderThickness = _resources.AxamlProcessTable.SelectionBorderThickness;
    }

    private static SettingsScrollBarStyle CreateProcessTableScrollBarStyle(
        TaskManagerWindowResources resources) =>
        new(
            resources.AxamlProcessTable.ScrollBarTrackThickness,
            resources.AxamlProcessTable.ScrollBarIdleThumbThickness,
            resources.AxamlProcessTable.ScrollBarHoverThumbThickness,
            resources.AxamlProcessTable.ScrollBarThumbEndMargin,
            resources.AxamlProcessTable.ScrollBarMinimumThumbLength,
            TaskManagerWindowResources.ProcessGridBackgroundColor,
            TaskManagerWindowResources.ProcessGridScrollThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            TaskManagerWindowResources.ProcessGridScrollHoverThumbColor,
            ShowButtonsOnHover: true);

    private static double GetProcessTableVerticalScrollBarTopInset(
        TaskManagerWindowResources resources) =>
        resources.AxamlProcessTable.HeaderHeight;

    private static void ApplyColumnHeaderBorderResources(
        Border columnHeaderBorder,
        TaskManagerWindowResources resources)
    {
        double borderThickness = resources.AxamlProcessTable.GridLineThickness;
        columnHeaderBorder.BorderThickness = new Thickness(0, 0, 0, borderThickness);
        columnHeaderBorder.Height = resources.AxamlProcessTable.HeaderHeight + borderThickness / 2;
    }

    private void OnSelectedProcessChanged(ProcessTerminationTarget? target)
    {
        _endTaskButton.IsEnabled = target.HasValue;
        _armTerminationTarget(target);
    }

    private void OnRowHoverGeometryChanged(ProcessRowHoverGeometry geometry)
    {
        _hoverHighlight.SetGeometry(geometry);
    }

    private void OnSelectionRowTopChanged(double? rowTop)
    {
        _selectionHighlight.IsVisible = rowTop.HasValue;
        if (rowTop.HasValue) _selectionTransform.Y = rowTop.Value;
    }

    private void OnGridMetricsChanged(double fontSize, double rowHeight)
    {
        _selectionHighlight.Height = rowHeight;
    }

    private void OnGridZoomRequested(int direction)
    {
        if (direction == 0) return;

        double fontSize = Math.Clamp(
            _settings.GridFontSize + Math.Sign(direction) * GridFontZoomStep,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum);
        ApplyGridTypography(fontSize, _settings.GridRowSpacing);
    }

    private void OnGridZoomResetRequested()
    {
        ApplyGridTypography(
            AppSettings.GridFontSizeDefault,
            _settings.GridRowSpacing);
    }

    private void OnGridRowSpacingRequested(int direction)
    {
        if (direction == 0) return;

        double rowSpacing = Math.Clamp(
            _settings.GridRowSpacing + Math.Sign(direction) * GridRowSpacingStep,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum);
        ApplyGridTypography(_settings.GridFontSize, rowSpacing);
    }

    private void OnGridRowSpacingResetRequested()
    {
        ApplyGridTypography(
            _settings.GridFontSize,
            AppSettings.GridRowSpacingDefault);
    }

    private void ApplyGridTypography(double fontSize, double rowSpacing)
    {
        _processCanvas.SetGridTypography(fontSize, rowSpacing);
        _settings.UpdateGridMetrics(fontSize, _processCanvas.RowHeight, rowSpacing);
    }

    private void OnGroupProcessesChanged(object? sender, bool groupProcesses)
    {
        _processCanvas.SetGroupProcesses(groupProcesses);
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
        entries.Add("Arrange buttons", ShowHeaderButtonArrangement);
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
                entries.Add("Enable elevated termination...", _requestElevatedTermination);
                break;
            case ElevatedHelperState.Declined:
            case ElevatedHelperState.Failed:
                entries.Add("Retry elevated termination...", _requestElevatedTermination);
                break;
            case ElevatedHelperState.Starting:
                entries.Add("Waiting for Windows approval", static () => { });
                break;
            case ElevatedHelperState.Ready:
                entries.Add("Elevated termination enabled", static () => { });
                break;
            case ElevatedHelperState.Disposed:
                entries.Add("Elevated termination unavailable", static () => { });
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

    /// <summary>Gets the process-grid top edge in the requested control's coordinate space.</summary>
    internal bool TryGetTableTop(Control relativeTo, out double tableTop)
    {
        Point? tableOrigin = _tableScrollViewport.TranslatePoint(default, relativeTo);
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
        if (_processCanvas.SelectedEndTaskRequest is { } request)
            RequestEndTask(request);
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
                _reportMessage("Restart explorer failed", result.ErrorMessage);
                return;
            }

            _snapshotService.RequestRefresh();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Restart Explorer failed: {exception}");
            if (!_disposed) _reportMessage("Restart explorer failed", exception.Message);
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

            if (!_terminateProcess(request.Target, out string errorMessage))
            {
                _reportMessage("End task failed", errorMessage);
                return;
            }

            _snapshotService.RequestRefresh();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"End task confirmation failed: {exception}");
            if (!_disposed) _reportMessage("End task failed", exception.Message);
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

        _disposed = true;
        _armTerminationTarget(null);
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _processCanvas.SelectedProcessChanged -= OnSelectedProcessChanged;
        _processCanvas.RowHoverGeometryChanged -= OnRowHoverGeometryChanged;
        _processCanvas.SelectionRowTopChanged -= OnSelectionRowTopChanged;
        _processCanvas.ColumnPropertiesRequested -= OnColumnPropertiesRequested;
        _processCanvas.ColumnLayoutChanged -= OnColumnLayoutChanged;
        _processCanvas.GridMetricsChanged -= OnGridMetricsChanged;
        _processCanvas.GridZoomRequested -= OnGridZoomRequested;
        _processCanvas.GridZoomResetRequested -= OnGridZoomResetRequested;
        _processCanvas.GridRowSpacingRequested -= OnGridRowSpacingRequested;
        _processCanvas.GridRowSpacingResetRequested -= OnGridRowSpacingResetRequested;
        _processCanvas.EndTaskRequested -= RequestEndTask;
        _processCanvas.RowContextMenuRequested -= OnRowContextMenuRequested;
        _groupProcessesToggle.CheckedChanged -= OnGroupProcessesChanged;
        _searchBox.TextChanged -= OnSearchTextChanged;
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

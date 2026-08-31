using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts the shared search, run-task panel, actions, and painted table for simple Task Manager pages.</summary>
internal class TaskManagerTablePage : TaskManagerPageLayout, ITaskManagerSearchOverlayPage, IDisposable
{
    private const double GridFontZoomStep = 0.5;
    private const double GridRowSpacingStep = 1;

    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly Func<string, bool> _startProcess;
    private readonly List<HeaderActionRegistration> _headerActionRegistrations = [];
    private readonly TaskManagerTableControl _table;
    private readonly SettingsScrollViewport _tableScrollViewport;
    private readonly TaskManagerResizeGrip _resizeGrip;
    private readonly Border _columnHeaderBorder;
    private readonly Grid _informationHost;
    private readonly TextBlock _emptyMessage;
    private readonly TextBox _searchBox;
    private readonly Grid _searchOverlay;
    private readonly TextBox _runInput;
    private readonly Border _runPanel;
    private readonly SettingsButton _runTaskButton;
    private readonly SettingsButton _submitRunButton;
    private readonly SettingsButton _cancelRunButton;
    private TaskManagerContextMenuWindow? _actionMenuWindow;
    private bool _externalSubscriptionsAttached;
    private bool _disposed;
#if DEBUG
    private double _pendingHotReloadHorizontalOffset;
    private double _pendingHotReloadVerticalOffset;
    private bool _hasPendingHotReloadOffsets;
    private bool _hasReceivedRows;
#endif

    protected TaskManagerTablePage(
        string title,
        TaskManagerTableSchema schema,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<string, bool> startProcess,
        string searchPlaceholder)
        : base(title, palette, resources)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(processIconService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(startProcess);

        _settings = settings;
        _palette = palette;
        _resources = resources;
        _startProcess = startProcess;
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainContent.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _runTaskButton = AddHeaderAction("Run new task", OnRunTaskClick);
        _searchBox = TrayAppDotNETSettingsUI.SearchTextBox(
            palette,
            resources.AxamlTaskManagerDetails.SearchWidth);
        _searchBox.PlaceholderText = searchPlaceholder;
        _searchBox.VerticalAlignment = VerticalAlignment.Top;
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchOverlay = new Grid
        {
            HorizontalAlignment = settings.LeftAlignProcessSearchBar
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = resources.AxamlTaskManagerDetails.SearchMargin,
            Children = { _searchBox }
        };

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
        _runPanel = BuildRunPanel();
        _runPanel.IsVisible = false;
        _runPanel.Margin = resources.AxamlTaskManagerDetails.RunPanelMargin;
        MainContent.Children.Add(_runPanel);

        _informationHost = new Grid
        {
            Margin = resources.AxamlTaskManagerTable.InformationMargin,
            IsVisible = false
        };
        Grid.SetRow(_informationHost, 1);
        MainContent.Children.Add(_informationHost);

        _table = new TaskManagerTableControl(
            schema,
            processIconService,
            settings,
            palette,
            resources);
        _table.SelectedRowChanged += OnSelectedRowChanged;
        _table.RowActivated += OnRowActivated;
        _table.GridZoomRequested += OnGridZoomRequested;
        _table.GridZoomResetRequested += OnGridZoomResetRequested;
        _table.GridRowSpacingRequested += OnGridRowSpacingRequested;
        _table.GridRowSpacingResetRequested += OnGridRowSpacingResetRequested;

        Grid tableSurface = new();
        tableSurface.Children.Add(_table);
        _emptyMessage = TrayAppDotNETSettingsUI.DescriptionText("No items to display.", palette);
        _emptyMessage.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyMessage.VerticalAlignment = VerticalAlignment.Center;
        _emptyMessage.IsHitTestVisible = false;
        tableSurface.Children.Add(_emptyMessage);

        _resizeGrip = new TaskManagerResizeGrip(resources);
        _tableScrollViewport = new SettingsScrollViewport(
            tableSurface,
            default,
            resources.AxamlProcessTable.GridBackgroundColor,
            TaskManagerScrollBarStyles.CreateProcessGrid(resources),
            TaskManagerContextMenuWindow.CreateOptions(
                palette,
                settings.EnableRoundedCorners,
                settings),
            _resizeGrip,
            overlayVerticalScrollBar: true)
        {
            Margin = resources.AxamlTaskManagerDetails.TableMargin
        };
        _tableScrollViewport.SetVerticalScrollBarTopInset(resources.AxamlProcessTable.HeaderHeight);
        _columnHeaderBorder = new Border
        {
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        ApplyColumnHeaderBorderResources();
        Grid.SetColumnSpan(_columnHeaderBorder, 2);
        _tableScrollViewport.Children.Add(_columnHeaderBorder);
        Grid.SetRow(_tableScrollViewport, 2);
        MainContent.Children.Add(_tableScrollViewport);

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public event Action<TaskManagerTableRow?>? SelectedRowChanged;
    public event Action<TaskManagerTableRow>? RowActivated;

    internal override Control? PageOverlay => _searchOverlay;

    /// <summary>Returns the generic search-box width; these pages have no leading search actions.</summary>
    public bool TryGetSearchDragRegionPixelWidths(
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
        searchWidth = Math.Abs(screenRight.X - screenLeft.X);
        return searchWidth > 0;
    }

    protected TaskManagerTableRow? SelectedRow => _table.SelectedRow;
    protected SettingsButton RunTaskButton => _runTaskButton;
    protected TaskManagerTableControl Table => _table;

#if DEBUG
    /// <summary>Captures interactive generic-page state before a hot-reload shell rebuild.</summary>
    internal TaskManagerTableHotReloadState CaptureHotReloadState() =>
        new(
            _searchBox.Text ?? string.Empty,
            _runInput.Text ?? string.Empty,
            _runPanel.IsVisible,
            _hasPendingHotReloadOffsets
                ? _pendingHotReloadHorizontalOffset
                : _tableScrollViewport.HorizontalOffset,
            _hasPendingHotReloadOffsets
                ? _pendingHotReloadVerticalOffset
                : _tableScrollViewport.VerticalOffset,
            _table.CaptureHotReloadState());

    /// <summary>Restores interactive generic-page state after a hot-reload shell rebuild.</summary>
    internal void RestoreHotReloadState(TaskManagerTableHotReloadState state)
    {
        if (_disposed) return;

        _table.RestoreHotReloadState(state.TableState);
        _searchBox.Text = state.SearchText;
        _table.SetFilter(state.SearchText);
        _runInput.Text = state.RunInputText;
        _runPanel.IsVisible = state.RunPanelVisible;
        _pendingHotReloadHorizontalOffset = state.HorizontalOffset;
        _pendingHotReloadVerticalOffset = state.VerticalOffset;
        _hasPendingHotReloadOffsets = true;
        ApplyPendingHotReloadOffsets();
        UpdateEmptyMessage();
    }

    private void ApplyPendingHotReloadOffsets()
    {
        if (!_hasPendingHotReloadOffsets || !_hasReceivedRows) return;

        UpdateLayout();
        _tableScrollViewport.SetOffsets(
            _pendingHotReloadHorizontalOffset,
            _pendingHotReloadVerticalOffset);
        _hasPendingHotReloadOffsets = false;
    }
#endif

    /// <summary>Adds a right-aligned page action and tracks its event subscription for disposal.</summary>
    protected SettingsButton AddHeaderAction(
        string label,
        EventHandler clickHandler,
        bool isEnabled = true)
    {
        SettingsButton button = TrayAppDotNETSettingsUI.Button(label, _palette);
        button.IsEnabled = isEnabled;
        button.Click += clickHandler;
        HeaderActions.Children.Add(button);
        _headerActionRegistrations.Add(new HeaderActionRegistration(button, clickHandler));
        return button;
    }

    /// <summary>Adds the standard compact More action used by Task Manager page headers.</summary>
    protected SettingsButton AddMoreAction(EventHandler clickHandler)
    {
        SettingsButton button = AddHeaderAction(string.Empty, clickHandler);
        button.Width = _resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        button.Height = _resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        button.MinHeight = _resources.AxamlTaskManagerReorderDialog.MoreButtonSize;
        button.Padding = _resources.AxamlTaskManagerReorderDialog.MoreButtonPadding;
        button.Label.FontSize = _resources.AxamlTaskManagerReorderDialog.MoreGlyphFontSize;
        GlyphApplicator.ApplyTo(button.Label, TaskManagerGlyphCatalog.MORE);
        TrayAppDotNETToolTip.SetTip(button, "More");
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        return button;
    }

    /// <summary>Sets an optional information strip between the run panel and table.</summary>
    protected void SetInformationContent(Control? content)
    {
        _informationHost.Children.Clear();
        _informationHost.IsVisible = content != null;
        if (content != null) _informationHost.Children.Add(content);
    }

    /// <summary>Replaces the table rows and updates the empty-state indicator.</summary>
    protected void SetRows(IReadOnlyList<TaskManagerTableRow> rows)
    {
        _table.SetRows(rows);
        UpdateEmptyMessage();
#if DEBUG
        _hasReceivedRows = true;
        ApplyPendingHotReloadOffsets();
#endif
    }

    protected void SetColumnTitle(int columnIndex, string title) =>
        _table.SetColumnTitle(columnIndex, title);

    /// <summary>Shows a standard Task Manager action menu below a header control.</summary>
    protected void ShowActionMenu(
        Control anchor,
        IReadOnlyList<ContextMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(entries);
        if (_disposed || TopLevel.GetTopLevel(anchor) is not Window owner) return;

        CloseActionMenu();
        TaskManagerContextMenuWindow menuWindow = new(
            entries,
            _palette,
            _settings.EnableRoundedCorners,
            _settings);
        _actionMenuWindow = menuWindow;
        menuWindow.Closed += OnActionMenuClosed;
        menuWindow.ShowAt(owner, anchor.PointToScreen(new Point(0, anchor.Bounds.Height)));
    }

    protected virtual void HandleSelectedRowChanged(TaskManagerTableRow? row)
    {
    }

    protected virtual void HandleRowActivated(TaskManagerTableRow row)
    {
    }

    private Border BuildRunPanel()
    {
        ColumnDefinition inputColumn = new(GridLength.Star)
        {
            MaxWidth = _resources.AxamlTaskManagerDetails.RunInputWidth
        };
        Grid actions = new()
        {
            ColumnSpacing = _resources.AxamlTaskManagerDetails.ToolbarSpacing,
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
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            CornerRadius = _resources.AxamlTaskManagerDetails.PanelCornerRadius,
            Padding = _resources.AxamlTaskManagerDetails.RunPanelPadding,
            Child = actions
        };
    }

    private void OnSelectedRowChanged(TaskManagerTableRow? row)
    {
        HandleSelectedRowChanged(row);
        SelectedRowChanged?.Invoke(row);
    }

    private void OnRowActivated(TaskManagerTableRow row)
    {
        HandleRowActivated(row);
        RowActivated?.Invoke(row);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        _table.SetFilter(_searchBox.Text);
        UpdateEmptyMessage();
    }

    private void UpdateEmptyMessage() => _emptyMessage.IsVisible = _table.VisibleRows.Count == 0;

    private void OnGridZoomRequested(int direction)
    {
        if (direction == 0) return;

        double fontSize = Math.Clamp(
            _settings.GridFontSize + Math.Sign(direction) * GridFontZoomStep,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum);
        ApplyGridTypography(fontSize, _settings.GridRowSpacing);
    }

    private void OnGridZoomResetRequested() =>
        ApplyGridTypography(AppSettings.GridFontSizeDefault, _settings.GridRowSpacing);

    private void OnGridRowSpacingRequested(int direction)
    {
        if (direction == 0) return;

        double rowSpacing = Math.Clamp(
            _settings.GridRowSpacing + Math.Sign(direction) * GridRowSpacingStep,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum);
        ApplyGridTypography(_settings.GridFontSize, rowSpacing);
    }

    private void OnGridRowSpacingResetRequested() =>
        ApplyGridTypography(_settings.GridFontSize, AppSettings.GridRowSpacingDefault);

    private void ApplyGridTypography(double fontSize, double rowSpacing)
    {
        _table.SetGridTypography(fontSize, rowSpacing);
        _settings.UpdateGridMetrics(fontSize, _table.RowHeight, rowSpacing);
    }

    private void OnRunTaskClick(object? sender, EventArgs eventArgs)
    {
        _runPanel.IsVisible = true;
        _runInput.Focus();
        _runInput.SelectAll();
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
    }

    private void HideRunPanel()
    {
        _runPanel.IsVisible = false;
        _runTaskButton.Focus();
    }

    private void ApplyColumnHeaderBorderResources()
    {
        double borderThickness = _resources.AxamlProcessTable.GridLineThickness;
        _columnHeaderBorder.BorderThickness = new Thickness(0, 0, 0, borderThickness);
        _columnHeaderBorder.Height = _resources.AxamlProcessTable.HeaderHeight + borderThickness / 2;
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs eventArgs)
    {
        if (_disposed || _externalSubscriptionsAttached) return;

        _table.AttachExternalSubscriptions();
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded += OnContextMenuAXAMLResourcesReloaded;
#endif
        _externalSubscriptionsAttached = true;
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs eventArgs) => DetachExternalSubscriptions();

    private void DetachExternalSubscriptions()
    {
        if (!_externalSubscriptionsAttached) return;

        _externalSubscriptionsAttached = false;
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded -= OnContextMenuAXAMLResourcesReloaded;
#endif
        _table.DetachExternalSubscriptions();
    }

#if DEBUG
    private void OnContextMenuAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        _tableScrollViewport.SetContextMenuOptions(
            TaskManagerContextMenuWindow.CreateOptions(
                _palette,
                _settings.EnableRoundedCorners,
                _settings));
        CloseActionMenu();
    }
#endif

    private void CloseActionMenu()
    {
        TaskManagerContextMenuWindow? menuWindow = _actionMenuWindow;
        if (menuWindow == null) return;

        _actionMenuWindow = null;
        menuWindow.Closed -= OnActionMenuClosed;
        menuWindow.Close();
    }

    private void OnActionMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerContextMenuWindow menuWindow)
            menuWindow.Closed -= OnActionMenuClosed;
        if (ReferenceEquals(sender, _actionMenuWindow)) _actionMenuWindow = null;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        DetachExternalSubscriptions();
        _searchBox.TextChanged -= OnSearchTextChanged;
        _runInput.KeyDown -= OnRunInputKeyDown;
        _submitRunButton.Click -= OnSubmitRunClick;
        _cancelRunButton.Click -= OnCancelRunClick;
        _table.SelectedRowChanged -= OnSelectedRowChanged;
        _table.RowActivated -= OnRowActivated;
        _table.GridZoomRequested -= OnGridZoomRequested;
        _table.GridZoomResetRequested -= OnGridZoomResetRequested;
        _table.GridRowSpacingRequested -= OnGridRowSpacingRequested;
        _table.GridRowSpacingResetRequested -= OnGridRowSpacingResetRequested;
        for (int registrationIndex = 0;
             registrationIndex < _headerActionRegistrations.Count;
             registrationIndex++)
        {
            HeaderActionRegistration registration = _headerActionRegistrations[registrationIndex];
            registration.Button.Click -= registration.ClickHandler;
        }
        _headerActionRegistrations.Clear();
        CloseActionMenu();
        SelectedRowChanged = null;
        RowActivated = null;
        _tableScrollViewport.Dispose();
        _table.Dispose();
    }

    private readonly record struct HeaderActionRegistration(
        SettingsButton Button,
        EventHandler ClickHandler);
}

#if DEBUG
/// <summary>Interactive generic-page state retained across a Debug hot-reload rebuild.</summary>
internal readonly record struct TaskManagerTableHotReloadState(
    string SearchText,
    string RunInputText,
    bool RunPanelVisible,
    double HorizontalOffset,
    double VerticalOffset,
    TaskManagerTableControlHotReloadState TableState);
#endif

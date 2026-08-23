using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Builds the Details toolbar around the allocation-light painted process table.</summary>
internal sealed class ProcessDetailsPage : Grid, IDisposable
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly Action<ProcessTerminationTarget?> _armTerminationTarget;
    private readonly Func<ProcessTerminationTarget, bool> _terminateProcess;
    private readonly Func<string, bool> _startProcess;
    private readonly AppSettings _settings;
    private readonly ProcessDetailsCanvas _processCanvas;
    private readonly TextBox _searchBox;
    private readonly TextBox _runInput;
    private readonly Border _runPanel;
    private readonly SettingsButton _runTaskButton;
    private readonly SettingsButton _columnsButton;
    private readonly SettingsButton _endTaskButton;
    private readonly SettingsButton _submitRunButton;
    private readonly SettingsButton _cancelRunButton;
    private readonly SettingsScrollViewport _tableScrollViewport;
    private readonly Border _hoverHighlight;
    private readonly Border _selectionHighlight;
    private readonly TranslateTransform _hoverTransform = new();
    private readonly TranslateTransform _selectionTransform = new();
    private ProcessColumnChooserWindow? _columnChooserWindow;
    private bool _disposed;

    public ProcessDetailsPage(
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<ProcessTerminationTarget?> armTerminationTarget,
        Func<ProcessTerminationTarget, bool> terminateProcess,
        Func<string, bool> startProcess)
    {
        _snapshotService = snapshotService;
        _settings = settings;
        _armTerminationTarget = armTerminationTarget;
        _terminateProcess = terminateProcess;
        _startProcess = startProcess;
        ProcessDataSchema schema = ProcessDataSchema.Create(settings.DetailsColumns);
        _snapshotService.SetActiveSchema(schema);
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _processCanvas = new ProcessDetailsCanvas(
            processIconService,
            schema,
            settings.DetailsColumns,
            palette,
            resources);
        _processCanvas.SelectedProcessChanged += OnSelectedProcessChanged;
        _processCanvas.HoverRowTopChanged += OnHoverRowTopChanged;
        _processCanvas.SelectionRowTopChanged += OnSelectionRowTopChanged;
        _processCanvas.ColumnsRequested += OnColumnsRequested;
        _processCanvas.ColumnLayoutChanged += OnColumnLayoutChanged;

        _runTaskButton = TrayAppDotNETSettingsUI.Button("Run new task", palette);
        _runTaskButton.Click += OnRunTaskClick;
        _columnsButton = TrayAppDotNETSettingsUI.Button("Columns", palette);
        _columnsButton.Click += OnColumnsClick;
        _endTaskButton = TrayAppDotNETSettingsUI.Button("End task", palette);
        _endTaskButton.IsEnabled = false;
        _endTaskButton.Click += OnEndTaskClick;

        Grid titleBar = BuildTitleBar(palette, resources);
        titleBar.Margin = resources.AxamlTaskManagerDetails.HeaderMargin;
        Children.Add(titleBar);

        _searchBox = TrayAppDotNETSettingsUI.TextBox(
            palette,
            resources.AxamlTaskManagerDetails.SearchWidth);
        _searchBox.PlaceholderText = "Type a name, user, or PID to search";
        _searchBox.HorizontalAlignment = HorizontalAlignment.Left;
        _searchBox.Margin = resources.AxamlTaskManagerDetails.SearchMargin;
        _searchBox.TextChanged += OnSearchTextChanged;
        Grid.SetRow(_searchBox, 1);
        Children.Add(_searchBox);

        _runInput = TrayAppDotNETSettingsUI.TextBox(
            palette,
            resources.AxamlTaskManagerDetails.RunInputWidth);
        _runInput.PlaceholderText = "Executable, document, or URI";
        _runInput.KeyDown += OnRunInputKeyDown;
        _submitRunButton = TrayAppDotNETSettingsUI.Button("Run", palette);
        _submitRunButton.Click += OnSubmitRunClick;
        _cancelRunButton = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        _cancelRunButton.Click += OnCancelRunClick;
        _runPanel = BuildRunPanel(palette, resources);
        _runPanel.IsVisible = false;
        _runPanel.Margin = resources.AxamlTaskManagerDetails.RunPanelMargin;
        Grid.SetRow(_runPanel, 2);
        Children.Add(_runPanel);

        SettingsScrollBarStyle scrollBarStyle = new(
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
        _hoverHighlight = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Hover),
            Height = resources.AxamlProcessTable.RowHeight,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransform = _hoverTransform
        };
        _selectionHighlight = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.SearchListItemSelected),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Accent),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Height = resources.AxamlProcessTable.RowHeight,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransform = _selectionTransform
        };
        Grid tableSurface = new();
        tableSurface.Children.Add(_hoverHighlight);
        tableSurface.Children.Add(_selectionHighlight);
        tableSurface.Children.Add(_processCanvas);

        _tableScrollViewport = new SettingsScrollViewport(
            tableSurface,
            default,
            TaskManagerWindowResources.ProcessGridBackgroundColor,
            scrollBarStyle,
            new TaskManagerResizeGrip(resources))
        {
            Margin = resources.AxamlTaskManagerDetails.TableMargin
        };
        Grid.SetRow(_tableScrollViewport, 3);
        Children.Add(_tableScrollViewport);

        _snapshotService.SnapshotAvailable += OnSnapshotAvailable;
        _processCanvas.RefreshFrom(_snapshotService);
    }

    private Grid BuildTitleBar(SettingsPalette palette, TaskManagerWindowResources resources)
    {
        Grid titleBar = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            "Details",
            palette,
            resources.AxamlTaskManagerDetails.TitleFontSize,
            FontWeight.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleBar.Children.Add(title);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = resources.AxamlTaskManagerDetails.ToolbarSpacing,
            Children = { _runTaskButton, _columnsButton, _endTaskButton }
        };
        Grid.SetColumn(actions, 1);
        titleBar.Children.Add(actions);
        return titleBar;
    }

    private Border BuildRunPanel(SettingsPalette palette, TaskManagerWindowResources resources)
    {
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = resources.AxamlTaskManagerDetails.ToolbarSpacing,
            Children = { _runInput, _submitRunButton, _cancelRunButton }
        };
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

    private void OnSelectedProcessChanged(ProcessTerminationTarget? target)
    {
        _endTaskButton.IsEnabled = target.HasValue;
        _armTerminationTarget(target);
    }

    private void OnHoverRowTopChanged(double? rowTop)
    {
        _hoverHighlight.IsVisible = rowTop.HasValue;
        if (rowTop.HasValue) _hoverTransform.Y = rowTop.Value;
    }

    private void OnSelectionRowTopChanged(double? rowTop)
    {
        _selectionHighlight.IsVisible = rowTop.HasValue;
        if (rowTop.HasValue) _selectionTransform.Y = rowTop.Value;
    }

    private void OnColumnsRequested() => ShowColumnChooser();

    private void OnColumnLayoutChanged(List<ProcessColumnSetting> settings) =>
        _settings.UpdateDetailsColumnLayout(settings);

    private void OnColumnsClick(object? sender, EventArgs eventArgs) => ShowColumnChooser();

    private void ShowColumnChooser()
    {
        if (_columnChooserWindow != null)
        {
            _columnChooserWindow.Activate();
            return;
        }

        _columnChooserWindow = new ProcessColumnChooserWindow(
            _settings.DetailsColumns,
            ApplyColumnSettings);
        _columnChooserWindow.Closed += OnColumnChooserClosed;
        if (TopLevel.GetTopLevel(this) is Window owner)
            _columnChooserWindow.Show(owner);
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

    private void ApplyColumnSettings(List<ProcessColumnSetting> settings)
    {
        _settings.DetailsColumns = settings;
        Dispatcher.UIThread.Post(RefreshColumnsAfterApply, DispatcherPriority.Background);
    }

    private void RefreshColumnsAfterApply()
    {
        if (_disposed) return;
        if (TopLevel.GetTopLevel(this) is TaskManagerWindow window)
            window.RefreshDetailsColumns();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs eventArgs) =>
        _processCanvas.SetFilter(_searchBox.Text);

    private void OnRunTaskClick(object? sender, EventArgs eventArgs)
    {
        _runPanel.IsVisible = true;
        _runInput.Focus();
        _runInput.SelectAll();
    }

    private void OnEndTaskClick(object? sender, EventArgs eventArgs)
    {
        ProcessTerminationTarget? target = _processCanvas.SelectedTerminationTarget;
        if (!target.HasValue) return;
        if (_terminateProcess(target.Value))
            _snapshotService.RequestRefresh();
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
        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _processCanvas.SelectedProcessChanged -= OnSelectedProcessChanged;
        _processCanvas.HoverRowTopChanged -= OnHoverRowTopChanged;
        _processCanvas.SelectionRowTopChanged -= OnSelectionRowTopChanged;
        _processCanvas.ColumnsRequested -= OnColumnsRequested;
        _processCanvas.ColumnLayoutChanged -= OnColumnLayoutChanged;
        _searchBox.TextChanged -= OnSearchTextChanged;
        _runInput.KeyDown -= OnRunInputKeyDown;
        _runTaskButton.Click -= OnRunTaskClick;
        _columnsButton.Click -= OnColumnsClick;
        _endTaskButton.Click -= OnEndTaskClick;
        _submitRunButton.Click -= OnSubmitRunClick;
        _cancelRunButton.Click -= OnCancelRunClick;
        if (_columnChooserWindow != null)
        {
            _columnChooserWindow.Closed -= OnColumnChooserClosed;
            _columnChooserWindow.Close();
            _columnChooserWindow = null;
        }
        _tableScrollViewport.Dispose();
        _processCanvas.Dispose();
    }
}

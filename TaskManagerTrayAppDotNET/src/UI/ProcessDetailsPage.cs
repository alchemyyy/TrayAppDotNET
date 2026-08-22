using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Builds the Details toolbar around the allocation-light painted process table.</summary>
internal sealed class ProcessDetailsPage : Grid, IDisposable
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly Func<int, bool> _terminateProcess;
    private readonly Func<string, bool> _startProcess;
    private readonly ProcessDetailsCanvas _processCanvas;
    private readonly TextBox _searchBox;
    private readonly TextBox _runInput;
    private readonly Border _runPanel;
    private readonly SettingsButton _runTaskButton;
    private readonly SettingsButton _endTaskButton;
    private readonly SettingsButton _submitRunButton;
    private readonly SettingsButton _cancelRunButton;
    private readonly SettingsScrollViewport _tableScrollViewport;
    private bool _disposed;

    public ProcessDetailsPage(
        ProcessSnapshotService snapshotService,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<int, bool> terminateProcess,
        Func<string, bool> startProcess)
    {
        _snapshotService = snapshotService;
        _terminateProcess = terminateProcess;
        _startProcess = startProcess;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _processCanvas = new ProcessDetailsCanvas(palette, resources);
        _processCanvas.SelectedProcessChanged += OnSelectedProcessChanged;

        _runTaskButton = TrayAppDotNETSettingsUI.Button("Run new task", palette);
        _runTaskButton.Click += OnRunTaskClick;
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
        _tableScrollViewport = new SettingsScrollViewport(
            _processCanvas,
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
            Children = { _runTaskButton, _endTaskButton }
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

    private void OnSelectedProcessChanged(int? processID) =>
        _endTaskButton.IsEnabled = processID.HasValue;

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
        int? processID = _processCanvas.SelectedProcessID;
        if (!processID.HasValue) return;
        if (_terminateProcess(processID.Value))
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
        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _processCanvas.SelectedProcessChanged -= OnSelectedProcessChanged;
        _searchBox.TextChanged -= OnSearchTextChanged;
        _runInput.KeyDown -= OnRunInputKeyDown;
        _runTaskButton.Click -= OnRunTaskClick;
        _endTaskButton.Click -= OnEndTaskClick;
        _submitRunButton.Click -= OnSubmitRunClick;
        _cancelRunButton.Click -= OnCancelRunClick;
        _tableScrollViewport.Dispose();
        _processCanvas.Dispose();
    }
}

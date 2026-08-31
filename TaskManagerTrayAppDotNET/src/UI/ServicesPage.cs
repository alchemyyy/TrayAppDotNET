using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Enumerates and controls local Windows services without blocking the UI thread.</summary>
internal sealed class ServicesPage : TaskManagerTablePage
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly WindowsServiceManager _serviceManager;
    private readonly Func<string, bool> _startProcess;
    private readonly Func<WindowsServiceSnapshot, Task<bool>> _confirmDisable;
    private readonly Action<string, string> _reportMessage;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SettingsButton _startButton;
    private readonly SettingsButton _stopButton;
    private readonly SettingsButton _restartButton;
    private readonly SettingsButton _disableButton;
    private readonly SettingsButton _openServicesButton;
    private readonly SettingsButton _moreButton;
    private bool _queryPending;
    private bool _operationPending;
    private bool _isPageActive;
    private bool _disposed;

    public ServicesPage(
        WindowsServiceManager serviceManager,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<string, bool> startProcess,
        Func<WindowsServiceSnapshot, Task<bool>> confirmDisable,
        Action<string, string> reportMessage)
        : base(
            title: "Services",
            CreateSchema(resources),
            processIconService,
            settings,
            palette,
            resources,
            startProcess,
            searchPlaceholder: "Search services")
    {
        _serviceManager = serviceManager;
        _startProcess = startProcess;
        _confirmDisable = confirmDisable;
        _reportMessage = reportMessage;
        _startButton = AddHeaderAction(label: "Start", OnStartClick, isEnabled: false);
        _stopButton = AddHeaderAction(label: "Stop", OnStopClick, isEnabled: false);
        _restartButton = AddHeaderAction(label: "Restart", OnRestartClick, isEnabled: false);
        _disableButton = AddHeaderAction(label: "Disable", OnDisableClick, isEnabled: false);
        _openServicesButton = AddHeaderAction(label: "Open Services", OnOpenServicesClick);
        _moreButton = AddMoreAction(OnMoreClick);

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    private static TaskManagerTableSchema CreateSchema(TaskManagerWindowResources resources) =>
        new(
        [
            new TaskManagerTableColumn(
                Key: "name",
                Title: "Name",
                resources.AxamlTaskManagerTable.ServicesNameColumnWidth),
            new TaskManagerTableColumn(
                Key: "pid",
                Title: "PID",
                resources.AxamlTaskManagerTable.ServicesPIDColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                Key: "description",
                Title: "Description",
                resources.AxamlTaskManagerTable.ServicesDescriptionColumnWidth),
            new TaskManagerTableColumn(
                Key: "status",
                Title: "Status",
                resources.AxamlTaskManagerTable.ServicesStatusColumnWidth),
            new TaskManagerTableColumn(
                Key: "group",
                Title: "Group",
                resources.AxamlTaskManagerTable.ServicesGroupColumnWidth)
        ], resources.AxamlTaskManagerTable.MinimumColumnWidth);

    protected override void HandleSelectedRowChanged(TaskManagerTableRow? row) =>
        UpdateActionButtons(row?.Tag as WindowsServiceSnapshot);

    protected override void HandleRowActivated(TaskManagerTableRow row)
    {
        if (row.Tag is not WindowsServiceSnapshot service) return;
        WindowsServiceActionState state = WindowsServiceState.GetActionState(service);
        if (state.CanStart) _ = RunActionAsync(WindowsServiceAction.Start, service);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs eventArgs) =>
        _ = RefreshAsync(reportFailure: false, refreshConfiguration: false);

    internal override void SetPageActive(bool isActive)
    {
        if (_disposed || _isPageActive == isActive) return;

        _isPageActive = isActive;
        if (isActive)
        {
            _refreshTimer.Start();
            _ = RefreshAsync(reportFailure: true, refreshConfiguration: false);
            return;
        }

        _refreshTimer.Stop();
    }

    private async Task RefreshAsync(bool reportFailure, bool refreshConfiguration)
    {
        if (_disposed || !_isPageActive || _queryPending) return;

        _queryPending = true;
        try
        {
            WindowsServiceQueryResult
                result = await Task.Run(() => _serviceManager.QueryServices(refreshConfiguration));
            if (_disposed || !_isPageActive) return;
            if (!result.Succeeded)
            {
                if (reportFailure)
                {
                    _reportMessage(
                        arg1: "Services unavailable",
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Windows could not enumerate local services."
                            : result.ErrorMessage);
                }

                return;
            }

            List<TaskManagerTableRow> rows = new(result.Services.Count);
            for (int serviceIndex = 0; serviceIndex < result.Services.Count; serviceIndex++)
            {
                WindowsServiceSnapshot service = result.Services[serviceIndex];
                rows.Add(CreateRow(service));
            }

            SetRows(rows);
            UpdateActionButtons(SelectedRow?.Tag as WindowsServiceSnapshot);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Service refresh failed: {exception}");
            if (reportFailure && !_disposed)
                _reportMessage(arg1: "Services unavailable", exception.Message);
        }
        finally
        {
            _queryPending = false;
        }
    }

    private static TaskManagerTableRow CreateRow(WindowsServiceSnapshot service)
    {
        string pidText = service.PID == 0 ? string.Empty : service.PID.ToString();
        return new TaskManagerTableRow
        {
            Key = service.ServiceName,
            Tag = service,
            Cells =
            [
                TaskManagerTableCell.TextCell(service.ServiceName),
                TaskManagerTableCell.UnsignedCell(pidText, service.PID),
                TaskManagerTableCell.TextCell(service.Description),
                TaskManagerTableCell.TextCell(WindowsServiceState.GetStatusText(service.Status)),
                TaskManagerTableCell.TextCell(service.Group)
            ]
        };
    }

    private void UpdateActionButtons(WindowsServiceSnapshot? service)
    {
        WindowsServiceActionState state = service == null
            ? default
            : WindowsServiceState.GetActionState(service);
        _startButton.IsEnabled = !_operationPending && state.CanStart;
        _stopButton.IsEnabled = !_operationPending && state.CanStop;
        _restartButton.IsEnabled = !_operationPending && state.CanRestart;
        _disableButton.IsEnabled = !_operationPending && state.CanDisable;
    }

    private void OnStartClick(object? sender, EventArgs eventArgs) =>
        RunSelectedAction(WindowsServiceAction.Start);

    private void OnStopClick(object? sender, EventArgs eventArgs) =>
        RunSelectedAction(WindowsServiceAction.Stop);

    private void OnRestartClick(object? sender, EventArgs eventArgs) =>
        RunSelectedAction(WindowsServiceAction.Restart);

    private void OnDisableClick(object? sender, EventArgs eventArgs) =>
        RunSelectedAction(WindowsServiceAction.Disable);

    private void RunSelectedAction(WindowsServiceAction action)
    {
        if (SelectedRow?.Tag is WindowsServiceSnapshot service)
            _ = RunActionAsync(action, service);
    }

    private async Task RunActionAsync(
        WindowsServiceAction action,
        WindowsServiceSnapshot service)
    {
        if (_disposed || _operationPending || !CanRunAction(service, action)) return;
        if (action == WindowsServiceAction.Disable && !await _confirmDisable(service)) return;
        if (_disposed) return;

        _operationPending = true;
        UpdateActionButtons(service);
        try
        {
            WindowsServiceOperationResult result = await Task.Run(() => action switch
            {
                WindowsServiceAction.Start => WindowsServiceManager.Start(service.ServiceName),
                WindowsServiceAction.Stop => WindowsServiceManager.Stop(service.ServiceName),
                WindowsServiceAction.Restart => WindowsServiceManager.Restart(service.ServiceName),
                WindowsServiceAction.Disable => _serviceManager.Disable(service.ServiceName),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, message: "Unknown service action.")
            });
            if (_disposed) return;
            if (!result.Succeeded)
            {
                string actionName = action.ToString().ToLowerInvariant();
                _reportMessage(
                    $"Service {actionName} failed",
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? $"Windows could not {actionName} '{service.ServiceName}'."
                        : result.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Service {action} failed for '{service.ServiceName}': {exception}");
            if (!_disposed) _reportMessage($"Service {action} failed", exception.Message);
        }
        finally
        {
            _operationPending = false;
            if (!_disposed)
            {
                UpdateActionButtons(SelectedRow?.Tag as WindowsServiceSnapshot);
                await RefreshAsync(
                    reportFailure: false,
                    action == WindowsServiceAction.Disable);
            }
        }
    }

    private static bool CanRunAction(
        WindowsServiceSnapshot service,
        WindowsServiceAction action)
    {
        WindowsServiceActionState state = WindowsServiceState.GetActionState(service);
        return action switch
        {
            WindowsServiceAction.Start => state.CanStart,
            WindowsServiceAction.Stop => state.CanStop,
            WindowsServiceAction.Restart => state.CanRestart,
            WindowsServiceAction.Disable => state.CanDisable,
            _ => false
        };
    }

    private void OnOpenServicesClick(object? sender, EventArgs eventArgs) =>
        _ = _startProcess("services.msc");

    private void OnMoreClick(object? sender, EventArgs eventArgs)
    {
        ContextMenuEntryBuilder entries = new();
        if (SelectedRow?.Tag is WindowsServiceSnapshot service)
        {
            WindowsServiceActionState state = WindowsServiceState.GetActionState(service);
            if (!_operationPending && state.CanStart)
                entries.Add(text: "Start", () => _ = RunActionAsync(WindowsServiceAction.Start, service));
            if (!_operationPending && state.CanStop)
                entries.Add(text: "Stop", () => _ = RunActionAsync(WindowsServiceAction.Stop, service));
            if (!_operationPending && state.CanRestart)
                entries.Add(text: "Restart", () => _ = RunActionAsync(WindowsServiceAction.Restart, service));
            if (!_operationPending && state.CanDisable)
                entries.Add(text: "Disable", () => _ = RunActionAsync(WindowsServiceAction.Disable, service));
            if (entries.Count > 0) entries.AddSeparator();
        }

        entries.Add(text: "Refresh", () =>
            _ = RefreshAsync(reportFailure: true, refreshConfiguration: true));
        entries.Add(text: "Open Services", () => _ = _startProcess("services.msc"));
        ShowActionMenu(_moreButton, entries.ToList());
    }

    public override void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        _refreshTimer.Tick -= OnRefreshTimerTick;
        base.Dispose();
    }
}

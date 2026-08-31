using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays conventional Windows startup registrations and their approval state.</summary>
internal sealed class StartupAppsPage : TaskManagerTablePage
{
    private readonly StartupAppsService _startupAppsService;
    private readonly Action<string, string> _reportMessage;
    private readonly SettingsButton _enableButton;
    private readonly SettingsButton _disableButton;
    private readonly SettingsButton _propertiesButton;
    private readonly SettingsButton _moreButton;
    private bool _queryPending;
    private bool _operationPending;
    private bool _isPageActive;
    private bool _disposed;

    public StartupAppsPage(
        StartupAppsService startupAppsService,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<string, bool> startProcess,
        Action<string, string> reportMessage)
        : base(
            title: "Startup apps",
            CreateSchema(resources),
            processIconService,
            settings,
            palette,
            resources,
            startProcess,
            searchPlaceholder: "Search startup apps and publishers")
    {
        _startupAppsService = startupAppsService;
        _reportMessage = reportMessage;
        _enableButton = AddHeaderAction(label: "Enable", OnEnableClick, isEnabled: false);
        _disableButton = AddHeaderAction(label: "Disable", OnDisableClick, isEnabled: false);
        _propertiesButton = AddHeaderAction(label: "Properties", OnPropertiesClick, isEnabled: false);
        _moreButton = AddMoreAction(OnMoreClick);
    }

    private static TaskManagerTableSchema CreateSchema(TaskManagerWindowResources resources) =>
        new(
        [
            new TaskManagerTableColumn(
                Key: "name",
                Title: "Name",
                resources.AxamlTaskManagerTable.StartupNameColumnWidth),
            new TaskManagerTableColumn(
                Key: "publisher",
                Title: "Publisher",
                resources.AxamlTaskManagerTable.StartupPublisherColumnWidth),
            new TaskManagerTableColumn(
                Key: "status",
                Title: "Status",
                resources.AxamlTaskManagerTable.StartupStatusColumnWidth),
            new TaskManagerTableColumn(
                Key: "impact",
                Title: "Startup impact",
                resources.AxamlTaskManagerTable.StartupImpactColumnWidth)
        ], resources.AxamlTaskManagerTable.MinimumColumnWidth);

    protected override void HandleSelectedRowChanged(TaskManagerTableRow? row) =>
        UpdateActionButtons(row?.Tag as StartupAppEntry);

    protected override void HandleRowActivated(TaskManagerTableRow row)
    {
        if (row.Tag is StartupAppEntry { ActionEligibility.CanShowProperties: true } entry)
            ShowProperties(entry);
    }

    internal override void SetPageActive(bool isActive)
    {
        if (_disposed || _isPageActive == isActive) return;

        _isPageActive = isActive;
        if (isActive) _ = RefreshAsync(true);
    }

    private async Task RefreshAsync(bool reportFailure)
    {
        if (_disposed || !_isPageActive || _queryPending) return;

        _queryPending = true;
        try
        {
            IReadOnlyList<StartupAppEntry> entries = await Task.Run(
                _startupAppsService.ReadEntries);
            if (_disposed || !_isPageActive) return;

            List<TaskManagerTableRow> rows = new(entries.Count);
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                rows.Add(CreateRow(entries[entryIndex]));
            SetRows(rows);
            UpdateActionButtons(SelectedRow?.Tag as StartupAppEntry);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Startup Apps refresh failed: {exception}");
            if (reportFailure && !_disposed)
                _reportMessage(arg1: "Startup apps unavailable", exception.Message);
        }
        finally
        {
            _queryPending = false;
        }
    }

    private static TaskManagerTableRow CreateRow(StartupAppEntry entry)
    {
        string executablePath = entry.ExecutablePath ?? entry.TargetPath ?? string.Empty;
        return new TaskManagerTableRow
        {
            Key = CreateKey(entry.Identity),
            IconSource = new ProcessIconSource(
                executablePath.Length == 0 ? null : executablePath,
                ApplicationUserModelID: null),
            Tag = entry,
            IsEnabled = entry.Status != StartupAppStatus.Disabled,
            Cells =
            [
                TaskManagerTableCell.TextCell(entry.Name),
                TaskManagerTableCell.TextCell(entry.Publisher),
                TaskManagerTableCell.TextCell(GetStatusText(entry.Status)),
                TaskManagerTableCell.TextCell(GetImpactText(entry.Impact))
            ]
        };
    }

    private static string CreateKey(StartupAppIdentity identity) =>
        $"{identity.Scope}:{identity.SourceKind}:{identity.RegistryView}:"
        + $"{identity.SourceLocation}:{identity.EntryName}";

    private static string GetStatusText(StartupAppStatus status) => status switch
    {
        StartupAppStatus.Enabled => "Enabled",
        StartupAppStatus.Disabled => "Disabled",
        _ => "Unknown"
    };

    private static string GetImpactText(StartupAppImpact impact) => impact switch
    {
        StartupAppImpact.None => "None",
        StartupAppImpact.Low => "Low",
        StartupAppImpact.Medium => "Medium",
        StartupAppImpact.High => "High",
        _ => "Not measured"
    };

    private void UpdateActionButtons(StartupAppEntry? entry)
    {
        StartupAppActionEligibility eligibility = entry?.ActionEligibility ?? default;
        _enableButton.IsEnabled = !_operationPending && eligibility.CanEnable;
        _disableButton.IsEnabled = !_operationPending && eligibility.CanDisable;
        _propertiesButton.IsEnabled = !_operationPending && eligibility.CanShowProperties;
    }

    private void OnEnableClick(object? sender, EventArgs eventArgs) =>
        RunSelectedStatusAction(StartupAppStatus.Enabled);

    private void OnDisableClick(object? sender, EventArgs eventArgs) =>
        RunSelectedStatusAction(StartupAppStatus.Disabled);

    private void RunSelectedStatusAction(StartupAppStatus status)
    {
        if (SelectedRow?.Tag is StartupAppEntry entry)
            _ = ChangeStatusAsync(entry, status);
    }

    private async Task ChangeStatusAsync(StartupAppEntry entry, StartupAppStatus status)
    {
        if (_disposed || _operationPending) return;
        StartupAppActionEligibility eligibility = entry.ActionEligibility;
        if (status == StartupAppStatus.Enabled && !eligibility.CanEnable) return;
        if (status == StartupAppStatus.Disabled && !eligibility.CanDisable) return;

        _operationPending = true;
        UpdateActionButtons(entry);
        try
        {
            StartupAppActionResult result = await Task.Run(() => status switch
            {
                StartupAppStatus.Enabled => _startupAppsService.Enable(entry),
                StartupAppStatus.Disabled => _startupAppsService.Disable(entry),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: "Unknown startup status.")
            });
            if (_disposed) return;
            if (!result.Succeeded)
            {
                _reportMessage(
                    arg1: "Startup app change failed",
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? $"Windows could not mark '{entry.Name}' {status.ToString().ToLowerInvariant()}."
                        : result.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Startup app status change failed for '{entry.Name}': {exception}");
            if (!_disposed) _reportMessage(arg1: "Startup app change failed", exception.Message);
        }
        finally
        {
            _operationPending = false;
            if (!_disposed)
            {
                UpdateActionButtons(SelectedRow?.Tag as StartupAppEntry);
                await RefreshAsync(false);
            }
        }
    }

    private void OnPropertiesClick(object? sender, EventArgs eventArgs)
    {
        if (SelectedRow?.Tag is StartupAppEntry entry) ShowProperties(entry);
    }

    private void ShowProperties(StartupAppEntry entry)
    {
        if (ShellFileActions.TryShowProperties(entry.TargetPath, out string errorMessage)) return;
        _reportMessage(arg1: "Properties unavailable", errorMessage);
    }

    private void OnMoreClick(object? sender, EventArgs eventArgs)
    {
        ContextMenuEntryBuilder entries = new();
        if (SelectedRow?.Tag is StartupAppEntry entry)
        {
            StartupAppActionEligibility eligibility = entry.ActionEligibility;
            if (!_operationPending && eligibility.CanEnable)
                entries.Add(text: "Enable", () => _ = ChangeStatusAsync(entry, StartupAppStatus.Enabled));
            if (!_operationPending && eligibility.CanDisable)
                entries.Add(text: "Disable", () => _ = ChangeStatusAsync(entry, StartupAppStatus.Disabled));
            if (!_operationPending && eligibility.CanShowProperties)
                entries.Add(text: "Properties", () => ShowProperties(entry));
            if (entries.Count > 0) entries.AddSeparator();
        }

        entries.Add(text: "Refresh", () => _ = RefreshAsync(true));
        ShowActionMenu(_moreButton, entries.ToList());
    }

    public override void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        _startupAppsService.Dispose();
        base.Dispose();
    }
}

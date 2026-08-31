using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays interactive Windows sessions and their process resource usage.</summary>
internal sealed class UsersPage : TaskManagerTablePage
{
    private static readonly int[] NoWarmProcessIDs = [];

    private readonly ProcessSnapshotService _snapshotService;
    private readonly WindowsUserSessionService _sessionService;
    private readonly ProcessDataSchema _schema;
    private readonly Func<string, bool> _startProcess;
    private readonly Action<string, string> _reportMessage;
    private readonly ProcessSnapshotBuffer _snapshot = new();
    private readonly SettingsButton _disconnectButton;
    private readonly SettingsButton _manageUsersButton;
    private readonly SettingsButton _moreButton;
    private long _snapshotVersion = -1;
    private bool _disconnectPending;
    private bool _refreshPending;
    private bool _refreshRequested;
    private bool _isPageActive;
    private bool _disposed;

    public UsersPage(
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService,
        WindowsUserSessionService sessionService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<string, bool> startProcess,
        Action<string, string> reportMessage)
        : base(
            title: "Users",
            CreateSchema(resources),
            processIconService,
            settings,
            palette,
            resources,
            startProcess,
            searchPlaceholder: "Search users and processes")
    {
        _snapshotService = snapshotService;
        _sessionService = sessionService;
        _startProcess = startProcess;
        _reportMessage = reportMessage;
        _schema = ProcessDataSchema.Create(
            [],
            UserSnapshotBuilder.RequiredColumnMask);
        _disconnectButton = AddHeaderAction(label: "Disconnect", OnDisconnectClick, isEnabled: false);
        _manageUsersButton = AddHeaderAction(label: "Manage user accounts", OnManageUsersClick);
        _moreButton = AddMoreAction(OnMoreClick);
    }

    private static TaskManagerTableSchema CreateSchema(TaskManagerWindowResources resources) =>
        new(
        [
            new TaskManagerTableColumn(
                Key: "user",
                Title: "User",
                resources.AxamlTaskManagerTable.UsersNameColumnWidth),
            new TaskManagerTableColumn(
                Key: "status",
                Title: "Status",
                resources.AxamlTaskManagerTable.UsersStatusColumnWidth),
            new TaskManagerTableColumn(
                Key: "cpu",
                Title: "CPU",
                resources.AxamlTaskManagerTable.UsersCPUColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                Key: "memory",
                Title: "Memory",
                resources.AxamlTaskManagerTable.UsersMemoryColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                Key: "disk",
                Title: "Disk",
                resources.AxamlTaskManagerTable.UsersDiskColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                Key: "network",
                Title: "Network",
                resources.AxamlTaskManagerTable.UsersNetworkColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true)
        ], resources.AxamlTaskManagerTable.MinimumColumnWidth);

    protected override void HandleSelectedRowChanged(TaskManagerTableRow? row) =>
        UpdateDisconnectButton(row?.Tag as UserGroupSnapshot);

    internal override void SetPageActive(bool isActive)
    {
        if (_disposed || _isPageActive == isActive) return;

        _isPageActive = isActive;
        if (isActive)
        {
            _snapshotService.SnapshotAvailable += OnSnapshotAvailable;
            _snapshotService.SetActiveSchema(_schema);
            _snapshotService.SetWarmProcesses(
                _schema.VisibleMask,
                NoWarmProcessIDs,
                count: 0,
                sampleEveryProcess: true);
            _snapshotService.RequestRefresh();
            _ = RefreshFromSnapshotServiceAsync();
            return;
        }

        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _refreshRequested = false;
    }

    private void OnSnapshotAvailable() => _ = RefreshFromSnapshotServiceAsync();

    private async Task RefreshFromSnapshotServiceAsync()
    {
        if (_disposed || !_isPageActive) return;
        if (_refreshPending)
        {
            _refreshRequested = true;
            return;
        }

        _refreshPending = true;
        try
        {
            UserPageRenderData? renderData = await Task.Run(BuildRenderData);
            if (_disposed || !_isPageActive || renderData == null) return;

            ApplyRenderData(renderData);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Users refresh failed: {exception}");
        }
        finally
        {
            _refreshPending = false;
            if (_isPageActive && _refreshRequested)
            {
                _refreshRequested = false;
                _ = RefreshFromSnapshotServiceAsync();
            }
        }
    }

    private UserPageRenderData? BuildRenderData()
    {
        if (!_snapshotService.TryCopyLatest(
                _snapshot,
                _schema.VisibleMask,
                out int count,
                out long version)
            || version == _snapshotVersion)
            return null;

        _snapshotVersion = version;
        _ = count;
        IReadOnlyList<UserSessionInfo> sessions = _sessionService.ReadSessions();
        if (!UserSnapshotBuilder.TryBuild(_snapshot, sessions, out UserSnapshot userSnapshot))
            return null;

        return CreateRenderData(
            userSnapshot,
            _snapshotService.GetLatestSystemPerformanceSample());
    }

    private static UserPageRenderData CreateRenderData(
        UserSnapshot snapshot,
        SystemPerformanceSample systemSample)
    {
        int totalRowCount = snapshot.Groups.Count;
        for (int groupIndex = 0; groupIndex < snapshot.Groups.Count; groupIndex++)
            totalRowCount += snapshot.Groups[groupIndex].Processes.Count;
        List<TaskManagerTableRow> rows = new(totalRowCount);

        double totalDiskBytesPerSecond = 0;
        double totalNetworkBytesPerSecond = 0;
        bool hasDiskUsage = false;
        bool hasNetworkUsage = false;
        for (int groupIndex = 0; groupIndex < snapshot.Groups.Count; groupIndex++)
        {
            UserGroupSnapshot group = snapshot.Groups[groupIndex];
            string groupKey = CreateGroupKey(group.Session.SessionID);
            rows.Add(CreateGroupRow(groupKey, group));
            for (int processIndex = 0; processIndex < group.Processes.Count; processIndex++)
                rows.Add(CreateProcessRow(groupKey, group.Processes[processIndex]));

            if (group.HasDiskUsage)
            {
                hasDiskUsage = true;
                totalDiskBytesPerSecond += group.DiskBytesPerSecond;
            }

            if (group.HasNetworkUsage)
            {
                hasNetworkUsage = true;
                totalNetworkBytesPerSecond += group.NetworkBytesPerSecond;
            }
        }

        string CPUHeader = string.Concat(
            TaskManagerUsageFormatter.FormatCPUPercent(systemSample.CPUAveragePercent),
            str1: " CPU");
        string memoryHeader = string.Concat(
            TaskManagerUsageFormatter.FormatCPUPercent(systemSample.MemoryPercent),
            str1: " Memory");
        string diskHeader = string.Concat(
            TaskManagerUsageFormatter.FormatDiskRate(hasDiskUsage, totalDiskBytesPerSecond),
            str1: " Disk");
        string networkHeader = string.Concat(
            TaskManagerUsageFormatter.FormatNetworkRate(hasNetworkUsage, totalNetworkBytesPerSecond),
            str1: " Network");
        return new UserPageRenderData(
            rows,
            CPUHeader,
            memoryHeader,
            diskHeader,
            networkHeader);
    }

    private void ApplyRenderData(UserPageRenderData renderData)
    {
        SetColumnTitle(columnIndex: 2, renderData.CPUHeader);
        SetColumnTitle(columnIndex: 3, renderData.MemoryHeader);
        SetColumnTitle(columnIndex: 4, renderData.DiskHeader);
        SetColumnTitle(columnIndex: 5, renderData.NetworkHeader);
        SetRows(renderData.Rows);
        UpdateDisconnectButton(SelectedRow?.Tag as UserGroupSnapshot);
    }

    private static TaskManagerTableRow CreateGroupRow(string groupKey, UserGroupSnapshot group) =>
        new()
        {
            Key = groupKey,
            IsGroup = true,
            Tag = group,
            Cells =
            [
                TaskManagerTableCell.TextCell(
                    $"{group.Session.UserName} ({group.ProcessCount})"),
                TaskManagerTableCell.TextCell(
                    TaskManagerUsageFormatter.FormatSessionState(group.Session.State)),
                TaskManagerTableCell.DecimalCell(
                    TaskManagerUsageFormatter.FormatCPUPercent(group.CPUPercent),
                    group.CPUPercent),
                TaskManagerTableCell.SignedCell(
                    TaskManagerUsageFormatter.FormatMemory(group.WorkingSetBytes),
                    group.WorkingSetBytes),
                CreateRateCell(
                    group.HasDiskUsage,
                    group.DiskBytesPerSecond,
                    TaskManagerUsageFormatter.FormatDiskRate),
                CreateRateCell(
                    group.HasNetworkUsage,
                    group.NetworkBytesPerSecond,
                    TaskManagerUsageFormatter.FormatNetworkRate)
            ]
        };

    private static TaskManagerTableRow CreateProcessRow(
        string groupKey,
        UserProcessSnapshot process) =>
        new()
        {
            Key = $"process:{process.Key.ProcessID}:{process.Key.CreationTimeTicks}",
            ParentKey = groupKey,
            IconSource = process.IconSource,
            Tag = process,
            Cells =
            [
                TaskManagerTableCell.TextCell(process.Name),
                TaskManagerTableCell.Empty,
                TaskManagerTableCell.DecimalCell(
                    TaskManagerUsageFormatter.FormatCPUPercent(process.CPUPercent),
                    process.CPUPercent),
                TaskManagerTableCell.SignedCell(
                    TaskManagerUsageFormatter.FormatMemory(process.WorkingSetBytes),
                    process.WorkingSetBytes),
                CreateRateCell(
                    process.HasDiskUsage,
                    process.DiskBytesPerSecond,
                    TaskManagerUsageFormatter.FormatDiskRate),
                CreateRateCell(
                    process.HasNetworkUsage,
                    process.NetworkBytesPerSecond,
                    TaskManagerUsageFormatter.FormatNetworkRate)
            ]
        };

    private static TaskManagerTableCell CreateRateCell(
        bool isAvailable,
        double bytesPerSecond,
        Func<bool, double, System.Globalization.CultureInfo?, string> formatter) =>
        isAvailable
            ? TaskManagerTableCell.DecimalCell(
                formatter(arg1: true, bytesPerSecond, arg3: null),
                bytesPerSecond)
            : TaskManagerTableCell.Empty;

    private static string CreateGroupKey(int sessionID) => $"user:{sessionID}";

    private void UpdateDisconnectButton(UserGroupSnapshot? group) =>
        _disconnectButton.IsEnabled = !_disconnectPending && group?.CanDisconnect == true;

    private void OnDisconnectClick(object? sender, EventArgs eventArgs)
    {
        if (SelectedRow?.Tag is UserGroupSnapshot group)
            _ = DisconnectAsync(group);
    }

    private async Task DisconnectAsync(UserGroupSnapshot group)
    {
        if (_disposed || _disconnectPending || !group.CanDisconnect) return;

        _disconnectPending = true;
        UpdateDisconnectButton(group);
        try
        {
            UserSessionActionResult result = await Task.Run(() => _sessionService.Disconnect(group.Session));
            if (_disposed) return;
            if (!result.Succeeded)
            {
                _reportMessage(
                    arg1: "Disconnect failed",
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? "Windows could not disconnect the selected user session."
                        : result.ErrorMessage);
                return;
            }

            _snapshotService.RequestRefresh();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Disconnect user session failed: {exception}");
            if (!_disposed) _reportMessage(arg1: "Disconnect failed", exception.Message);
        }
        finally
        {
            _disconnectPending = false;
            if (!_disposed) UpdateDisconnectButton(SelectedRow?.Tag as UserGroupSnapshot);
        }
    }

    private void OnManageUsersClick(object? sender, EventArgs eventArgs) =>
        _ = _startProcess("ms-settings:otherusers");

    private void OnMoreClick(object? sender, EventArgs eventArgs)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(Text: "Refresh", _snapshotService.RequestRefresh));
        UserGroupSnapshot? group = SelectedRow?.Tag as UserGroupSnapshot;
        if (!_disconnectPending && group?.CanDisconnect == true)
        {
            entries.Add(new ContextMenuEntry(Text: "Disconnect", () =>
            {
                if (SelectedRow?.Tag is UserGroupSnapshot selectedGroup)
                    _ = DisconnectAsync(selectedGroup);
            }));
        }

        entries.Add(new ContextMenuEntry(
            Text: "Manage user accounts",
            () => _ = _startProcess("ms-settings:otherusers")));
        ShowActionMenu(_moreButton, entries.ToList());
    }

    public override void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        base.Dispose();
    }

    private sealed record UserPageRenderData(
        IReadOnlyList<TaskManagerTableRow> Rows,
        string CPUHeader,
        string MemoryHeader,
        string DiskHeader,
        string NetworkHeader);
}

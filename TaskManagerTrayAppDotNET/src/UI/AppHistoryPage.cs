using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays session-scoped per-application CPU and network history.</summary>
internal sealed class AppHistoryPage : TaskManagerTablePage
{
    private static readonly int[] NoWarmProcessIDs = [];

    private readonly ProcessSnapshotService _snapshotService;
    private readonly AppHistoryStore _historyStore;
    private readonly ProcessDataSchema _schema;
    private readonly ProcessSnapshotBuffer _snapshot = new();
    private readonly TextBlock _historyDescription;
    private readonly SettingsButton _deleteHistoryButton;
    private readonly SettingsButton _moreButton;
    private long _snapshotVersion = -1;
    private long _historyVersion = -1;
    private bool _refreshPending;
    private bool _refreshRequested;
    private bool _isPageActive;
    private bool _disposed;

    public AppHistoryPage(
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService,
        AppHistoryStore historyStore,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Func<string, bool> startProcess)
        : base(
            "App history",
            CreateSchema(resources),
            processIconService,
            settings,
            palette,
            resources,
            startProcess,
            "Search app history")
    {
        _snapshotService = snapshotService;
        _historyStore = historyStore;
        _schema = ProcessDataSchema.Create(
            Array.Empty<ProcessColumnSetting>(),
            AppHistoryStore.RequiredColumnMask);

        _deleteHistoryButton = new SettingsButton(
            "Delete usage history",
            palette,
            transparentBase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = resources.AxamlTaskManagerTable.LinkButtonPadding
        };
        _deleteHistoryButton.Click += OnDeleteHistoryClick;
        _historyDescription = TrayAppDotNETSettingsUI.DescriptionText(string.Empty, palette);
        StackPanel information = new()
        {
            Spacing = resources.AxamlTaskManagerTable.InformationSpacing,
            Children = { _historyDescription, _deleteHistoryButton }
        };
        SetInformationContent(information);
        UpdateHistoryDescription();

        _moreButton = AddMoreAction(OnMoreClick);
    }

    private static TaskManagerTableSchema CreateSchema(TaskManagerWindowResources resources) =>
        new(
        [
            new TaskManagerTableColumn(
                "name",
                "Name",
                resources.AxamlTaskManagerTable.AppHistoryNameColumnWidth),
            new TaskManagerTableColumn(
                "cpuTime",
                "CPU time",
                resources.AxamlTaskManagerTable.AppHistoryCPUTimeColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                "network",
                "Network",
                resources.AxamlTaskManagerTable.AppHistoryNetworkColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true),
            new TaskManagerTableColumn(
                "notifications",
                "Notifications",
                resources.AxamlTaskManagerTable.AppHistoryNotificationsColumnWidth,
                TaskManagerTableAlignment.Right,
                SortDescendingByDefault: true)
        ]);

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
                0,
                sampleEveryProcess: true);
            _snapshotService.RequestRefresh();
            _ = RefreshHistoryAsync(consumeLatestSnapshot: false);
            return;
        }

        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _refreshRequested = false;
    }

    private void OnSnapshotAvailable() =>
        _ = RefreshHistoryAsync(consumeLatestSnapshot: true);

    private async Task RefreshHistoryAsync(bool consumeLatestSnapshot)
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
            long renderedVersion = _historyVersion;
            AppHistoryRenderData? renderData = await Task.Run(() =>
                BuildRenderData(consumeLatestSnapshot, renderedVersion));
            if (_disposed || !_isPageActive || renderData == null) return;

            _historyVersion = renderData.Version;
            SetRows(renderData.Rows);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"App history refresh failed: {exception}");
        }
        finally
        {
            _refreshPending = false;
            if (_isPageActive && _refreshRequested)
            {
                _refreshRequested = false;
                _ = RefreshHistoryAsync(consumeLatestSnapshot: true);
            }
        }
    }

    private AppHistoryRenderData? BuildRenderData(
        bool consumeLatestSnapshot,
        long renderedVersion)
    {
        if (consumeLatestSnapshot
            && _snapshotService.TryCopyLatest(
                _snapshot,
                _schema.VisibleMask,
                out int count,
                out long snapshotVersion)
            && snapshotVersion != _snapshotVersion)
        {
            _ = count;
            _snapshotVersion = snapshotVersion;
            _ = _historyStore.Consume(_snapshot);
        }

        AppHistorySnapshot snapshot = _historyStore.GetSnapshot();
        if (snapshot.Version == renderedVersion) return null;

        List<TaskManagerTableRow> rows = new(snapshot.Entries.Count);
        for (int entryIndex = 0; entryIndex < snapshot.Entries.Count; entryIndex++)
        {
            AppHistoryEntry entry = snapshot.Entries[entryIndex];
            TaskManagerTableCell notifications = entry.NotificationsAvailable
                ? TaskManagerTableCell.SignedCell(
                    entry.NotificationCount.ToString("N0"),
                    entry.NotificationCount)
                : TaskManagerTableCell.Empty;
            rows.Add(new TaskManagerTableRow
            {
                Key = entry.Key,
                IconSource = entry.IconSource,
                Tag = entry,
                Cells =
                [
                    TaskManagerTableCell.TextCell(entry.Name),
                    TaskManagerTableCell.SignedCell(
                        TaskManagerUsageFormatter.FormatCPUTime(entry.CPUTimeTicks),
                        entry.CPUTimeTicks),
                    TaskManagerTableCell.DecimalCell(
                        TaskManagerUsageFormatter.FormatAppHistoryNetwork(entry.NetworkBytes),
                        entry.NetworkBytes),
                    notifications
                ]
            });
        }

        return new AppHistoryRenderData(snapshot.Version, rows);
    }

    private void OnDeleteHistoryClick(object? sender, EventArgs eventArgs)
    {
        _historyStore.DeleteHistory();
        UpdateHistoryDescription();
        _ = RefreshHistoryAsync(consumeLatestSnapshot: false);
    }

    private void UpdateHistoryDescription()
    {
        _historyDescription.Text = string.Concat(
            "Resource usage collected by TaskManagerTrayAppDotNET since ",
            _historyStore.StartedAt.ToString("g"),
            " for current user and system accounts.");
    }

    private void OnMoreClick(object? sender, EventArgs eventArgs)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry("Refresh", _snapshotService.RequestRefresh));
        entries.Add(new ContextMenuEntry("Delete usage history", () =>
        {
            _historyStore.DeleteHistory();
            UpdateHistoryDescription();
            _ = RefreshHistoryAsync(consumeLatestSnapshot: false);
        }));
        ShowActionMenu(_moreButton, entries.ToList());
    }

    public override void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        _deleteHistoryButton.Click -= OnDeleteHistoryClick;
        base.Dispose();
    }

    private sealed record AppHistoryRenderData(
        long Version,
        IReadOnlyList<TaskManagerTableRow> Rows);
}

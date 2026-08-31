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
    private readonly TextBlock _historyDescription;
    private readonly SettingsButton _deleteHistoryButton;
    private readonly SettingsButton _moreButton;
    private long _historyVersion = -1;
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
        _snapshotService.SnapshotAvailable += OnSnapshotAvailable;
        ProcessDataSchema schema = ProcessDataSchema.Create(
            Array.Empty<ProcessColumnSetting>(),
            AppHistoryStore.RequiredColumnMask);
        _snapshotService.SetActiveSchema(schema);
        _snapshotService.SetWarmProcesses(
            schema.VisibleMask,
            NoWarmProcessIDs,
            0,
            sampleEveryProcess: true);
        _snapshotService.RequestRefresh();
        RenderLatestHistory();
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

    private void OnSnapshotAvailable()
    {
        if (!_disposed) RenderLatestHistory();
    }

    private void RenderLatestHistory()
    {
        AppHistorySnapshot snapshot = _historyStore.GetSnapshot();
        if (snapshot.Version == _historyVersion) return;

        _historyVersion = snapshot.Version;
        RenderHistorySnapshot(snapshot);
    }

    private void RenderHistorySnapshot(AppHistorySnapshot snapshot)
    {
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

        SetRows(rows);
    }

    private void OnDeleteHistoryClick(object? sender, EventArgs eventArgs)
    {
        _historyStore.DeleteHistory();
        UpdateHistoryDescription();
        RenderLatestHistory();
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
            RenderLatestHistory();
        }));
        ShowActionMenu(_moreButton, entries.ToList());
    }

    public override void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _snapshotService.SnapshotAvailable -= OnSnapshotAvailable;
        _deleteHistoryButton.Click -= OnDeleteHistoryClick;
        base.Dispose();
    }
}

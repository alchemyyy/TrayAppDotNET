#if DEBUG
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[Collection(TaskManagerReorderListLifetimeTestCollection.CollectionName)]
public sealed class TaskManagerTableHotReloadStateTests
{
    [Fact]
    public async Task RestoreBeforeRowsRetainsSortCollapsedGroupsAndSelection()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                TaskManagerTableSchema schema = CreateSchema(100, 120);
                using ProcessIconService processIconService = new();
                using TaskManagerTableControl table = new(
                    schema,
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());
                TaskManagerTableControlHotReloadState state = new(
                    SelectedRowKey: "process-b",
                    SortColumnIndex: 1,
                    SortDescending: true,
                    CollapsedGroupKeys: ["group-a"],
                    ColumnWidths: [100, 120],
                    BaselineColumnWidths: [100, 120]);
                List<string?> selectionNotifications = [];
                table.SelectedRowChanged += row => selectionNotifications.Add(row?.Key);

                table.RestoreHotReloadState(state);

                Assert.Empty(table.VisibleRows);
                Assert.Null(table.SelectedRow);

                table.SetRows(CreateGroupedRows());

                Assert.Equal(
                    ["group-b", "process-b", "group-a"],
                    table.VisibleRows.Select(static row => row.Key));
                Assert.Equal("process-b", table.SelectedRow?.Key);
                Assert.Equal(["process-b"], selectionNotifications);
                TaskManagerTableControlHotReloadState restored = table.CaptureHotReloadState();
                Assert.Equal(1, restored.SortColumnIndex);
                Assert.True(restored.SortDescending);
                Assert.Equal(["group-a"], restored.CollapsedGroupKeys);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RestoreUsesNewAXAMLBaselinesButRetainsUserResizedWidths()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using TaskManagerTableControl previousTable = new(
                    CreateSchema(100, 120),
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());
                TaskManagerTableControlHotReloadState previousState =
                    previousTable.CaptureHotReloadState() with
                    {
                        // The second width differs from its AXAML baseline, as it would after a drag
                        ColumnWidths = [100, 190]
                    };

                using TaskManagerTableControl rebuiltTable = new(
                    CreateSchema(140, 120),
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());
                rebuiltTable.RestoreHotReloadState(previousState);

                TaskManagerTableControlHotReloadState restoredState =
                    rebuiltTable.CaptureHotReloadState();
                Assert.Equal([140, 190], restoredState.ColumnWidths);
                Assert.Equal([140, 120], restoredState.BaselineColumnWidths);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task SchemaMinimumNormalizesRenderedAndCapturedColumnBaseline()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                const double minimumColumnWidth = 48;
                TaskManagerTableSchema schema = new(
                    [new TaskManagerTableColumn("name", "Name", Width: 20)],
                    minimumColumnWidth);

                Assert.Equal(minimumColumnWidth, schema.Columns[0].Width);

                using ProcessIconService processIconService = new();
                using TaskManagerTableControl table = new(
                    schema,
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());

                TaskManagerTableControlHotReloadState state = table.CaptureHotReloadState();
                Assert.Equal([minimumColumnWidth], state.ColumnWidths);
                Assert.Equal([minimumColumnWidth], state.BaselineColumnWidths);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RestoreDefersBothScrollOffsetsUntilRowsAndLayoutExist()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                const double horizontalOffset = 75;
                const double verticalOffset = 90;
                TaskManagerTableSchema schema = CreateSchema(600, 600);
                using ProcessIconService processIconService = new();
                using TestTaskManagerTablePage page = new(
                    schema,
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());
                Window window = new()
                {
                    Width = 360,
                    Height = 240,
                    Content = page
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    TaskManagerTableControlHotReloadState tableState = new(
                        SelectedRowKey: null,
                        SortColumnIndex: 0,
                        SortDescending: false,
                        CollapsedGroupKeys: [],
                        ColumnWidths: [600, 600],
                        BaselineColumnWidths: [600, 600]);
                    TaskManagerTableHotReloadState state = new(
                        SearchText: string.Empty,
                        RunInputText: string.Empty,
                        RunPanelVisible: false,
                        HorizontalOffset: horizontalOffset,
                        VerticalOffset: verticalOffset,
                        TableState: tableState);

                    page.RestoreHotReloadState(state);

                    TaskManagerTableHotReloadState beforeRows = page.CaptureHotReloadState();
                    SettingsScrollViewport viewport = GetPrivateField<SettingsScrollViewport>(
                        page,
                        "_tableScrollViewport");
                    Assert.True(GetPrivateField<bool>(page, "_hasPendingHotReloadOffsets"));
                    Assert.Equal(horizontalOffset, beforeRows.HorizontalOffset);
                    Assert.Equal(verticalOffset, beforeRows.VerticalOffset);
                    Assert.Equal(0, viewport.HorizontalOffset);
                    Assert.Equal(0, viewport.VerticalOffset);

                    page.SupplyRows(CreateFlatRows(80));
                    window.UpdateLayout();

                    TaskManagerTableHotReloadState afterRows = page.CaptureHotReloadState();
                    ScrollViewer scrollViewer = Assert.Single(
                        viewport.Children.OfType<ScrollViewer>());
                    Assert.False(GetPrivateField<bool>(page, "_hasPendingHotReloadOffsets"));
                    Assert.True(
                        afterRows.HorizontalOffset.Equals(horizontalOffset)
                        && afterRows.VerticalOffset.Equals(verticalOffset),
                        $"Expected ({horizontalOffset}, {verticalOffset}); actual "
                        + $"({afterRows.HorizontalOffset}, {afterRows.VerticalOffset}); extent "
                        + $"{scrollViewer.Extent}; viewport {scrollViewer.Viewport}; bounds "
                        + $"{viewport.Bounds}.");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RestoreAppliesPendingOffsetsWhenSuppliedRowsAreFilteredOut()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                const double horizontalOffset = 75;
                TaskManagerTableSchema schema = CreateSchema(600, 600);
                using ProcessIconService processIconService = new();
                using TestTaskManagerTablePage page = new(
                    schema,
                    processIconService,
                    new AppSettings(),
                    CreatePalette(),
                    new TaskManagerWindowResources());
                Window window = new()
                {
                    Width = 360,
                    Height = 240,
                    Content = page
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    TaskManagerTableControlHotReloadState tableState = new(
                        SelectedRowKey: null,
                        SortColumnIndex: 0,
                        SortDescending: false,
                        CollapsedGroupKeys: [],
                        ColumnWidths: [600, 600],
                        BaselineColumnWidths: [600, 600]);
                    TaskManagerTableHotReloadState state = new(
                        SearchText: "no matching row",
                        RunInputText: string.Empty,
                        RunPanelVisible: false,
                        HorizontalOffset: horizontalOffset,
                        VerticalOffset: 90,
                        TableState: tableState);

                    page.RestoreHotReloadState(state);
                    page.SupplyRows(CreateFlatRows(80));
                    window.UpdateLayout();

                    TaskManagerTableHotReloadState restored = page.CaptureHotReloadState();
                    TaskManagerTableControl table = GetPrivateField<TaskManagerTableControl>(
                        page,
                        "_table");
                    Assert.Empty(table.VisibleRows);
                    Assert.False(GetPrivateField<bool>(page, "_hasPendingHotReloadOffsets"));
                    Assert.Equal(horizontalOffset, restored.HorizontalOffset);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static TaskManagerTableSchema CreateSchema(double nameWidth, double valueWidth) =>
        new(
        [
            new TaskManagerTableColumn("name", "Name", nameWidth),
            new TaskManagerTableColumn("value", "Value", valueWidth)
        ],
        minimumColumnWidth: 48);

    private static TaskManagerTableRow[] CreateGroupedRows() =>
    [
        Row("group-a", null, "Group A", 10, isGroup: true),
        Row("process-a", "group-a", "Process A", 100),
        Row("group-b", null, "Group B", 20, isGroup: true),
        Row("process-b", "group-b", "Process B", 50)
    ];

    private static TaskManagerTableRow[] CreateFlatRows(int count)
    {
        TaskManagerTableRow[] rows = new TaskManagerTableRow[count];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            rows[rowIndex] = Row($"row-{rowIndex}", null, $"Row {rowIndex}", rowIndex);
        return rows;
    }

    private static TaskManagerTableRow Row(
        string key,
        string? parentKey,
        string name,
        long value,
        bool isGroup = false) =>
        new()
        {
            Key = key,
            ParentKey = parentKey,
            IsGroup = isGroup,
            Cells =
            [
                TaskManagerTableCell.TextCell(name),
                TaskManagerTableCell.SignedCell(value.ToString(), value)
            ]
        };

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo field = typeof(TaskManagerTablePage).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Private field '{typeof(TaskManagerTablePage).FullName}.{fieldName}' was not found.");
        object? value = field.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private static SettingsPalette CreatePalette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);

    private sealed class TestTaskManagerTablePage(
        TaskManagerTableSchema schema,
        ProcessIconService processIconService,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
        : TaskManagerTablePage(
            "Test",
            schema,
            processIconService,
            settings,
            palette,
            resources,
            static _ => false,
            "Search")
    {
        public void SupplyRows(IReadOnlyList<TaskManagerTableRow> rows) => SetRows(rows);
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
#endif

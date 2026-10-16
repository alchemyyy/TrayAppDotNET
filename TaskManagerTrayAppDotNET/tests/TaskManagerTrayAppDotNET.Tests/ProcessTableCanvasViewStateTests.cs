using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

/// <summary>Checks the view state a rebuilt Processes canvas takes over from the canvas it replaces.</summary>
[Collection(ProcessTableCanvasSemanticGroupTestCollection.CollectionName)]
public sealed class ProcessTableCanvasViewStateTests
{
    [Fact]
    public async Task RestoredSortAppliesToAShownColumn()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessTableCanvas canvas = CreateCanvas(processIconService);

                canvas.RestoreSort(ProcessTableColumnKind.CPU, descending: false);

                Assert.Equal(ProcessTableColumnKind.CPU, canvas.SortColumn);
                Assert.False(canvas.SortDescending);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RestoredSortOnAHiddenColumnKeepsTheCurrentOrder()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessTableCanvas canvas = CreateCanvas(processIconService);
                ProcessTableColumnKind initialColumn = canvas.SortColumn;
                bool initialDescending = canvas.SortDescending;

                canvas.RestoreSort(ProcessTableColumnKind.WorkingSet, !initialDescending);

                Assert.Equal(initialColumn, canvas.SortColumn);
                Assert.Equal(initialDescending, canvas.SortDescending);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task SelectionCannotRestoreBeforeASnapshotArrives()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessTableCanvas canvas = CreateCanvas(processIconService);
                ProcessTerminationTarget target = new(ProcessID: 4321, CreationTimeFileTime: 1234);

                // A canvas built while the window is hidden has no rows, which is why the page defers its restore
                Assert.False(canvas.HasRows);
                canvas.RestoreSelectedProcesses([target], target);

                Assert.Empty(canvas.SelectedTerminationTargets);
                Assert.Null(canvas.SelectedTerminationTarget);
            },
            CancellationToken.None);
    }

    private static ProcessTableCanvas CreateCanvas(ProcessIconService processIconService)
    {
        List<ProcessColumnSetting> columnSettings = ProcessColumnSettings.CreateDefault();
        foreach (ProcessColumnSetting setting in columnSettings)
            setting.Visible = setting.Column is ProcessTableColumnKind.Name or ProcessTableColumnKind.CPU;

        return new ProcessTableCanvas(
            processIconService,
            ProcessDataSchema.Create(columnSettings, ProcessTableColumnKind.Name),
            columnSettings,
            enableLiveColumnResizing: false,
            ProcessTreeDefaultState.Expanded,
            expandSemanticSectionsByDefault: true,
            useRootProcessForSemanticGroups: false,
            useRootProcessForSemanticSubgroups: false,
            groupWindowsProcesses: true,
            AppSettings.GridFontSizeDefault,
            TaskManagerGridFontWeight.Normal,
            AppSettings.GridRowSpacingDefault,
            ProcessLiveTotalAppearance.Default,
            CreatePalette(),
            new TaskManagerWindowResources(),
            isLightTheme: false);
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

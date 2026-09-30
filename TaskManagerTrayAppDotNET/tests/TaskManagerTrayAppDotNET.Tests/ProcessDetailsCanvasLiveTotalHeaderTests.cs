using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

/// <summary>Checks the header height the painted Processes table reserves for stacked live totals.</summary>
[Collection(ProcessDetailsCanvasSemanticGroupTestCollection.CollectionName)]
public sealed class ProcessDetailsCanvasLiveTotalHeaderTests
{
    private const int StackedHeaderLineCount = 2;

    [Fact]
    public async Task StackedTotalsDoubleTheHeaderOnlyWhileAColumnShowsATotal()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                TaskManagerWindowResources resources = new();
                double singleLineHeight = resources.AxamlProcessTable.HeaderHeight;
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateCanvas(
                    processIconService,
                    resources,
                    showTotalsAboveColumnNames: true,
                    cpuShowsLiveTotal: false);
                int headerHeightChanges = 0;
                canvas.HeaderHeightChanged += () => headerHeightChanges++;

                Assert.Equal(singleLineHeight, canvas.HeaderHeight);

                canvas.ApplyColumnProperties(WithLiveTotal(canvas, ProcessTableColumnKind.CPU, showLiveTotal: true));

                Assert.Equal(singleLineHeight * StackedHeaderLineCount, canvas.HeaderHeight);
                Assert.Equal(expected: 1, headerHeightChanges);

                canvas.ApplyColumnProperties(WithLiveTotal(canvas, ProcessTableColumnKind.CPU, showLiveTotal: false));

                Assert.Equal(singleLineHeight, canvas.HeaderHeight);
                Assert.Equal(expected: 2, headerHeightChanges);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task SideBySideTotalsKeepTheSingleLineHeader()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                TaskManagerWindowResources resources = new();
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateCanvas(
                    processIconService,
                    resources,
                    showTotalsAboveColumnNames: false,
                    cpuShowsLiveTotal: true);

                Assert.Equal(resources.AxamlProcessTable.HeaderHeight, canvas.HeaderHeight);
            },
            CancellationToken.None);
    }

    private static ProcessDetailsCanvas CreateCanvas(
        ProcessIconService processIconService,
        TaskManagerWindowResources resources,
        bool showTotalsAboveColumnNames,
        bool cpuShowsLiveTotal)
    {
        List<ProcessColumnSetting> columnSettings = ProcessColumnSettings.CreateDefault();
        foreach (ProcessColumnSetting setting in columnSettings)
        {
            setting.Visible = setting.Column is ProcessTableColumnKind.Name or ProcessTableColumnKind.CPU;
            setting.ShowLiveTotal = cpuShowsLiveTotal && setting.Column == ProcessTableColumnKind.CPU;
        }

        return new ProcessDetailsCanvas(
            processIconService,
            ProcessDataSchema.Create(columnSettings, ProcessTableColumnKind.Name),
            columnSettings,
            enableLiveColumnResizing: false,
            ProcessTreeDefaultState.Expanded,
            expandSemanticSectionsByDefault: true,
            useRootProcessForSemanticGroups: false,
            useRootProcessForSemanticSubgroups: false,
            AppSettings.GridFontSizeDefault,
            DetailsGridFontWeight.Normal,
            AppSettings.GridRowSpacingDefault,
            ProcessLiveTotalAppearance.Default with { ShowAboveColumnNames = showTotalsAboveColumnNames },
            CreatePalette(),
            resources);
    }

    private static ProcessColumnSetting WithLiveTotal(
        ProcessDetailsCanvas canvas,
        ProcessTableColumnKind column,
        bool showLiveTotal)
    {
        ProcessColumnSetting setting = canvas.GetColumnSetting(column);
        setting.ShowLiveTotal = showLiveTotal;
        return setting;
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

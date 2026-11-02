using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

/// <summary>Checks the header height the painted Processes table reserves for stacked live totals.</summary>
[Collection(ProcessTableCanvasSemanticGroupTestCollection.CollectionName)]
public sealed class ProcessTableCanvasLiveTotalHeaderTests
{
    private const int StackedHeaderLineCount = 2;

    [Fact]
    public void StackedTotalsDoubleTheHeaderOnlyWhileAColumnShowsATotal() => AvaloniaTestHost.RunWithFluentTheme(static () =>
    {
        TaskManagerWindowResources resources = new();
        double singleLineHeight = resources.AxamlProcessTable.HeaderHeight;
        using ProcessIconService processIconService = new();
        using ProcessTableCanvas canvas = CreateCanvas(
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
    });

    [Fact]
    public void SideBySideTotalsKeepTheSingleLineHeader() => AvaloniaTestHost.RunWithFluentTheme(static () =>
    {
        TaskManagerWindowResources resources = new();
        using ProcessIconService processIconService = new();
        using ProcessTableCanvas canvas = CreateCanvas(
            processIconService,
            resources,
            showTotalsAboveColumnNames: false,
            cpuShowsLiveTotal: true);

        Assert.Equal(resources.AxamlProcessTable.HeaderHeight, canvas.HeaderHeight);
    });

    private static ProcessTableCanvas CreateCanvas(
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
            ProcessLiveTotalAppearance.Default with { ShowAboveColumnNames = showTotalsAboveColumnNames },
            TestPalettes.Default(),
            resources,
            isLightTheme: false);
    }

    private static ProcessColumnSetting WithLiveTotal(
        ProcessTableCanvas canvas,
        ProcessTableColumnKind column,
        bool showLiveTotal)
    {
        ProcessColumnSetting setting = canvas.GetColumnSetting(column);
        setting.ShowLiveTotal = showLiveTotal;
        return setting;
    }
}

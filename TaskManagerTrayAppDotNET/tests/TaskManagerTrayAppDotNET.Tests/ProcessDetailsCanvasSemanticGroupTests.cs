using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ProcessDetailsCanvasSemanticGroupTestCollection
{
    public const string CollectionName = "Process details canvas semantic group tests";
}

/// <summary>Drives the painted Processes table through semantic grouping with a seeded snapshot.</summary>
[Collection(ProcessDetailsCanvasSemanticGroupTestCollection.CollectionName)]
public sealed class ProcessDetailsCanvasSemanticGroupTests
{
    private const string UserSID = "S-1-5-21-1000";
    private const string BrowserPath = @"C:\Apps\Browser\browser.exe";
    private const string AlphaPath = @"C:\Apps\Browser\alpha.exe";
    private const string ZetaPath = @"C:\Apps\Browser\zeta.exe";
    private const string GammaPath = @"C:\Apps\Browser\gamma.exe";
    private const string OtherPath = @"C:\Apps\Other\other.exe";
    private const int AppSpacerProcessID = -1;
    private const int AppHeaderProcessID = -2;
    private const int GroupRowProcessID = SemanticProcessSections.FirstGroupSyntheticProcessID;
    private const int SubgroupRootLineProcessID = GroupRowProcessID - 1;
    private const long BrowserRootPrivateBytes = 100;
    private const long BrowserWindowPrivateBytes = 200;
    private const long AlphaPrivateBytes = 400;
    private const long ZetaPrivateBytes = 800;
    private const long GammaPrivateBytes = 3_200;
    private const long GroupPrivateBytes =
        BrowserRootPrivateBytes + BrowserWindowPrivateBytes + AlphaPrivateBytes + ZetaPrivateBytes;
    private const long RootBasePriority = 8;
    private const long WindowBasePriority = 13;

    [Fact]
    public async Task SubgroupHeadsShowSubtreeTotalsAboveTheirOwnRootLine()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true,
                    useRootProcessForSemanticSubgroups: true,
                    CreateRowsWithGrandchild());

                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("browser.exe", 10),
                    ("Root", GroupRowProcessID),
                    ("alpha.exe", 12),
                    ("Root", SubgroupRootLineProcessID),
                    ("gamma.exe", 14),
                    ("browser.exe", 11),
                    ("zeta.exe", 13),
                    ("other.exe", 20)
                ], GetVisibleRows(canvas));
                Assert.Equal(GroupPrivateBytes + GammaPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 2));
                Assert.Equal(AlphaPrivateBytes + GammaPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 4));
                Assert.Equal(AlphaPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 5));
                Assert.Equal(
                    string.Empty,
                    GetVisibleCellText(canvas, visibleIndex: 4, ProcessTableColumnKind.Status));
                Assert.Equal(
                    ProcessStatusFunctions.SuspendedText,
                    GetVisibleCellText(canvas, visibleIndex: 5, ProcessTableColumnKind.Status));

                InvokePrivate(canvas, methodName: "ApplyPointerSelection", 4, KeyModifiers.None);
                ProcessEndTaskRequest? subgroupRequest = canvas.SelectedEndTaskRequest;
                InvokePrivate(canvas, methodName: "ApplyPointerSelection", 5, KeyModifiers.None);
                ProcessEndTaskRequest? subgroupRootLineRequest = canvas.SelectedEndTaskRequest;
                Assert.NotNull(subgroupRequest);
                Assert.Equal([12, 14], subgroupRequest.Processes.Select(static process => process.Target.ProcessID));
                Assert.NotNull(subgroupRootLineRequest);
                Assert.Equal(
                    [12],
                    subgroupRootLineRequest.Processes.Select(static process => process.Target.ProcessID));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task SubgroupHeadsKeepTheirOwnUsageByDefault()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true,
                    useRootProcessForSemanticSubgroups: false,
                    CreateRowsWithGrandchild());

                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("browser.exe", 10),
                    ("Root", GroupRowProcessID),
                    ("alpha.exe", 12),
                    ("gamma.exe", 14),
                    ("browser.exe", 11),
                    ("zeta.exe", 13),
                    ("other.exe", 20)
                ], GetVisibleRows(canvas));
                Assert.Equal(AlphaPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 4));
                Assert.Equal(
                    ProcessStatusFunctions.SuspendedText,
                    GetVisibleCellText(canvas, visibleIndex: 4, ProcessTableColumnKind.Status));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RootProcessRowShowsGroupTotalsAboveItsRootLine()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true);

                // Ascending names would otherwise place alpha.exe and browser.exe ahead of the Root line
                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("browser.exe", 10),
                    ("Root", GroupRowProcessID),
                    ("alpha.exe", 12),
                    ("browser.exe", 11),
                    ("zeta.exe", 13),
                    ("other.exe", 20)
                ], GetVisibleRows(canvas));
                Assert.Equal(GroupPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 2));
                Assert.Equal(BrowserRootPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 3));
                Assert.Equal(
                    "10",
                    GetVisibleCellText(canvas, visibleIndex: 2, ProcessTableColumnKind.ProcessID));
                Assert.Equal(
                    string.Empty,
                    GetVisibleCellText(canvas, visibleIndex: 3, ProcessTableColumnKind.ProcessID));

                // A column that cannot be summed shows the root process rather than the windowed representative
                Assert.Equal(
                    RootBasePriority.ToString(),
                    GetVisibleCellText(canvas, visibleIndex: 2, ProcessTableColumnKind.BasePriority));
                Assert.Equal(
                    RootBasePriority.ToString(),
                    GetVisibleCellText(canvas, visibleIndex: 3, ProcessTableColumnKind.BasePriority));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RootProcessRowShowsNoStatusWhileRootLineKeepsTheRootsOwn()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true);

                Assert.Equal(
                    string.Empty,
                    GetVisibleCellText(canvas, visibleIndex: 2, ProcessTableColumnKind.Status));
                Assert.Equal(
                    ProcessStatusFunctions.EfficiencyModeText,
                    GetVisibleCellText(canvas, visibleIndex: 3, ProcessTableColumnKind.Status));
                Assert.Equal(
                    ProcessStatusFunctions.SuspendedText,
                    GetVisibleCellText(canvas, visibleIndex: 5, ProcessTableColumnKind.Status));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RootLineStaysFirstWhenTheSortReverses()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true);

                // A second click on the sorted Name header reverses it, which would put zeta.exe first
                InvokePrivate(canvas, methodName: "SortFromHeader", 1.0);

                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("other.exe", 20),
                    ("browser.exe", 10),
                    ("Root", GroupRowProcessID),
                    ("zeta.exe", 13),
                    ("browser.exe", 11),
                    ("alpha.exe", 12)
                ], GetVisibleRows(canvas));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RootLineFollowsItsRootProcessIntoSearchResults()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true);

                canvas.SetFilter("alpha");

                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("browser.exe", 10),
                    ("Root", GroupRowProcessID),
                    ("alpha.exe", 12)
                ], GetVisibleRows(canvas));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task RootProcessRowEndsTheGroupWhileRootLineEndsOnlyTheRoot()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: true);

                InvokePrivate(canvas, methodName: "ApplyPointerSelection", 2, KeyModifiers.None);
                ProcessEndTaskRequest? groupRequest = canvas.SelectedEndTaskRequest;
                int groupSelectionCount = canvas.SelectedProcessCount;
                InvokePrivate(canvas, methodName: "ApplyPointerSelection", 3, KeyModifiers.None);
                ProcessEndTaskRequest? rootLineRequest = canvas.SelectedEndTaskRequest;
                int rootLineSelectionCount = canvas.SelectedProcessCount;

                // The root leads the group request, so single-target menu actions act on the process shown
                Assert.NotNull(groupRequest);
                Assert.Equal(
                    [10, 11, 12, 13],
                    groupRequest.Processes.Select(static process => process.Target.ProcessID));
                Assert.Equal(expected: 4, groupSelectionCount);
                Assert.NotNull(rootLineRequest);
                Assert.Equal([10], rootLineRequest.Processes.Select(static process => process.Target.ProcessID));
                Assert.Equal(expected: 1, rootLineSelectionCount);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task SyntheticGroupRowRemainsTheDefault()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                using ProcessIconService processIconService = new();
                using ProcessDetailsCanvas canvas = CreateSemanticCanvas(
                    processIconService,
                    useRootProcessForSemanticGroups: false);

                Assert.Equal(
                [
                    ("", AppSpacerProcessID),
                    ("Apps (2)", AppHeaderProcessID),
                    ("browser.exe (4)", GroupRowProcessID),
                    ("browser.exe", 10),
                    ("alpha.exe", 12),
                    ("browser.exe", 11),
                    ("zeta.exe", 13),
                    ("other.exe", 20)
                ], GetVisibleRows(canvas));
                Assert.Equal(GroupPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 2));
                Assert.Equal(BrowserRootPrivateBytes, GetVisiblePrivateBytes(canvas, visibleIndex: 3));
                Assert.Equal(
                    ProcessStatusFunctions.EfficiencyModeText,
                    GetVisibleCellText(canvas, visibleIndex: 2, ProcessTableColumnKind.Status));
                Assert.Equal(
                    WindowBasePriority.ToString(),
                    GetVisibleCellText(canvas, visibleIndex: 2, ProcessTableColumnKind.BasePriority));
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Builds a canvas over one four-process browser group and one singleton unless other rows are given. The
    /// windowed browser child is the group representative, so the root process must be found by walking up from it.
    /// </summary>
    private static ProcessDetailsCanvas CreateSemanticCanvas(
        ProcessIconService processIconService,
        bool useRootProcessForSemanticGroups,
        bool useRootProcessForSemanticSubgroups = false,
        ProcessRow[]? rows = null)
    {
        List<ProcessColumnSetting> columnSettings = CreateColumnSettings();
        ProcessDataSchema schema = ProcessDataSchema.Create(columnSettings, ProcessTableColumnKind.Name);
        ProcessDetailsCanvas canvas = new(
            processIconService,
            schema,
            columnSettings,
            enableLiveColumnResizing: false,
            ProcessTreeDefaultState.Expanded,
            expandSemanticSectionsByDefault: true,
            useRootProcessForSemanticGroups,
            useRootProcessForSemanticSubgroups,
            AppSettings.GridFontSizeDefault,
            DetailsGridFontWeight.Normal,
            AppSettings.GridRowSpacingDefault,
            ProcessLiveTotalAppearance.Default,
            CreatePalette(),
            new TaskManagerWindowResources());
        try
        {
            ProcessSnapshotBuffer sourceSnapshot = GetPrivateField<ProcessSnapshotBuffer>(canvas, "_sourceSnapshot");
            rows ??= CreateRows();
            sourceSnapshot.BeginWrite(schema, rows.Length);
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                WriteRow(sourceSnapshot, schema, rowIndex, rows[rowIndex]);
            sourceSnapshot.CompleteWrite(rows.Length);

            canvas.SetProcessGroupingStyle(ProcessGroupingStyle.Semantic);
            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    private static ProcessRow[] CreateRows() =>
    [
        new(10, 100, -1, "browser.exe", BrowserPath, ProcessIndependentWindowState.None,
            BrowserRootPrivateBytes, RootBasePriority, ProcessStatus.EfficiencyMode),
        new(11, 200, 10, "browser.exe", BrowserPath, ProcessIndependentWindowState.Qualifying,
            BrowserWindowPrivateBytes, WindowBasePriority, ProcessStatus.Suspended),
        new(12, 300, 10, "alpha.exe", AlphaPath, ProcessIndependentWindowState.None,
            AlphaPrivateBytes, RootBasePriority),
        new(13, 350, 10, "zeta.exe", ZetaPath, ProcessIndependentWindowState.None,
            ZetaPrivateBytes, RootBasePriority),
        new(20, 400, -1, "other.exe", OtherPath, ProcessIndependentWindowState.Qualifying,
            PrivateBytes: 1_600, RootBasePriority)
    ];

    /// <summary>Adds a child beneath alpha.exe, so alpha.exe heads a subgroup, and suspends alpha.exe.</summary>
    private static ProcessRow[] CreateRowsWithGrandchild() =>
    [
        new(10, 100, -1, "browser.exe", BrowserPath, ProcessIndependentWindowState.None,
            BrowserRootPrivateBytes, RootBasePriority, ProcessStatus.EfficiencyMode),
        new(11, 200, 10, "browser.exe", BrowserPath, ProcessIndependentWindowState.Qualifying,
            BrowserWindowPrivateBytes, WindowBasePriority),
        new(12, 300, 10, "alpha.exe", AlphaPath, ProcessIndependentWindowState.None,
            AlphaPrivateBytes, RootBasePriority, ProcessStatus.Suspended),
        new(13, 350, 10, "zeta.exe", ZetaPath, ProcessIndependentWindowState.None,
            ZetaPrivateBytes, RootBasePriority),
        new(14, 380, 12, "gamma.exe", GammaPath, ProcessIndependentWindowState.None,
            GammaPrivateBytes, RootBasePriority),
        new(20, 400, -1, "other.exe", OtherPath, ProcessIndependentWindowState.Qualifying,
            PrivateBytes: 1_600, RootBasePriority)
    ];

    private static List<ProcessColumnSetting> CreateColumnSettings()
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.CreateDefault();
        foreach (ProcessColumnSetting setting in settings)
        {
            setting.Visible = setting.Column is ProcessTableColumnKind.Name
                or ProcessTableColumnKind.ProcessID
                or ProcessTableColumnKind.Status
                or ProcessTableColumnKind.BasePriority
                or ProcessTableColumnKind.PrivateMemory;
        }

        return settings;
    }

    private static void WriteRow(
        ProcessSnapshotBuffer snapshot,
        ProcessDataSchema schema,
        int rowIndex,
        ProcessRow row)
    {
        ProcessInstanceKey instanceKey = new(row.ProcessID, row.CreationTime);
        ProcessStaticData staticData = new()
        {
            InstanceKey = instanceKey,
            IsCreationTimeKnown = true,
            ParentProcessID = row.ParentProcessID,
            Image = new ProcessImageIdentity(
                key: row.Name,
                row.Name,
                row.ExecutablePath,
                description: string.Empty,
                iconSource: default),
            UserName = "user",
            UserSID = UserSID,
            SessionID = 1,
            NumericValues = new long[schema.StaticNumericCount],
            TextValues = new string?[schema.StaticTextCount]
        };
        long[] dynamicNumericValues = new long[schema.DynamicNumericCount];
        dynamicNumericValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.PrivateMemory)] = row.PrivateBytes;
        dynamicNumericValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.BasePriority)] = row.BasePriority;
        dynamicNumericValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.Status)] = (long)row.Status;
        ProcessGroupingFacts groupingFacts = new(
            instanceKey,
            IsCreationTimeKnown: true,
            row.ParentProcessID,
            row.Name,
            row.ExecutablePath,
            UserSID,
            SessionID: 1,
            PackageFullName: string.Empty,
            ApplicationUserModelID: null,
            IsApplicationUserModelIDAmbiguous: false,
            row.WindowState,
            IsCriticalOrProtected: false);
        snapshot.SetRow(
            rowIndex,
            staticData,
            dynamicNumericValues,
            new string?[schema.DynamicTextCount],
            groupingFacts);
    }

    private static List<(string Name, int ProcessID)> GetVisibleRows(ProcessDetailsCanvas canvas)
    {
        int[] visibleRowIndexes = GetPrivateField<int[]>(canvas, "_visibleRowIndexes");
        int visibleRowCount = GetPrivateField<int>(canvas, "_visibleRowCount");
        ProcessSnapshotBuffer snapshot = GetPrivateField<ProcessSnapshotBuffer>(canvas, "_snapshot");
        List<(string Name, int ProcessID)> rows = new(visibleRowCount);
        for (int visibleIndex = 0; visibleIndex < visibleRowCount; visibleIndex++)
        {
            ProcessStaticData row = Assert.IsType<ProcessStaticData>(
                snapshot.StaticRows[visibleRowIndexes[visibleIndex]]);
            rows.Add((row.Image.Name, row.ProcessID));
        }

        return rows;
    }

    private static long GetVisiblePrivateBytes(ProcessDetailsCanvas canvas, int visibleIndex)
    {
        int[] visibleRowIndexes = GetPrivateField<int[]>(canvas, "_visibleRowIndexes");
        ProcessSnapshotBuffer snapshot = GetPrivateField<ProcessSnapshotBuffer>(canvas, "_snapshot");
        return snapshot.GetDynamicNumeric(
            visibleRowIndexes[visibleIndex],
            ProcessTableColumnKind.PrivateMemory);
    }

    private static string GetVisibleCellText(
        ProcessDetailsCanvas canvas,
        int visibleIndex,
        ProcessTableColumnKind column)
    {
        int[] visibleRowIndexes = GetPrivateField<int[]>(canvas, "_visibleRowIndexes");
        return Assert.IsType<string>(InvokePrivate(
            canvas,
            methodName: "GetCellDisplayValue",
            visibleRowIndexes[visibleIndex],
            column));
    }

    private static T GetPrivateField<T>(ProcessDetailsCanvas canvas, string fieldName)
    {
        FieldInfo field = typeof(ProcessDetailsCanvas).GetField(
                              fieldName,
                              BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException(
                              $"Private field '{typeof(ProcessDetailsCanvas).FullName}.{fieldName}' was not found.");
        return Assert.IsType<T>(field.GetValue(canvas));
    }

    private static object? InvokePrivate(ProcessDetailsCanvas canvas, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(ProcessDetailsCanvas).GetMethod(
                                methodName,
                                BindingFlags.Instance | BindingFlags.NonPublic)
                            ?? throw new InvalidOperationException(
                                $"Private method '{typeof(ProcessDetailsCanvas).FullName}.{methodName}' was not found.");
        return method.Invoke(canvas, arguments);
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

    private readonly record struct ProcessRow(
        int ProcessID,
        long CreationTime,
        int ParentProcessID,
        string Name,
        string ExecutablePath,
        ProcessIndependentWindowState WindowState,
        long PrivateBytes,
        long BasePriority,
        ProcessStatus Status = ProcessStatus.None);

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

using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessLifetimeSearchIntegrationTests
{
    private const int SnapshotTimeoutMilliseconds = 5_000;

    [Fact]
    public void SamplerPublishesSearchableLifetimeAndCommandLineForCurrentProcess()
    {
        List<ProcessColumnSetting> settings =
        [
            Setting(ProcessTableColumnKind.Name),
            Setting(ProcessTableColumnKind.Lifetime),
            Setting(ProcessTableColumnKind.CommandLine, visible: false)
        ];
        ProcessSearchQuery lifetimeQuery = ProcessSearchQuery.Parse(
            "{Lifetime}>=0s&&{Lifetime}<7d",
            settings);
        ProcessSearchQuery commandLineQuery = ProcessSearchQuery.Parse(
            "{Command line}=~\"\\S+\"",
            settings);
        ProcessDataSchema schema = ProcessDataSchema.Create(
            settings,
            lifetimeQuery.RequiredColumnMask | commandLineQuery.RequiredColumnMask);
        using ProcessSnapshotService service = new();
        int[] warmProcessIDs = [];
        service.SetActiveSchema(schema);
        service.SetWarmProcesses(schema.VisibleMask, warmProcessIDs, 0, sampleEveryProcess: true);
        service.Start();

        ProcessSnapshotBuffer snapshot = WaitForCurrentProcess(service, schema.VisibleMask);
        int rowIndex = FindCurrentProcessRow(snapshot);
        long lifetimeTicks = snapshot.GetDynamicNumeric(rowIndex, ProcessTableColumnKind.Lifetime);
        ProcessStaticData row = snapshot.StaticRows[rowIndex]
            ?? throw new InvalidOperationException("The current process row has no static data.");
        int commandLineSlot = schema.GetStaticTextSlot(ProcessTableColumnKind.CommandLine);
        string commandLine = row.TextValues[commandLineSlot] ?? string.Empty;
        string formattedLifetime = ProcessLifetime.Format(lifetimeTicks);
        ProcessSearchColumnValue ResolveValue(int ignoredRowIndex, ProcessTableColumnKind column) => column switch
        {
            ProcessTableColumnKind.Lifetime => ProcessSearchColumnValue.Numeric(
                formattedLifetime,
                lifetimeTicks),
            ProcessTableColumnKind.CommandLine => ProcessSearchColumnValue.TextOnly(commandLine),
            _ => ProcessSearchColumnValue.TextOnly(string.Empty)
        };

        Assert.InRange(lifetimeTicks, 0, TimeSpan.FromDays(7).Ticks);
        Assert.NotEmpty(commandLine);
        Assert.True(lifetimeQuery.Matches(0, ResolveValue));
        Assert.True(commandLineQuery.Matches(0, ResolveValue));
    }

    private static ProcessSnapshotBuffer WaitForCurrentProcess(
        ProcessSnapshotService service,
        ulong schemaMask)
    {
        ProcessSnapshotBuffer snapshot = new();
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < SnapshotTimeoutMilliseconds)
        {
            int count = service.CopyLatest(snapshot, schemaMask, out _);
            if (count > 0 && FindCurrentProcessRow(snapshot) >= 0) return snapshot;
            Thread.Sleep(25);
        }

        throw new TimeoutException("The process sampler did not publish the current process in time.");
    }

    private static int FindCurrentProcessRow(ProcessSnapshotBuffer snapshot)
    {
        int currentProcessID = Environment.ProcessId;
        for (int rowIndex = 0; rowIndex < snapshot.Count; rowIndex++)
        {
            if (snapshot.StaticRows[rowIndex]?.ProcessID == currentProcessID) return rowIndex;
        }

        return -1;
    }

    private static ProcessColumnSetting Setting(
        ProcessTableColumnKind column,
        bool visible = true) =>
        new()
        {
            Column = column,
            Visible = visible,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };
}

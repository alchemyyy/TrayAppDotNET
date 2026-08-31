#if DEBUG
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableAXAMLHotReloadTests
{
    [Fact]
    public void ChangedAXAMLWidthsUpdateEveryBackedSettingWithoutMutatingSource()
    {
        List<ProcessColumnSetting> source = ProcessColumnSettings.CreateDefault();
        Dictionary<ProcessTableColumnKind, double> originalWidths = source.ToDictionary(
            static setting => setting.Column,
            static setting => setting.Width);
        ProcessTableAXAMLColumnWidths current = new(
            Name: 280,
            ProcessID: 82,
            Status: 106,
            UserName: 140,
            CPU: 68,
            Lifetime: 112,
            PrivateMemory: 136,
            WorkingSet: 136,
            CommandLine: 520);
        ProcessTableAXAMLColumnWidths next = new(
            Name: 281,
            ProcessID: 83,
            Status: 107,
            UserName: 141,
            CPU: 69,
            Lifetime: 113,
            PrivateMemory: 137,
            WorkingSet: 138,
            CommandLine: 521);

        bool changed = ProcessTableAXAMLHotReload.TryApplyColumnWidths(
            source,
            current,
            next,
            out List<ProcessColumnSetting> updated);

        Assert.True(changed);
        Assert.Equal(281, Find(updated, ProcessTableColumnKind.Name).Width);
        Assert.Equal(83, Find(updated, ProcessTableColumnKind.ProcessID).Width);
        Assert.Equal(107, Find(updated, ProcessTableColumnKind.Status).Width);
        Assert.Equal(141, Find(updated, ProcessTableColumnKind.UserName).Width);
        Assert.Equal(69, Find(updated, ProcessTableColumnKind.CPU).Width);
        Assert.Equal(113, Find(updated, ProcessTableColumnKind.Lifetime).Width);
        Assert.Equal(137, Find(updated, ProcessTableColumnKind.PrivateMemory).Width);
        Assert.Equal(138, Find(updated, ProcessTableColumnKind.WorkingSet).Width);
        Assert.Equal(138, Find(updated, ProcessTableColumnKind.SharedWorkingSet).Width);
        Assert.Equal(521, Find(updated, ProcessTableColumnKind.CommandLine).Width);
        Assert.Equal(
            originalWidths[ProcessTableColumnKind.Disk],
            Find(updated, ProcessTableColumnKind.Disk).Width);
        Assert.All(
            source,
            setting => Assert.Equal(originalWidths[setting.Column], setting.Width));
    }

    [Fact]
    public void UnchangedAXAMLWidthsReturnAnIndependentUnchangedList()
    {
        List<ProcessColumnSetting> source = ProcessColumnSettings.CreateDefault();
        ProcessTableAXAMLColumnWidths widths = new(
            Name: 280,
            ProcessID: 82,
            Status: 106,
            UserName: 140,
            CPU: 68,
            Lifetime: 112,
            PrivateMemory: 136,
            WorkingSet: 136,
            CommandLine: 520);

        bool changed = ProcessTableAXAMLHotReload.TryApplyColumnWidths(
            source,
            widths,
            widths,
            out List<ProcessColumnSetting> updated);

        Assert.False(changed);
        Assert.NotSame(source, updated);
        Assert.Equal(
            source.Select(static setting => setting.Width),
            updated.Select(static setting => setting.Width));
    }

    private static ProcessColumnSetting Find(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessTableColumnKind column) =>
        Assert.Single(settings, setting => setting.Column == column);
}
#endif

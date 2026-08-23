using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessColumnSettingsTests
{
    [Fact]
    public void CatalogDefinesEveryColumnExactlyOnceInEnumOrder()
    {
        ProcessTableColumnKind[] kinds = Enum.GetValues<ProcessTableColumnKind>();

        Assert.Equal(kinds.Length, ProcessTableColumnCatalog.Definitions.Length);
        for (int columnIndex = 0; columnIndex < kinds.Length; columnIndex++)
            Assert.Equal(kinds[columnIndex], ProcessTableColumnCatalog.Definitions[columnIndex].Kind);
    }

    [Fact]
    public void StaticAndDynamicMasksPartitionTheCatalog()
    {
        ulong fullMask = 0;
        foreach (ProcessTableColumnKind kind in Enum.GetValues<ProcessTableColumnKind>())
            fullMask |= ProcessTableColumnCatalog.GetMask(kind);

        Assert.Equal(0UL, ProcessTableColumnCatalog.StaticMask & ProcessTableColumnCatalog.DynamicMask);
        Assert.Equal(fullMask, ProcessTableColumnCatalog.StaticMask | ProcessTableColumnCatalog.DynamicMask);
        Assert.True(ProcessTableColumnCatalog.Contains(
            ProcessTableColumnCatalog.StaticMask,
            ProcessTableColumnKind.Name));
        Assert.True(ProcessTableColumnCatalog.Contains(
            ProcessTableColumnCatalog.DynamicMask,
            ProcessTableColumnKind.Status));
    }

    [Fact]
    public void VisibleMaskSupportsInterleavedColumnLifetimes()
    {
        List<ProcessColumnSetting> settings =
        [
            Setting(ProcessTableColumnKind.Name, true, 120),
            Setting(ProcessTableColumnKind.CPU, true, 80),
            Setting(ProcessTableColumnKind.UserName, true, 140),
            Setting(ProcessTableColumnKind.PrivateMemory, true, 130),
            Setting(ProcessTableColumnKind.CommandLine, false, 300)
        ];

        ulong mask = ProcessTableColumnCatalog.CreateVisibleMask(settings);

        Assert.True(ProcessTableColumnCatalog.Contains(mask, ProcessTableColumnKind.Name));
        Assert.True(ProcessTableColumnCatalog.Contains(mask, ProcessTableColumnKind.CPU));
        Assert.True(ProcessTableColumnCatalog.Contains(mask, ProcessTableColumnKind.UserName));
        Assert.True(ProcessTableColumnCatalog.Contains(mask, ProcessTableColumnKind.PrivateMemory));
        Assert.False(ProcessTableColumnCatalog.Contains(mask, ProcessTableColumnKind.CommandLine));
    }

    [Fact]
    public void NormalizePreservesOrderAndRepairsDuplicatesWidthsAndMissingColumns()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.CPU, true, double.NaN),
            Setting(ProcessTableColumnKind.Name, false, 333),
            Setting(ProcessTableColumnKind.CPU, false, 999)
        ];

        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(source);

        Assert.Equal(ProcessTableColumnCatalog.Definitions.Length, normalized.Count);
        Assert.Equal(ProcessTableColumnKind.CPU, normalized[0].Column);
        Assert.Equal(ProcessTableColumnCatalog.Get(ProcessTableColumnKind.CPU).DefaultWidth, normalized[0].Width);
        Assert.True(normalized[0].Visible);
        Assert.Equal(ProcessTableColumnKind.Name, normalized[1].Column);
        Assert.Equal(333, normalized[1].Width);
        Assert.Single(normalized, static setting => setting.Column == ProcessTableColumnKind.CPU);
    }

    [Fact]
    public void NormalizeAlwaysLeavesAtLeastOneVisibleColumn()
    {
        List<ProcessColumnSetting> source = [];
        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
            source.Add(Setting(definition.Kind, false, definition.DefaultWidth));

        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(source);

        Assert.True(normalized[0].Visible);
        Assert.Single(normalized, static setting => setting.Visible);
    }

    [Fact]
    public void WithWidthChangesOnlyTheRequestedColumn()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.Name, true, 280),
            Setting(ProcessTableColumnKind.CPU, true, 68)
        ];

        List<ProcessColumnSetting> resized = ProcessColumnSettings.WithWidth(
            source,
            ProcessTableColumnKind.Name,
            360);

        Assert.Equal(360, resized.Single(static setting => setting.Column == ProcessTableColumnKind.Name).Width);
        Assert.Equal(68, resized.Single(static setting => setting.Column == ProcessTableColumnKind.CPU).Width);
        Assert.Equal(280, source[0].Width);
    }

    [Fact]
    public void MoveVisibleReordersVisibleColumnsWithoutMovingHiddenSlots()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.Name, true, 280),
            Setting(ProcessTableColumnKind.CommandLine, false, 520),
            Setting(ProcessTableColumnKind.ProcessID, true, 82),
            Setting(ProcessTableColumnKind.CPU, true, 68)
        ];

        List<ProcessColumnSetting> reordered = ProcessColumnSettings.MoveVisible(
            source,
            ProcessTableColumnKind.CPU,
            0);

        Assert.Equal(ProcessTableColumnKind.CPU, reordered[0].Column);
        Assert.Equal(ProcessTableColumnKind.CommandLine, reordered[1].Column);
        Assert.False(reordered[1].Visible);
        Assert.Equal(ProcessTableColumnKind.Name, reordered[2].Column);
        Assert.Equal(ProcessTableColumnKind.ProcessID, reordered[3].Column);
    }

    [Fact]
    public void AppliedLayoutDoesNotRaiseAGlobalSettingsRefresh()
    {
        AppSettings settings = new() { Autosave = false };
        int changedCount = 0;
        settings.Changed += () => changedCount++;
        List<ProcessColumnSetting> resized = ProcessColumnSettings.WithWidth(
            settings.DetailsColumns,
            ProcessTableColumnKind.Name,
            360);

        settings.UpdateDetailsColumnLayout(resized);

        Assert.Equal(0, changedCount);
        Assert.Equal(
            360,
            settings.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.Name).Width);
    }

    private static ProcessColumnSetting Setting(
        ProcessTableColumnKind column,
        bool visible,
        double width) =>
        new()
        {
            Column = column,
            Visible = visible,
            Width = width
        };
}

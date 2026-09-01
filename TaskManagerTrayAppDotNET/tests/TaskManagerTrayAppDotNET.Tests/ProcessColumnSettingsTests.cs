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

        Assert.Equal(expected: 0UL, ProcessTableColumnCatalog.StaticMask & ProcessTableColumnCatalog.DynamicMask);
        Assert.Equal(fullMask, ProcessTableColumnCatalog.StaticMask | ProcessTableColumnCatalog.DynamicMask);
        Assert.True(ProcessTableColumnCatalog.Contains(
            ProcessTableColumnCatalog.StaticMask,
            ProcessTableColumnKind.Name));
        Assert.True(ProcessTableColumnCatalog.Contains(
            ProcessTableColumnCatalog.DynamicMask,
            ProcessTableColumnKind.Status));
    }

    [Fact]
    public void DiskAndNetworkAreDefaultDynamicColumns()
    {
        ProcessTableColumnDefinition disk =
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.Disk);
        ProcessTableColumnDefinition network =
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.Network);

        Assert.True(disk.DefaultVisible);
        Assert.Equal(ProcessTableColumnLifetime.Dynamic, disk.Lifetime);
        Assert.True(network.DefaultVisible);
        Assert.Equal(ProcessTableColumnLifetime.Dynamic, network.Lifetime);
    }

    [Fact]
    public void CPUSingleIsAnOptionalDynamicPercentageColumn()
    {
        ProcessTableColumnDefinition definition =
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.CPUSingle);

        Assert.Equal(expected: "CPU (single)", definition.Title);
        Assert.Equal(ProcessTableColumnLifetime.Dynamic, definition.Lifetime);
        Assert.Equal(ProcessTableColumnAlignment.Right, definition.Alignment);
        Assert.False(definition.DefaultVisible);
    }

    [Fact]
    public void VisibleMaskSupportsInterleavedColumnLifetimes()
    {
        List<ProcessColumnSetting> settings =
        [
            Setting(ProcessTableColumnKind.Name, visible: true, width: 120),
            Setting(ProcessTableColumnKind.CPU, visible: true, width: 80),
            Setting(ProcessTableColumnKind.UserName, visible: true, width: 140),
            Setting(ProcessTableColumnKind.PrivateMemory, visible: true, width: 130),
            Setting(ProcessTableColumnKind.CommandLine, visible: false, width: 300)
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
            Setting(ProcessTableColumnKind.CPU, visible: true, double.NaN),
            Setting(ProcessTableColumnKind.Name, visible: false, width: 333),
            Setting(ProcessTableColumnKind.CPU, visible: false, width: 999)
        ];

        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(source);

        Assert.Equal(ProcessTableColumnCatalog.Definitions.Length, normalized.Count);
        Assert.Equal(ProcessTableColumnKind.CPU, normalized[0].Column);
        Assert.Equal(ProcessTableColumnCatalog.Get(ProcessTableColumnKind.CPU).DefaultWidth, normalized[0].Width);
        Assert.True(normalized[0].Visible);
        Assert.Equal(ProcessTableColumnKind.Name, normalized[1].Column);
        Assert.Equal(expected: 333, normalized[1].Width);
        Assert.Single(normalized, static setting => setting.Column == ProcessTableColumnKind.CPU);
    }

    [Fact]
    public void NormalizeAlwaysLeavesAtLeastOneVisibleColumn()
    {
        List<ProcessColumnSetting> source = [];
        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
            source.Add(Setting(definition.Kind, visible: false, definition.DefaultWidth));

        List<ProcessColumnSetting> normalized = ProcessColumnSettings.Normalize(source);

        Assert.True(normalized[0].Visible);
        Assert.Single(normalized, static setting => setting.Visible);
    }

    [Fact]
    public void WithWidthChangesOnlyTheRequestedColumn()
    {
        List<ProcessColumnSetting> source =
        [
            new()
            {
                Column = ProcessTableColumnKind.Name,
                Visible = true,
                Width = 280,
                Nickname = "Executable",
                ShowUserNamePrefix = true
            },
            Setting(ProcessTableColumnKind.CPU, visible: true, width: 68)
        ];

        List<ProcessColumnSetting> resized = ProcessColumnSettings.WithWidth(
            source,
            ProcessTableColumnKind.Name,
            width: 360);

        Assert.Equal(expected: 360,
            resized.Single(static setting => setting.Column == ProcessTableColumnKind.Name).Width);
        Assert.Equal(expected: 68,
            resized.Single(static setting => setting.Column == ProcessTableColumnKind.CPU).Width);
        Assert.Equal(expected: "Executable", resized[0].Nickname);
        Assert.True(resized[0].ShowUserNamePrefix);
        Assert.Equal(expected: 280, source[0].Width);
    }

    [Fact]
    public void MoveVisibleReordersVisibleColumnsWithoutMovingHiddenSlots()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.Name, visible: true, width: 280),
            Setting(ProcessTableColumnKind.CommandLine, visible: false, width: 520),
            Setting(ProcessTableColumnKind.ProcessID, visible: true, width: 82),
            Setting(ProcessTableColumnKind.CPU, visible: true, width: 68)
        ];

        List<ProcessColumnSetting> reordered = ProcessColumnSettings.MoveVisible(
            source,
            ProcessTableColumnKind.CPU,
            insertionIndex: 0);

        Assert.Equal(ProcessTableColumnKind.CPU, reordered[0].Column);
        Assert.Equal(ProcessTableColumnKind.CommandLine, reordered[1].Column);
        Assert.False(reordered[1].Visible);
        Assert.Equal(ProcessTableColumnKind.Name, reordered[2].Column);
        Assert.Equal(ProcessTableColumnKind.ProcessID, reordered[3].Column);
    }

    [Fact]
    public void ColumnDisplayOptionsHaveBackwardCompatibleDefaults()
    {
        ProcessColumnSetting setting = new();

        Assert.Empty(setting.Nickname);
        Assert.True(setting.ShowPercentSuffix);
        Assert.True(setting.ShowDecimalUsage);
        Assert.Equal(ProcessMemoryUnit.Kilobytes, setting.MemoryUnit);
        Assert.Equal(expected: "K", setting.MemorySuffix);
        Assert.False(setting.ShowUserNamePrefix);
        Assert.False(setting.ShowLiveTotal);
    }

    [Fact]
    public void NormalizeClonesAndPreservesEveryColumnDisplayOption()
    {
        ProcessColumnSetting source = new()
        {
            Column = ProcessTableColumnKind.PrivateMemory,
            Visible = true,
            Width = 150,
            Nickname = "Private bytes",
            ShowPercentSuffix = false,
            ShowDecimalUsage = false,
            MemoryUnit = ProcessMemoryUnit.Gigabytes,
            MemorySuffix = " GiB",
            ShowUserNamePrefix = true,
            ShowLiveTotal = true
        };

        ProcessColumnSetting normalized = ProcessColumnSettings.Normalize([source])[0];

        Assert.NotSame(source, normalized);
        Assert.Equal(expected: "Private bytes", normalized.Nickname);
        Assert.False(normalized.ShowPercentSuffix);
        Assert.False(normalized.ShowDecimalUsage);
        Assert.Equal(ProcessMemoryUnit.Gigabytes, normalized.MemoryUnit);
        Assert.Equal(expected: " GiB", normalized.MemorySuffix);
        Assert.True(normalized.ShowUserNamePrefix);
        Assert.True(normalized.ShowLiveTotal);
    }

    [Fact]
    public void MoveVisiblePreservesOptionsOnTheMovedColumn()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.Name, visible: true, width: 280),
            Setting(ProcessTableColumnKind.ProcessID, visible: true, width: 82),
            new()
            {
                Column = ProcessTableColumnKind.CPU,
                Visible = true,
                Width = 68,
                Nickname = "Processor",
                ShowPercentSuffix = false,
                ShowDecimalUsage = false
            }
        ];

        List<ProcessColumnSetting> reordered = ProcessColumnSettings.MoveVisible(
            source,
            ProcessTableColumnKind.CPU,
            insertionIndex: 0);

        Assert.Equal(ProcessTableColumnKind.CPU, reordered[0].Column);
        Assert.Equal(expected: "Processor", reordered[0].Nickname);
        Assert.False(reordered[0].ShowPercentSuffix);
        Assert.False(reordered[0].ShowDecimalUsage);
    }

    [Fact]
    public void WithPropertiesChangesOptionsWithoutReplacingLayoutState()
    {
        List<ProcessColumnSetting> source =
        [
            Setting(ProcessTableColumnKind.Name, visible: true, width: 280),
            Setting(ProcessTableColumnKind.PrivateMemory, visible: true, width: 136)
        ];
        ProcessColumnSetting replacement = new()
        {
            Column = ProcessTableColumnKind.PrivateMemory,
            Visible = false,
            Width = 999,
            Nickname = "Private",
            MemoryUnit = ProcessMemoryUnit.Megabytes,
            MemorySuffix = " MB",
            ShowLiveTotal = true
        };

        List<ProcessColumnSetting> changed = ProcessColumnSettings.WithProperties(source, replacement);
        ProcessColumnSetting memory =
            changed.Single(static setting => setting.Column == ProcessTableColumnKind.PrivateMemory);

        Assert.True(memory.Visible);
        Assert.Equal(expected: 136, memory.Width);
        Assert.Equal(expected: "Private", memory.Nickname);
        Assert.Equal(ProcessMemoryUnit.Megabytes, memory.MemoryUnit);
        Assert.Equal(expected: " MB", memory.MemorySuffix);
        Assert.True(memory.ShowLiveTotal);
    }

    [Theory]
    [InlineData(ProcessMemoryUnit.Kilobytes, "K")]
    [InlineData(ProcessMemoryUnit.Megabytes, "M")]
    [InlineData(ProcessMemoryUnit.Gigabytes, "G")]
    [InlineData(ProcessMemoryUnit.PercentageOfSystem, "%")]
    public void MemoryUnitsHaveStableDefaultSuffixes(ProcessMemoryUnit unit, string expectedSuffix) =>
        Assert.Equal(expectedSuffix, ProcessColumnSettings.GetDefaultMemorySuffix(unit));

    [Fact]
    public void MemoryClassificationIncludesProcessPoolAndAcceleratorMemory()
    {
        Assert.True(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.WorkingSet));
        Assert.True(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.PrivateMemory));
        Assert.True(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.PagedPool));
        Assert.True(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.DedicatedGPUMemory));
        Assert.True(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.SharedNPUMemory));
        Assert.False(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.IOReadBytes));
        Assert.False(ProcessColumnSettings.IsMemoryColumn(ProcessTableColumnKind.CPU));
    }

    [Fact]
    public void LiveTotalsCoverResourceCountersButNotIdentifiersOrDisplayStates()
    {
        ProcessTableColumnKind[] supportedColumns =
        [
            ProcessTableColumnKind.CPU,
            ProcessTableColumnKind.CPUTime,
            ProcessTableColumnKind.PrivateMemory,
            ProcessTableColumnKind.SharedWorkingSet,
            ProcessTableColumnKind.Disk,
            ProcessTableColumnKind.Network,
            ProcessTableColumnKind.Handles,
            ProcessTableColumnKind.Threads,
            ProcessTableColumnKind.GDIObjects,
            ProcessTableColumnKind.IOReads,
            ProcessTableColumnKind.IOWriteBytes,
            ProcessTableColumnKind.GPU,
            ProcessTableColumnKind.DedicatedGPUMemory,
            ProcessTableColumnKind.NPU,
            ProcessTableColumnKind.SharedNPUMemory
        ];
        ProcessTableColumnKind[] unsupportedColumns =
        [
            ProcessTableColumnKind.Name,
            ProcessTableColumnKind.ProcessID,
            ProcessTableColumnKind.Status,
            ProcessTableColumnKind.Lifetime,
            ProcessTableColumnKind.BasePriority,
            ProcessTableColumnKind.GPUEngine,
            ProcessTableColumnKind.NPUEngine
        ];

        Assert.All(supportedColumns, static column =>
            Assert.True(ProcessColumnSettings.SupportsLiveTotal(column)));
        Assert.All(unsupportedColumns, static column =>
            Assert.False(ProcessColumnSettings.SupportsLiveTotal(column)));
    }

    [Fact]
    public void OnlyVisibleSupportedLiveTotalsRequireFullSampling()
    {
        ProcessColumnSetting hiddenCPU = Setting(ProcessTableColumnKind.CPU, visible: false, width: 68);
        hiddenCPU.ShowLiveTotal = true;
        ProcessColumnSetting visibleName = Setting(ProcessTableColumnKind.Name, visible: true, width: 280);
        visibleName.ShowLiveTotal = true;

        Assert.False(ProcessColumnSettings.HasVisibleLiveTotals([hiddenCPU, visibleName]));

        hiddenCPU.Visible = true;

        Assert.True(ProcessColumnSettings.HasVisibleLiveTotals([hiddenCPU, visibleName]));
    }

    [Fact]
    public void ResolveTitleUsesNicknameOrOriginalCatalogTitle()
    {
        ProcessColumnSetting setting = Setting(ProcessTableColumnKind.ProcessID, visible: true, width: 82);

        Assert.Equal(expected: "PID", ProcessColumnSettings.ResolveTitle(setting));

        setting.Nickname = "Identifier";
        Assert.Equal(expected: "Identifier", ProcessColumnSettings.ResolveTitle(setting));

        setting.Nickname = "  ";
        Assert.Equal(expected: "PID", ProcessColumnSettings.ResolveTitle(setting));
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
            width: 360);

        settings.UpdateDetailsColumnLayout(resized);

        Assert.Equal(expected: 0, changedCount);
        Assert.Equal(
            expected: 360,
            settings.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.Name).Width);
    }

    [Fact]
    public void LiveColumnResizingIsEnabledByDefault()
    {
        AppSettings settings = new();

        Assert.True(settings.EnableLiveDetailsColumnResizing);
    }

    [Fact]
    public void LiveColumnResizingModeRoundTripsThroughSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new() { Autosave = false, EnableLiveDetailsColumnResizing = false };
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.False(loaded.EnableLiveDetailsColumnResizing);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ColumnDisplayOptionsRoundTripThroughSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new() { Autosave = false };
            ProcessColumnSetting memory = settings.DetailsColumns.Single(static setting =>
                setting.Column == ProcessTableColumnKind.PrivateMemory);
            memory.Nickname = "Private";
            memory.MemoryUnit = ProcessMemoryUnit.Gigabytes;
            memory.MemorySuffix = " GiB";
            memory.ShowLiveTotal = true;
            ProcessColumnSetting cpu =
                settings.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.CPU);
            cpu.ShowPercentSuffix = false;
            cpu.ShowDecimalUsage = false;
            ProcessColumnSetting userName =
                settings.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.UserName);
            userName.ShowUserNamePrefix = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);
            ProcessColumnSetting loadedMemory = loaded.DetailsColumns.Single(static setting =>
                setting.Column == ProcessTableColumnKind.PrivateMemory);
            ProcessColumnSetting loadedCPU =
                loaded.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.CPU);
            ProcessColumnSetting loadedUserName =
                loaded.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.UserName);

            Assert.Equal(expected: "Private", loadedMemory.Nickname);
            Assert.Equal(ProcessMemoryUnit.Gigabytes, loadedMemory.MemoryUnit);
            Assert.Equal(expected: " GiB", loadedMemory.MemorySuffix);
            Assert.True(loadedMemory.ShowLiveTotal);
            Assert.False(loadedCPU.ShowPercentSuffix);
            Assert.False(loadedCPU.ShowDecimalUsage);
            Assert.True(loadedUserName.ShowUserNamePrefix);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyColumnXmlUsesDefaultsForNewDisplayOptions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                path,
                contents: """
                          <?xml version="1.0" encoding="utf-8"?>
                          <AppSettings>
                            <DetailsColumns>
                              <Column Column="CPU" Visible="true" Width="91" />
                            </DetailsColumns>
                          </AppSettings>
                          """);

            AppSettings loaded = AppSettings.LoadOrDefault(path);
            ProcessColumnSetting cpu =
                loaded.DetailsColumns.Single(static setting => setting.Column == ProcessTableColumnKind.CPU);

            Assert.Equal(expected: 91, cpu.Width);
            Assert.Empty(cpu.Nickname);
            Assert.True(cpu.ShowPercentSuffix);
            Assert.True(cpu.ShowDecimalUsage);
            Assert.Equal(ProcessMemoryUnit.Kilobytes, cpu.MemoryUnit);
            Assert.Equal(expected: "K", cpu.MemorySuffix);
            Assert.False(cpu.ShowUserNamePrefix);
            Assert.False(cpu.ShowLiveTotal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ProcessColumnSetting Setting(
        ProcessTableColumnKind column,
        bool visible,
        double width) =>
        new() { Column = column, Visible = visible, Width = width };
}

using TaskManagerTrayAppDotNET.Models;
using TrayAppDotNETCommon.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void RenderingBackendDefaultsToGPUPreferred()
    {
        AppSettings settings = new();

        Assert.Equal(TrayAppDotNETRenderingBackend.GPUPreferred, settings.RenderingBackend);
    }

    [Fact]
    public void TrayGraphDefaultsToAverageCPUMarquee()
    {
        AppSettings settings = new();

        Assert.Equal(TrayGraphStyle.Marquee, settings.TrayGraphStyle);
        Assert.Equal(TrayGraphDataSource.CPUAverage, settings.TrayGraphDataSource);
    }

    [Fact]
    public void ProcessHeaderButtonsDefaultToCurrentLeftToRightOrder()
    {
        AppSettings settings = new();

        Assert.Equal(
            [
                ProcessHeaderButtonKind.RunNewTask,
                ProcessHeaderButtonKind.Columns,
                ProcessHeaderButtonKind.EndTask,
                ProcessHeaderButtonKind.RestartExplorer
            ],
            settings.ProcessHeaderButtonOrder);
    }

    [Fact]
    public void ProcessHeaderButtonNormalizationRemovesInvalidDuplicatesAndAppendsMissingButtons()
    {
        List<ProcessHeaderButtonKind> normalized = ProcessHeaderButtonSettings.Normalize(
        [
            ProcessHeaderButtonKind.EndTask,
            (ProcessHeaderButtonKind)int.MaxValue,
            ProcessHeaderButtonKind.EndTask
        ]);

        Assert.Equal(
            [
                ProcessHeaderButtonKind.EndTask,
                ProcessHeaderButtonKind.RunNewTask,
                ProcessHeaderButtonKind.Columns,
                ProcessHeaderButtonKind.RestartExplorer
            ],
            normalized);
    }

    [Fact]
    public void EquivalentProcessHeaderButtonOrderDoesNotRaiseChangeNotifications()
    {
        AppSettings settings = new() { Autosave = false };
        int propertyChangedCount = 0;
        int changedCount = 0;
        settings.PropertyChanged += (_, _) => propertyChangedCount++;
        settings.Changed += () => changedCount++;

        settings.UpdateProcessHeaderButtonOrder(ProcessHeaderButtonSettings.CreateDefault());

        Assert.Equal(expected: 0, propertyChangedCount);
        Assert.Equal(expected: 0, changedCount);
    }

    [Fact]
    public void UpdatedProcessHeaderButtonOrderSuppressesGlobalShellChangeNotification()
    {
        AppSettings settings = new() { Autosave = false };
        int propertyChangedCount = 0;
        int changedCount = 0;
        settings.PropertyChanged += (_, _) => propertyChangedCount++;
        settings.Changed += () => changedCount++;

        settings.UpdateProcessHeaderButtonOrder(
        [
            ProcessHeaderButtonKind.EndTask,
            ProcessHeaderButtonKind.Columns,
            ProcessHeaderButtonKind.RunNewTask,
            ProcessHeaderButtonKind.RestartExplorer
        ]);

        Assert.Equal(expected: 1, propertyChangedCount);
        Assert.Equal(expected: 0, changedCount);
        Assert.Equal(ProcessHeaderButtonKind.EndTask, settings.ProcessHeaderButtonOrder[0]);
    }

    [Fact]
    public void ProcessHeaderButtonOrderRoundTripsThroughSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new()
            {
                Autosave = false,
                ProcessHeaderButtonOrder =
                [
                    ProcessHeaderButtonKind.EndTask,
                    ProcessHeaderButtonKind.Columns,
                    ProcessHeaderButtonKind.RunNewTask,
                    ProcessHeaderButtonKind.RestartExplorer
                ]
            };
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(settings.ProcessHeaderButtonOrder, loaded.ProcessHeaderButtonOrder);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacySettingsWithoutProcessHeaderButtonOrderUseDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                path,
                contents: """
                          <?xml version="1.0" encoding="utf-8"?>
                          <AppSettings>
                            <AlwaysOnTop>true</AlwaysOnTop>
                          </AppSettings>
                          """);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(ProcessHeaderButtonSettings.CreateDefault(), loaded.ProcessHeaderButtonOrder);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PerformanceDeviceOrderingDefaultsToPriorityWithoutAnExplicitOrder()
    {
        AppSettings settings = new();

        Assert.Equal(PerformanceDeviceOrdering.DefaultPriority, settings.PerformanceDevicePriority);
        Assert.Empty(settings.PerformanceDeviceOrder);
    }

    [Fact]
    public void PerformanceSamplingSettingsDefaultAndClampAtAssignment()
    {
        AppSettings settings = new() { Autosave = false };

        Assert.Equal(
            PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
            settings.PerformanceHistoryLengthMinutes);
        Assert.Equal(
            PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
            settings.PerformanceSampleIntervalMilliseconds);

        settings.PerformanceHistoryLengthMinutes = int.MinValue;
        settings.PerformanceSampleIntervalMilliseconds = int.MaxValue;

        Assert.Equal(
            PerformanceSamplingSettings.MinimumHistoryLengthMinutes,
            settings.PerformanceHistoryLengthMinutes);
        Assert.Equal(
            PerformanceSamplingSettings.MaximumSampleIntervalMilliseconds,
            settings.PerformanceSampleIntervalMilliseconds);
    }

    [Fact]
    public void OnlyTheLiveAppSettingsInstanceCanAutosaveToTheDefaultPath()
    {
        AppSettings settings = new();
        AppSettings otherSettings = new();

        Assert.False(settings.CanAutosaveToDefaultPath(null));
        Assert.False(settings.CanAutosaveToDefaultPath(otherSettings));
        Assert.True(settings.CanAutosaveToDefaultPath(settings));

        settings.Autosave = false;

        Assert.False(settings.CanAutosaveToDefaultPath(settings));
    }

    [Fact]
    public void PerformanceSamplingChangesRaiseNormalSettingsNotifications()
    {
        AppSettings settings = new() { Autosave = false };
        List<string?> changedProperties = [];
        int changedCount = 0;
        settings.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        settings.Changed += () => changedCount++;

        settings.PerformanceHistoryLengthMinutes = 5;
        settings.PerformanceSampleIntervalMilliseconds = 2_000;

        Assert.Equal(
            [
                nameof(AppSettings.PerformanceHistoryLengthMinutes),
                nameof(AppSettings.PerformanceSampleIntervalMilliseconds)
            ],
            changedProperties);
        Assert.Equal(expected: 2, changedCount);
    }

    [Fact]
    public void PerformanceSamplingSettingsRoundTripThroughSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new()
            {
                Autosave = false,
                PerformanceHistoryLengthMinutes = 15,
                PerformanceSampleIntervalMilliseconds = 2_500
            };
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(expected: 15, loaded.PerformanceHistoryLengthMinutes);
            Assert.Equal(expected: 2_500, loaded.PerformanceSampleIntervalMilliseconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CPUHighestCoreTraceDefaultsDisabledAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.False(settings.ShowCPUHighestCoreTrace);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.ShowCPUHighestCoreTrace = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.ShowCPUHighestCoreTrace);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PerformanceGraphUnderfillDefaultsEnabledAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.True(settings.ShowPerformanceGraphUnderfill);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.ShowPerformanceGraphUnderfill = false;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.False(loaded.ShowPerformanceGraphUnderfill);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CPUPerformanceGraphViewDefaultsToLogicalProcessorsAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.Equal(
            CPUPerformanceGraphView.LogicalProcessors,
            settings.CPUPerformanceGraphView);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.CPUPerformanceGraphView = CPUPerformanceGraphView.DetailedView;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(
                CPUPerformanceGraphView.DetailedView,
                loaded.CPUPerformanceGraphView);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LiveCPUPerformanceGraphViewUpdateAvoidsGlobalShellNotification()
    {
        AppSettings settings = new() { Autosave = false };
        int propertyChangedCount = 0;
        int changedCount = 0;
        settings.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(AppSettings.CPUPerformanceGraphView))
                propertyChangedCount++;
        };
        settings.Changed += () => changedCount++;

        settings.UpdateCPUPerformanceGraphView(CPUPerformanceGraphView.DetailedView);

        Assert.Equal(expected: 1, propertyChangedCount);
        Assert.Equal(expected: 0, changedCount);
        Assert.Equal(
            CPUPerformanceGraphView.DetailedView,
            settings.CPUPerformanceGraphView);
    }

    [Fact]
    public void PerformanceSamplingSettingsNormalizeOutOfRangeXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                path,
                contents: """
                          <?xml version="1.0" encoding="utf-8"?>
                          <AppSettings>
                            <PerformanceHistoryLengthMinutes>-100</PerformanceHistoryLengthMinutes>
                            <PerformanceSampleIntervalMilliseconds>2147483647</PerformanceSampleIntervalMilliseconds>
                          </AppSettings>
                          """);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(
                PerformanceSamplingSettings.MinimumHistoryLengthMinutes,
                loaded.PerformanceHistoryLengthMinutes);
            Assert.Equal(
                PerformanceSamplingSettings.MaximumSampleIntervalMilliseconds,
                loaded.PerformanceSampleIntervalMilliseconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacySettingsWithoutPerformanceSamplingValuesUseDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, contents: "<AppSettings />");

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(
                PerformanceSamplingSettings.DefaultHistoryLengthMinutes,
                loaded.PerformanceHistoryLengthMinutes);
            Assert.Equal(
                PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds,
                loaded.PerformanceSampleIntervalMilliseconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PerformanceDeviceOrderingRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new()
        {
            Autosave = false,
            PerformanceDevicePriority =
            [
                PerformanceDeviceKind.Disk,
                PerformanceDeviceKind.Network,
                PerformanceDeviceKind.GPU,
                PerformanceDeviceKind.Memory,
                PerformanceDeviceKind.CPU
            ],
            PerformanceDeviceOrder = ["disk:1", "cpu", "gpu:0"]
        };

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(settings.PerformanceDevicePriority, loaded.PerformanceDevicePriority);
            Assert.Equal(settings.PerformanceDeviceOrder, loaded.PerformanceDeviceOrder);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PerformanceDeviceOrderingNormalizesCorruptSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                path,
                contents: """
                          <?xml version="1.0" encoding="utf-8"?>
                          <AppSettings>
                            <PerformanceDevicePriority>
                              <Kind>Disk</Kind>
                              <Kind>2147483647</Kind>
                              <Kind>Disk</Kind>
                              <Kind>CPU</Kind>
                            </PerformanceDevicePriority>
                            <PerformanceDeviceOrder>
                              <DeviceID> gpu:0 </DeviceID>
                              <DeviceID></DeviceID>
                              <DeviceID>gpu:0</DeviceID>
                              <DeviceID>disk:0</DeviceID>
                            </PerformanceDeviceOrder>
                          </AppSettings>
                          """);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(
                [
                    PerformanceDeviceKind.Disk,
                    PerformanceDeviceKind.CPU,
                    PerformanceDeviceKind.Memory,
                    PerformanceDeviceKind.GPU,
                    PerformanceDeviceKind.Network
                ],
                loaded.PerformanceDevicePriority);
            Assert.Equal(["gpu:0", "disk:0"], loaded.PerformanceDeviceOrder);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyAndExplicitlyEmptyPriorityXmlUseTheCompleteDefault()
    {
        string legacyPath = Path.Combine(
            Path.GetTempPath(),
            $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        string emptyPath = Path.Combine(
            Path.GetTempPath(),
            $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(legacyPath, contents: "<AppSettings />");
            File.WriteAllText(
                emptyPath,
                contents: "<AppSettings><PerformanceDevicePriority /></AppSettings>");

            AppSettings legacy = AppSettings.LoadOrDefault(legacyPath);
            AppSettings explicitlyEmpty = AppSettings.LoadOrDefault(emptyPath);

            Assert.Equal(PerformanceDeviceOrdering.DefaultPriority, legacy.PerformanceDevicePriority);
            Assert.Equal(PerformanceDeviceOrdering.DefaultPriority, explicitlyEmpty.PerformanceDevicePriority);
        }
        finally
        {
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            if (File.Exists(emptyPath)) File.Delete(emptyPath);
        }
    }

    [Fact]
    public void AppliedPerformanceDeviceOrderDoesNotRaiseAGlobalSettingsRefresh()
    {
        AppSettings settings = new() { Autosave = false };
        int changedCount = 0;
        settings.Changed += () => changedCount++;

        settings.UpdatePerformanceDeviceOrder(["disk:0", "cpu", "memory"]);

        Assert.Equal(expected: 0, changedCount);
        Assert.Equal(["disk:0", "cpu", "memory"], settings.PerformanceDeviceOrder);
    }

    [Fact]
    public void PerformanceHardwareNameReplacementRulesRoundTripThroughSettingsXml()
    {
        AppSettings settings = new()
        {
            Autosave = false,
            PerformanceHardwareNameReplacementRules =
            [
                new PerformanceHardwareNameReplacementRule
                {
                    DeviceKind = PerformanceDeviceKind.Network,
                    MatchPattern = "^Intel\\(R\\) (.+)$",
                    Replacement = "$1"
                },
                new PerformanceHardwareNameReplacementRule
                {
                    DeviceKind = PerformanceDeviceKind.GPU,
                    MatchPattern = "^NVIDIA GeForce (.+)$",
                    Replacement = "GeForce $1"
                }
            ]
        };
        string path = Path.Combine(
            Path.GetTempPath(),
            $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Collection(
                loaded.PerformanceHardwareNameReplacementRules,
                rule =>
                {
                    Assert.Equal(PerformanceDeviceKind.Network, rule.DeviceKind);
                    Assert.Equal(expected: "^Intel\\(R\\) (.+)$", rule.MatchPattern);
                    Assert.Equal(expected: "$1", rule.Replacement);
                },
                rule =>
                {
                    Assert.Equal(PerformanceDeviceKind.GPU, rule.DeviceKind);
                    Assert.Equal(expected: "^NVIDIA GeForce (.+)$", rule.MatchPattern);
                    Assert.Equal(expected: "GeForce $1", rule.Replacement);
                });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LiveHardwareNameRuleUpdatesAvoidAGlobalSettingsRefresh()
    {
        AppSettings settings = new() { Autosave = false };
        List<string?> changedProperties = [];
        int changedCount = 0;
        settings.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        settings.Changed += () => changedCount++;

        settings.UpdatePerformanceHardwareNameReplacementRules(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Network, MatchPattern = "Adapter", Replacement = "NIC"
            }
        ]);

        Assert.Equal(expected: 0, changedCount);
        Assert.Equal(
            [nameof(AppSettings.PerformanceHardwareNameReplacementRules)],
            changedProperties);
        PerformanceHardwareNameReplacementRule rule =
            Assert.Single(settings.PerformanceHardwareNameReplacementRules);
        Assert.Equal(PerformanceDeviceKind.Network, rule.DeviceKind);
        Assert.Equal(expected: "Adapter", rule.MatchPattern);
        Assert.Equal(expected: "NIC", rule.Replacement);
    }

    [Fact]
    public void TrayGraphSettingsNormalizeAndRoundTripThroughSettingsXml()
    {
        AppSettings settings = new()
        {
            Autosave = false,
            TrayGraphStyle = (TrayGraphStyle)int.MaxValue,
            TrayGraphDataSource = (TrayGraphDataSource)int.MaxValue
        };
        Assert.Equal(TrayGraphStyle.Marquee, settings.TrayGraphStyle);
        Assert.Equal(TrayGraphDataSource.CPUAverage, settings.TrayGraphDataSource);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.TrayGraphStyle = TrayGraphStyle.Current;
            settings.TrayGraphDataSource = TrayGraphDataSource.Memory;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(TrayGraphStyle.Current, loaded.TrayGraphStyle);
            Assert.Equal(TrayGraphDataSource.Memory, loaded.TrayGraphDataSource);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SubmenuDelayDefaultsToCustom150Milliseconds()
    {
        AppSettings settings = new();

        Assert.False(settings.UseSystemSubmenuShowDelay);
        Assert.Equal(expected: 150, settings.SubmenuShowDelayMs);
    }

    [Fact]
    public void SubmenuDelayClampsAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false, SubmenuShowDelayMs = int.MaxValue };
        Assert.Equal(TimeConstants.TrayMenuSubmenuShowDelayMaxMs, settings.SubmenuShowDelayMs);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.UseSystemSubmenuShowDelay = true;
            settings.SubmenuShowDelayMs = 275;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.UseSystemSubmenuShowDelay);
            Assert.Equal(expected: 275, loaded.SubmenuShowDelayMs);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GridFontWeightDefaultsToNormalAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.Equal(DetailsGridFontWeight.Normal, settings.GridFontWeight);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.GridFontWeight = DetailsGridFontWeight.SemiBold;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(DetailsGridFontWeight.SemiBold, loaded.GridFontWeight);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GridRowSpacingDefaultsAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.Equal(AppSettings.GridRowSpacingDefault, settings.GridRowSpacing);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.GridRowSpacing = 7.25;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(expected: 7.25, loaded.GridRowSpacing);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyOverlappingGridRowHeightMigratesToZeroSpacing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                path,
                contents: """
                          <?xml version="1.0" encoding="utf-8"?>
                          <AppSettings>
                            <GridFontSize>32</GridFontSize>
                            <GridRowHeight>14</GridRowHeight>
                          </AppSettings>
                          """);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(AppSettings.GridRowSpacingMinimum, loaded.GridRowSpacing);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WindowManagementDefaultsPreserveExistingTrayBehaviorAndRoundTrip()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.CloseToTray);
        Assert.False(settings.MinimizeToTray);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.AlwaysOnTop = true;
            settings.CloseToTray = false;
            settings.MinimizeToTray = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.AlwaysOnTop);
            Assert.False(loaded.CloseToTray);
            Assert.True(loaded.MinimizeToTray);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NarrowWindowSidebarCollapseDefaultsEnabledAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.True(settings.CollapseSidebarWhenNarrow);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.CollapseSidebarWhenNarrow = false;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.False(loaded.CollapseSidebarWhenNarrow);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProcessSearchBarAlignmentDefaultsCenteredAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.False(settings.LeftAlignProcessSearchBar);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.LeftAlignProcessSearchBar = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.LeftAlignProcessSearchBar);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExplorerRestartConfirmationDefaultsEnabledAndSkipPreferenceRoundTrips()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.False(settings.SkipRestartExplorerConfirmation);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.SkipRestartExplorerConfirmation = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.SkipRestartExplorerConfirmation);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WindowsTaskManagerHotkeyOverrideDefaultsDisabledAndRoundTrips()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.False(settings.OverrideWindowsTaskManagerHotkey);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.OverrideWindowsTaskManagerHotkey = true;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.OverrideWindowsTaskManagerHotkey);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsSidebarWidthDefaultsToSentinelAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.Equal(expected: 0, settings.SettingsSidebarWidth);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.SettingsSidebarWidth = 337.5;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(expected: 337.5, loaded.SettingsSidebarWidth);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

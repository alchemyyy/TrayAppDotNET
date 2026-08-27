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
                ProcessHeaderButtonKind.EndTask
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
                ProcessHeaderButtonKind.Columns
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

        Assert.Equal(0, propertyChangedCount);
        Assert.Equal(0, changedCount);
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
            ProcessHeaderButtonKind.RunNewTask
        ]);

        Assert.Equal(1, propertyChangedCount);
        Assert.Equal(0, changedCount);
        Assert.Equal(ProcessHeaderButtonKind.EndTask, settings.ProcessHeaderButtonOrder[0]);
    }

    [Fact]
    public void ProcessHeaderButtonOrderRoundTripsThroughSettingsXml()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new() { Autosave = false };
            settings.ProcessHeaderButtonOrder =
            [
                ProcessHeaderButtonKind.EndTask,
                ProcessHeaderButtonKind.Columns,
                ProcessHeaderButtonKind.RunNewTask
            ];
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
                """
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
    public void TrayGraphSettingsNormalizeAndRoundTripThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        settings.TrayGraphStyle = (TrayGraphStyle)int.MaxValue;
        settings.TrayGraphDataSource = (TrayGraphDataSource)int.MaxValue;
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
        Assert.Equal(150, settings.SubmenuShowDelayMs);
    }

    [Fact]
    public void SubmenuDelayClampsAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        settings.SubmenuShowDelayMs = int.MaxValue;
        Assert.Equal(TimeConstants.TrayMenuSubmenuShowDelayMaxMs, settings.SubmenuShowDelayMs);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.UseSystemSubmenuShowDelay = true;
            settings.SubmenuShowDelayMs = 275;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.True(loaded.UseSystemSubmenuShowDelay);
            Assert.Equal(275, loaded.SubmenuShowDelayMs);
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
    public void SettingsSidebarWidthDefaultsToSentinelAndRoundTripsThroughSettingsXml()
    {
        AppSettings settings = new() { Autosave = false };
        Assert.Equal(0, settings.SettingsSidebarWidth);

        string path = Path.Combine(Path.GetTempPath(), $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            settings.SettingsSidebarWidth = 337.5;
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.Equal(337.5, loaded.SettingsSidebarWidth);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

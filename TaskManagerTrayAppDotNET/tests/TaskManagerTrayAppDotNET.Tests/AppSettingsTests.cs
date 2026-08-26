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
}

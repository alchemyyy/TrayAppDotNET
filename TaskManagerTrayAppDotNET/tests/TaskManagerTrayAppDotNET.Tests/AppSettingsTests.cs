using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class AppSettingsTests
{
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
}

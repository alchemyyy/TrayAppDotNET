using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace NetworkTrayAppDotNET.Tests;

public sealed class AppSettingsRoundTripTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    private string SettingsPath => Path.Combine(TestDirectory, "settings.xml");

    public AppSettingsRoundTripTests() => Directory.CreateDirectory(TestDirectory);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void NetworkSettingsSurviveASaveAndLoad()
    {
        AppSettings settings = new()
        {
            TrayIconStyle = TrayAppDotNETTrayIconStyle.Static,
            FlyoutStyle = FlyoutStyle.Windows11,
            AdapterSettingsStyle = AdapterSettingsStyle.ControlPanel,
            TrayDoubleClickAction = TrayClickAction.Nothing,
            TrayAltRightClickAction = TrayClickAction.OpenSettings
        };
        TrayXmlSerializer.WriteFile(SettingsPath, settings);

        AppSettings restored = AppSettings.ReadOrDefault<AppSettings>(SettingsPath);

        Assert.Equal(TrayAppDotNETTrayIconStyle.Static, restored.TrayIconStyle);
        Assert.Equal(FlyoutStyle.Windows11, restored.FlyoutStyle);
        Assert.Equal(AdapterSettingsStyle.ControlPanel, restored.AdapterSettingsStyle);
        Assert.Equal(TrayClickAction.Nothing, restored.TrayDoubleClickAction);
        Assert.Equal(TrayClickAction.OpenSettings, restored.TrayAltRightClickAction);
    }

    [Fact]
    public void AMissingFileLoadsTheDefaults()
    {
        AppSettings restored = AppSettings.ReadOrDefault<AppSettings>(SettingsPath);

        Assert.Equal(TrayAppDotNETTrayIconStyle.Dynamic, restored.TrayIconStyle);
        Assert.Equal(AppSettings.DefaultFlyoutStyle, restored.FlyoutStyle);
        Assert.Equal(TrayClickAction.OpenAdapterSettings, restored.TrayDoubleClickAction);
    }
}

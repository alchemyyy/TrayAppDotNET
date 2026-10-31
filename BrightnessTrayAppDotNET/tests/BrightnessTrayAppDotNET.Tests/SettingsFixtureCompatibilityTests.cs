using BrightnessTrayAppDotNET.Models;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>
/// settings.xml files written by the serializer before Brightness derived from AppSettingsCommon (Fixtures/) must
/// keep every value.
/// </summary>
public sealed class SettingsFixtureCompatibilityTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void CustomizedPreRefactorFileKeepsEveryMovedValue()
    {
        AppSettings settings = TrayXmlSerializer.ReadFile<AppSettings>(FixturePath("settings-customized.xml"));

        Assert.False(settings.RunOnStartup);
        Assert.False(settings.AutosaveProfiles);
        Assert.True(settings.Autosave);
        Assert.Equal(TrayAppDotNETContextMenuPosition.Classic, settings.ContextMenuPosition);
        Assert.True(settings.UseSystemSubmenuShowDelay);
        Assert.Equal(expected: 650, settings.SubmenuShowDelayMs);
        Assert.False(settings.KeepFlyoutWarm);
        Assert.False(settings.KeepTrayContextMenuWarm);
        Assert.Equal(TrayAppDotNETRenderingBackend.Software, settings.RenderingBackend);
        Assert.Equal(expected: "en-US", settings.Language);
        Assert.False(settings.CheckForUpdatesEnabled);
        Assert.True(settings.ShowUpdateNotificationsEnabled);
        Assert.False(settings.ShowUpdateButtonInFlyout);
        Assert.Equal(expected: 7_200_000, settings.UpdateCheckIntervalMs);
        Assert.Equal(expected: 4321, settings.SkippedUpdateVersion);
        Assert.Equal(expected: 19, settings.ContextMenuFontSize);
        Assert.Equal(TrayAppDotNETThemeMode.Dark, settings.ThemeMode);
        Assert.Equal(TrayAppDotNETTrayIconStyle.Static, settings.TrayIconStyle);
        Assert.Equal(expected: "#FF102030", settings.TextColor.LightHex);
        Assert.Equal(expected: "#FFA0B0C0", settings.TextColor.DarkHex);
        Assert.Equal(expected: "#FF010203", settings.BackgroundColor.DarkHex);
        Assert.Equal(expected: "#FF445566", settings.TrayIconColor.LightHex);
        Assert.Equal(expected: "#FFFFEE00", settings.TrayIconBrightColor.DarkHex);
        Assert.Equal(expected: "#FF778899", settings.FooterBackgroundColor.LightHex);
        Assert.False(settings.EnableRoundedCorners);
        Assert.True(settings.UseWindows11SettingsNavigation);
        Assert.Equal(expected: 233.5, settings.SettingsSidebarWidth);
        Assert.True(settings.FlyoutUndocked);
        Assert.True(settings.FlyoutHasSavedPosition);
        Assert.Equal(expected: 120.25, settings.FlyoutLeft);
        Assert.Equal(expected: 340.5, settings.FlyoutTop);
        Assert.False(settings.AllowFlyoutUndock);
        Assert.False(settings.ClampUndockedFlyoutToScreen);
        Assert.False(settings.RestoreFlyoutUndockedOnStartup);
        Assert.Equal(expected: 120_000, settings.EnvironmentalCurveTickIntervalMs);
        Assert.Equal(expected: "DEL4321_ABC", Assert.Single(settings.KnownDisplays).EDIDKey);

        Assert.Equal(expected: 3, settings.Hotkeys.Count);
        Assert.Contains(settings.Hotkeys, b => b is
        {
            Action: BrightnessHotkeyAction.FullBright, RemovedByUser: true, Modifiers: 11, VirtualKey: 0x46
        });
        Assert.Contains(settings.Hotkeys, b => b is
        {
            Action: BrightnessHotkeyAction.ProfileSelect, Parameter: "2", BindingID: 1, Enabled: false
        });
        Assert.Contains(settings.Hotkeys, b => b is
        {
            Action: BrightnessHotkeyAction.MonitorOff, Parameter: "edid:DEL4321_ABC", Modifiers: 9, VirtualKey: 0x4F
        });
    }

    [Fact]
    public void DefaultPreRefactorFileLoadsDefaults()
    {
        AppSettings settings = TrayXmlSerializer.ReadFile<AppSettings>(FixturePath("settings-defaults.xml"));
        AppSettings fresh = new();

        Assert.Equal(fresh.Autosave, settings.Autosave);
        Assert.Equal(fresh.AutosaveProfiles, settings.AutosaveProfiles);
        Assert.Equal(fresh.ContextMenuPosition, settings.ContextMenuPosition);
        Assert.Equal(fresh.ContextMenuFontSize, settings.ContextMenuFontSize);
        Assert.Equal(fresh.ThemeMode, settings.ThemeMode);
        Assert.Equal(fresh.TrayIconStyle, settings.TrayIconStyle);
        Assert.Equal(fresh.UpdateCheckIntervalMs, settings.UpdateCheckIntervalMs);
        Assert.Equal(fresh.EnvironmentalCurveTickIntervalMs, settings.EnvironmentalCurveTickIntervalMs);
        Assert.Equal(fresh.Language, settings.Language);
    }

    [Fact]
    public void TickIntervalStaysInsideTheRangeTheCardAndServiceUse()
    {
        AppSettings settings = new() { EnvironmentalCurveTickIntervalMs = 0 };
        Assert.Equal(TimeConstants.EnvironmentalCurveTickIntervalMinMs, settings.EnvironmentalCurveTickIntervalMs);

        settings.EnvironmentalCurveTickIntervalMs = 300_000;
        Assert.Equal(expected: 300_000, settings.EnvironmentalCurveTickIntervalMs);

        settings.EnvironmentalCurveTickIntervalMs = int.MaxValue;
        Assert.Equal(TimeConstants.EnvironmentalCurveTickIntervalMaxMs, settings.EnvironmentalCurveTickIntervalMs);
    }
}

using FanControlTrayAppDotNET.Models;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

/// <summary>
/// settings.xml files in the shape written before Fan derived from AppSettingsCommon must keep every value.
/// </summary>
public sealed class SettingsFixtureCompatibilityTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void CustomizedPreRefactorFileKeepsEveryMovedValue()
    {
        AppSettings settings = TrayXmlSerializer.ReadFile<AppSettings>(FixturePath("settings-customized.xml"));

        Assert.False(settings.RunOnStartup);
        Assert.False(settings.CaptureControlStateOnProfileSwitch);
        Assert.True(settings.Autosave);
        Assert.Equal(TrayAppDotNETContextMenuPosition.Classic, settings.ContextMenuPosition);
        Assert.Equal(expected: 21, settings.ContextMenuFontSize);
        Assert.True(settings.UseSystemSubmenuShowDelay);
        Assert.Equal(expected: 650, settings.SubmenuShowDelayMs);
        Assert.Equal(TrayAppDotNETThemeMode.Light, settings.ThemeMode);
        Assert.Equal(TrayAppDotNETTrayIconStyle.Static, settings.TrayIconStyle);
        Assert.Equal(expected: "#FF112233", settings.TextColor.LightHex);
        Assert.Equal(expected: "#FF0A0B0C", settings.BackgroundColor.DarkHex);
        Assert.Equal(expected: "#FF998877", settings.TrayIconColor.LightHex);
        Assert.Equal(expected: "#FF303132", settings.FanCardBackgroundColor.DarkHex);
        Assert.False(settings.EnableRoundedCorners);
        Assert.True(settings.UseWindows11SettingsNavigation);
        Assert.Equal(expected: 211.5, settings.SettingsSidebarWidth);
        Assert.False(settings.AllowFlyoutUndock);
        Assert.False(settings.ClampUndockedFlyoutToScreen);
        Assert.False(settings.RestoreFlyoutUndockedOnStartup);
        Assert.True(settings.FlyoutUndocked);
        Assert.True(settings.FlyoutHasSavedPosition);
        Assert.Equal(expected: 64.5, settings.FlyoutLeft);
        Assert.Equal(expected: 99.75, settings.FlyoutTop);
        Assert.False(settings.CheckForUpdatesEnabled);
        Assert.True(settings.ShowUpdateNotificationsEnabled);
        Assert.False(settings.ShowUpdateButtonInFlyout);
        Assert.Equal(expected: 7_200_000, settings.UpdateCheckIntervalMs);
        Assert.Equal(expected: 1234, settings.SkippedUpdateVersion);
        Assert.False(settings.KeepFlyoutWarm);
        Assert.False(settings.KeepTrayContextMenuWarm);
        Assert.Equal(TrayAppDotNETRenderingBackend.Software, settings.RenderingBackend);
        Assert.Equal(expected: "en-US", settings.Language);
        Assert.True(settings.LegacyOpenSettingsHotkeyRetired);
        Assert.Equal(expected: 3, settings.Hotkeys.Count);
        Assert.Contains(settings.Hotkeys, b => b is { Action: HotkeyAction.OpenFlyout, Modifiers: 3, VirtualKey: 0x46 });
        Assert.Contains(settings.Hotkeys, b =>
            b is { Action: HotkeyAction.OpenSettings, BindingID: 2, Enabled: false, VirtualKey: 0x53 });
        Assert.Single(settings.Hotkeys, b => b.HasUnknownAction);
    }

    [Fact]
    public void LoadDropsBindingsWithAnUnknownActionAndKeepsTheRest()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("settings.xml");
        File.Copy(FixturePath("settings-customized.xml"), path);

        AppSettings settings = AppSettings.LoadOrDefault(path);
        settings.Store!.Flush();

        Assert.Equal(DataFileLoadResult.Loaded, settings.Store.LoadResult);
        Assert.Equal(expected: 2, settings.Hotkeys.Count);
        Assert.DoesNotContain(settings.Hotkeys, b => b.HasUnknownAction);
        Assert.False(settings.CaptureControlStateOnProfileSwitch);
    }

    [Fact]
    public void DefaultPreRefactorFileLoadsDefaults()
    {
        AppSettings settings = TrayXmlSerializer.ReadFile<AppSettings>(FixturePath("settings-defaults.xml"));
        AppSettings fresh = new();

        Assert.Equal(fresh.Autosave, settings.Autosave);
        Assert.Equal(fresh.CaptureControlStateOnProfileSwitch, settings.CaptureControlStateOnProfileSwitch);
        Assert.Equal(fresh.ContextMenuPosition, settings.ContextMenuPosition);
        Assert.Equal(fresh.ContextMenuFontSize, settings.ContextMenuFontSize);
        Assert.Equal(fresh.SubmenuShowDelayMs, settings.SubmenuShowDelayMs);
        Assert.Equal(fresh.ThemeMode, settings.ThemeMode);
        Assert.Equal(fresh.TrayIconStyle, settings.TrayIconStyle);
        Assert.Equal(fresh.UpdateCheckIntervalMs, settings.UpdateCheckIntervalMs);
        Assert.Equal(fresh.Language, settings.Language);
        Assert.Empty(settings.Hotkeys);
    }
}

using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private TrayAppDotNETAboutPage? _aboutPage;

    private StackPanel BuildAboutPage()
    {
        StopAboutUpdateRefresh();
        _aboutPage = new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L("Settings_About_Tagline", "A tray-based brightness controller for DDC/CI monitors."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Log = static message => WPFLog.Log(message),
            RebuildAboutPage = () => RebuildShell(BrightnessSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
            KnownIssues =
            [
                new TrayAppDotNETKnownIssue(
                    L("Settings_About_NightLightCorruption_Title", "Night Light corruption"),
                    L("Settings_About_NightLightCorruption_Description",
                        "If Night Light becomes unresponsive, win+alt+shift+b then signing out and back in should clear it.")),
                new TrayAppDotNETKnownIssue(
                    L("Settings_About_DDCCorruption_Title", "DDC state corruption"),
                    L("Settings_About_DDCCorruption_Description",
                        "If a monitor becomes unrecoverable, its slider will show with a warning triangle glyph. The monitor will have to be power cycled to restore DDC.")),
            ],
        });
        return _aboutPage.Build();
    }

    private void StopAboutUpdateRefresh() =>
        _aboutPage?.StopUpdateRefresh();
}

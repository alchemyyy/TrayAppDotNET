using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private readonly List<TrayAppDotNETAboutPage> _aboutPageGenerations = [];

    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L(nameof(AppStrings.Settings_About_Tagline), "A tray-based brightness controller for DDC/CI monitors."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            PromptOwner = () => this,
            FlushLog = static () => WPFLog.Flush(),
            Log = static message => WPFLog.Log(message),
            RebuildAboutPage = () => RebuildShell(BrightnessSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
            KnownIssues =
            [
                new TrayAppDotNETKnownIssue(
                    L(nameof(AppStrings.Settings_About_NightLightCorruption_Title), "Night Light corruption"),
                    L(nameof(AppStrings.Settings_About_NightLightCorruption_Description),
                        "If Night Light becomes unresponsive, win+alt+shift+b then signing out and back in should clear it.")),
                new TrayAppDotNETKnownIssue(
                    L(nameof(AppStrings.Settings_About_DDCCorruption_Title), "DDC state corruption"),
                    L(nameof(AppStrings.Settings_About_DDCCorruption_Description),
                        "If a monitor becomes unrecoverable, its slider will show with a warning triangle glyph. The monitor will have to be power cycled to restore DDC."))
            ]
        }));
        _aboutPageGenerations.Add(aboutPage);
        AddPageCleanup(() => _aboutPageGenerations.Remove(aboutPage));
        return aboutPage.Build();
    }
}

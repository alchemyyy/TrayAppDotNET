using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
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
            Tagline = L("Settings_About_Tagline", "A tray-based volume controller."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Log = static message => TADNLog.Log(message),
            RebuildAboutPage = () => RebuildShell(VolumeSettingsPage.About),
            StaleCheckTimerIntervalMs = 1_000,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
            KnownIssues =
            [
                new(
                    Loc("Settings_About_BluetoothCodecNotDisplaying_Title"),
                    Loc("Settings_About_BluetoothCodecNotDisplaying_Description")),
            ],
        });
        return _aboutPage.Build();
    }

    private void StopAboutUpdateRefresh() =>
        _aboutPage?.StopUpdateRefresh();
}

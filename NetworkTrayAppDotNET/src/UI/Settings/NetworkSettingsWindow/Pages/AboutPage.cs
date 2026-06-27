using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI;

public sealed partial class NetworkSettingsWindow
{
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
            Tagline = L("Settings_About_Tagline", "A tray-based network controller."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Shutdown = static () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            },
            Log = message => TADNLog.Log(message),
            RebuildAboutPage = () => RebuildShell(NetworkSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
        });
        return _aboutPage.Build();
    }
}

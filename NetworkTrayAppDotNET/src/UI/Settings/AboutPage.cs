using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(
            new TrayAppDotNETAboutPageOptions
            {
                Palette = Palette,
                ButtonRadius = RadiusMedium,
                CardRadius = RadiusLarge,
                Localize = L,
                Save = Save,
                ApplicationName = Constants.ApplicationName,
                Tagline = L(nameof(AppStrings.Settings_About_Tagline), "A tray-based network controller."),
                BuildNumber = BuildInfo.BuildNumber,
                Publisher = Constants.Publisher,
                HelpLink = Constants.HelpLink,
                OpenSettingsFolderText = OpenSettingsFolderText,
                SettingsFolderPath = SettingsFolderPath,
                UpdateSettings = _settings,
                UpdateService = static () => AppServices.UpdateCheckService,
                ConfirmAsync = ConfirmAsync,
                PromptOwner = () => this,
                SupportsFlyoutUpdateButton = false,
                Shutdown = static () =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                },
                Log = TADNLog.Log,
                RebuildAboutPage = () => RebuildShell(NetworkSettingsPage.About),
                StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
                UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs
            }));
        _aboutPageGenerations.Add(aboutPage);
        _aboutPage = aboutPage;
        AddPageCleanup(() =>
        {
            _aboutPageGenerations.Remove(aboutPage);
            if (ReferenceEquals(_aboutPage, aboutPage))
            {
                _aboutPage = _aboutPageGenerations.Count > 0
                    ? _aboutPageGenerations[^1]
                    : null;
            }
        });
        return aboutPage.Build();
    }
}

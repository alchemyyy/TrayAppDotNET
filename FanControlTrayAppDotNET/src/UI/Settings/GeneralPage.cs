using Avalonia.Controls;
using FanControlTrayAppDotNET.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Settings;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection =
            CreateGeneralSettingsSection(AppServices.Install, AppLocalizationCultures.Names);
        stack.Children.Add(commonSection.BuildLanguageCard());
        stack.Children.Add(commonSection.BuildStartupCard());
        AddStandardInstallationSection(stack, commonSection, AppServices.Install, _showUninstaller, includeStoreCard: true);
        // Offered here because startup stops asking once the administrator prompt was declined
        if (PawnIoDriverInstaller.IsInstallNeeded())
            stack.Children.Add(BuildPawnIoCard(p));
        AddRenderingSection(stack, supportsFlyoutWarmWindow: true, supportsTrayContextMenuWarmWindow: true);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_General_FanDefaults_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_DefaultToRPMMode_Title)),
            L(nameof(AppStrings.Settings_General_DefaultToRPMMode_Description)),
            _settings.DefaultToRPMMode,
            v => _settings.DefaultToRPMMode = v,
            p,
            searchKeywords: [L(nameof(AppStrings.Settings_General_DefaultToRPMMode_SearchKeywords))],
            node: ControlMap.Settings.GeneralPage.DefaultToRPMMode));

        return stack;
    }

    private Border BuildPawnIoCard(SettingsPalette p)
    {
        SettingsButton install = TrayAppDotNETSettingsCards.Button(
                L(nameof(AppStrings.Settings_General_PawnIo_Button)),
                p,
                RadiusMedium)
            .MapTo(ControlMap.Settings.GeneralPage.PawnIoInstall);
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            try
            {
                await InstallPawnIoAsync();
            }
            finally
            {
                install.IsEnabled = true;
            }
        };

        return TrayAppDotNETSettingsCards.Card(
            L(nameof(AppStrings.Settings_General_PawnIo_Title)),
            L(nameof(AppStrings.Settings_General_PawnIo_Description)),
            install,
            p,
            RadiusLarge,
            [L(nameof(AppStrings.Settings_General_PawnIo_SearchKeywords))]);
    }

    /// <summary>Installs PawnIO on request; the hardware opens with it only after Fan Control restarts.</summary>
    private async Task InstallPawnIoAsync()
    {
        PawnIoInstallOutcome outcome;
        try
        {
            outcome = await Task.Run(() =>
                PawnIoDriverInstaller.EnsureInstalledAsync(userRequested: true, CancellationToken.None));
        }
        catch (Exception exception)
        {
            TADNLog.Log($"PawnIO install from settings failed: {exception}");
            outcome = PawnIoInstallOutcome.Failed;
        }

        if (outcome == PawnIoInstallOutcome.Failed)
        {
            await ShowMessage(
                L(nameof(AppStrings.Settings_General_PawnIo_Title)),
                L(nameof(AppStrings.Settings_General_PawnIo_Failed_Message)));
            return;
        }

        if (outcome is not (PawnIoInstallOutcome.Installed or PawnIoInstallOutcome.AlreadyInstalled)) return;

        bool restart = await ConfirmAsync(
            L(nameof(AppStrings.Settings_General_PawnIo_Title)),
            L(nameof(AppStrings.Settings_General_PawnIo_Installed_Message)),
            L(nameof(AppStrings.Settings_General_PawnIo_Restart_Button)),
            L(nameof(CommonStrings.Settings_General_NotNow_Button)));
        if (!restart || Environment.ProcessPath is not { } executablePath) return;

        try
        {
            TrayAppDotNETAppLifetime.Restart(executablePath, TrayAppDotNETProgram.SettingsDirectoryCommandLine());
        }
        catch (Exception exception)
        {
            await ShowMessage(L(nameof(CommonStrings.Settings_General_RestartFailed_Title)), exception.Message);
        }
    }
}

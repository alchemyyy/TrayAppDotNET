using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(ComboCard(
            Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_Title)),
            Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_Description)),
            [
                ("Windows10", Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_Windows10))),
                ("Windows11", Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_Windows11))),
                ("QuickSettings", Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_QuickSettings))),
                ("AvailableNetworks", Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_AvailableNetworks))),
                ("Settings", Loc(nameof(AppStrings.Settings_Network_FlyoutStyle_Settings)))
            ],
            _settings.FlyoutStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out FlyoutStyle value))
                    _settings.FlyoutStyle = value;
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_FlyoutStyle_SearchKeywords))
            ]));
        stack.Children.Add(ComboCard(
            Loc(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Title)),
            Loc(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Description)),
            [
                ("Explorer", Loc(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Explorer))),
                ("ControlPanel", Loc(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_ControlPanel)))
            ],
            _settings.AdapterSettingsStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out AdapterSettingsStyle value))
                    _settings.AdapterSettingsStyle = value;
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_SearchKeywords))
            ]));
        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = Loc(nameof(AppStrings.Settings_General_LocalUser_Title)),
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static () => AppServices.Installation.InstallToLocalAppData(),
                    UninstallAsync = refresh =>
                    {
                        _showUninstaller(AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            InstallScope.LocalAppData);
                        return Task.CompletedTask;
                    }
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.ProgramFiles,
                    Title = Loc(nameof(AppStrings.Settings_General_SystemWide_Title)),
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static () => AppServices.Installation.InstallSystemWide(),
                    UninstallAsync = refresh =>
                    {
                        _showUninstaller(AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            InstallScope.ProgramFiles);
                        return Task.CompletedTask;
                    }
                }
            ],
            new TrayAppDotNETStoreInstallOptions(
                Loc(nameof(AppStrings.Settings_General_WindowsStore_Title)),
                StoreInstallDescription));
        CreateRenderingSettingsSection(p).AddCards(stack);

        ControlNames.AssignLogicalSubtree(stack, nameof(NetworkSettingsPage.General));
        return stack;
    }

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = static () => AppServices.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                AppServices.Startup.SetRunOnStartup(enabled);
                _settings.RunOnStartup = enabled;
            },
            GetCurrentStartupShortcutTarget = static () => AppServices.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = static () => AppServices.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = static () => AppServices.Installation.DetectAll(),
            CurrentBuildNumber = BuildInfo.BuildNumber
        });

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            WarmWindowSettings = _settings,
            SupportsTrayContextMenuWarmWindow = true
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == InstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? Loc(nameof(AppStrings.Settings_General_StoreRunning))
            : Loc(nameof(AppStrings.Settings_General_StoreNotInstalled));
    }
}

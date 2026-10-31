using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection =
            CreateGeneralSettingsSection(AppServices.Install, AppLocalizationCultures.Names);
        stack.Children.Add(commonSection.BuildLanguageCard());
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Network_FlyoutStyle_Title)),
            L(nameof(AppStrings.Settings_Network_FlyoutStyle_Description)),
            [
                ("Windows10", L(nameof(AppStrings.Settings_Network_FlyoutStyle_Windows10))),
                ("Windows11", L(nameof(AppStrings.Settings_Network_FlyoutStyle_Windows11))),
                ("QuickSettings", L(nameof(AppStrings.Settings_Network_FlyoutStyle_QuickSettings))),
                ("AvailableNetworks", L(nameof(AppStrings.Settings_Network_FlyoutStyle_AvailableNetworks))),
                ("Settings", L(nameof(AppStrings.Settings_Network_FlyoutStyle_Settings)))
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
            ],
            node: ControlMap.Settings.GeneralPage.FlyoutStyle));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Title)),
            L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Description)),
            [
                ("Explorer", L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_Explorer))),
                ("ControlPanel", L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_ControlPanel)))
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
            ],
            node: ControlMap.Settings.GeneralPage.AdapterSettingsStyle));
        AddStandardInstallationSection(stack, commonSection, AppServices.Install, _showUninstaller, includeStoreCard: true);
        AddRenderingSection(stack, supportsFlyoutWarmWindow: false, supportsTrayContextMenuWarmWindow: true);

        return stack;
    }
}

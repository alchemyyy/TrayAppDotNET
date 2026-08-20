#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildNetworkPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_Network_SectionHeader)), p);
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
                if (Enum.TryParse(tag, out FlyoutStyle value)) _settings.FlyoutStyle = value;
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
                if (Enum.TryParse(tag, out AdapterSettingsStyle value)) _settings.AdapterSettingsStyle = value;
            },
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_AdapterSettingsStyle_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            Loc(nameof(AppStrings.Settings_Network_StateColors_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc(nameof(AppStrings.Settings_Network_StateColors_Description)), p, new Thickness(0, 0, 0, 8)));
        stack.Children.Add(ColorCard(
            "Connected",
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_DarkTooltip)),
            _settings.NetworkConnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_ConnectedColor_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            "NoInternet",
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_DarkTooltip)),
            _settings.NetworkNoInternetColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_NoInternetColor_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            "Disconnected",
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_DarkTooltip)),
            _settings.NetworkDisconnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Network_DisconnectedColor_SearchKeywords))
            ]));
        ControlNames.AssignLogicalSubtree(stack, nameof(NetworkSettingsPage.Network));
        return stack;
    }
}

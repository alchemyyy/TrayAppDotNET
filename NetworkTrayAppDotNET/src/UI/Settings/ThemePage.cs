#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Theme_SectionHeader)), p);

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_TrayIcon_Header)), p));
        bool staticIcon = _settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Static;
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Title)),
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Description)),
            [
                (TrayAppDotNETTrayIconStyle.Dynamic, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Dynamic))),
                (TrayAppDotNETTrayIconStyle.Static, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Static)))
            ],
            _settings.TrayIconStyle,
            value => _settings.TrayIconStyle = value,
            p,
            RefreshCurrentPage,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_TrayIconStyle_SearchKeywords))],
            node: ControlMap.Settings.ThemePage.TrayIconStyle));
        stack.Children.Add(Maybe(staticIcon, VariantColorCard(
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_Title)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.TrayIconColor.ID)));
        if (staticIcon)
            return stack;

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Network_StateColors_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_Network_StateColors_Description)),
            p,
            new Thickness(left: 0, top: 0, right: 0, bottom: 8)));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Network_ConnectedColor_Title)),
            L(nameof(AppStrings.Settings_Network_ConnectedColor_Description)),
            L(nameof(AppStrings.Settings_Network_ConnectedColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Network_ConnectedColor_DarkTooltip)),
            _settings.NetworkConnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_ConnectedColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.ConnectedColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Network_NoInternetColor_Title)),
            L(nameof(AppStrings.Settings_Network_NoInternetColor_Description)),
            L(nameof(AppStrings.Settings_Network_NoInternetColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Network_NoInternetColor_DarkTooltip)),
            _settings.NetworkNoInternetColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_NoInternetColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.NoInternetColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Network_DisconnectedColor_Title)),
            L(nameof(AppStrings.Settings_Network_DisconnectedColor_Description)),
            L(nameof(AppStrings.Settings_Network_DisconnectedColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Network_DisconnectedColor_DarkTooltip)),
            _settings.NetworkDisconnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_DisconnectedColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.DisconnectedColor.ID));
        return stack;
    }
}

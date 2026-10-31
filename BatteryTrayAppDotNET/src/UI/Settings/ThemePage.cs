using Avalonia.Controls;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_Flyout_Header)), p));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_DarkTooltip)),
            _settings.FlyoutBackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.FlyoutBackgroundColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_DarkTooltip)),
            _settings.FlyoutTitleBarBackgroundColor,
            theme.FooterBackground.Light,
            theme.FooterBackground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.FlyoutTitleBarBackgroundColor.ID));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_TrayIcon_Header)), p));
        stack.Children.Add(VariantColorCard(
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_Title)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.TrayIconColor.ID));

        return stack;
    }
}

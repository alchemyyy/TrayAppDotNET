#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NetworkTrayAppDotNET.UI.Settings;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_Theme_SectionHeader)), p);

        TextBlock contextHeader = TrayAppDotNETSettingsUI.TitleText(
            L(nameof(AppStrings.Settings_Theme_ContextMenu_Header)), p);
        contextHeader.FontWeight = FontWeight.SemiBold;
        contextHeader.Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 8);
        stack.Children.Add(contextHeader);
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Title)),
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Description)),
            _settings.ContextMenuFontSize,
            min: 8,
            max: 48,
            v => _settings.ContextMenuFontSize = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_FontSize_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            Loc(nameof(AppStrings.Settings_Theme_Appearance_Header)), p));
        stack.Children.Add(ComboCard(
            Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Title)),
            Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Description)),
            [
                ("System", Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_System))),
                ("Light", Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Light))),
                ("Dark", Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Dark)))
            ],
            _settings.ThemeMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out ThemeMode value))
                    _settings.ThemeMode = value;
            },
            p,
            () => RebuildShell(NetworkSettingsPage.Theme),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_ThemeStyle_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            _settings.UseWindows11SettingsNavigation,
            value => _settings.UseWindows11SettingsNavigation = value,
            p,
            () => RebuildShell(NetworkSettingsPage.Theme),
            [
                L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            name: "Text",
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_DarkTooltip)),
            _settings.TextColor,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_TextColor_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            name: "Background",
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
            _settings.BackgroundColor,
            (AppServices.Theme ?? AppTheme.Default).Background.Light,
            (AppServices.Theme ?? AppTheme.Default).Background.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Flyout_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            () => RebuildShell(NetworkSettingsPage.Theme),
            [
                L(nameof(AppStrings.Settings_Theme_RoundedCorners_SearchKeywords))
            ]));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Theme_Animations_Title)),
            L(nameof(AppStrings.Settings_Theme_Animations_Description)),
            [
                (nameof(TrayAppDotNETAnimationMode.System), L(nameof(AppStrings.Settings_Theme_Animations_System))),
                (nameof(TrayAppDotNETAnimationMode.Disabled), L(nameof(AppStrings.Settings_Theme_Animations_Disabled))),
                (nameof(TrayAppDotNETAnimationMode.Enabled), L(nameof(AppStrings.Settings_Theme_Animations_Enabled)))
            ],
            _settings.AnimationMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                    _settings.AnimationMode = value;
            },
            p,
            () =>
            {
                if (Application.Current != null)
                    TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                RebuildShell(NetworkSettingsPage.Theme);
            },
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_Animations_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Title)),
            L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Description)),
            _settings.ToolTipShowDelayMs,
            TimeConstants.ToolTipShowDelayMinMs,
            TimeConstants.ToolTipShowDelayMaxMs,
            v =>
            {
                _settings.ToolTipShowDelayMs = v;
                TrayAppDotNETToolTip.ShowDelayMs = v;
                TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
            },
            p,
            suffix: " ms",
            [
                L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_TrayIcon_Header)), p));
        stack.Children.Add(ColorCard(
            name: "TrayIcon",
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Title)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
            ]));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            Loc(nameof(AppStrings.Settings_Network_StateColors_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc(nameof(AppStrings.Settings_Network_StateColors_Description)),
            p,
            new Thickness(left: 0, top: 0, right: 0, bottom: 8)));
        stack.Children.Add(ColorCard(
            name: "Connected",
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_ConnectedColor_DarkTooltip)),
            _settings.NetworkConnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkConnectedTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_ConnectedColor_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            name: "NoInternet",
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_NoInternetColor_DarkTooltip)),
            _settings.NetworkNoInternetColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkNoInternetTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_NoInternetColor_SearchKeywords))
            ]));
        stack.Children.Add(ColorCard(
            name: "Disconnected",
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_Title)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_Description)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Network_DisconnectedColor_DarkTooltip)),
            _settings.NetworkDisconnectedColor,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Light,
            (AppServices.Theme ?? AppTheme.Default).NetworkDisconnectedTrayIconColor.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Network_DisconnectedColor_SearchKeywords))
            ]));

        ControlNames.AssignLogicalSubtree(stack, nameof(NetworkSettingsPage.Theme));
        return stack;
    }
}

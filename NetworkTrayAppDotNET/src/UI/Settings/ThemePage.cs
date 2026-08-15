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
            L("Settings_Theme_ContextMenu_Header", "Context menu"), p);
        contextHeader.FontWeight = FontWeight.SemiBold;
        contextHeader.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(contextHeader);
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Title)),
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Description)),
            _settings.ContextMenuFontSize,
            8,
            48,
            v => _settings.ContextMenuFontSize = v,
            p));

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
            afterSave: () => RebuildShell(NetworkSettingsPage.Theme)));
        stack.Children.Add(ColorCard(
            "Text",
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_DarkTooltip)),
            _settings.TextColor,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
            p));
        stack.Children.Add(ColorCard(
            "Background",
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
            _settings.BackgroundColor,
            (AppServices.Theme ?? AppTheme.Default).Background.Light,
            (AppServices.Theme ?? AppTheme.Default).Background.Dark,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_Flyout_Header", "Flyout"), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(NetworkSettingsPage.Theme)));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Theme_Animations_Title), "Animations"),
            L(nameof(AppStrings.Settings_Theme_Animations_Description),
                "Controls whether tooltip fades and other UI animations are allowed."),
            [
                (nameof(TrayAppDotNETAnimationMode.System), L(nameof(AppStrings.Settings_Theme_Animations_System), "System")),
                (nameof(TrayAppDotNETAnimationMode.Disabled), L(nameof(AppStrings.Settings_Theme_Animations_Disabled), "Disabled")),
                (nameof(TrayAppDotNETAnimationMode.Enabled), L(nameof(AppStrings.Settings_Theme_Animations_Enabled), "Enabled"))
            ],
            _settings.AnimationMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                    _settings.AnimationMode = value;
            },
            p,
            afterSave: () =>
            {
                if (Application.Current != null)
                    TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                RebuildShell(NetworkSettingsPage.Theme);
            }));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Title), "Tooltip delay"),
            L(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Description), "Milliseconds to wait before showing a tooltip."),
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
            " ms"));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_TrayIcon_Header", "Tray icon"), p));
        stack.Children.Add(ColorCard(
            "TrayIcon",
            L("Settings_Theme_StaticIconColor_Title", "Static tray icon color"),
            L("Settings_Theme_StaticIconColor_Description",
                "Override the tray icon color when Tray icon style is set to Static. Each variant falls back to the default when unset."),
            L("Settings_Theme_StaticIconColor_LightTooltip", "Light theme static tray icon color"),
            L("Settings_Theme_StaticIconColor_DarkTooltip", "Dark theme static tray icon color"),
            _settings.TrayIconColor,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
            (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
            p));

        return stack;
    }
}

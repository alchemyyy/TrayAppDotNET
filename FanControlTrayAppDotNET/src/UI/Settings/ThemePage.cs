using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Theme_Flyout_Header)), p));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_DarkTooltip)),
            _settings.FlyoutBackgroundColor,
            theme.FlyoutBackground.Light,
            theme.FlyoutBackground.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_FlyoutBackgroundColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.FlyoutBackgroundColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_DarkTooltip)),
            _settings.FlyoutTitleBarBackgroundColor,
            theme.FlyoutTitleBarBackground.Light,
            theme.FlyoutTitleBarBackground.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_FlyoutTitleBarBackgroundColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.FlyoutTitleBarColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FanCardBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FanCardBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FanCardBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FanCardBackgroundColor_DarkTooltip)),
            _settings.FanCardBackgroundColor,
            theme.FanCardBackground.Light,
            theme.FanCardBackground.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_FanCardBackgroundColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.FanCardColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_GroupCardBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_GroupCardBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_GroupCardBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_GroupCardBackgroundColor_DarkTooltip)),
            _settings.GroupCardBackgroundColor,
            theme.GroupCardBackground.Light,
            theme.GroupCardBackground.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_GroupCardBackgroundColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.GroupCardColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_CardBorderColor_Title)),
            L(nameof(AppStrings.Settings_Theme_CardBorderColor_Description)),
            L(nameof(AppStrings.Settings_Theme_CardBorderColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_CardBorderColor_DarkTooltip)),
            _settings.CardBorderColor,
            theme.FlyoutCardBorder.Light,
            theme.FlyoutCardBorder.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_CardBorderColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.CardBorderColor.ID));
        SettingsComboBox sliderThumbCombo = ControlNames.Assign(
            TrayAppDotNETSettingsUI.ComboBox(
                p,
                autoSizeToText: true,
                autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem)
                .MapTo(ControlMap.Settings.ThemePage.SliderThumb),
            parentName: "SliderThumb");
        foreach (SliderThumbGlyphOption option in _settings.SliderThumbOptions)
            sliderThumbCombo.Items.Add(new SettingsComboBoxItem(option.Name, option.Name, p));
        TrayAppDotNETSettingsUI.SelectComboByTag(sliderThumbCombo, _settings.SliderThumbGlyph);
        sliderThumbCombo.SelectionChanged += (_, _) =>
        {
            if (TrayAppDotNETSettingsUI.SelectedTag(sliderThumbCombo) is not { Length: > 0 } tag) return;
            if (_settings.SliderThumbOptions.Any(o => o.Name == tag))
                _settings.SliderThumbGlyph = tag;
            Save();
        };
        stack.Children.Add(Card(
            L(nameof(AppStrings.Settings_Theme_SliderThumb_Title)),
            L(nameof(AppStrings.Settings_Theme_SliderThumb_Description)),
            sliderThumbCombo,
            p,
            [L(nameof(AppStrings.Settings_Theme_SliderThumb_SearchKeywords))]));

        SettingsComboBox curveSliderThumbCombo = ControlNames.Assign(
            TrayAppDotNETSettingsUI.ComboBox(
                p,
                autoSizeToText: true,
                autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem)
                .MapTo(ControlMap.Settings.ThemePage.CurveSliderThumb),
            parentName: "CurveSliderThumb");
        foreach (SliderThumbGlyphOption option in _settings.SliderThumbOptions.Where(static o => o.IsGlyph))
            curveSliderThumbCombo.Items.Add(new SettingsComboBoxItem(option.Name, option.Name, p));
        TrayAppDotNETSettingsUI.SelectComboByTag(curveSliderThumbCombo, _settings.CurveSliderThumbGlyph);
        curveSliderThumbCombo.SelectionChanged += (_, _) =>
        {
            if (TrayAppDotNETSettingsUI.SelectedTag(curveSliderThumbCombo) is not { Length: > 0 } tag) return;
            if (_settings.SliderThumbOptions.Any(o => o is { IsGlyph: true } && o.Name == tag))
                _settings.CurveSliderThumbGlyph = tag;
            Save();
        };
        stack.Children.Add(Card(
            L(nameof(AppStrings.Settings_Theme_CurveSliderThumb_Title)),
            L(nameof(AppStrings.Settings_Theme_CurveSliderThumb_Description)),
            curveSliderThumbCombo, p,
            [L(nameof(AppStrings.Settings_Theme_CurveSliderThumb_SearchKeywords))]));


        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Theme_TrayIcon_Header)), p));
        stack.Children.Add(VariantColorCard(
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_Title)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            L(nameof(CommonStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            [L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))],
            ControlMap.Settings.ThemePage.TrayIconColor.ID));
        return stack;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_ContextMenu_Header)),
            p));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Theme_FontSize_Title)),
            L(nameof(AppStrings.Settings_Theme_FontSize_Description)),
            _settings.ContextMenuFontSize,
            AXAMLSettingsUI.ContextMenuFontSizeMin,
            AXAMLSettingsUI.ContextMenuFontSizeMax,
            v => _settings.ContextMenuFontSize = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_FontSize_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Appearance_Header)),
            p));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Theme_ThemeStyle_Title)),
            L(nameof(AppStrings.Settings_Theme_ThemeStyle_Description)),
            [
                (ThemeMode.System, L(nameof(AppStrings.Settings_Theme_ThemeStyle_System))),
                (ThemeMode.Light, L(nameof(AppStrings.Settings_Theme_ThemeStyle_Light))),
                (ThemeMode.Dark, L(nameof(AppStrings.Settings_Theme_ThemeStyle_Dark)))
            ],
            _settings.ThemeMode,
            v => _settings.ThemeMode = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme),
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
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme),
            searchKeywords:
            [
                L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "Text",
            L(nameof(AppStrings.Settings_Theme_TextColor_Title)),
            L(nameof(AppStrings.Settings_Theme_TextColor_Description)),
            L(nameof(AppStrings.Settings_Theme_TextColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_TextColor_DarkTooltip)),
            _settings.TextColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TextColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "Background",
            L(nameof(AppStrings.Settings_Theme_BackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_BackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_BackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
            _settings.BackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_BackgroundColor_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Flyout_Header)),
            p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
            L(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_RoundedCorners_SearchKeywords))
            ]));

        SettingsComboBox sliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem);
        OwnPageResource(sliderThumbCombo);
        foreach (SliderThumbGlyphOption option in _settings.SliderThumbOptions)
        {
            string label = SliderThumbDisplayName(option.Name);
            sliderThumbCombo.Items.Add(new SettingsComboBoxItem(
                option.Name,
                label,
                p,
                () => SliderThumbComboContent(option, label, p)));
        }

        TrayAppDotNETSettingsUI.SelectComboByTag(sliderThumbCombo, _settings.SliderThumbGlyph);
        sliderThumbCombo.SelectionChanged += (_, _) =>
        {
            if (TrayAppDotNETSettingsUI.SelectedTag(sliderThumbCombo) is not { Length: > 0 } tag) return;
            if (_settings.SliderThumbOptions.Any(o => o.Name == tag))
                _settings.SliderThumbGlyph = tag;
            Save();
        };
        stack.Children.Add(Card(
            L(nameof(AppStrings.Settings_Theme_SliderIndicator_Title)),
            L(nameof(AppStrings.Settings_Theme_SliderIndicator_Description)),
            sliderThumbCombo,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_SliderIndicator_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "FooterBackground",
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_DarkTooltip)),
            _settings.FooterBackgroundColor,
            theme.FooterBackground.Light,
            theme.FooterBackground.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_TrayIcon_Header)),
            p));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Title)),
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Description)),
            [
                (TrayIconStyle.Dynamic, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Dynamic))),
                (TrayIconStyle.Static, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Static)))
            ],
            _settings.TrayIconStyle,
            v => _settings.TrayIconStyle = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TrayIconStyle_SearchKeywords))
            ]));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, StringComboCard(
            L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_Title)),
            L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_Description)),
            [
                (MasterSliderMode.Lowest, L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_Lowest))),
                (MasterSliderMode.Average, L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_Average))),
                (MasterSliderMode.Highest, L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_Highest)))
            ],
            _settings.DynamicIconBrightnessTracking,
            v => _settings.DynamicIconBrightnessTracking = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_DynamicIconTracking_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, BoolCard(
            L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_Title)),
            L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_Description)),
            _settings.DynamicIconTrackEnabledOnly,
            v => _settings.DynamicIconTrackEnabledOnly = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Static, VariantColorCard(
            "TrayIcon",
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Title)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, VariantColorCard(
            "TrayIconBright",
            L(nameof(AppStrings.Settings_Theme_BrightColor_Title)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_Description)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_DarkTooltip)),
            _settings.TrayIconBrightColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_BrightColor_SearchKeywords))
            ])));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, VariantColorCard(
            "TrayIconDim",
            L(nameof(AppStrings.Settings_Theme_DimColor_Title)),
            L(nameof(AppStrings.Settings_Theme_DimColor_Description)),
            L(nameof(AppStrings.Settings_Theme_DimColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_DimColor_DarkTooltip)),
            _settings.TrayIconDimColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_DimColor_SearchKeywords))
            ])));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Environmental_Header)),
            p));
        stack.Children.Add(VariantColorCard(
            "EnvBrightnessCurve",
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_Title)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_Description)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_DarkTooltip)),
            _settings.EnvironmentalBrightnessCurveColor,
            theme.EnvironmentalBrightnessCurve.Light,
            theme.EnvironmentalBrightnessCurve.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "EnvNightLightCurve",
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_Title)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_Description)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_DarkTooltip)),
            _settings.EnvironmentalNightLightCurveColor,
            theme.EnvironmentalNightLightCurve.Light,
            theme.EnvironmentalNightLightCurve.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "EnvCurrentTime",
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_Title)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_Description)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_DarkTooltip)),
            _settings.EnvironmentalCurrentTimeColor,
            theme.EnvironmentalCurrentTime.Light,
            theme.EnvironmentalCurrentTime.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "EnvTwilightBackdrop",
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_Title)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_Description)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_DarkTooltip)),
            _settings.EnvironmentalTwilightBackdropColor,
            theme.EnvironmentalTwilightBackdrop.Light,
            theme.EnvironmentalTwilightBackdrop.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "EnvNightBackdrop",
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_Title)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_Description)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_DarkTooltip)),
            _settings.EnvironmentalNightBackdropColor,
            theme.EnvironmentalNightBackdrop.Light,
            theme.EnvironmentalNightBackdrop.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_NightBackdropColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "EnvGridLine",
            L(nameof(AppStrings.Settings_Theme_GridLineColor_Title)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_Description)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_DarkTooltip)),
            _settings.EnvironmentalGridLineColor,
            theme.EnvironmentalGridLine.Light,
            theme.EnvironmentalGridLine.Dark,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_GridLineColor_SearchKeywords))
            ]));

        return stack;
    }

    private static string SliderThumbDisplayName(string name) => name switch
    {
        "Capsule" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Capsule)),
        "Circle" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Circle)),
        "Diamond" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Diamond)),
        "Star" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Star)),
        "Square" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Square)),
        "Heart" => L(nameof(AppStrings.Settings_Theme_SliderThumb_Heart)),
        _ => name
    };

    private static Grid SliderThumbComboContent(SliderThumbGlyphOption option, string label, SettingsPalette p)
    {
        Grid preview = new()
        {
            Width = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
        };

        double width = Math.Max(1, option.Width);
        double height = Math.Max(1, option.Height);
        if (option.IsCapsule)
        {
            preview.Children.Add(new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                Background = TrayAppDotNETSettingsUI.Brush(p.Foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            TextBlock glyph = TrayAppDotNETSettingsUI.Text(option.Glyph, p, Math.Max(1, option.FontSize));
            glyph.FontFamily = new FontFamily(option.FontFamily);
            glyph.Width = width;
            glyph.Height = height;
            glyph.TextAlignment = TextAlignment.Center;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            if (Math.Abs(option.XScale - 1.0) > 0.001)
                glyph.RenderTransform = new ScaleTransform(option.XScale, 1);
            preview.Children.Add(glyph);
        }

        TextBlock name = TrayAppDotNETSettingsUI.Text(label, p);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;

        Grid row = new() { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        row.Children.Add(preview);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        return row;
    }
}

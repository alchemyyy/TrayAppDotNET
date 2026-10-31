using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.Models;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_Flyout_Header)),
            p));
        SettingsComboBox sliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem).MapTo(ControlMap.Settings.ThemePage.SliderIndicator);
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
            [
                L(nameof(AppStrings.Settings_Theme_SliderIndicator_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_Title)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_Description)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_DarkTooltip)),
            _settings.FooterBackgroundColor,
            theme.FooterBackground.Light,
            theme.FooterBackground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_FooterBackgroundColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.FooterBackgroundColor.ID));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_TrayIcon_Header)),
            p));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Title)),
            L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Description)),
            [
                (TrayAppDotNETTrayIconStyle.Dynamic, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Dynamic))),
                (TrayAppDotNETTrayIconStyle.Static, L(nameof(AppStrings.Settings_Theme_TrayIconStyle_Static)))
            ],
            _settings.TrayIconStyle,
            v => _settings.TrayIconStyle = v,
            p,
            () => RebuildShell(BrightnessSettingsPage.Theme),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TrayIconStyle_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.TrayIconStyle));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Dynamic, StringComboCard(
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
            ],
            node: ControlMap.Settings.ThemePage.DynamicIconTracking)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Dynamic, BoolCard(
            L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_Title)),
            L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_Description)),
            _settings.DynamicIconTrackEnabledOnly,
            v => _settings.DynamicIconTrackEnabledOnly = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Theme_TrackEnabledOnly_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.TrackEnabledOnly)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Static, VariantColorCard(
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
            node: ControlMap.Settings.ThemePage.StaticIconColor.ID)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Dynamic, VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_BrightColor_Title)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_Description)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_BrightColor_DarkTooltip)),
            _settings.TrayIconBrightColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_BrightColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.BrightColor.ID)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Dynamic, VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_DimColor_Title)),
            L(nameof(AppStrings.Settings_Theme_DimColor_Description)),
            L(nameof(AppStrings.Settings_Theme_DimColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_DimColor_DarkTooltip)),
            _settings.TrayIconDimColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_DimColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.DimColor.ID)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Environmental_Header)),
            p));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_Title)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_Description)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_DarkTooltip)),
            _settings.EnvironmentalBrightnessCurveColor,
            theme.EnvironmentalBrightnessCurve.Light,
            theme.EnvironmentalBrightnessCurve.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_BrightnessCurveColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.BrightnessCurveColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_Title)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_Description)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_DarkTooltip)),
            _settings.EnvironmentalNightLightCurveColor,
            theme.EnvironmentalNightLightCurve.Light,
            theme.EnvironmentalNightLightCurve.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_NightLightCurveColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.NightLightCurveColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_Title)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_Description)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_DarkTooltip)),
            _settings.EnvironmentalCurrentTimeColor,
            theme.EnvironmentalCurrentTime.Light,
            theme.EnvironmentalCurrentTime.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_CurrentTimeMarkerColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.CurrentTimeMarkerColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_Title)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_Description)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_DarkTooltip)),
            _settings.EnvironmentalTwilightBackdropColor,
            theme.EnvironmentalTwilightBackdrop.Light,
            theme.EnvironmentalTwilightBackdrop.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_TwilightBackdropColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.TwilightBackdropColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_Title)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_Description)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_NightBackdropColor_DarkTooltip)),
            _settings.EnvironmentalNightBackdropColor,
            theme.EnvironmentalNightBackdrop.Light,
            theme.EnvironmentalNightBackdrop.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_NightBackdropColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.NightBackdropColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(AppStrings.Settings_Theme_GridLineColor_Title)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_Description)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_LightTooltip)),
            L(nameof(AppStrings.Settings_Theme_GridLineColor_DarkTooltip)),
            _settings.EnvironmentalGridLineColor,
            theme.EnvironmentalGridLine.Light,
            theme.EnvironmentalGridLine.Dark,
            p,
            [
                L(nameof(AppStrings.Settings_Theme_GridLineColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.GridLineColor.ID));

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
            Width = 22, Margin = new Thickness(left: 0, top: 0, right: 10, bottom: 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        double width = Math.Max(val1: 1, option.Width);
        double height = Math.Max(val1: 1, option.Height);
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
            TextBlock glyph = TrayAppDotNETSettingsUI.Text(option.Glyph, p, Math.Max(val1: 1, option.FontSize));
            glyph.FontFamily = new FontFamily(option.FontFamily);
            glyph.Width = width;
            glyph.Height = height;
            glyph.TextAlignment = TextAlignment.Center;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.RenderTransformOrigin = new RelativePoint(x: 0.5, y: 0.5, RelativeUnit.Relative);
            if (Math.Abs(option.XScale - 1.0) > 0.001)
                glyph.RenderTransform = new ScaleTransform(option.XScale, scaleY: 1);
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
        Grid.SetColumn(name, value: 1);
        row.Children.Add(name);
        return row;
    }
}

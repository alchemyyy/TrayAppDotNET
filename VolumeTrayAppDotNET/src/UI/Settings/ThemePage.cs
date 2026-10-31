using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Theme_Flyout_Header)), p));
        SettingsComboBox sliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem);
        sliderThumbCombo.MapTo(ControlMap.Settings.ThemePage.SliderIndicator);
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

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(CommonStrings.Settings_Theme_TrayIcon_Header)), p));
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

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_Theme_MeterPeak_Header)), p));
        stack.Children.Add(SingleColorCard(
            L(nameof(AppStrings.Settings_Theme_MeterPeakColor_Title)),
            L(nameof(AppStrings.Settings_Theme_MeterPeakColor_Description)),
            _settings.EffectiveMeterPeakColor,
            ColorMath.TryParseHexOrNull(AppSettings.MeterPeakColorDefaultHex) ?? AppTheme.MeterPeakColorDefault,
            c => _settings.TemporaryMeterPeakColor = c,
            hex => _settings.MeterPeakColorHex = hex,
            () =>
            {
                _settings.MeterPeakColorHex = AppSettings.MeterPeakColorDefaultHex;
                _settings.TemporaryMeterPeakColor = null;
            },
            p,
            L(nameof(AppStrings.Settings_Theme_MeterPeakColor_Tooltip)),
            [
                L(nameof(AppStrings.Settings_Theme_MeterPeakColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.MeterPeakColor.ID));
        stack.Children.Add(SingleColorCard(
            L(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Title)),
            L(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Description)),
            _settings.EffectiveMeterPeakStereoColor,
            ColorMath.TryParseHexOrNull(AppSettings.MeterPeakStereoColorDefaultHex)
            ?? AppTheme.MeterPeakStereoColorDefault,
            c => _settings.TemporaryMeterPeakStereoColor = c,
            hex => _settings.MeterPeakStereoColorHex = hex,
            () =>
            {
                _settings.MeterPeakStereoColorHex = AppSettings.MeterPeakStereoColorDefaultHex;
                _settings.TemporaryMeterPeakStereoColor = null;
            },
            p,
            L(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Tooltip)),
            [
                L(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_SearchKeywords))
            ],
            node: ControlMap.Settings.ThemePage.MeterPeakStereoColor.ID));

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

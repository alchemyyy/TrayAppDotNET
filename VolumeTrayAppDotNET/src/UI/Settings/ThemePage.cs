using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VolumeTrayAppDotNET.UI.Settings;

public sealed partial class VolumeSettingsWindow
{
    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc(nameof(AppStrings.Settings_Theme_SectionHeader)), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLight = ResolveEffectiveIsLight();

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Common_ContextMenu_Header)), p));
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Title)),
            Loc(nameof(AppStrings.Settings_Theme_FontSize_Description)),
            _settings.ContextMenuFontSize,
            AppSettings.ContextMenuFontSizeMin,
            AppSettings.ContextMenuFontSizeMax,
            v => _settings.ContextMenuFontSize = v,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_FontSize_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Theme_Appearance_Header)), p));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Title)),
            Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Description)),
            [
                (ThemeMode.System, Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_System))),
                (ThemeMode.Light, Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Light))),
                (ThemeMode.Dark, Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_Dark)))
            ],
            _settings.ThemeMode,
            v => _settings.ThemeMode = v,
            p,
            afterSave: () => RebuildShell(VolumeSettingsPage.Theme),
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_ThemeStyle_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            Loc(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            Loc(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            _settings.UseWindows11SettingsNavigation,
            value => _settings.UseWindows11SettingsNavigation = value,
            p,
            afterSave: () => RebuildShell(VolumeSettingsPage.Theme),
            searchKeywords:
            [
                Loc(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "Text",
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_TextColor_DarkTooltip)),
            _settings.TextColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_TextColor_SearchKeywords))
            ]));
        stack.Children.Add(VariantColorCard(
            "Background",
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
            _settings.BackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_BackgroundColor_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Theme_Flyout_Header)), p));
        stack.Children.Add(BoolCard(
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
            Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(VolumeSettingsPage.Theme),
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_RoundedCorners_SearchKeywords))
            ]));
        stack.Children.Add(StringComboCard(
            Loc(nameof(AppStrings.Settings_Theme_Animations_Title)),
            Loc(nameof(AppStrings.Settings_Theme_Animations_Description)),
            [
                (TrayAppDotNETAnimationMode.System, Loc(nameof(AppStrings.Settings_Theme_Animations_System))),
                (TrayAppDotNETAnimationMode.Disabled, Loc(nameof(AppStrings.Settings_Theme_Animations_Disabled))),
                (TrayAppDotNETAnimationMode.Enabled, Loc(nameof(AppStrings.Settings_Theme_Animations_Enabled)))
            ],
            _settings.AnimationMode,
            v => _settings.AnimationMode = v,
            p,
            afterSave: () =>
            {
                if (Application.Current != null)
                    TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                RebuildShell(VolumeSettingsPage.Theme);
            },
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_Animations_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            Loc(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Title)),
            Loc(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_Description)),
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
            Loc(nameof(AppStrings.Common_MillisecondsSuffix)),
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_ToolTipShowDelay_SearchKeywords))
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
            Loc(nameof(AppStrings.Settings_Theme_SliderIndicator_Title)),
            Loc(nameof(AppStrings.Settings_Theme_SliderIndicator_Description)),
            sliderThumbCombo,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_SliderIndicator_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Theme_TrayIcon_Header)), p));
        stack.Children.Add(VariantColorCard(
            "TrayIcon",
            Loc(nameof(AppStrings.Settings_Theme_StaticIconColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_StaticIconColor_Description)),
            Loc(nameof(AppStrings.Settings_Theme_StaticIconColor_LightTooltip)),
            Loc(nameof(AppStrings.Settings_Theme_StaticIconColor_DarkTooltip)),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_StaticIconColor_SearchKeywords))
            ]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc(nameof(AppStrings.Settings_Theme_MeterPeak_Header)), p));
        stack.Children.Add(SingleColorCard(
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakColor_Description)),
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
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakColor_Tooltip)),
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_MeterPeakColor_SearchKeywords))
            ]));
        stack.Children.Add(SingleColorCard(
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Title)),
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Description)),
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
            Loc(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_Tooltip)),
            searchKeywords:
            [
                Loc(nameof(AppStrings.Settings_Theme_MeterPeakStereoColor_SearchKeywords))
            ]));

        ControlNames.AssignLogicalSubtree(stack, nameof(VolumeSettingsPage.Theme));
        return stack;
    }

    private static string SliderThumbDisplayName(string name) => name switch
    {
        "Capsule" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Capsule)),
        "Circle" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Circle)),
        "Diamond" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Diamond)),
        "Star" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Star)),
        "Square" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Square)),
        "Heart" => Loc(nameof(AppStrings.Settings_Theme_SliderThumb_Heart)),
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

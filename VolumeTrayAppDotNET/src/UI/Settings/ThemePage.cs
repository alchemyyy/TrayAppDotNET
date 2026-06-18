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
        StackPanel stack = PageStack(Loc("Settings_Theme_SectionHeader"), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLight = ResolveEffectiveIsLight();

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Common_ContextMenu_Header"), p));
        stack.Children.Add(IntCard(
            Loc("Settings_Theme_FontSize_Title"),
            Loc("Settings_Theme_FontSize_Description"),
            _settings.ContextMenuFontSize,
            AppSettings.ContextMenuFontSizeMin,
            AppSettings.ContextMenuFontSizeMax,
            v => _settings.ContextMenuFontSize = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Theme_Appearance_Header"), p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Theme_ThemeStyle_Title"),
            Loc("Settings_Theme_ThemeStyle_Description"),
            [
                (ThemeMode.System, Loc("Settings_Theme_ThemeStyle_System")),
                (ThemeMode.Light, Loc("Settings_Theme_ThemeStyle_Light")),
                (ThemeMode.Dark, Loc("Settings_Theme_ThemeStyle_Dark")),
            ],
            _settings.ThemeMode,
            v => _settings.ThemeMode = v,
            p,
            afterSave: () => RebuildShell(VolumeSettingsPage.Theme)));
        stack.Children.Add(VariantColorCard(
            "Text",
            Loc("Settings_Theme_TextColor_Title"),
            Loc("Settings_Theme_TextColor_Description"),
            Loc("Settings_Theme_TextColor_LightTooltip"),
            Loc("Settings_Theme_TextColor_DarkTooltip"),
            _settings.TextColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "Background",
            Loc("Settings_Theme_BackgroundColor_Title"),
            Loc("Settings_Theme_BackgroundColor_Description"),
            Loc("Settings_Theme_BackgroundColor_LightTooltip"),
            Loc("Settings_Theme_BackgroundColor_DarkTooltip"),
            _settings.BackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Theme_Flyout_Header"), p));
        stack.Children.Add(BoolCard(
            Loc("Settings_Theme_RoundedCorners_Title"),
            Loc("Settings_Theme_RoundedCorners_Description"),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(VolumeSettingsPage.Theme)));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Theme_Animations_Title"),
            Loc("Settings_Theme_Animations_Description"),
            [
                (TrayAppDotNETAnimationMode.System, Loc("Settings_Theme_Animations_System")),
                (TrayAppDotNETAnimationMode.Disabled, Loc("Settings_Theme_Animations_Disabled")),
                (TrayAppDotNETAnimationMode.Enabled, Loc("Settings_Theme_Animations_Enabled")),
            ],
            _settings.AnimationMode,
            v => _settings.AnimationMode = v,
            p,
            afterSave: () =>
            {
                if (Application.Current != null)
                    TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                RebuildShell(VolumeSettingsPage.Theme);
            }));
        stack.Children.Add(IntCard(
            L("Settings_Theme_ToolTipShowDelay_Title", "Tooltip delay"),
            L("Settings_Theme_ToolTipShowDelay_Description", "Milliseconds to wait before showing a tooltip."),
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
            Loc("Common_MillisecondsSuffix")));

        SettingsComboBox sliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(
            p,
            autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem);
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
            Loc("Settings_Theme_SliderIndicator_Title"),
            Loc("Settings_Theme_SliderIndicator_Description"),
            sliderThumbCombo,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Theme_TrayIcon_Header"), p));
        stack.Children.Add(VariantColorCard(
            "TrayIcon",
            Loc("Settings_Theme_StaticIconColor_Title"),
            Loc("Settings_Theme_StaticIconColor_Description"),
            Loc("Settings_Theme_StaticIconColor_LightTooltip"),
            Loc("Settings_Theme_StaticIconColor_DarkTooltip"),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(Loc("Settings_Theme_MeterPeak_Header"), p));
        stack.Children.Add(SingleColorCard(
            Loc("Settings_Theme_MeterPeakColor_Title"),
            Loc("Settings_Theme_MeterPeakColor_Description"),
            _settings.EffectiveMeterPeakColor,
            ColorMath.TryParseHexOrNull(AppSettings.MeterPeakColorDefaultHex) ?? Colors.White,
            c => _settings.TemporaryMeterPeakColor = c,
            hex => _settings.MeterPeakColorHex = hex,
            () =>
            {
                _settings.MeterPeakColorHex = AppSettings.MeterPeakColorDefaultHex;
                _settings.TemporaryMeterPeakColor = null;
            },
            p,
            Loc("Settings_Theme_MeterPeakColor_Tooltip")));
        stack.Children.Add(SingleColorCard(
            Loc("Settings_Theme_MeterPeakStereoColor_Title"),
            Loc("Settings_Theme_MeterPeakStereoColor_Description"),
            _settings.EffectiveMeterPeakStereoColor,
            ColorMath.TryParseHexOrNull(AppSettings.MeterPeakStereoColorDefaultHex) ?? Colors.Transparent,
            c => _settings.TemporaryMeterPeakStereoColor = c,
            hex => _settings.MeterPeakStereoColorHex = hex,
            () =>
            {
                _settings.MeterPeakStereoColorHex = AppSettings.MeterPeakStereoColorDefaultHex;
                _settings.TemporaryMeterPeakStereoColor = null;
            },
            p,
            Loc("Settings_Theme_MeterPeakStereoColor_Tooltip")));

        return stack;
    }

    private static string SliderThumbDisplayName(string name) => name switch
    {
        "Capsule" => Loc("Settings_Theme_SliderThumb_Capsule"),
        "Circle" => Loc("Settings_Theme_SliderThumb_Circle"),
        "Diamond" => Loc("Settings_Theme_SliderThumb_Diamond"),
        "Star" => Loc("Settings_Theme_SliderThumb_Star"),
        "Square" => Loc("Settings_Theme_SliderThumb_Square"),
        "Heart" => Loc("Settings_Theme_SliderThumb_Heart"),
        _ => name,
    };

    private static Grid SliderThumbComboContent(SliderThumbGlyphOption option, string label, SettingsPalette p)
    {
        Grid preview = new()
        {
            Width = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
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
                VerticalAlignment = VerticalAlignment.Center,
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

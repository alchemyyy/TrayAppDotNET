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
        StackPanel stack = PageStack(L("Settings_Theme_SectionHeader", "Theme"), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_ContextMenu_Header", "Context menu"),
            p));
        stack.Children.Add(IntCard(
            L("Settings_Theme_FontSize_Title", "Context menu font size"),
            L("Settings_Theme_FontSize_Description", "The font size used by the tray context menu."),
            _settings.ContextMenuFontSize,
            ContextMenuFontSizeMin,
            ContextMenuFontSizeMax,
            v => _settings.ContextMenuFontSize = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_Appearance_Header", "Appearance"),
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_Theme_ThemeStyle_Title", "Theme"),
            L("Settings_Theme_ThemeStyle_Description",
                "Choose whether the app follows Windows or uses a fixed light or dark theme."),
            [
                (ThemeMode.System, L("Settings_Theme_ThemeStyle_System", "System")),
                (ThemeMode.Light, L("Settings_Theme_ThemeStyle_Light", "Light")),
                (ThemeMode.Dark, L("Settings_Theme_ThemeStyle_Dark", "Dark")),
            ],
            _settings.ThemeMode,
            v => _settings.ThemeMode = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme)));
        stack.Children.Add(VariantColorCard(
            "Text",
            L("Settings_Theme_TextColor_Title", "Text color"),
            L("Settings_Theme_TextColor_Description", "Override the primary text color."),
            L("Settings_Theme_TextColor_LightTooltip", "Light text color"),
            L("Settings_Theme_TextColor_DarkTooltip", "Dark text color"),
            _settings.TextColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "Background",
            L("Settings_Theme_BackgroundColor_Title", "Background color"),
            L("Settings_Theme_BackgroundColor_Description", "Override the main window and flyout background color."),
            L("Settings_Theme_BackgroundColor_LightTooltip", "Light background color"),
            L("Settings_Theme_BackgroundColor_DarkTooltip", "Dark background color"),
            _settings.BackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_Flyout_Header", "Flyout"),
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Theme_RoundedCorners_Title", "Rounded corners"),
            L("Settings_Theme_RoundedCorners_Description", "Use rounded corners on flyout and settings surfaces."),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme)));

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
            L("Settings_Theme_SliderIndicator_Title", "Slider indicator"),
            L("Settings_Theme_SliderIndicator_Description", "Choose the slider thumb shape used in the flyout."),
            sliderThumbCombo,
            p));
        stack.Children.Add(VariantColorCard(
            "FooterBackground",
            L("Settings_Theme_FooterBackgroundColor_Title", "Footer background"),
            L("Settings_Theme_FooterBackgroundColor_Description", "Override the flyout footer background color."),
            L("Settings_Theme_FooterBackgroundColor_LightTooltip", "Light footer background"),
            L("Settings_Theme_FooterBackgroundColor_DarkTooltip", "Dark footer background"),
            _settings.FooterBackgroundColor,
            theme.FooterBackground.Light,
            theme.FooterBackground.Dark,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_TrayIcon_Header", "Tray icon"),
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_Theme_TrayIconStyle_Title", "Tray icon style"),
            L("Settings_Theme_TrayIconStyle_Description",
                "Choose whether the tray icon uses a fixed color or changes with brightness."),
            [
                (TrayIconStyle.Dynamic, L("Settings_Theme_TrayIconStyle_Dynamic", "Dynamic")),
                (TrayIconStyle.Static, L("Settings_Theme_TrayIconStyle_Static", "Static")),
            ],
            _settings.TrayIconStyle,
            v => _settings.TrayIconStyle = v,
            p,
            afterSave: () => RebuildShell(BrightnessSettingsPage.Theme)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, StringComboCard(
            L("Settings_Theme_DynamicIconTracking_Title", "Dynamic icon tracking"),
            L("Settings_Theme_DynamicIconTracking_Description",
                "Choose which display brightness drives the dynamic tray icon."),
            [
                (MasterSliderMode.Lowest, L("Settings_Theme_DynamicIconTracking_Lowest", "Lowest")),
                (MasterSliderMode.Average, L("Settings_Theme_DynamicIconTracking_Average", "Average")),
                (MasterSliderMode.Highest, L("Settings_Theme_DynamicIconTracking_Highest", "Highest")),
            ],
            _settings.DynamicIconBrightnessTracking,
            v => _settings.DynamicIconBrightnessTracking = v,
            p)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, BoolCard(
            L("Settings_Theme_TrackEnabledOnly_Title", "Track enabled monitors only"),
            L("Settings_Theme_TrackEnabledOnly_Description",
                "Ignore disabled monitors when calculating the dynamic tray icon brightness."),
            _settings.DynamicIconTrackEnabledOnly,
            v => _settings.DynamicIconTrackEnabledOnly = v,
            p)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Static, VariantColorCard(
            "TrayIcon",
            L("Settings_Theme_StaticIconColor_Title", "Static icon color"),
            L("Settings_Theme_StaticIconColor_Description",
                "Override the tray icon color when tray icon style is Static."),
            L("Settings_Theme_StaticIconColor_LightTooltip", "Light icon color"),
            L("Settings_Theme_StaticIconColor_DarkTooltip", "Dark icon color"),
            _settings.TrayIconColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, VariantColorCard(
            "TrayIconBright",
            L("Settings_Theme_BrightColor_Title", "Bright color"),
            L("Settings_Theme_BrightColor_Description",
                "Override the bright endpoint color used by the dynamic tray icon."),
            L("Settings_Theme_BrightColor_LightTooltip", "Light bright color"),
            L("Settings_Theme_BrightColor_DarkTooltip", "Dark bright color"),
            _settings.TrayIconBrightColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p)));
        stack.Children.Add(Maybe(_settings.TrayIconStyle == TrayIconStyle.Dynamic, VariantColorCard(
            "TrayIconDim",
            L("Settings_Theme_DimColor_Title", "Dim color"),
            L("Settings_Theme_DimColor_Description", "Override the dim endpoint color used by the dynamic tray icon."),
            L("Settings_Theme_DimColor_LightTooltip", "Light dim color"),
            L("Settings_Theme_DimColor_DarkTooltip", "Dark dim color"),
            _settings.TrayIconDimColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p)));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_Theme_Environmental_Header", "Environmental curves"),
            p));
        stack.Children.Add(VariantColorCard(
            "EnvBrightnessCurve",
            L("Settings_Theme_BrightnessCurveColor_Title", "Brightness curve"),
            L("Settings_Theme_BrightnessCurveColor_Description", "Override the environmental brightness curve color."),
            L("Settings_Theme_BrightnessCurveColor_LightTooltip", "Light brightness curve"),
            L("Settings_Theme_BrightnessCurveColor_DarkTooltip", "Dark brightness curve"),
            _settings.EnvironmentalBrightnessCurveColor,
            theme.EnvironmentalBrightnessCurve.Light,
            theme.EnvironmentalBrightnessCurve.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "EnvNightLightCurve",
            L("Settings_Theme_NightLightCurveColor_Title", "Night-light curve"),
            L("Settings_Theme_NightLightCurveColor_Description", "Override the environmental night-light curve color."),
            L("Settings_Theme_NightLightCurveColor_LightTooltip", "Light night-light curve"),
            L("Settings_Theme_NightLightCurveColor_DarkTooltip", "Dark night-light curve"),
            _settings.EnvironmentalNightLightCurveColor,
            theme.EnvironmentalNightLightCurve.Light,
            theme.EnvironmentalNightLightCurve.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "EnvCurrentTime",
            L("Settings_Theme_CurrentTimeMarkerColor_Title", "Current-time marker"),
            L("Settings_Theme_CurrentTimeMarkerColor_Description", "Override the current-time marker color."),
            L("Settings_Theme_CurrentTimeMarkerColor_LightTooltip", "Light current-time marker"),
            L("Settings_Theme_CurrentTimeMarkerColor_DarkTooltip", "Dark current-time marker"),
            _settings.EnvironmentalCurrentTimeColor,
            theme.EnvironmentalCurrentTime.Light,
            theme.EnvironmentalCurrentTime.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "EnvTwilightBackdrop",
            L("Settings_Theme_TwilightBackdropColor_Title", "Twilight backdrop"),
            L("Settings_Theme_TwilightBackdropColor_Description", "Override the twilight overlay color."),
            L("Settings_Theme_TwilightBackdropColor_LightTooltip", "Light twilight backdrop"),
            L("Settings_Theme_TwilightBackdropColor_DarkTooltip", "Dark twilight backdrop"),
            _settings.EnvironmentalTwilightBackdropColor,
            theme.EnvironmentalTwilightBackdrop.Light,
            theme.EnvironmentalTwilightBackdrop.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "EnvNightBackdrop",
            L("Settings_Theme_NightBackdropColor_Title", "Night backdrop"),
            L("Settings_Theme_NightBackdropColor_Description", "Override the night overlay color."),
            L("Settings_Theme_NightBackdropColor_LightTooltip", "Light night backdrop"),
            L("Settings_Theme_NightBackdropColor_DarkTooltip", "Dark night backdrop"),
            _settings.EnvironmentalNightBackdropColor,
            theme.EnvironmentalNightBackdrop.Light,
            theme.EnvironmentalNightBackdrop.Dark,
            p));
        stack.Children.Add(VariantColorCard(
            "EnvGridLine",
            L("Settings_Theme_GridLineColor_Title", "Grid line"),
            L("Settings_Theme_GridLineColor_Description", "Override the environmental curve grid line color."),
            L("Settings_Theme_GridLineColor_LightTooltip", "Light grid line"),
            L("Settings_Theme_GridLineColor_DarkTooltip", "Dark grid line"),
            _settings.EnvironmentalGridLineColor,
            theme.EnvironmentalGridLine.Light,
            theme.EnvironmentalGridLine.Dark,
            p));

        return stack;
    }

    private static string SliderThumbDisplayName(string name) => name switch
    {
        "Capsule" => L("Settings_Theme_SliderThumb_Capsule", "Capsule"),
        "Circle" => L("Settings_Theme_SliderThumb_Circle", "Circle"),
        "Diamond" => L("Settings_Theme_SliderThumb_Diamond", "Diamond"),
        "Star" => L("Settings_Theme_SliderThumb_Star", "Star"),
        "Square" => L("Settings_Theme_SliderThumb_Square", "Square"),
        "Heart" => L("Settings_Theme_SliderThumb_Heart", "Heart"),
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

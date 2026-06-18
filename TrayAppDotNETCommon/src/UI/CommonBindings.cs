using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;

namespace TrayAppDotNETCommon.UI;

public abstract partial class SettingsWindowCommon<TPageKey>
    where TPageKey : notnull
{
    protected static Border Maybe(bool visible, Border card)
    {
        card.IsVisible = visible;
        return card;
    }

    protected Border StringComboCard<TEnum>(
        string title,
        string description,
        IReadOnlyList<(TEnum Value, string Text)> items,
        TEnum selected,
        Action<TEnum> set,
        SettingsPalette palette,
        Action? afterSave = null,
        bool autoSizeToText = true)
        where TEnum : struct, Enum =>
        ComboCard(
            title,
            description,
            items.Select(i => (i.Value.ToString(), i.Text)).ToArray(),
            selected.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TEnum value))
                    set(value);
            },
            palette,
            afterSave,
            autoSizeToText,
            SettingsComboBoxAutoSizeMode.SelectedItem);

    protected Border PairBoolCard(
        string title,
        string description,
        string leftHeader,
        string rightHeader,
        bool? leftValue,
        Action<bool>? setLeft,
        bool? rightValue,
        Action<bool>? setRight,
        SettingsPalette palette,
        bool showLeft = true,
        bool showRight = true,
        Action? afterSave = null)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));

        if (leftValue.HasValue && setLeft != null)
        {
            SettingsToggle toggle = Toggle(leftValue.Value, palette, v =>
            {
                setLeft(v);
                Save();
                afterSave?.Invoke();
            });
            toggle.HorizontalAlignment = HorizontalAlignment.Center;
            toggle.IsVisible = showLeft;
            Grid.SetColumn(toggle, 0);
            row.Children.Add(toggle);
        }

        if (rightValue.HasValue && setRight != null)
        {
            SettingsToggle toggle = Toggle(rightValue.Value, palette, v =>
            {
                setRight(v);
                Save();
                afterSave?.Invoke();
            });
            toggle.HorizontalAlignment = HorizontalAlignment.Center;
            toggle.IsVisible = showRight;
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
        }

        TrayAppDotNETToolTip.SetTip(row, $"{leftHeader} / {rightHeader}");
        return Card(title, description, row, palette);
    }

    protected static Grid PairColumnHeader(string title, SettingsPalette palette)
    {
        Grid grid = new() { Margin = new Thickness(0, 16, 16, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(152)));
        grid.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(title, palette));

        Grid pair = new();
        pair.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));
        pair.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));
        TextBlock playback = TrayAppDotNETSettingsUI.DescriptionText(Loc("Settings_Common_Playback"), palette);
        TextBlock recording = TrayAppDotNETSettingsUI.DescriptionText(Loc("Settings_Common_Recording"), palette);
        playback.FontWeight = FontWeight.SemiBold;
        recording.FontWeight = FontWeight.SemiBold;
        playback.HorizontalAlignment = HorizontalAlignment.Center;
        recording.HorizontalAlignment = HorizontalAlignment.Center;
        pair.Children.Add(playback);
        Grid.SetColumn(recording, 1);
        pair.Children.Add(recording);
        Grid.SetColumn(pair, 1);
        grid.Children.Add(pair);
        return grid;
    }

    protected Border SingleColorCard(
        string title,
        string description,
        Color value,
        Color defaultColor,
        Action<Color?> setTemporary,
        Action<string> commitHex,
        Action reset,
        SettingsPalette palette,
        string? tooltip = null)
    {
        SettingsSwatch swatch = new(palette);
        swatch.SetColor(value, defaultColor);
        if (!string.IsNullOrWhiteSpace(tooltip))
            TrayAppDotNETToolTip.SetTip(swatch, tooltip);
        SettingsButton resetButton = Button(Loc("Settings_Theme_Reset"), palette);

        swatch.Click += (_, _) =>
        {
            TrayAppDotNETColorPickerWindow picker = new(
                title,
                hasAlpha: true,
                value,
                defaultColor,
                palette,
                ColorPickerStrings()) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };

            picker.ColorChanged += (_, color) =>
            {
                setTemporary(color);
                RebuildShell(CurrentPageKey);
            };
            picker.Closed += (sender, _) =>
            {
                TrayAppDotNETColorPickerWindow closed = (TrayAppDotNETColorPickerWindow)sender!;
                if (closed.IsDirty)
                {
                    commitHex(NullableThemeColor.ToHex(closed.CurrentColor));
                    Save();
                }

                setTemporary(null);
                if (!IsClosing) RebuildShell(CurrentPageKey);
            };
            picker.Show(this);
        };
        resetButton.Click += (_, _) =>
        {
            reset();
            Save();
            RebuildShell(CurrentPageKey);
        };

        return Card(title, description, TrayAppDotNETSettingsUI.Horizontal(swatch, resetButton), palette);
    }

    protected Border VariantColorCard(
        string name,
        string title,
        string description,
        string lightTooltip,
        string darkTooltip,
        NullableThemeColor color,
        Color lightFallback,
        Color darkFallback,
        SettingsPalette palette)
    {
        SettingsSwatch light = new(palette);
        SettingsSwatch dark = new(palette);
        TrayAppDotNETToolTip.SetTip(light, lightTooltip);
        TrayAppDotNETToolTip.SetTip(dark, darkTooltip);
        SettingsButton reset = Button(Loc("Settings_Theme_Reset"), palette);

        bool effectiveIsLight = ResolveEffectiveIsLightForBindings();
        light.IsVisible = effectiveIsLight;
        dark.IsVisible = !effectiveIsLight;
        light.SetColor(color.LightColor, lightFallback);
        dark.SetColor(color.DarkColor, darkFallback);

        light.Click += (_, _) => OpenVariantColorPicker(title, color, isLight: true, lightFallback, palette);
        dark.Click += (_, _) => OpenVariantColorPicker(title, color, isLight: false, darkFallback, palette);
        reset.Click += (_, _) =>
        {
            color.LightHex = null;
            color.DarkHex = null;
            Save();
            RebuildShell(CurrentPageKey);
        };

        StackPanel row = TrayAppDotNETSettingsUI.Horizontal(light, dark, reset);
        row.Tag = name;
        return Card(title, description, row, palette);
    }

    protected virtual bool ResolveEffectiveIsLightForBindings() => false;

    private void OpenVariantColorPicker(
        string title,
        NullableThemeColor target,
        bool isLight,
        Color fallback,
        SettingsPalette palette)
    {
        Color initial = (isLight ? target.LightColor : target.DarkColor) ?? fallback;
        TrayAppDotNETColorPickerWindow picker = new(
            VariantPickerTitle(title, isLight),
            hasAlpha: true,
            initial,
            fallback,
            palette,
            ColorPickerStrings()) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };

        picker.ColorChanged += (_, editedColor) =>
        {
            if (isLight) target.TemporaryLightColor = editedColor;
            else target.TemporaryDarkColor = editedColor;

            if (!IsClosing) RebuildShell(CurrentPageKey);
        };

        picker.Closed += (sender, _) =>
        {
            TrayAppDotNETColorPickerWindow closed = (TrayAppDotNETColorPickerWindow)sender!;
            if (closed.IsDirty)
            {
                Color finalColor = closed.CurrentColor;
                if (isLight) target.LightHex = NullableThemeColor.ToHex(finalColor);
                else target.DarkHex = NullableThemeColor.ToHex(finalColor);
                Save();
            }

            if (isLight) target.TemporaryLightColor = null;
            else target.TemporaryDarkColor = null;

            if (!IsClosing) RebuildShell(CurrentPageKey);
        };

        picker.Show(this);
    }

    private static SettingsToggle Toggle(bool value, SettingsPalette palette, Action<bool> changed) =>
        TrayAppDotNETSettingsUI.Toggle(palette, value, (_, enabled) => changed(enabled));

    private static string VariantPickerTitle(string title, bool isLight) =>
        string.Format(
            Loc("Settings_Theme_PickerTitle_Format"),
            title,
            Loc(isLight ? "Settings_Theme_PickerTitle_LightVariant" : "Settings_Theme_PickerTitle_DarkVariant"));

    private static TrayAppDotNETColorPickerStrings ColorPickerStrings() =>
        new(
            Loc("ColorPicker_DefaultTitle"),
            Loc("ColorPicker_CloseTooltip"),
            Loc("ColorPicker_ChannelLabel_Hue"),
            Loc("ColorPicker_ChannelLabel_Alpha"),
            Loc("ColorPicker_ChannelLabel_R"),
            Loc("ColorPicker_ChannelLabel_G"),
            Loc("ColorPicker_ChannelLabel_B"),
            L("ColorPicker_RGBAHexLabel", L("ColorPicker_RgbaHexLabel", "rgba hex:")),
            L("ColorPicker_ARGBHexLabel", L("ColorPicker_ArgbHexLabel", "argb hex:")),
            Loc("ColorPicker_DefaultButton"),
            Loc("ColorPicker_ResetButton"));
}

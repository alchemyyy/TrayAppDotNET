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
        bool autoSizeToText = true,
        IReadOnlyList<string>? searchKeywords = null)
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
            SettingsComboBoxAutoSizeMode.SelectedItem,
            searchKeywords);

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
        Action? afterSave = null,
        IReadOnlyList<string>? searchKeywords = null)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(_commonBindingResources.AxamlCommonBindings.PairColumnWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(_commonBindingResources.AxamlCommonBindings.PairColumnWidth)));

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
        return Card(title, description, row, palette, searchKeywords);
    }

    protected Grid PairColumnHeader(string title, SettingsPalette palette)
    {
        Grid grid = new() { Margin = _commonBindingResources.AxamlCommonBindings.PairHeaderMargin };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(_commonBindingResources.AxamlCommonBindings.PairHeaderWidth)));
        grid.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(title, palette));

        Grid pair = new();
        pair.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(_commonBindingResources.AxamlCommonBindings.PairColumnWidth)));
        pair.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(_commonBindingResources.AxamlCommonBindings.PairColumnWidth)));
        TextBlock playback = TrayAppDotNETSettingsUI.DescriptionText(
            Loc(nameof(CommonStrings.Settings_Common_Playback)),
            palette);
        TextBlock recording = TrayAppDotNETSettingsUI.DescriptionText(
            Loc(nameof(CommonStrings.Settings_Common_Recording)),
            palette);
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
        string? tooltip = null,
        IReadOnlyList<string>? searchKeywords = null)
    {
        Color currentValue = value;
        SettingsSwatch swatch = new(palette);
        swatch.SetColor(currentValue, defaultColor);
        if (!string.IsNullOrWhiteSpace(tooltip))
            TrayAppDotNETToolTip.SetTip(swatch, tooltip);
        SettingsButton resetButton = Button(Loc(nameof(CommonStrings.Settings_Theme_Reset)), palette);

        swatch.Click += (_, _) =>
        {
            TrayAppDotNETColorPickerWindow picker = new(
                title,
                hasAlpha: true,
                currentValue,
                defaultColor,
                palette.Snapshot(),
                ColorPickerStrings(),
                EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };

            picker.ColorChanged += (_, color) =>
            {
                currentValue = color;
                setTemporary(color);
                swatch.SetColor(color, defaultColor);
                RefreshPalette();
            };
            picker.Closed += (sender, _) =>
            {
                TrayAppDotNETColorPickerWindow closed = (TrayAppDotNETColorPickerWindow)sender!;
                _openColorPickers.Remove(closed);
                if (closed.IsDirty)
                {
                    commitHex(NullableThemeColor.ToHex(closed.CurrentColor));
                    Save();
                }

                currentValue = closed.CurrentColor;
                setTemporary(null);
                swatch.SetColor(currentValue, defaultColor);
                if (!IsClosing) RefreshPalette();
            };
            _openColorPickers.Add(picker);
            try
            {
                picker.Show(this);
            }
            catch
            {
                _openColorPickers.Remove(picker);
                throw;
            }
        };
        resetButton.Click += (_, _) =>
        {
            reset();
            Save();
            currentValue = defaultColor;
            swatch.SetColor(currentValue, defaultColor);
            RefreshPalette();
        };

        return Card(
            title,
            description,
            TrayAppDotNETSettingsUI.Horizontal(swatch, resetButton),
            palette,
            searchKeywords);
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
        SettingsPalette palette,
        IReadOnlyList<string>? searchKeywords = null)
    {
        SettingsSwatch light = new(palette);
        SettingsSwatch dark = new(palette);
        TrayAppDotNETToolTip.SetTip(light, lightTooltip);
        TrayAppDotNETToolTip.SetTip(dark, darkTooltip);
        SettingsButton reset = Button(Loc(nameof(CommonStrings.Settings_Theme_Reset)), palette);

        bool effectiveIsLight = ResolveEffectiveIsLightForBindings();
        light.IsVisible = effectiveIsLight;
        dark.IsVisible = !effectiveIsLight;
        light.SetColor(color.LightColor, lightFallback);
        dark.SetColor(color.DarkColor, darkFallback);

        light.Click += (_, _) =>
            OpenVariantColorPicker(title, color, isLight: true, lightFallback, palette, light);
        dark.Click += (_, _) =>
            OpenVariantColorPicker(title, color, isLight: false, darkFallback, palette, dark);
        reset.Click += (_, _) =>
        {
            color.LightHex = null;
            color.DarkHex = null;
            Save();
            light.SetColor(null, lightFallback);
            dark.SetColor(null, darkFallback);
            RefreshPalette();
        };

        StackPanel row = TrayAppDotNETSettingsUI.Horizontal(light, dark, reset);
        row.Tag = name;
        return Card(title, description, row, palette, searchKeywords);
    }

    protected virtual bool ResolveEffectiveIsLightForBindings() => false;

    private void OpenVariantColorPicker(
        string title,
        NullableThemeColor target,
        bool isLight,
        Color fallback,
        SettingsPalette palette,
        SettingsSwatch swatch)
    {
        Color initial = (isLight ? target.LightColor : target.DarkColor) ?? fallback;
        TrayAppDotNETColorPickerWindow picker = new(
            VariantPickerTitle(title, isLight),
            hasAlpha: true,
            initial,
            fallback,
            palette.Snapshot(),
            ColorPickerStrings(),
            EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };

        picker.ColorChanged += (_, editedColor) =>
        {
            if (isLight) target.TemporaryLightColor = editedColor;
            else target.TemporaryDarkColor = editedColor;

            swatch.SetColor(editedColor, fallback);
            if (!IsClosing) RefreshPalette();
        };

        picker.Closed += (sender, _) =>
        {
            TrayAppDotNETColorPickerWindow closed = (TrayAppDotNETColorPickerWindow)sender!;
            _openColorPickers.Remove(closed);
            if (closed.IsDirty)
            {
                Color finalColor = closed.CurrentColor;
                if (isLight) target.LightHex = NullableThemeColor.ToHex(finalColor);
                else target.DarkHex = NullableThemeColor.ToHex(finalColor);
                Save();
            }

            if (isLight) target.TemporaryLightColor = null;
            else target.TemporaryDarkColor = null;

            swatch.SetColor(isLight ? target.LightColor : target.DarkColor, fallback);
            if (!IsClosing) RefreshPalette();
        };

        _openColorPickers.Add(picker);
        try
        {
            picker.Show(this);
        }
        catch
        {
            _openColorPickers.Remove(picker);
            throw;
        }
    }

    private static SettingsToggle Toggle(bool value, SettingsPalette palette, Action<bool> changed) =>
        TrayAppDotNETSettingsUI.Toggle(palette, value, (_, enabled) => changed(enabled));

    private static string VariantPickerTitle(string title, bool isLight) =>
        string.Format(
            Loc(nameof(CommonStrings.Settings_Theme_PickerTitle_Format)),
            title,
            Loc(isLight
                ? nameof(CommonStrings.Settings_Theme_PickerTitle_LightVariant)
                : nameof(CommonStrings.Settings_Theme_PickerTitle_DarkVariant)));

    private static TrayAppDotNETColorPickerStrings ColorPickerStrings() =>
        new(
            Loc(nameof(CommonStrings.ColorPicker_DefaultTitle)),
            Loc(nameof(CommonStrings.ColorPicker_CloseTooltip)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_Hue)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_Alpha)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_R)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_G)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_B)),
            L(nameof(CommonStrings.ColorPicker_RGBAHexLabel)),
            L(nameof(CommonStrings.ColorPicker_ARGBHexLabel)),
            Loc(nameof(CommonStrings.ColorPicker_DefaultButton)),
            Loc(nameof(CommonStrings.ColorPicker_ResetButton)));
}

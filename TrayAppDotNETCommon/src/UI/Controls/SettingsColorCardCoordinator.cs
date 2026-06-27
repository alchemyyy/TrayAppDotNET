using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Models;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed class TrayAppDotNETSettingsColorCardCoordinator
{
    private readonly Dictionary<(NullableThemeColor Target, bool IsLight), TrayAppDotNETColorPickerWindow>
        _openColorPickers = new();

    public Border ColorCard(
        Window owner,
        string name,
        string title,
        string description,
        string lightTooltip,
        string darkTooltip,
        NullableThemeColor color,
        Color lightFallback,
        Color darkFallback,
        SettingsPalette palette,
        CornerRadius buttonRadius,
        CornerRadius cardRadius,
        string resetText,
        Func<bool> resolveEffectiveIsLight,
        Func<string, bool, string> variantPickerTitle,
        TrayAppDotNETColorPickerStrings colorPickerStrings,
        Action save,
        Action rebuild,
        Func<bool> isClosing)
    {
        SettingsSwatch light = new(palette) { Tag = "Light", Name = name + "LightSwatch" };
        SettingsSwatch dark = new(palette) { Tag = "Dark", Name = name + "DarkSwatch" };
        TrayAppDotNETToolTip.SetTip(light, lightTooltip);
        TrayAppDotNETToolTip.SetTip(dark, darkTooltip);
        SettingsButton reset = TrayAppDotNETSettingsCards.Button(resetText, palette, buttonRadius);

        void Update()
        {
            light.SetColor(color.LightColor, lightFallback);
            dark.SetColor(color.DarkColor, darkFallback);
            bool isLight = resolveEffectiveIsLight();
            light.IsVisible = isLight;
            dark.IsVisible = !isLight;
        }

        light.Click += (_, _) =>
        {
            OpenColorPicker(
                owner,
                variantPickerTitle(title, true),
                color,
                isLight: true,
                lightFallback,
                palette,
                colorPickerStrings,
                save,
                rebuild,
                isClosing,
                Update);
        };
        dark.Click += (_, _) =>
        {
            OpenColorPicker(
                owner,
                variantPickerTitle(title, false),
                color,
                isLight: false,
                darkFallback,
                palette,
                colorPickerStrings,
                save,
                rebuild,
                isClosing,
                Update);
        };
        reset.Click += (_, _) =>
        {
            color.LightHex = null;
            color.DarkHex = null;
            save();
            Update();
            rebuild();
        };

        Update();
        StackPanel row = TrayAppDotNETSettingsUI.Horizontal(light, dark, reset);
        row.Tag = "ColorRow";
        return TrayAppDotNETSettingsCards.Card(title, description, row, palette, cardRadius);
    }

    public void CloseOpenColorPickers()
    {
        foreach (TrayAppDotNETColorPickerWindow picker in _openColorPickers.Values.ToArray())
            picker.Close();
        _openColorPickers.Clear();
    }

    private void OpenColorPicker(
        Window owner,
        string title,
        NullableThemeColor target,
        bool isLight,
        Color fallback,
        SettingsPalette palette,
        TrayAppDotNETColorPickerStrings colorPickerStrings,
        Action save,
        Action rebuild,
        Func<bool> isClosing,
        Action updateSwatches)
    {
        (NullableThemeColor Target, bool IsLight) key = (target, isLight);
        if (_openColorPickers.TryGetValue(key, out TrayAppDotNETColorPickerWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        Color initial = (isLight ? target.LightColor : target.DarkColor) ?? fallback;
        TrayAppDotNETColorPickerWindow picker = new(
            title,
            hasAlpha: true,
            initial,
            fallback,
            palette,
            colorPickerStrings) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };

        picker.ColorChanged += (_, editedColor) =>
        {
            if (isLight) target.TemporaryLightColor = editedColor;
            else target.TemporaryDarkColor = editedColor;

            if (!isClosing())
            {
                updateSwatches();
                rebuild();
            }
        };

        picker.Closed += (sender, _) =>
        {
            _openColorPickers.Remove(key);
            TrayAppDotNETColorPickerWindow closed = (TrayAppDotNETColorPickerWindow)sender!;
            if (closed.IsDirty)
            {
                Color finalColor = closed.CurrentColor;
                if (isLight) target.LightHex = NullableThemeColor.ToHex(finalColor);
                else target.DarkHex = NullableThemeColor.ToHex(finalColor);
                save();
            }

            if (isLight) target.TemporaryLightColor = null;
            else target.TemporaryDarkColor = null;

            if (!isClosing())
            {
                updateSwatches();
                rebuild();
            }
        };

        _openColorPickers[key] = picker;
        picker.Show(owner);
    }
}

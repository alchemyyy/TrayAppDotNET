using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Common close caption button for custom-chrome TrayAppDotNET dialogs.
/// </summary>
public sealed class TrayAppDotNETCaptionCloseButton : Border
{
    private readonly SettingsPalette _palette;
    private readonly TextBlock _glyph;
    private bool _isPointerOver;
    private bool _isPressed;

    public TrayAppDotNETCaptionCloseButton(SettingsPalette palette)
        : this(
            palette,
            GlyphCatalog.CHROME_CLOSE,
            SettingsUILayout.CaptionButtonWidth,
            SettingsUILayout.CaptionButtonHeight,
            SettingsUILayout.CaptionButtonGlyphFontSize)
    {
    }

    public TrayAppDotNETCaptionCloseButton(
        SettingsPalette palette,
        Glyph glyph,
        double width,
        double height,
        double glyphFontSize)
        : this(palette, glyph.Text, width, height, glyphFontSize)
    {
        GlyphApplicator.ApplyTo(_glyph, glyph);
    }

    public TrayAppDotNETCaptionCloseButton(
        SettingsPalette palette,
        string glyph,
        double width,
        double height,
        double glyphFontSize)
    {
        _palette = palette;
        Width = width;
        Height = height;
        Background = Brushes.Transparent;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;

        _glyph = new TextBlock
        {
            Text = glyph,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = glyphFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Child = _glyph;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            _isPressed = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            _isPressed = true;
            e.Pointer.Capture(this);
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_isPressed) return;

            _isPressed = false;
            e.Pointer.Capture(null);
            bool clicked = _isPointerOver;
            UpdateVisual();
            if (!clicked) return;

            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;

            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        if (_isPressed)
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonPressed);
            _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonGlyphActive);
            return;
        }

        if (_isPointerOver)
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonHover);
            _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonGlyphActive);
            return;
        }

        Background = Brushes.Transparent;
        _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
    }
}

using Avalonia;
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
    private readonly IBrush _normalGlyphForeground;
    private readonly IBrush _activeGlyphForeground;
    private readonly IBrush _hoverBackground;
    private readonly IBrush _pressedBackground;
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
        Glyph glyph,
        double width,
        double height,
        double glyphFontSize,
        FontWeight glyphFontWeight,
        Color hoverBackground,
        Color pressedBackground,
        CornerRadius cornerRadius)
        : this(
            glyph.Text,
            width,
            height,
            glyphFontSize,
            glyphFontWeight,
            TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            TrayAppDotNETSettingsUI.Brush(hoverBackground),
            TrayAppDotNETSettingsUI.Brush(pressedBackground),
            cornerRadius)
    {
        GlyphApplicator.ApplyTo(_glyph, glyph);
    }

    public TrayAppDotNETCaptionCloseButton(
        SettingsPalette palette,
        string glyph,
        double width,
        double height,
        double glyphFontSize)
        : this(
            glyph,
            width,
            height,
            glyphFontSize,
            FontWeight.Normal,
            TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            TrayAppDotNETSettingsUI.Brush(palette.CloseButtonGlyphActive),
            TrayAppDotNETSettingsUI.Brush(palette.CloseButtonHover),
            TrayAppDotNETSettingsUI.Brush(palette.CloseButtonPressed),
            default)
    {
    }

    private TrayAppDotNETCaptionCloseButton(
        string glyph,
        double width,
        double height,
        double glyphFontSize,
        FontWeight glyphFontWeight,
        IBrush normalGlyphForeground,
        IBrush activeGlyphForeground,
        IBrush hoverBackground,
        IBrush pressedBackground,
        CornerRadius cornerRadius)
    {
        _normalGlyphForeground = normalGlyphForeground;
        _activeGlyphForeground = activeGlyphForeground;
        _hoverBackground = hoverBackground;
        _pressedBackground = pressedBackground;
        Width = width;
        Height = height;
        CornerRadius = cornerRadius;
        Background = Brushes.Transparent;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;

        _glyph = new TextBlock
        {
            Text = glyph,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = glyphFontSize,
            FontWeight = glyphFontWeight,
            Foreground = _normalGlyphForeground,
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

            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, e);
            _isPointerOver = releasedInside;
            _isPressed = false;
            UpdateVisual();
            e.Pointer.Capture(null);
            if (!releasedInside) return;

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
            Background = _pressedBackground;
            _glyph.Foreground = _activeGlyphForeground;
            return;
        }

        if (_isPointerOver)
        {
            Background = _hoverBackground;
            _glyph.Foreground = _activeGlyphForeground;
            return;
        }

        Background = Brushes.Transparent;
        _glyph.Foreground = _normalGlyphForeground;
    }
}

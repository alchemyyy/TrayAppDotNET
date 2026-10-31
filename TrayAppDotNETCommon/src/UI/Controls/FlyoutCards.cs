using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Debugging;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class FlyoutCardsLayout
{
    private static FlyoutCardsResources AXAMLResources => FlyoutCardsResources.Current;

    public static Thickness ZeroThickness => AXAMLResources.AxamlFlyoutCards.ZeroThickness;
    public static CornerRadius IconButtonCornerRadius => AXAMLResources.AxamlFlyoutCards.IconButtonCornerRadius;

    public static double IconButtonLineHeightPadding =>
        AXAMLResources.AxamlFlyoutCards.IconButtonLineHeightPadding;

    public static double TextButtonFontSize => AXAMLResources.AxamlFlyoutCards.TextButtonFontSize;
    public static Thickness TextButtonBorderThickness => AXAMLResources.AxamlFlyoutCards.TextButtonBorderThickness;
    public static CornerRadius TextButtonCornerRadius => AXAMLResources.AxamlFlyoutCards.TextButtonCornerRadius;
    public static Thickness TextButtonPadding => AXAMLResources.AxamlFlyoutCards.TextButtonPadding;
}

public readonly record struct FlyoutControlPalette(
    Color Foreground,
    Color SecondaryForeground,
    Color Border,
    Color Hover,
    Color Pressed,
    Color ControlBackground,
    Color CardBackground,
    Color IconForeground,
    Color SliderTrack,
    Color SliderProgress,
    Color SliderThumb);

public static class TrayAppDotNETFlyoutUI
{
    public static IBrush Brush(Color color) =>
        color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

    public static IBrush Brush(Color color, double opacity) =>
        new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(color.A * Math.Clamp(opacity, min: 0, max: 1)),
            color.R,
            color.G,
            color.B));

    public static TextBlock Text(
        string text,
        FlyoutControlPalette palette,
        double fontSize,
        FontWeight? weight = null,
        Color? color = null) =>
        new()
        {
            Text = text,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = Brush(color ?? palette.Foreground)
        };

    public static TextBlock IconText(
        string glyph,
        FlyoutControlPalette palette,
        double fontSize,
        string? fontFamily = null,
        FontWeight? weight = null)
    {
        TextBlock icon = new()
        {
            Text = glyph,
            FontFamily = fontFamily == null ? TrayAppDotNETSettingsUI.IconFont : new FontFamily(fontFamily),
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = Brush(palette.IconForeground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        ApplyGlyphTextRendering(icon);
        DebugUIProvenance.RecordBuilder(icon);
        return icon;
    }

    /// <summary>
    /// Creates icon text from a glyph object while preserving caller-owned layout metrics.
    /// </summary>
    public static TextBlock IconText(
        Glyph glyph,
        FlyoutControlPalette palette,
        double fontSize,
        string? fontFamily = null,
        FontWeight? weight = null)
    {
        TextBlock icon = IconText(
            glyph.Text,
            palette,
            fontSize,
            fontFamily ?? TADNFontResolver.ResolveFontFamilyName(glyph.Font),
            weight);
        GlyphApplicator.ApplyTo(icon, glyph, fontFamily == null);
        return icon;
    }

    public static void ApplyGlyphTextRendering(TextBlock text)
    {
        TextOptions.SetTextRenderingMode(text, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(text, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(text, BaselinePixelAlignment.Unaligned);
        DebugUIProvenance.RecordBuilder(text);
    }

    public static Border Card(
        Control content,
        Color background,
        Color border,
        CornerRadius cornerRadius,
        Thickness padding,
        Thickness margin,
        Thickness borderThickness) =>
        new()
        {
            Background = Brush(background),
            BorderBrush = Brush(border),
            BorderThickness = borderThickness,
            CornerRadius = cornerRadius,
            Padding = padding,
            Margin = margin,
            Child = content
        };

    /// <summary>
    /// Creates a flyout icon button from a glyph object. <paramref name="lineHeight"/> pins the glyph's line box; a
    /// non-positive <paramref name="fontSize"/> leaves the button empty for caller-built content.
    /// </summary>
    public static Border IconButton(
        Glyph glyph,
        FlyoutControlPalette palette,
        Action<ControlActivation> click,
        double width,
        double height,
        double fontSize,
        bool enabled = true,
        Thickness? margin = null,
        string? tooltip = null,
        string? fontFamily = null,
        Action<ControlActivation>? rightClick = null,
        FontWeight? fontWeight = null,
        CornerRadius? cornerRadius = null,
        double? lineHeight = null)
    {
        Control content;
        if (string.IsNullOrEmpty(glyph.Text) || fontSize <= 0)
        {
            content = new Grid { IsHitTestVisible = false };
        }
        else
        {
            TextBlock icon = IconText(glyph, palette, fontSize, fontFamily, fontWeight);
            if (lineHeight is { } pinnedLineHeight) icon.LineHeight = pinnedLineHeight;
            content = icon;
        }

        return IconButtonCore(content, palette, click, width, height, enabled, margin, tooltip, rightClick,
            cornerRadius);
    }

    /// <summary>
    /// Creates the transparent flyout button every icon button uses: palette hover and pressed backgrounds, a hand
    /// cursor, a tooltip that hides while the button is engaged, and click and keyboard activation through
    /// <see cref="FlyoutButtonState"/>.
    /// </summary>
    public static Border IconButtonCore(
        Control content,
        FlyoutControlPalette palette,
        Action<ControlActivation> click,
        double width,
        double height,
        bool enabled = true,
        Thickness? margin = null,
        string? tooltip = null,
        Action<ControlActivation>? rightClick = null,
        CornerRadius? cornerRadius = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Border button = new()
        {
            Width = width,
            Height = height,
            Margin = margin ?? FlyoutCardsLayout.ZeroThickness,
            CornerRadius = cornerRadius ?? FlyoutCardsLayout.IconButtonCornerRadius,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = enabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow,
            IsEnabled = enabled
        };

        if (tooltip != null) TrayAppDotNETToolTip.SetTip(button, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        AttachButtonState(button, Colors.Transparent, palette, click, enabled, rightClick);
        DebugUIProvenance.RecordBuilder(button);
        return button;
    }

    /// <summary>Gets the line height that centers an icon glyph of <paramref name="fontSize"/> in its button.</summary>
    public static double IconLineHeight(double fontSize) =>
        Math.Ceiling(fontSize + FlyoutCardsLayout.IconButtonLineHeightPadding);

    public static Border TextButton(
        string text,
        FlyoutControlPalette palette,
        Action click,
        double fontSize = double.NaN,
        Thickness? padding = null,
        CornerRadius? cornerRadius = null)
    {
        double effectiveFontSize = double.IsNaN(fontSize) ? FlyoutCardsLayout.TextButtonFontSize : fontSize;
        TextBlock label = Text(text, palette, effectiveFontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        DebugUIProvenance.RecordBuilder(label);

        Border button = new()
        {
            Background = Brush(palette.ControlBackground),
            BorderBrush = Brush(palette.Border),
            BorderThickness = FlyoutCardsLayout.TextButtonBorderThickness,
            CornerRadius = cornerRadius ?? FlyoutCardsLayout.TextButtonCornerRadius,
            Padding = padding ?? FlyoutCardsLayout.TextButtonPadding,
            Child = label,
            Cursor = TrayAppDotNETCursors.Hand
        };

        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        AttachButtonState(button, palette.ControlBackground, palette, _ => click(), enabled: true, rightClick: null);
        DebugUIProvenance.RecordBuilder(button);
        return button;
    }

    /// <summary>
    /// Attaches the one hover and pressed rule every flyout button follows: the resting colour, then the palette's
    /// Hover while the pointer is over it, then its Pressed while the left button is held.
    /// </summary>
    public static FlyoutButtonState AttachButtonState(
        Border button,
        Color restingColor,
        FlyoutControlPalette palette,
        Action<ControlActivation> click,
        bool enabled = true,
        Action<ControlActivation>? rightClick = null)
    {
        IBrush resting = Brush(restingColor);
        IBrush hover = Brush(palette.Hover);
        IBrush pressed = Brush(palette.Pressed);
        return FlyoutButtonState.Attach(button, () => resting, () => hover, () => pressed, click, enabled, rightClick);
    }

    public static bool IsPointerInside(Control control, PointerEventArgs e)
    {
        Point point = e.GetPosition(control);
        return point is { X: >= 0, Y: >= 0 }
               && point.X <= control.Bounds.Width
               && point.Y <= control.Bounds.Height;
    }

    /// <summary>Gets whether a press on <paramref name="source"/> belongs to a control instead of the window chrome.</summary>
    public static bool IsInteractiveDragSource(Visual? source)
    {
        while (source != null)
        {
            if (source is FlyoutSlider or TextBox or Button or Slider or Thumb or RepeatButton or ComboBox
                or ScrollViewer or Image) return true;
            if (source is Control { Cursor: not null }) return true;
            source = source.GetVisualParent();
        }

        return false;
    }
}

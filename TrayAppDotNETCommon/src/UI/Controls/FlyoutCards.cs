using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.Debugging;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class FlyoutCardsLayout
{
    private static FlyoutCardsResources AXAMLResources => FlyoutCardsResources.Current;

    public static Thickness ZeroThickness => AXAMLResources.AxamlFlyoutCards.ZeroThickness;
    public static CornerRadius IconButtonCornerRadius => AXAMLResources.AxamlFlyoutCards.IconButtonCornerRadius;
    public static double TextButtonFontSize => AXAMLResources.AxamlFlyoutCards.TextButtonFontSize;
    public static Thickness TextButtonBorderThickness => AXAMLResources.AxamlFlyoutCards.TextButtonBorderThickness;
    public static CornerRadius TextButtonCornerRadius => AXAMLResources.AxamlFlyoutCards.TextButtonCornerRadius;
    public static Thickness TextButtonPadding => AXAMLResources.AxamlFlyoutCards.TextButtonPadding;
    public static double SlotCoverOpacity => AXAMLResources.AxamlFlyoutCards.SlotCoverOpacity;
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
            (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)),
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
        GlyphApplicator.ApplyTo(icon, glyph, applyFontFamily: fontFamily == null);
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

    public static Border IconButton(
        string glyph,
        FlyoutControlPalette palette,
        Action<PointerReleasedEventArgs> click,
        double width,
        double height,
        double fontSize,
        bool enabled = true,
        Thickness? margin = null,
        string? tooltip = null,
        string? fontFamily = null,
        Action<PointerReleasedEventArgs>? rightClick = null,
        FontWeight? fontWeight = null)
    {
        Control content = string.IsNullOrEmpty(glyph) || fontSize <= 0
            ? new Grid { IsHitTestVisible = false }
            : IconText(glyph, palette, fontSize, fontFamily, fontWeight);

        Border button = new()
        {
            Width = width,
            Height = height,
            Margin = margin ?? FlyoutCardsLayout.ZeroThickness,
            CornerRadius = FlyoutCardsLayout.IconButtonCornerRadius,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = enabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow,
            IsEnabled = enabled
        };

        if (tooltip != null) TrayAppDotNETToolTip.SetTip(button, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brushes.Transparent,
            () => Brush(palette.Hover),
            () => Brush(palette.Pressed),
            click,
            enabled,
            rightClick);

        DebugUIProvenance.RecordBuilder(button);
        return button;
    }

    /// <summary>
    /// Creates an icon button from a glyph object while preserving caller-owned layout metrics.
    /// </summary>
    public static Border IconButton(
        Glyph glyph,
        FlyoutControlPalette palette,
        Action<PointerReleasedEventArgs> click,
        double width,
        double height,
        double fontSize,
        bool enabled = true,
        Thickness? margin = null,
        string? tooltip = null,
        string? fontFamily = null,
        Action<PointerReleasedEventArgs>? rightClick = null,
        FontWeight? fontWeight = null)
    {
        Control content = string.IsNullOrEmpty(glyph.Text) || fontSize <= 0
            ? new Grid { IsHitTestVisible = false }
            : IconText(glyph, palette, fontSize, fontFamily, fontWeight);

        Border button = new()
        {
            Width = width,
            Height = height,
            Margin = margin ?? FlyoutCardsLayout.ZeroThickness,
            CornerRadius = FlyoutCardsLayout.IconButtonCornerRadius,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = enabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow,
            IsEnabled = enabled
        };

        if (tooltip != null) TrayAppDotNETToolTip.SetTip(button, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brushes.Transparent,
            () => Brush(palette.Hover),
            () => Brush(palette.Pressed),
            click,
            enabled,
            rightClick);

        DebugUIProvenance.RecordBuilder(button);
        return button;
    }

    public static Border TextButton(
        string text,
        FlyoutControlPalette palette,
        Action click,
        double fontSize = double.NaN,
        Thickness? padding = null)
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
            CornerRadius = FlyoutCardsLayout.TextButtonCornerRadius,
            Padding = padding ?? FlyoutCardsLayout.TextButtonPadding,
            Child = label,
            Cursor = TrayAppDotNETCursors.Hand
        };

        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brush(palette.ControlBackground),
            () => Brush(palette.Hover),
            () => Brush(palette.Pressed),
            _ => click());
        DebugUIProvenance.RecordBuilder(button);
        return button;
    }

    public static Border SlotCover(
        Color color,
        CornerRadius cornerRadius,
        double opacity = double.NaN) =>
        new()
        {
            Background = Brush(color, double.IsNaN(opacity) ? FlyoutCardsLayout.SlotCoverOpacity : opacity),
            CornerRadius = cornerRadius,
            IsVisible = false,
            IsHitTestVisible = false
        };

    public static bool IsPointerInside(Control control, PointerEventArgs e)
    {
        Point point = e.GetPosition(control);
        return point is { X: >= 0, Y: >= 0 }
               && point.X <= control.Bounds.Width
               && point.Y <= control.Bounds.Height;
    }

    public static bool IsInteractiveDragSource(Visual? source)
    {
        while (source != null)
        {
            if (source is FlyoutSlider or TextBox or Button or Slider or Thumb or RepeatButton or ComboBox
                or ScrollViewer) return true;
            if (source is Control { Cursor: not null }) return true;
            source = source.GetVisualParent();
        }

        return false;
    }
}

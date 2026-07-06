using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class FlyoutCardsLayout
{
    private static readonly Lazy<FlyoutCardsResources> Resources = new(static () => new FlyoutCardsResources());

    private static FlyoutCardsResources AXAMLResources => Resources.Value;

    public static Thickness ZeroThickness => AXAMLResources.AxamlFlyoutCards.ZeroThickness;
    public static CornerRadius IconButtonRadius => AXAMLResources.AxamlFlyoutCards.IconButtonRadius;
    public static double TextButtonFontSize => AXAMLResources.AxamlFlyoutCards.TextButtonFontSize;
    public static Thickness TextButtonBorderThickness => AXAMLResources.AxamlFlyoutCards.TextButtonBorderThickness;
    public static CornerRadius TextButtonRadius => AXAMLResources.AxamlFlyoutCards.TextButtonRadius;
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
        return icon;
    }

    public static void ApplyGlyphTextRendering(TextBlock text)
    {
        TextOptions.SetTextRenderingMode(text, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(text, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(text, BaselinePixelAlignment.Unaligned);
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
            CornerRadius = FlyoutCardsLayout.IconButtonRadius,
            Background = Brushes.Transparent,
            Child = content,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
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

        Border button = new()
        {
            Background = Brush(palette.ControlBackground),
            BorderBrush = Brush(palette.Border),
            BorderThickness = FlyoutCardsLayout.TextButtonBorderThickness,
            CornerRadius = FlyoutCardsLayout.TextButtonRadius,
            Padding = padding ?? FlyoutCardsLayout.TextButtonPadding,
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brush(palette.ControlBackground),
            () => Brush(palette.Hover),
            () => Brush(palette.Pressed),
            _ => click());
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

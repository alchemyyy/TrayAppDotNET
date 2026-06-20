using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI.Controls;

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
            Foreground = Brush(color ?? palette.Foreground),
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
            IsHitTestVisible = false,
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
            Child = content,
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
            Margin = margin ?? new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = content,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsEnabled = enabled,
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
        double fontSize = 13,
        Thickness? padding = null)
    {
        TextBlock label = Text(text, palette, fontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        Border button = new()
        {
            Background = Brush(palette.ControlBackground),
            BorderBrush = Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = padding ?? new Thickness(10, 5),
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand),
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
        double opacity = 0.22) =>
        new()
        {
            Background = Brush(color, opacity),
            CornerRadius = cornerRadius,
            IsVisible = false,
            IsHitTestVisible = false,
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

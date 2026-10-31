using Avalonia.Media;
using SkiaSharp;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>The one bitmap to PNG to HICON pipeline every tray renderer draws through.</summary>
public static class TrayIconRasterizer
{
    /// <summary>Draws a transparent square icon of <paramref name="size"/> pixels and returns it as an owned icon.</summary>
    public static NativeIcon Render<TState>(int size, TState state, Action<SKCanvas, int, TState> draw) =>
        NativeIcon.FromIconImage(RenderPng(size, state, draw), size);

    /// <summary>Draws a transparent square icon of <paramref name="size"/> pixels and returns its PNG bytes.</summary>
    public static byte[] RenderPng<TState>(int size, TState state, Action<SKCanvas, int, TState> draw)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentNullException.ThrowIfNull(draw);

        SKImageInfo info = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            draw(canvas, size, state);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    public static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);
}

/// <summary>Font resolution and glyph placement shared by the tray renderers.</summary>
public static class TrayGlyphLayout
{
    /// <summary>
    /// Returns the drawing origin that centers a glyph's advance box horizontally and its line box vertically on a
    /// square canvas, the placement every tray glyph uses.
    /// </summary>
    public static SKPoint Center(SKFont font, string glyph, float canvasSize)
    {
        ArgumentNullException.ThrowIfNull(font);

        float advanceWidth = font.MeasureText(glyph);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        return new SKPoint((canvasSize - advanceWidth) / 2f, (canvasSize - lineHeight) / 2f - metrics.Ascent);
    }

    /// <summary>Returns the first installed family of <paramref name="families"/>, or Skia's default typeface.</summary>
    public static SKTypeface ResolveTypeface(IReadOnlyList<string> families, SKFontStyle style, Action<string>? log)
    {
        foreach (string family in families)
        {
            SKTypeface typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
                return typeface;

            typeface.Dispose();
        }

        log?.Invoke(
            $"TrayGlyphLayout.ResolveTypeface: none of '{string.Join(", ", families)}' is installed; "
            + "using Skia's default typeface");
        return SKTypeface.Default;
    }
}

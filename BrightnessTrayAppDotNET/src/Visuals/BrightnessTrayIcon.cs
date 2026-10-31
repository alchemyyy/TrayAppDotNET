using Avalonia.Media;
using SkiaSharp;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.Visuals;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Brightness's eclipse tray glyph: a sun whose disc is eclipsed in proportion to the tracked brightness. In the
/// static style the input is fixed, so brightness changes never render.
/// </summary>
internal sealed class BrightnessTrayIcon(AppTheme? theme) : ITrayIconRenderer<BrightnessTrayIconRenderInput>
{
    private const int StaticStyleBrightness = 50;

    private static readonly string[] IconFontFamilies =
    [
        GlyphCatalog.SEGOE_FLUENT_ICONS,
        GlyphCatalog.SEGOE_MDL2_ASSETS
    ];

    private readonly Lock _gate = new();
    private readonly AppTheme _theme = theme ?? AppTheme.Default;
    private SKTypeface? _iconTypeface;

    public bool IsLightTheme { get; set; }
    public TrayAppDotNETTrayIconStyle IconStyle { get; set; } = TrayAppDotNETTrayIconStyle.Dynamic;

    public int BrightnessPercent
    {
        get;
        set => field = Math.Clamp(value, min: 0, max: 100);
    } = 50;

    public Color? CustomColor { get; set; }
    public Color? BrightColor { get; set; }
    public Color? DimColor { get; set; }

    public BrightnessTrayIconRenderInput CreateRenderInput()
    {
        int visualBrightness = IconStyle == TrayAppDotNETTrayIconStyle.Static
            ? StaticStyleBrightness
            : BrightnessPercent;
        return new BrightnessTrayIconRenderInput(visualBrightness, ResolveColor(visualBrightness));
    }

    public NativeIcon RenderIcon(BrightnessTrayIconRenderInput input, int iconSize)
    {
        lock (_gate)
        {
            SKTypeface typeface = _iconTypeface ??=
                TrayGlyphLayout.ResolveTypeface(IconFontFamilies, SKFontStyle.Normal, TADNLog.Log);
            return TrayIconRasterizer.Render(
                iconSize,
                (Typeface: typeface, Input: input),
                static (canvas, size, state) => Draw(canvas, size, state.Typeface, state.Input));
        }
    }

    private Color ResolveColor(int brightnessPercent)
    {
        if (IconStyle == TrayAppDotNETTrayIconStyle.Static)
            return CustomColor ?? _theme.Foreground.For(IsLightTheme);

        if (BrightColor.HasValue || DimColor.HasValue)
        {
            Color fallback = _theme.Foreground.For(IsLightTheme);
            Color bright = BrightColor ?? fallback;
            Color dim = DimColor ?? fallback;
            double t = Math.Clamp(brightnessPercent / 100.0, min: 0, max: 1);
            return Blend(dim, bright, t);
        }

        return _theme.Foreground.For(IsLightTheme);
    }

    private static void Draw(SKCanvas canvas, int size, SKTypeface typeface, BrightnessTrayIconRenderInput input)
    {
        SKColor color = TrayIconRasterizer.ToSKColor(input.ForegroundColor);
        int brightnessPercent = Math.Clamp(input.VisualBrightness, min: 0, max: 100);

        double t = brightnessPercent / 100.0;
        double x = 2 * t - 1;
        const double ratio = 0.75;
        double dSquared = 1 - ratio * ratio * (1 - x * x);
        double d = dSquared > 0 ? Math.Sqrt(dSquared) : 0;
        double eclipseOffset = (x + d) * 50;

        SKPath? eclipseClip = brightnessPercent is > 0 and < 100
            ? GetEclipsePath(typeface, size, size, eclipseOffset, offsetYPercent: 0)
            : null;
        bool isClipped = false;
        try
        {
            if (eclipseClip != null)
            {
                canvas.Save();
                canvas.ClipPath(eclipseClip, SKClipOperation.Difference, antialias: true);
                isClipped = true;
            }

            switch (brightnessPercent)
            {
                case > 99:
                    DrawGlyph(canvas, typeface, GlyphCatalog.HALF_SUN, size, size, color);
                    DrawMirroredGlyph(canvas, typeface, GlyphCatalog.HALF_SUN, size, size, color);
                    break;

                case > 0:
                    DrawGlyph(canvas, typeface, GlyphCatalog.HALF_SUN, size, size, color);
                    DrawGlyph(canvas, typeface, GlyphCatalog.FILLED_CIRCLE_SMALL, size + 2, size, color);
                    break;

                default:
                    DrawGlyph(canvas, typeface, GlyphCatalog.ECLIPSED_SUN, size, size, color);
                    break;
            }
        }
        finally
        {
            if (isClipped) canvas.Restore();
            eclipseClip?.Dispose();
        }
    }

    private static void DrawMirroredGlyph(
        SKCanvas canvas,
        SKTypeface typeface,
        Glyph glyph,
        double fontSize,
        int canvasSize,
        SKColor color)
    {
        canvas.Save();
        float center = canvasSize / 2f;
        canvas.Scale(sx: -1, sy: 1, center, center);
        DrawGlyph(canvas, typeface, glyph, fontSize, canvasSize, color);
        canvas.Restore();
    }

    private static void DrawGlyph(
        SKCanvas canvas,
        SKTypeface typeface,
        Glyph glyph,
        double fontSize,
        int canvasSize,
        SKColor color)
    {
        using SKFont font = CreateFont(typeface, fontSize);
        using SKPaint paint = new();
        paint.IsAntialias = true;
        paint.Color = color;

        SKPoint origin = TrayGlyphLayout.Center(font, glyph.Text, canvasSize);
        canvas.DrawText(glyph.Text, origin.X, origin.Y, font, paint);
    }

    private static SKPath? GetEclipsePath(
        SKTypeface typeface,
        double fontSize,
        int canvasSize,
        double offsetXPercent,
        double offsetYPercent)
    {
        if (offsetXPercent >= 100 && offsetYPercent >= 100) return null;

        using SKFont font = CreateFont(typeface, fontSize);
        using SKPath glyphPath = font.GetTextPath(GlyphCatalog.FILLED_CIRCLE_SMALL.Text, new SKPoint(x: 0, y: 0));
        if (glyphPath.IsEmpty) return null;

        SKRect bounds = glyphPath.Bounds;
        float centerOffsetX = (canvasSize - bounds.Width) / 2f - bounds.Left;
        float centerOffsetY = (canvasSize - bounds.Height) / 2f - bounds.Top;
        float radius = Math.Min(bounds.Width, bounds.Height) / 2f;
        float offsetX = (float)(offsetXPercent / 100.0) * radius * 2f;
        float offsetY = (float)(offsetYPercent / 100.0) * radius * 2f;

        using SKPath basePath = CreateTranslatedPath(glyphPath, centerOffsetX, centerOffsetY);
        using SKPath offsetPath = CreateTranslatedPath(glyphPath, centerOffsetX + offsetX, centerOffsetY + offsetY);

        SKPath result = new();
        if (basePath.Op(offsetPath, SKPathOp.Intersect, result) && !result.IsEmpty) return result;
        result.Dispose();
        return null;
    }

    private static SKFont CreateFont(SKTypeface typeface, double fontSize) =>
        new(typeface, (float)fontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal, Subpixel = false
        };

    private static SKPath CreateTranslatedPath(SKPath source, float x, float y)
    {
        SKPath translated = new();
        source.Transform(SKMatrix.CreateTranslation(x, y), translated);
        return translated;
    }

    private static Color Blend(Color from, Color to, double t)
    {
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_iconTypeface != null && !ReferenceEquals(_iconTypeface, SKTypeface.Default))
                _iconTypeface.Dispose();
            _iconTypeface = null;
        }
    }
}

internal sealed record BrightnessTrayIconRenderInput(int VisualBrightness, Color ForegroundColor);

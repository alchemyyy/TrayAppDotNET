using Avalonia.Media;
using SkiaSharp;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Tray;

namespace BrightnessTrayAppDotNET.Visuals;

internal sealed class BrightnessTrayIcon : IDisposable
{
    private static readonly string[] IconFontFamilies =
    [
        GlyphCatalog.SEGOE_FLUENT_ICONS,
        GlyphCatalog.SEGOE_MDL2_ASSETS,
    ];

    private readonly AppTheme _theme;
    private SKTypeface? _iconTypeface;
    private NativeIcon? _currentIcon;
    private bool _isDirty = true;
    private bool _isLightTheme;
    private TrayIconStyle _iconStyle = TrayIconStyle.Dynamic;
    private int _brightnessPercent = 50;
    private Color? _customColor;
    private Color? _brightColor;
    private Color? _dimColor;

    public BrightnessTrayIcon(AppTheme? theme) => _theme = theme ?? AppTheme.Default;

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set
        {
            if (_isLightTheme == value) return;
            _isLightTheme = value;
            _isDirty = true;
        }
    }

    public TrayIconStyle IconStyle
    {
        get => _iconStyle;
        set
        {
            if (_iconStyle == value) return;
            _iconStyle = value;
            _isDirty = true;
        }
    }

    public int BrightnessPercent
    {
        get => _brightnessPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_brightnessPercent == clamped) return;
            _brightnessPercent = clamped;
            _isDirty = true;
        }
    }

    public Color? CustomColor
    {
        get => _customColor;
        set
        {
            if (_customColor == value) return;
            _customColor = value;
            _isDirty = true;
        }
    }

    public Color? BrightColor
    {
        get => _brightColor;
        set
        {
            if (_brightColor == value) return;
            _brightColor = value;
            _isDirty = true;
        }
    }

    public Color? DimColor
    {
        get => _dimColor;
        set
        {
            if (_dimColor == value) return;
            _dimColor = value;
            _isDirty = true;
        }
    }

    public void InvalidateCache() => _isDirty = true;

    public NativeIcon? CreateIcon()
    {
        if (!_isDirty) return null;

        try
        {
            int size = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
            int visualBrightness = _iconStyle == TrayIconStyle.Static ? 50 : _brightnessPercent;
            byte[] png = RenderPng(size, visualBrightness, ResolveColor(visualBrightness));
            NativeIcon icon = NativeIcon.FromIconImage(png, size);
            NativeIcon? oldIcon = _currentIcon;
            _currentIcon = icon;
            oldIcon?.Dispose();
            _isDirty = false;
            return _currentIcon;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessTrayIcon.CreateIcon: {ex.Message}");
            return _currentIcon ?? AppTheme.LoadAppNativeIcon();
        }
    }

    private Color ResolveColor(int brightnessPercent)
    {
        if (_iconStyle == TrayIconStyle.Static)
            return _customColor ?? _theme.Foreground.For(_isLightTheme);

        if (_brightColor.HasValue || _dimColor.HasValue)
        {
            Color fallback = _theme.Foreground.For(_isLightTheme);
            Color bright = _brightColor ?? fallback;
            Color dim = _dimColor ?? fallback;
            double t = Math.Clamp(brightnessPercent / 100.0, 0, 1);
            return Blend(dim, bright, t);
        }

        return _theme.Foreground.For(_isLightTheme);
    }

    private byte[] RenderPng(int size, int brightnessPercent, Color foregroundColor)
    {
        SKImageInfo info = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        SKColor color = ToSKColor(foregroundColor);
        brightnessPercent = Math.Clamp(brightnessPercent, 0, 100);

        double t = brightnessPercent / 100.0;
        double x = (2 * t) - 1;
        const double ratio = 0.75;
        double dSquared = 1 - (ratio * ratio * (1 - (x * x)));
        double d = dSquared > 0 ? Math.Sqrt(dSquared) : 0;
        double eclipseOffset = (x + d) * 50;

        SKPath? eclipseClip = brightnessPercent is > 0 and < 100
            ? GetEclipsePath(size, size, eclipseOffset, 0)
            : null;
        bool isClipped = false;
        try
        {
            if (eclipseClip != null)
            {
                canvas.Save();
                canvas.ClipPath(eclipseClip, SKClipOperation.Difference, true);
                isClipped = true;
            }

            switch (brightnessPercent)
            {
                case > 99:
                    DrawGlyph(canvas, GlyphCatalog.HALF_SUN, size, size, color);
                    DrawMirroredGlyph(canvas, GlyphCatalog.HALF_SUN, size, size, color);
                    break;

                case > 0:
                    DrawGlyph(canvas, GlyphCatalog.HALF_SUN, size, size, color);
                    DrawGlyph(canvas, GlyphCatalog.FILLED_CIRCLE_SMALL, size + 2, size, color);
                    break;

                default:
                    DrawGlyph(canvas, GlyphCatalog.ECLIPSED_SUN, size, size, color);
                    break;
            }
        }
        finally
        {
            if (isClipped) canvas.Restore();
            eclipseClip?.Dispose();
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawMirroredGlyph(SKCanvas canvas, string glyph, double fontSize, int canvasSize, SKColor color)
    {
        canvas.Save();
        float center = canvasSize / 2f;
        canvas.Scale(-1, 1, center, center);
        DrawGlyph(canvas, glyph, fontSize, canvasSize, color);
        canvas.Restore();
    }

    private void DrawGlyph(SKCanvas canvas, string glyph, double fontSize, int canvasSize, SKColor color)
    {
        using SKFont font = new(IconTypeface, (float)fontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal, Subpixel = false,
        };
        using SKPaint paint = new() { IsAntialias = true, Color = color, };

        GlyphPlacement placement = MeasureGlyph(font, paint, glyph, canvasSize);
        canvas.DrawText(glyph, placement.X, placement.Y, font, paint);
    }

    private SKPath? GetEclipsePath(double fontSize, int canvasSize, double offsetXPercent, double offsetYPercent)
    {
        if (offsetXPercent >= 100 && offsetYPercent >= 100) return null;

        using SKFont font = new(IconTypeface, (float)fontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal, Subpixel = false,
        };
        using SKPath glyphPath = font.GetTextPath(GlyphCatalog.FILLED_CIRCLE_SMALL, new SKPoint(0, 0));
        if (glyphPath.IsEmpty) return null;

        SKRect bounds = glyphPath.Bounds;
        float centerOffsetX = ((canvasSize - bounds.Width) / 2f) - bounds.Left;
        float centerOffsetY = ((canvasSize - bounds.Height) / 2f) - bounds.Top;
        float radius = Math.Min(bounds.Width, bounds.Height) / 2f;
        float offsetX = (float)(offsetXPercent / 100.0) * radius * 2f;
        float offsetY = (float)(offsetYPercent / 100.0) * radius * 2f;

        using SKPath basePath = CreateTranslatedPath(glyphPath, centerOffsetX, centerOffsetY);
        using SKPath offsetPath = CreateTranslatedPath(glyphPath, centerOffsetX + offsetX, centerOffsetY + offsetY);

        SKPath result = new();
        if (!basePath.Op(offsetPath, SKPathOp.Intersect, result) || result.IsEmpty)
        {
            result.Dispose();
            return null;
        }

        return result;
    }

    private SKTypeface IconTypeface => _iconTypeface ??= ResolveIconTypeface();

    private static SKTypeface ResolveIconTypeface()
    {
        foreach (string family in IconFontFamilies)
        {
            SKTypeface typeface = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
            if (typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
                return typeface;

            typeface.Dispose();
        }

        WPFLog.Log("BrightnessTrayIcon.ResolveIconTypeface: icon fonts unavailable; using Skia default typeface");
        return SKTypeface.Default;
    }

    private static GlyphPlacement MeasureGlyph(SKFont font, SKPaint paint, string glyph, int canvasSize)
    {
        float advanceWidth = font.MeasureText(glyph, out _, paint);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        float x = (canvasSize - advanceWidth) / 2f;
        float y = (canvasSize - lineHeight) / 2f - metrics.Ascent;
        return new GlyphPlacement(x, y);
    }

    private static SKPath CreateTranslatedPath(SKPath source, float x, float y)
    {
        SKPath translated = new();
        source.Transform(SKMatrix.CreateTranslation(x, y), translated);
        return translated;
    }

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);

    private static Color Blend(Color from, Color to, double t)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    public void Dispose()
    {
        _currentIcon?.Dispose();
        _currentIcon = null;
        _iconTypeface?.Dispose();
        _iconTypeface = null;
    }

    private readonly record struct GlyphPlacement(float X, float Y);
}

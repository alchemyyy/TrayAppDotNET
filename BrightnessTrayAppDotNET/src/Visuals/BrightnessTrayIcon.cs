using Avalonia.Media;
using SkiaSharp;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Tray;

namespace BrightnessTrayAppDotNET.Visuals;

internal sealed class BrightnessTrayIcon(AppTheme? theme) : IDisposable
{
    private static readonly string[] IconFontFamilies =
    [
        GlyphCatalog.SEGOE_FLUENT_ICONS,
        GlyphCatalog.SEGOE_MDL2_ASSETS,
    ];

    private readonly Lock _gate = new();
    private readonly AppTheme _theme = theme ?? AppTheme.Default;
    private SKTypeface? _iconTypeface;
    private NativeIcon? _currentIcon;
    private bool _isDirty = true;
    private bool _isLightTheme;
    private TrayIconStyle _iconStyle = TrayIconStyle.Dynamic;
    private int _brightnessPercent = 50;
    private Color? _customColor;
    private Color? _brightColor;
    private Color? _dimColor;

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
        if (!TryCreateRenderInput(out BrightnessTrayIconRenderInput? input) || input == null) return null;

        NativeIcon? icon = RenderIcon(input);
        if (icon == null) return null;

        lock (_gate)
        {
            NativeIcon? oldIcon = _currentIcon;
            _currentIcon = icon;
            oldIcon?.Dispose();
            return _currentIcon;
        }
    }

    public bool TryCreateRenderInput(out BrightnessTrayIconRenderInput? input)
    {
        input = null;
        if (!_isDirty) return false;

        int visualBrightness = _iconStyle == TrayIconStyle.Static ? 50 : _brightnessPercent;
        input = new BrightnessTrayIconRenderInput(visualBrightness, ResolveColor(visualBrightness));
        _isDirty = false;
        return true;
    }

    public NativeIcon? RenderIcon(BrightnessTrayIconRenderInput input)
    {
        try
        {
            lock (_gate)
            {
                int size = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
                byte[] png = RenderPng(size, input.VisualBrightness, input.ForegroundColor);
                return NativeIcon.FromIconImage(png, size);
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessTrayIcon.CreateIcon: {ex.Message}");
            lock (_gate)
                return _currentIcon?.Clone() ?? AppTheme.LoadAppNativeIcon();
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
        using SKFont font = new(IconTypeface, (float)fontSize);
        font.Edging = SKFontEdging.Antialias;
        font.Hinting = SKFontHinting.Normal;
        font.Subpixel = false;
        using SKPaint paint = new();
        paint.IsAntialias = true;
        paint.Color = color;

        GlyphPlacement placement = MeasureGlyph(font, paint, glyph, canvasSize);
        canvas.DrawText(glyph, placement.X, placement.Y, font, paint);
    }

    private SKPath? GetEclipsePath(double fontSize, int canvasSize, double offsetXPercent, double offsetYPercent)
    {
        if (offsetXPercent >= 100 && offsetYPercent >= 100) return null;

        using SKFont font = new(IconTypeface, (float)fontSize);
        font.Edging = SKFontEdging.Antialias;
        font.Hinting = SKFontHinting.Normal;
        font.Subpixel = false;
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
        if (basePath.Op(offsetPath, SKPathOp.Intersect, result) && !result.IsEmpty) return result;
        result.Dispose();
        return null;

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
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _currentIcon?.Dispose();
            _currentIcon = null;
            _iconTypeface?.Dispose();
            _iconTypeface = null;
        }
    }

    private readonly record struct GlyphPlacement(float X, float Y);
}

internal sealed record BrightnessTrayIconRenderInput(int VisualBrightness, Color ForegroundColor);

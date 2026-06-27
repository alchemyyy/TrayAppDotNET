using Avalonia.Media;
using SkiaSharp;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed record TrayIconGlyphLayer(string? BackdropGlyph, string ForegroundGlyph);

public sealed class TrayIconRenderOptions
{
    public required IReadOnlyList<string> IconFontFamilies { get; init; }
    public SKFontStyle IconFontStyle { get; init; } = SKFontStyle.Normal;
    public SKFontEdging FontEdging { get; init; } = SKFontEdging.Antialias;
    public bool Subpixel { get; init; }
    public float MeasureFontScale { get; init; } = 1.0f;
    public float DrawFontScale { get; init; } = 1.0f;
    public Func<NativeIcon?>? FallbackIcon { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed class TrayIconRenderer : IDisposable
{
    private readonly TrayIconRenderOptions _options;
    private SKTypeface? _typeface;
    private NativeIcon? _currentIcon;

    public TrayIconRenderer(TrayIconRenderOptions options)
    {
        if (options.IconFontFamilies.Count == 0)
            throw new ArgumentException("At least one icon font family is required.", nameof(options));

        _options = options;
    }

    private SKTypeface IconTypeface => _typeface ??= ResolveIconTypeface();

    public NativeIcon? Render(TrayIconGlyphLayer glyphs, Color foregroundColor, double backdropOpacity)
    {
        try
        {
            int iconSize = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
            byte[] png = RenderLayeredPng(
                iconSize,
                glyphs.BackdropGlyph,
                glyphs.ForegroundGlyph,
                foregroundColor,
                backdropOpacity);
            NativeIcon icon = NativeIcon.FromIconImage(png, iconSize);
            NativeIcon? oldIcon = _currentIcon;
            _currentIcon = icon;
            oldIcon?.Dispose();
            return _currentIcon;
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"TrayIconRenderer.Render: {ex.Message}");
            return _currentIcon ?? _options.FallbackIcon?.Invoke();
        }
    }

    private byte[] RenderLayeredPng(
        int size,
        string? backdropGlyph,
        string foregroundGlyph,
        Color foregroundColor,
        double backdropOpacity)
    {
        SKImageInfo info = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        SKColor foreground = ToSkColor(foregroundColor);
        SKColor backdrop = foreground.WithAlpha((byte)(foreground.Alpha * backdropOpacity));

        if (!string.IsNullOrEmpty(backdropGlyph))
        {
            GlyphPlacement backdropPlacement = MeasureGlyph(backdropGlyph, size);
            DrawGlyph(canvas, backdropGlyph, backdropPlacement, backdrop);

            GlyphPlacement foregroundPlacement = MeasureGlyph(foregroundGlyph, size)
                .WithLayoutLeft(backdropPlacement.X);
            DrawGlyph(canvas, foregroundGlyph, foregroundPlacement, foreground);
        }
        else
            DrawGlyph(canvas, foregroundGlyph, MeasureGlyph(foregroundGlyph, size), foreground);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private GlyphPlacement MeasureGlyph(string glyph, int canvasSize)
    {
        using SKFont font = new(IconTypeface, canvasSize * _options.MeasureFontScale)
        {
            Edging = _options.FontEdging, Subpixel = _options.Subpixel,
        };
        using SKPaint paint = new() { IsAntialias = true, Color = SKColors.Transparent, };

        float advanceWidth = font.MeasureText(glyph, out SKRect bounds, paint);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        float x = (canvasSize - advanceWidth) / 2f;
        float y = (canvasSize - lineHeight) / 2f - metrics.Ascent;
        return new GlyphPlacement(canvasSize * _options.DrawFontScale, bounds, x, y);
    }

    private void DrawGlyph(SKCanvas canvas, string glyph, GlyphPlacement placement, SKColor color)
    {
        using SKFont font = new(IconTypeface, placement.FontSize)
        {
            Edging = _options.FontEdging, Subpixel = _options.Subpixel,
        };
        using SKPaint paint = new() { IsAntialias = true, Color = color, };

        canvas.DrawText(glyph, placement.X, placement.Y, font, paint);
    }

    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);

    private SKTypeface ResolveIconTypeface()
    {
        foreach (string family in _options.IconFontFamilies)
        {
            SKTypeface typeface = SKTypeface.FromFamilyName(family, _options.IconFontStyle);
            if (typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
                return typeface;

            typeface.Dispose();
        }

        return SKTypeface.Default;
    }

    public void Dispose()
    {
        _currentIcon?.Dispose();
        _currentIcon = null;
        _typeface?.Dispose();
        _typeface = null;
    }

    private readonly record struct GlyphPlacement(float FontSize, SKRect Bounds, float X, float Y)
    {
        public GlyphPlacement WithLayoutLeft(float layoutLeft) =>
            this with { X = layoutLeft };
    }
}

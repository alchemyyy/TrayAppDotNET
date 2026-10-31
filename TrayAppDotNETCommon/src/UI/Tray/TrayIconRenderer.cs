using Avalonia.Media;
using SkiaSharp;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed record TrayIconGlyphLayer(string? BackdropGlyph, string ForegroundGlyph);

public sealed record TrayIconRenderInput(
    TrayIconGlyphLayer Glyphs,
    Color ForegroundColor,
    double BackdropOpacity);

public sealed class TrayIconRenderOptions
{
    /// <summary>Installed font families tried in order. Ignored when <see cref="TypefaceFactory"/> is set.</summary>
    public IReadOnlyList<string> IconFontFamilies { get; init; } = [];

    /// <summary>Creates the typeface itself, for an app font that is not installed, such as Fan's icon font.</summary>
    public Func<SKTypeface>? TypefaceFactory { get; init; }

    public SKFontStyle IconFontStyle { get; init; } = SKFontStyle.Normal;
    public SKFontEdging FontEdging { get; init; } = SKFontEdging.Antialias;
    public bool Subpixel { get; init; }
    public float MeasureFontScale { get; init; } = 1.0f;
    public float DrawFontScale { get; init; } = 1.0f;
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Draws a tray icon from one glyph, optionally over a translucent backdrop glyph. Render inputs are values, so the
/// presenter decides when a render is needed.
/// </summary>
public sealed class TrayIconRenderer : IDisposable
{
    private readonly Lock _gate = new();
    private readonly TrayIconRenderOptions _options;
    private SKTypeface? _typeface;

    public TrayIconRenderer(TrayIconRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TypefaceFactory == null && options.IconFontFamilies.Count == 0)
            throw new ArgumentException(message: "An icon font family or a typeface factory is required.", nameof(options));

        _options = options;
    }

    /// <summary>Renders a caller-owned icon of <paramref name="size"/> pixels.</summary>
    public NativeIcon Render(TrayIconRenderInput input, int size)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_gate)
        {
            SKTypeface typeface = _typeface ??= CreateTypeface();
            return TrayIconRasterizer.Render(
                size,
                (Renderer: this, Typeface: typeface, Input: input),
                static (canvas, canvasSize, state) => state.Renderer.Draw(canvas, canvasSize, state.Typeface, state.Input));
        }
    }

    private void Draw(SKCanvas canvas, int size, SKTypeface typeface, TrayIconRenderInput input)
    {
        SKColor foreground = TrayIconRasterizer.ToSKColor(input.ForegroundColor);
        string? backdropGlyph = input.Glyphs.BackdropGlyph;
        string foregroundGlyph = input.Glyphs.ForegroundGlyph;

        using SKFont measureFont = CreateFont(typeface, size * _options.MeasureFontScale);
        using SKFont drawFont = CreateFont(typeface, size * _options.DrawFontScale);
        using SKPaint paint = new();
        paint.IsAntialias = true;

        SKPoint foregroundOrigin = TrayGlyphLayout.Center(measureFont, foregroundGlyph, size);
        if (!string.IsNullOrEmpty(backdropGlyph))
        {
            // The foreground layer shares the backdrop's left edge, so partial glyphs line up with the full one
            SKPoint backdropOrigin = TrayGlyphLayout.Center(measureFont, backdropGlyph, size);
            paint.Color = foreground.WithAlpha((byte)(foreground.Alpha * input.BackdropOpacity));
            canvas.DrawText(backdropGlyph, backdropOrigin.X, backdropOrigin.Y, drawFont, paint);
            foregroundOrigin = foregroundOrigin with { X = backdropOrigin.X };
        }

        paint.Color = foreground;
        canvas.DrawText(foregroundGlyph, foregroundOrigin.X, foregroundOrigin.Y, drawFont, paint);
    }

    private SKFont CreateFont(SKTypeface typeface, float size) =>
        new(typeface, size) { Edging = _options.FontEdging, Subpixel = _options.Subpixel };

    private SKTypeface CreateTypeface() =>
        _options.TypefaceFactory?.Invoke()
        ?? TrayGlyphLayout.ResolveTypeface(_options.IconFontFamilies, _options.IconFontStyle, _options.Log);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_typeface != null && !ReferenceEquals(_typeface, SKTypeface.Default))
                _typeface.Dispose();
            _typeface = null;
        }
    }
}

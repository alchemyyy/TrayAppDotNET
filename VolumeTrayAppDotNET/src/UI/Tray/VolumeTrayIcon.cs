using Avalonia.Media;
using SkiaSharp;

namespace VolumeTrayAppDotNET.UI.Tray;

internal sealed class VolumeTrayIcon(AppTheme? theme) : IDisposable
{
    private const double BackdropOpacityValue = 0.21;
    private const float MeasureFontScale = 1f;

    private static readonly SKFontStyle IconFontStyle = new(
        SKFontStyleWeight.Normal,
        SKFontStyleWidth.Normal,
        SKFontStyleSlant.Upright);

    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies = [GlyphCatalog.SEGOE_FLUENT_ICONS, GlyphCatalog.SEGOE_MDL2_ASSETS],
        IconFontStyle = IconFontStyle,
        FontEdging = SKFontEdging.Antialias,
        Subpixel = false,
        MeasureFontScale = MeasureFontScale,
        FallbackIcon = AppTheme.LoadAppNativeIcon,
        Log = TADNLog.Log,
    });

    private readonly VolumeTrayIconGlyphs _glyphs = new(
        GlyphCatalog.PLAYBACK_VOLUME_MUTE,
        GlyphCatalog.PLAYBACK_VOLUME_SILENT,
        GlyphCatalog.PLAYBACK_VOLUME_LOW,
        GlyphCatalog.PLAYBACK_VOLUME_MID,
        GlyphCatalog.PLAYBACK_VOLUME_HIGH);

    private readonly AppTheme _theme = theme ?? AppTheme.Default;
    private bool _isDirty = true;

    public bool IsLightTheme
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _isDirty = true;
        }
    }

    public float Volume
    {
        get;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(field - clamped) < 0.0001f) return;
            string oldGlyph = GlyphCatalog.GetVolumeTier(field, IsMuted);
            field = clamped;
            string newGlyph = GlyphCatalog.GetVolumeTier(field, IsMuted);
            if (oldGlyph != newGlyph) _isDirty = true;
        }
    }

    public bool IsMuted
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _isDirty = true;
        }
    }

    public Color? TrayIconColorOverride
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _isDirty = true;
        }
    }

    public void InvalidateCache() => _isDirty = true;

    public NativeIcon? CreateIcon()
    {
        if (!TryCreateRenderInput(out TrayIconRenderInput? input) || input == null) return null;

        return _renderer.Render(input);
    }

    public bool TryCreateRenderInput(out TrayIconRenderInput? input)
    {
        input = null;
        if (!_isDirty) return false;

        _isDirty = false;
        input = new TrayIconRenderInput(ResolveGlyphs(), ResolveColor(), BackdropOpacityValue);
        return true;
    }

    public NativeIcon? RenderIcon(TrayIconRenderInput input) => _renderer.RenderOwned(input);

    private TrayIconGlyphLayer ResolveGlyphs()
    {
        string foreground = GlyphCatalog.GetVolumeTier(Volume, IsMuted);
        string? backdrop = IsMuted || foreground == _glyphs.High
            ? null
            : _glyphs.High;
        return new TrayIconGlyphLayer(backdrop, foreground);
    }

    private Color ResolveColor() =>
        TrayIconColorOverride ?? _theme.Foreground.For(IsLightTheme);

    public void Dispose() => _renderer.Dispose();
}

internal sealed record VolumeTrayIconGlyphs(
    string Muted,
    string Silent,
    string Low,
    string Mid,
    string High);

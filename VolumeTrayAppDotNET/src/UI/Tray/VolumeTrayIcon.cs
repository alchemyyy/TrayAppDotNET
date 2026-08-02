using Avalonia.Media;
using SkiaSharp;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

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
        Log = TADNLog.Log
    });

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
            Glyph oldGlyph = GlyphCatalog.GetVolumeTier(field, IsMuted);
            field = clamped;
            Glyph newGlyph = GlyphCatalog.GetVolumeTier(field, IsMuted);
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

    /// <summary>
    /// Forces the next dirty-check render path to create a new icon input.
    /// </summary>
    public void InvalidateCache() => _isDirty = true;

    /// <summary>
    /// Creates and caches a tray icon for the current dirty render state.
    /// </summary>
    public NativeIcon? CreateIcon()
    {
        if (!TryCreateRenderInput(out TrayIconRenderInput? input) || input == null) return null;

        return _renderer.Render(input);
    }

    /// <summary>
    /// Creates the render input for the current tray icon state.
    /// </summary>
    public TrayIconRenderInput CreateRenderInput() =>
        new(ResolveGlyphs(), ResolveColor(), BackdropOpacityValue);

    /// <summary>
    /// Creates a render input only when the renderer's dirty state requires it.
    /// </summary>
    public bool TryCreateRenderInput(out TrayIconRenderInput? input)
    {
        input = null;
        if (!_isDirty) return false;

        _isDirty = false;
        input = CreateRenderInput();
        return true;
    }

    /// <summary>
    /// Renders a caller-owned tray icon from a precomputed render input.
    /// </summary>
    public NativeIcon? RenderIcon(TrayIconRenderInput input) => _renderer.RenderOwned(input);

    private TrayIconGlyphLayer ResolveGlyphs()
    {
        Glyph foreground = GlyphCatalog.GetVolumeTier(Volume, IsMuted);
        Glyph high = GlyphCatalog.PLAYBACK_VOLUME_HIGH;
        string? backdrop = IsMuted || foreground.Text == high.Text
            ? null
            : high.Text;
        return new TrayIconGlyphLayer(backdrop, foreground.Text);
    }

    private Color ResolveColor() =>
        TrayIconColorOverride ?? _theme.Foreground.For(IsLightTheme);

    public void Dispose() => _renderer.Dispose();
}

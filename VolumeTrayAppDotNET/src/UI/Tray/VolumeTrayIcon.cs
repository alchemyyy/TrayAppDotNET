using Avalonia.Media;
using SkiaSharp;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace VolumeTrayAppDotNET.UI.Tray;

/// <summary>
/// Volume's tray glyph: the volume tier over a translucent full-volume backdrop. The render input holds only the tier
/// glyphs and the color, so a volume change within one tier never renders.
/// </summary>
internal sealed class VolumeTrayIcon(AppTheme? theme) : ITrayIconRenderer<TrayIconRenderInput>
{
    private const double BackdropOpacityValue = 0.21;

    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies = [GlyphCatalog.SEGOE_FLUENT_ICONS, GlyphCatalog.SEGOE_MDL2_ASSETS],
        IconFontStyle = SKFontStyle.Normal,
        FontEdging = SKFontEdging.Antialias,
        Subpixel = false,
        Log = TADNLog.Log
    });

    private readonly AppTheme _theme = theme ?? AppTheme.Default;

    public bool IsLightTheme { get; set; }

    public float Volume
    {
        get;
        set => field = Math.Clamp(value, min: 0f, max: 1f);
    }

    public bool IsMuted { get; set; }
    public Color? TrayIconColorOverride { get; set; }

    public TrayIconRenderInput CreateRenderInput() =>
        new(ResolveGlyphs(), ResolveColor(), BackdropOpacityValue);

    public NativeIcon RenderIcon(TrayIconRenderInput input, int iconSize) => _renderer.Render(input, iconSize);

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

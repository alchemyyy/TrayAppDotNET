using Avalonia.Media;
using SkiaSharp;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.UI.Tray;

/// <summary>
/// Battery's tray glyph. The render input is only the glyph and its color, so the watt, capacity and estimate readings
/// that change with every poll never cause a render.
/// </summary>
internal sealed class BatteryTrayIcon(AppTheme? theme) : ITrayIconRenderer<TrayIconRenderInput>
{
    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    private static IReadOnlyList<string> BatteryIconFontFamilies => IsWindows11
        ? [GlyphCatalog.SEGOE_FLUENT_ICONS, GlyphCatalog.SEGOE_MDL2_ASSETS]
        : [GlyphCatalog.SEGOE_MDL2_ASSETS, GlyphCatalog.SEGOE_FLUENT_ICONS];

    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies = BatteryIconFontFamilies,
        IconFontStyle = SKFontStyle.Normal,
        FontEdging = SKFontEdging.Antialias,
        Subpixel = false,
        Log = TADNLog.Log
    });

    private readonly AppTheme _theme = theme ?? AppTheme.Default;

    public BatterySnapshot Snapshot { get; set; } = BatterySnapshot.Unknown;
    public bool IsLightTheme { get; set; }
    public Color? TrayIconColorOverride { get; set; }

    public TrayIconRenderInput CreateRenderInput()
    {
        Glyph glyph = BatteryGlyphResolver.Resolve(Snapshot);
        return new TrayIconRenderInput(
            new TrayIconGlyphLayer(BackdropGlyph: null, glyph.Text),
            ResolveColor(Snapshot),
            BackdropOpacity: 0);
    }

    public NativeIcon RenderIcon(TrayIconRenderInput input, int iconSize) => _renderer.Render(input, iconSize);

    private Color ResolveColor(BatterySnapshot snapshot)
    {
        if (TrayIconColorOverride.HasValue) return TrayIconColorOverride.Value;
        if (!snapshot.BatteryPresent) return _theme.DisabledForeground.For(IsLightTheme);
        return _theme.Foreground.For(IsLightTheme);
    }

    public void Dispose() => _renderer.Dispose();
}

using Avalonia.Media;
using SkiaSharp;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.UI.Tray;

internal sealed class BatteryTrayIcon(AppTheme? theme) : IDisposable
{
    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    private static readonly SKFontStyle IconFontStyle = new(
        SKFontStyleWeight.Normal,
        SKFontStyleWidth.Normal,
        SKFontStyleSlant.Upright);

    private static IReadOnlyList<string> BatteryIconFontFamilies => IsWindows11
        ? [GlyphCatalog.SEGOE_FLUENT_ICONS, GlyphCatalog.SEGOE_MDL2_ASSETS]
        : [GlyphCatalog.SEGOE_MDL2_ASSETS, GlyphCatalog.SEGOE_FLUENT_ICONS];

    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies = BatteryIconFontFamilies,
        IconFontStyle = IconFontStyle,
        FontEdging = SKFontEdging.Antialias,
        Subpixel = false,
        FallbackIcon = AppTheme.LoadAppNativeIcon,
        Log = TADNLog.Log
    });

    private readonly AppTheme _theme = theme ?? AppTheme.Default;
    private BatterySnapshot _snapshot = BatterySnapshot.Unknown;
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

    public void SetSnapshot(BatterySnapshot snapshot)
    {
        if (_snapshot == snapshot) return;
        _snapshot = snapshot;
        _isDirty = true;
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
        Glyph glyph = BatteryGlyphResolver.Resolve(_snapshot);
        input = new TrayIconRenderInput(
            new TrayIconGlyphLayer(BackdropGlyph: null, glyph.Text),
            ResolveColor(_snapshot),
            BackdropOpacity: 0);
        return true;
    }

    public NativeIcon? RenderIcon(TrayIconRenderInput input) => _renderer.RenderOwned(input);

    private Color ResolveColor(BatterySnapshot snapshot)
    {
        if (TrayIconColorOverride.HasValue) return TrayIconColorOverride.Value;
        if (!snapshot.BatteryPresent) return _theme.DisabledForeground.For(IsLightTheme);
        return _theme.Foreground.For(IsLightTheme);
    }

    public void Dispose() => _renderer.Dispose();
}

using Avalonia.Media;
using BatteryTrayAppDotNET.Models;
using SkiaSharp;

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
        Log = TADNLog.Log,
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
        if (!_isDirty) return null;

        _isDirty = false;
        return _renderer.Render(
            new TrayIconGlyphLayer(null, ResolveGlyph(_snapshot)),
            ResolveColor(_snapshot),
            backdropOpacity: 0);
    }

    private Color ResolveColor(BatterySnapshot snapshot)
    {
        if (TrayIconColorOverride.HasValue) return TrayIconColorOverride.Value;
        if (!snapshot.BatteryPresent) return _theme.DisabledForeground.For(IsLightTheme);
        return _theme.Foreground.For(IsLightTheme);
    }

    private static string ResolveGlyph(BatterySnapshot snapshot)
    {
        int level = Math.Clamp((int)Math.Ceiling(snapshot.ChargePercentage / 10.0), 0, 10);
        if (snapshot.IsCharging || snapshot.IsOnExternalPower)
        {
            return level switch
            {
                0 => GlyphCatalog.BATTERY_CHARGING_0,
                1 => GlyphCatalog.BATTERY_CHARGING_1,
                2 => GlyphCatalog.BATTERY_CHARGING_2,
                3 => GlyphCatalog.BATTERY_CHARGING_3,
                4 => GlyphCatalog.BATTERY_CHARGING_4,
                5 => GlyphCatalog.BATTERY_CHARGING_5,
                6 => GlyphCatalog.BATTERY_CHARGING_6,
                7 => GlyphCatalog.BATTERY_CHARGING_7,
                8 => GlyphCatalog.BATTERY_CHARGING_8,
                9 => GlyphCatalog.BATTERY_CHARGING_9,
                _ => GlyphCatalog.BATTERY_CHARGING_10,
            };
        }

        return level switch
        {
            0 => GlyphCatalog.BATTERY_0,
            1 => GlyphCatalog.BATTERY_1,
            2 => GlyphCatalog.BATTERY_2,
            3 => GlyphCatalog.BATTERY_3,
            4 => GlyphCatalog.BATTERY_4,
            5 => GlyphCatalog.BATTERY_5,
            6 => GlyphCatalog.BATTERY_6,
            7 => GlyphCatalog.BATTERY_7,
            8 => GlyphCatalog.BATTERY_8,
            9 => GlyphCatalog.BATTERY_9,
            _ => GlyphCatalog.BATTERY_10,
        };
    }

    public void Dispose() => _renderer.Dispose();
}

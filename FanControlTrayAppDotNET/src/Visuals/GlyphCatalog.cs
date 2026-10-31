using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // The fan glyph is drawn from Fan's own icon font, which Common resolves through the app font registration
    static GlyphCatalog() => TADNFontResolver.RegisterAppFont(Constants.FanFontFamilyName);

    public static Glyph FAN => Glyphs.Fan;
    public static Glyph VOLTAGE => Glyphs.Voltage;
    public static Glyph LOAD => Glyphs.Load;
    public static Glyph WATTAGE => Glyphs.Wattage;
    public static Glyph PROBE => Glyphs.Probe;
    public static Glyph TEMPERATURE => Glyphs.Temperature;
    public static Glyph CLOCK => Glyphs.Clock;

    public static Glyph ARROW_RIGHT => Glyphs.ArrowRight;

    public static Glyph CURVE_WINDOW => Glyphs.CurveWindow;
    public static Glyph ADD => Glyphs.Add;
    public static Glyph GROUP => Glyphs.Group;
    public static Glyph DELETE => Glyphs.Delete;
    public static Glyph CLOSE => Glyphs.Close;
    public static Glyph VIEW => Glyphs.View;
    public static Glyph HIDE => Glyphs.Hide;
    public static Glyph DRAG_HANDLE => Glyphs.DragHandle;

    public static Glyph PIN => Glyphs.Pin;
    public static Glyph PINNED => Glyphs.Pinned;

    public static Glyph COLLAPSED => Glyphs.Collapsed;
    public static Glyph EXPANDED => Glyphs.Expanded;

    public static Glyph FLYOUT_FAN_CONTROL_MODE_MANUAL => Glyphs.FlyoutFanControlModeManual;
    public static Glyph FLYOUT_FAN_CONTROL_MODE_CURVE => Glyphs.FlyoutFanControlModeCurve;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}

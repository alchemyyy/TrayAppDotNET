using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new static Glyph SETTINGS => Glyph("Settings");
    public new static Glyph POWER => Glyph("Power");
    public new static Glyph INFO => Glyph("Info");
    public new static Glyph EXIT => Glyph("Exit");
    public new static Glyph WARNING => Glyph("Warning");
    public new static Glyph UNDOCK => Glyph("Undock");
    public new static Glyph REDOCK => Glyph("Redock");

    public static Glyph FAN => Glyph("Fan");
    public static Glyph VOLTAGE => Glyph("Voltage");
    public static Glyph LOAD => Glyph("Load");
    public static Glyph WATTAGE => Glyph("Wattage");
    public static Glyph PROBE => Glyph("Probe");
    public static Glyph TEMPERATURE => Glyph("Temperature");
    public static Glyph CLOCK => Glyph("Clock");

    public static Glyph ARROW_LEFT => Glyph("ArrowLeft");
    public static Glyph ARROW_RIGHT => Glyph("ArrowRight");

    public static Glyph CURVE_WINDOW => Glyph("CurveWindow");
    public static Glyph ADD => Glyph("Add");
    public static Glyph CHECK => Glyph("Check");
    public static Glyph GROUP => Glyph("Group");
    public static Glyph DELETE => Glyph("Delete");
    public static Glyph CLOSE => Glyph("Close");
    public static Glyph VIEW => Glyph("View");
    public static Glyph HIDE => Glyph("Hide");
    public static Glyph DRAG_HANDLE => Glyph("DragHandle");

    public static Glyph PIN => Glyph("Pin");
    public static Glyph PINNED => Glyph("Pinned");

    public static Glyph COLLAPSED => Glyph("Collapsed");
    public static Glyph EXPANDED => Glyph("Expanded");

    public static Glyph FLYOUT_FAN_CONTROL_MODE_MANUAL => Glyph("FlyoutFanControlModeManual");
    public static Glyph FLYOUT_FAN_CONTROL_MODE_CURVE => Glyph("FlyoutFanControlModeCurve");

    public static Glyph CIRCLE => Glyph("Circle");
    public static Glyph DIAMOND => Glyph("Diamond");
    public static Glyph STAR => Glyph("Star");
    public static Glyph SQUARE => Glyph("Square");
    public static Glyph HEART => Glyph("Heart");

    private static Glyph Glyph(string name) => Resources.Value.Glyph(name);
}

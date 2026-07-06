using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Visuals.GlyphCatalog
{
    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new static readonly Glyph SETTINGS = Glyph.Fluent(CommonGlyphCatalog.SETTINGS);
    public new static readonly Glyph POWER = Glyph.Fluent(CommonGlyphCatalog.POWER);
    public new static readonly Glyph INFO = Glyph.Fluent(CommonGlyphCatalog.INFO);
    public new static readonly Glyph EXIT = Glyph.Fluent(CommonGlyphCatalog.EXIT);
    public new static readonly Glyph WARNING = Glyph.Fluent(CommonGlyphCatalog.WARNING);
    public new static readonly Glyph UNDOCK = Glyph.Fluent(CommonGlyphCatalog.UNDOCK);
    public new static readonly Glyph REDOCK = Glyph.Fluent(CommonGlyphCatalog.REDOCK);

    public static readonly Glyph FAN = Glyph.FanFont("\U000F1111"); // FanFont.ttf U+F1111
    public static readonly Glyph VOLTAGE = Glyph.Fluent("\uE945"); // Fluent, LightningBolt
    public static readonly Glyph LOAD = Glyph.Fluent("\uEAFC", scaleX: 0.9); // Fluent, Market
    public static readonly Glyph WATTAGE = Glyph.Fluent("\uECAD"); // Fluent, Calories
    public static readonly Glyph PROBE = Glyph.Fluent("\uE9CA"); // Fluent, Frigid
    public static readonly Glyph TEMPERATURE = PROBE; // Frigid
    public static readonly Glyph CLOCK = Glyph.Fluent("\uE916"); // Fluent, Stopwatch

    public static readonly Glyph ARROW_LEFT = Glyph.Fluent("\uF0D5"); // Fluent, ChromeBackContrast
    public static readonly Glyph ARROW_RIGHT = Glyph.Fluent("\uF0D6"); // Fluent, ChromeBackContrastMirrored

    public static readonly Glyph CURVE_WINDOW = Glyph.Fluent("\uE9E9"); // Fluent, Equalizer
    public static readonly Glyph ADD = Glyph.Fluent("\uE710"); // Fluent, Add
    public static readonly Glyph CHECK = Glyph.Fluent("\uE8FB"); // Fluent, Accept
    public static readonly Glyph GROUP = Glyph.Fluent("\uE81E"); // Fluent, MapLayers
    public static readonly Glyph DELETE = Glyph.Fluent("\uE653"); // Close
    public static readonly Glyph CLOSE = DELETE;
    public static readonly Glyph VIEW = Glyph.Fluent("\uE890"); // Fluent, View
    public static readonly Glyph HIDE = Glyph.Fluent("\uED1A"); // Fluent, Hide
    public static readonly Glyph DRAG_HANDLE = Glyph.Fluent("\uE700"); // Fluent, GlobalNavButton

    public static readonly Glyph PIN = Glyph.Fluent("\uE718"); // Fluent, Pin
    public static readonly Glyph PINNED = Glyph.Fluent("\uE840"); // Fluent, Pinned

    public static readonly Glyph COLLAPSED = Glyph.Fluent("\uE96D"); // Fluent, ChevronUpSmall
    public static readonly Glyph EXPANDED = Glyph.Fluent("\uE96E"); // Fluent, ChevronDownSmall


    public static readonly Glyph FLYOUT_FAN_CONTROL_MODE_MANUAL = Glyph.Fluent("\uE72E"); // Fluent, Lock
    public static readonly Glyph FLYOUT_FAN_CONTROL_MODE_CURVE = Glyph.Fluent("\uE785"); // Fluent, Unlock

    // Slider-thumb glyph defaults. Picked from Segoe Fluent Icons so we ship a working catalog
    // without an external font asset.
    public static readonly Glyph CIRCLE = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_CIRCLE);
    public static readonly Glyph DIAMOND = Glyph.Fluent("\uEB4B"); // Fluent, EndPointSolid
    public static readonly Glyph STAR = Glyph.Fluent("\uE735"); // Fluent, FavoriteStarFill
    public static readonly Glyph SQUARE = Glyph.Fluent("\uE003"); // CheckboxFill (square)
    public static readonly Glyph HEART = Glyph.Fluent("\uEB52"); // Fluent, HeartFill
}

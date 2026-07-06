using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Visuals.GlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SETTINGS = CommonGlyphCatalog.SETTINGS;
    public new const string POWER = CommonGlyphCatalog.POWER;
    public new const string INFO = CommonGlyphCatalog.INFO;
    public new const string EXIT = CommonGlyphCatalog.EXIT;
    public new const string WARNING = CommonGlyphCatalog.WARNING;
    public new const string UNDOCK = CommonGlyphCatalog.UNDOCK;
    public new const string REDOCK = CommonGlyphCatalog.REDOCK;

    public const string FAN = "\U000F1111"; // FanFont.ttf U+F1111
    public const string VOLTAGE = "\uE945"; // Fluent, LightningBolt
    public const string LOAD = "\uEAFC"; // Fluent, Market
    public const string WATTAGE = "\uECAD"; // Fluent, Calories
    public const string TEMPERATURE = PROBE; // Frigid
    public const string CLOCK = "\uE916"; // Fluent, Stopwatch

    public const string ARROW_LEFT = "\uF0D5"; // Fluent, ChromeBackContrast
    public const string ARROW_RIGHT = "\uF0D6"; // Fluent, ChromeBackContrastMirrored

    public const string CURVE_WINDOW = "\uE9E9"; // Fluent, Equalizer
    public const string ADD = "\uE710"; // Fluent, Add
    public const string CHECK = "\uE8FB"; // Fluent, Accept
    public const string GROUP = "\uE81E"; // Fluent, MapLayers
    public const string PROBE = "\uE9CA"; // Fluent, Frigid
    public const string DELETE = "\uE653"; // Close
    public const string CLOSE = DELETE;
    public const string VIEW = "\uE890"; // Fluent, View
    public const string HIDE = "\uED1A"; // Fluent, Hide
    public const string DRAG_HANDLE = "\uE700"; // Fluent, GlobalNavButton

    public const string PIN = "\uE718"; // Fluent, Pin
    public const string PINNED = "\uE840"; // Fluent, Pinned

    public const string COLLAPSED = "\uE96D"; // Fluent, ChevronUpSmall
    public const string EXPANDED = "\uE96E"; // Fluent, ChevronDownSmall


    public const string FLYOUT_FAN_CONTROL_MODE_MANUAL = "\uE72E"; // Fluent, Lock
    public const string FLYOUT_FAN_CONTROL_MODE_CURVE = "\uE785"; // Fluent, Unlock

    // Slider-thumb glyph defaults. Picked from Segoe Fluent Icons so we ship a working catalog
    // without an external font asset.
    public const string CIRCLE = CommonGlyphCatalog.SLIDER_THUMB_CIRCLE;
    public const string DIAMOND = "\uEB4B"; // Fluent, EndPointSolid
    public const string STAR = "\uE735"; // Fluent, FavoriteStarFill
    public const string SQUARE = "\uE003"; // CheckboxFill (square)
    public const string HEART = "\uEB52"; // Fluent, HeartFill
}

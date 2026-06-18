using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Visuals.GlyphCatalog
{
    // ===========================================================================
    // Brightness tray glyphs
    // ===========================================================================

    public const string ECLIPSED_SUN = "\uEC8A"; // lower brightness
    public const string HALF_SUN = "\uE793"; // Light
    public const string FILLED_CIRCLE_SMALL = "\uE915"; // radio bullet

    public const string CRESCENT_SUN = "\uF08C"; // Blue Light
    public const string CRESCENT_MOON_OLD = "\uE708"; // Quiet Hours
    public const string CRESCENT_MOON = "\uEC46"; // Mob Quiet Hours
    public const string CRESCENT_MOON_BOLD = "\uF0CE"; // Quiet Hours Badge 12

    public const string EMPTY_CIRCLE_0 = "\uEDAF"; // Inking Color Outline
    public const string EMPTY_CIRCLE_3 = "\uEA3A"; // Circle Ring
    public const string FILLED_CIRCLE_0 = "\uED67"; // Inking Color Fill
    public const string FILLED_CIRCLE_1 = "\uEDAF"; // Circle Ring Badge 12
    public const string FILLED_CIRCLE_2 = "\uEDB0"; // Circle Fill Badge 12
    public const string FILLED_CIRCLE_3 = "\uEA3B"; // Circle Fill
    public const string FILLED_CIRCLE_4 = "\uF0B6"; // status circle 7
    public const string FILLED_CIRCLE_LARGE = "\uE91F"; // filled circle mask

    public const string FILLED_SQUARE = "\uE978"; // Presence Chicklet

    // ===========================================================================
    // Night-light glyphs
    // ===========================================================================

    public const string LIGHTBULB = "\uEA80";

    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new const string CHROME_CLOSE = CommonGlyphCatalog.CHROME_CLOSE;
    public new const string CHEVRON_UP = CommonGlyphCatalog.CHEVRON_UP;
    public new const string CHEVRON_DOWN = CommonGlyphCatalog.CHEVRON_DOWN;
    public new const string CHEVRON_LEFT = CommonGlyphCatalog.CHEVRON_LEFT;
    public new const string CHEVRON_RIGHT = CommonGlyphCatalog.CHEVRON_RIGHT;
    public new const string CALENDAR = CommonGlyphCatalog.CALENDAR;

    public const string MONITOR = "\uE7F4";
    public const string SYNC_BADGE = "\uEDAB";
    public new const string POWER = CommonGlyphCatalog.POWER;
    public const string DISPLAY_SETTINGS = "\uE7F8"; // DeviceLaptopNoPic
    public new const string SETTINGS = CommonGlyphCatalog.SETTINGS;
    public new const string WARNING = CommonGlyphCatalog.WARNING;
    public const string PROFILE_SAVE = "\uE74E";
    public const string PROFILE_INDICATOR = "\uE915"; // matches FILLED_CIRCLE_SMALL
}

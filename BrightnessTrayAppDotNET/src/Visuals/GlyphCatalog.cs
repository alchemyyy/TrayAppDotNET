namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Theming.GlyphCatalog
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

    public const string MONITOR = "\uE7F4";
    public const string SYNC_BADGE = "\uEDAB";
    public new const string POWER = "\uE7E8";
    public const string DISPLAY_SETTINGS = "\uE7F8"; // DeviceLaptopNoPic
    public new const string SETTINGS = "\uE713";
    public new const string WARNING = "\uE7BA"; // Warning
    public const string PROFILE_SAVE = "\uE74E";
    public const string PROFILE_INDICATOR = "\uE915"; // matches FILLED_CIRCLE_SMALL
}

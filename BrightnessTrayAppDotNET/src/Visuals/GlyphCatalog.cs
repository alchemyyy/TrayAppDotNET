using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // ===========================================================================
    // Brightness tray glyphs
    // ===========================================================================

    public const string ECLIPSED_SUN = "\uEC8A"; // Fluent, LowerBrightness
    public const string HALF_SUN = "\uE793"; // Fluent, Light
    public const string FILLED_CIRCLE_SMALL = "\uE915"; // Fluent, RadioBullet

    public const string CRESCENT_SUN = "\uF08C"; // Fluent, BlueLight
    public const string CRESCENT_MOON_OLD = "\uE708"; // Fluent, QuietHours
    // NOTE: Also rendered explicitly with MDL2 in the disabled curve glyph
    public const string CRESCENT_MOON = "\uEC46"; // Fluent, MobQuietHours
    public const string CRESCENT_MOON_BOLD = "\uF0CE"; // Fluent, QuietHoursBadge12

    public const string EMPTY_CIRCLE_0 = "\uEDAF"; // Fluent, CircleRingBadge12
    public const string EMPTY_CIRCLE_3 = "\uEA3A"; // Fluent, CircleRing
    public const string FILLED_CIRCLE_0 = "\uED67"; // Fluent, InkingColorFill
    public const string FILLED_CIRCLE_1 = "\uEDAF"; // Fluent, CircleRingBadge12
    public const string FILLED_CIRCLE_2 = "\uEDB0"; // Fluent, CircleFillBadge12
    public const string FILLED_CIRCLE_3 = "\uEA3B"; // Fluent, CircleFill
    public const string FILLED_CIRCLE_4 = "\uF0B6"; // Fluent, StatusCircle7
    public const string FILLED_CIRCLE_LARGE = "\uE91F"; // Fluent, FullCircleMask

    public const string FILLED_SQUARE = "\uE978"; // Fluent, PresenceChicklet

    // ===========================================================================
    // Night-light glyphs
    // ===========================================================================

    public const string LIGHTBULB = "\uEA80"; // Fluent, Lightbulb

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

    public const string STOPWATCH = "\uE916"; // Fluent, Stopwatch
    public const string CHECK_MARK = "\uE73E"; // Fluent, CheckMark
    public const string MONITOR = "\uE7F4"; // Fluent, TVMonitor
    public const string SYNC_BADGE = "\uEDAB"; // Fluent, SyncBadge12
    public new const string POWER = CommonGlyphCatalog.POWER;
    public const string DISPLAY_SETTINGS = "\uE7F8"; // Fluent, DeviceLaptopNoPic
    public new const string SETTINGS = CommonGlyphCatalog.SETTINGS;
    public new const string WARNING = CommonGlyphCatalog.WARNING;
    public const string PROFILE_SAVE = "\uE74E"; // Fluent, Save
    public const string PROFILE_INDICATOR = "\uE915"; // Fluent, RadioBullet

    // ===========================================================================
    // Environmental map glyphs
    // ===========================================================================

    public const string MAP_CENTER = CHECK_MARK;
    public const string MAP_PIN = "\uECAF"; // Fluent, MapPin
}

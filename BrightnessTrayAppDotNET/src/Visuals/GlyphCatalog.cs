using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // ===========================================================================
    // Brightness tray glyphs
    // ===========================================================================

    public static readonly Glyph ECLIPSED_SUN = Glyph.SegoeFluent("\uEC8A"); // Fluent, LowerBrightness
    public static readonly Glyph HALF_SUN = Glyph.SegoeFluent("\uE793"); // Fluent, Light
    public static readonly Glyph FILLED_CIRCLE_SMALL = Glyph.SegoeFluent("\uE915"); // Fluent, RadioBullet

    public static readonly Glyph CRESCENT_SUN = Glyph.SegoeFluent("\uF08C"); // Fluent, BlueLight
    public static readonly Glyph CRESCENT_MOON_OLD = Glyph.SegoeFluent("\uE708"); // Fluent, QuietHours
    // NOTE: Also rendered explicitly with MDL2 in the disabled curve glyph
    public static readonly Glyph CRESCENT_MOON = Glyph.SegoeFluent("\uEC46"); // Fluent, MobQuietHours
    public static readonly Glyph CRESCENT_MOON_BOLD = Glyph.SegoeFluent("\uF0CE"); // Fluent, QuietHoursBadge12

    public static readonly Glyph EMPTY_CIRCLE_0 = Glyph.SegoeFluent("\uEDAF"); // Fluent, CircleRingBadge12
    public static readonly Glyph EMPTY_CIRCLE_3 = Glyph.SegoeFluent("\uEA3A"); // Fluent, CircleRing
    public static readonly Glyph FILLED_CIRCLE_0 = Glyph.SegoeFluent("\uED67"); // Fluent, InkingColorFill
    public static readonly Glyph FILLED_CIRCLE_1 = Glyph.SegoeFluent("\uEDAF"); // Fluent, CircleRingBadge12
    public static readonly Glyph FILLED_CIRCLE_2 = Glyph.SegoeFluent("\uEDB0"); // Fluent, CircleFillBadge12
    public static readonly Glyph FILLED_CIRCLE_3 = Glyph.SegoeFluent("\uEA3B"); // Fluent, CircleFill
    public static readonly Glyph FILLED_CIRCLE_4 = Glyph.SegoeFluent("\uF0B6"); // Fluent, StatusCircle7
    public static readonly Glyph FILLED_CIRCLE_LARGE = Glyph.SegoeFluent("\uE91F"); // Fluent, FullCircleMask

    public static readonly Glyph FILLED_SQUARE = Glyph.SegoeFluent("\uE978"); // Fluent, PresenceChicklet

    // ===========================================================================
    // Night-light glyphs
    // ===========================================================================

    public static readonly Glyph LIGHTBULB = Glyph.SegoeFluent("\uEA80"); // Fluent, Lightbulb

    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public new const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public new static readonly Glyph CHROME_CLOSE = Glyph.Fluent(CommonGlyphCatalog.CHROME_CLOSE);
    public new static readonly Glyph CHEVRON_UP = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_UP);
    public new static readonly Glyph CHEVRON_DOWN = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_DOWN);
    public new static readonly Glyph CHEVRON_LEFT = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_LEFT);
    public new static readonly Glyph CHEVRON_RIGHT = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_RIGHT);
    public new static readonly Glyph CALENDAR = Glyph.Fluent(CommonGlyphCatalog.CALENDAR);

    public static readonly Glyph STOPWATCH = Glyph.MDL2("\uE916"); // Fluent, Stopwatch
    public static readonly Glyph CHECK_MARK = Glyph.SegoeFluent("\uE73E"); // Fluent, CheckMark
    public static readonly Glyph MONITOR = Glyph.SegoeFluent("\uE7F4"); // Fluent, TVMonitor
    public static readonly Glyph SYNC_BADGE = Glyph.SegoeFluent("\uEDAB"); // Fluent, SyncBadge12
    public new static readonly Glyph POWER = Glyph.Fluent(CommonGlyphCatalog.POWER);
    public static readonly Glyph DISPLAY_SETTINGS = Glyph.SegoeFluent("\uE7F8"); // Fluent, DeviceLaptopNoPic
    public new static readonly Glyph SETTINGS = Glyph.Fluent(CommonGlyphCatalog.SETTINGS);
    public new static readonly Glyph WARNING = Glyph.Fluent(CommonGlyphCatalog.WARNING);
    public static readonly Glyph PROFILE_SAVE = Glyph.SegoeFluent("\uE74E"); // Fluent, Save
    public static readonly Glyph PROFILE_INDICATOR = Glyph.SegoeFluent("\uE915"); // Fluent, RadioBullet

    // ===========================================================================
    // Environmental map glyphs
    // ===========================================================================

    public static readonly Glyph MAP_CENTER = CHECK_MARK;
    public static readonly Glyph MAP_PIN = Glyph.Fluent("\uECAF"); // Fluent, MapPin
}

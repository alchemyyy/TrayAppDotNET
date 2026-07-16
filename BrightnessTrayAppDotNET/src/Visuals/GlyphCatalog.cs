using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());

    public static Glyph ECLIPSED_SUN => Glyph("EclipsedSun");
    public static Glyph HALF_SUN => Glyph("HalfSun");
    public static Glyph FILLED_CIRCLE_SMALL => Glyph("FilledCircleSmall");

    public static Glyph CRESCENT_SUN => Glyph("CrescentSun");
    public static Glyph CRESCENT_MOON_OLD => Glyph("CrescentMoonOld");
    public static Glyph CRESCENT_MOON => Glyph("CrescentMoon");
    public static Glyph CRESCENT_MOON_BOLD => Glyph("CrescentMoonBold");

    public static Glyph EMPTY_CIRCLE_0 => Glyph("EmptyCircle0");
    public static Glyph EMPTY_CIRCLE_3 => Glyph("EmptyCircle3");
    public static Glyph FILLED_CIRCLE_0 => Glyph("FilledCircle0");
    public static Glyph FILLED_CIRCLE_1 => Glyph("FilledCircle1");
    public static Glyph FILLED_CIRCLE_2 => Glyph("FilledCircle2");
    public static Glyph FILLED_CIRCLE_3 => Glyph("FilledCircle3");
    public static Glyph FILLED_CIRCLE_4 => Glyph("FilledCircle4");
    public static Glyph FILLED_CIRCLE_LARGE => Glyph("FilledCircleLarge");
    public static Glyph FILLED_SQUARE => Glyph("FilledSquare");

    public static Glyph LIGHTBULB => Glyph("Lightbulb");

    public new const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public new const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public new static Glyph CHROME_CLOSE => Glyph("ChromeClose");
    public new static Glyph CHEVRON_UP => Glyph("ChevronUp");
    public new static Glyph CHEVRON_DOWN => Glyph("ChevronDown");
    public new static Glyph CHEVRON_LEFT => Glyph("ChevronLeft");
    public new static Glyph CHEVRON_RIGHT => Glyph("ChevronRight");
    public new static Glyph CALENDAR => Glyph("Calendar");

    public static Glyph STOPWATCH => Glyph("Stopwatch");
    public static Glyph LOCK => Glyph("Lock");
    public static Glyph UNLOCK => Glyph("Unlock");
    public static Glyph CHECK_MARK => Glyph("CheckMark");
    public static Glyph MONITOR => Glyph("Monitor");
    public static Glyph SYNC_BADGE => Glyph("SyncBadge");
    public new static Glyph POWER => Glyph("Power");
    public static Glyph DISPLAY_SETTINGS => Glyph("DisplaySettings");
    public new static Glyph SETTINGS => Glyph("Settings");
    public new static Glyph WARNING => Glyph("Warning");
    public static Glyph PROFILE_SAVE => Glyph("ProfileSave");
    public static Glyph PROFILE_INDICATOR => Glyph("ProfileIndicator");

    public static Glyph MAP_CENTER => Glyph("MapCenter");
    public static Glyph MAP_PIN => Glyph("MapPin");

    private static Glyph Glyph(string name) => Resources.Value.Glyph(name);
}

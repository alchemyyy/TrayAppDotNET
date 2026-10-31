using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Glyph objects shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public static Glyph ECLIPSED_SUN => Glyphs.EclipsedSun;
    public static Glyph HALF_SUN => Glyphs.HalfSun;
    public static Glyph FILLED_CIRCLE_SMALL => Glyphs.FilledCircleSmall;

    public static Glyph CRESCENT_SUN => Glyphs.CrescentSun;
    public static Glyph CRESCENT_MOON => Glyphs.CrescentMoon;

    public static Glyph FILLED_CIRCLE_2 => Glyphs.FilledCircle2;
    public static Glyph FILLED_SQUARE => Glyphs.FilledSquare;

    public static Glyph LIGHTBULB => Glyphs.Lightbulb;

    public static Glyph STOPWATCH => Glyphs.Stopwatch;
    public static Glyph LOCK => Glyphs.Lock;
    public static Glyph UNLOCK => Glyphs.Unlock;
    public static Glyph CHECK_MARK => Glyphs.CheckMark;
    public static Glyph MONITOR => Glyphs.Monitor;
    public static Glyph DISCONNECT_DISPLAY => Glyphs.DisconnectDisplay;
    public static Glyph SYNC_BADGE => Glyphs.SyncBadge;
    public static Glyph DISPLAY_SETTINGS => Glyphs.DisplaySettings;
    public static Glyph PROFILE_SAVE => Glyphs.ProfileSave;
    public static Glyph PROFILE_INDICATOR => Glyphs.ProfileIndicator;

    public static Glyph MAP_CENTER => Glyphs.MapCenter;
    public static Glyph MAP_PIN => Glyphs.MapPin;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}

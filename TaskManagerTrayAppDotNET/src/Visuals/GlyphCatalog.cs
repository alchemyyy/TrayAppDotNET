using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.Visuals;

/// <summary>
/// Task Manager glyph objects shared by app renderers.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public static Glyph MORE => Glyphs.More;

    public static Glyph SAVE => Glyphs.Save;

    public static Glyph CLOSE => CHROME_CLOSE;

    public static Glyph CARET_LEFT => Glyphs.CaretLeft;

    public static Glyph CARET_RIGHT => Glyphs.CaretRight;

    public static Glyph PROCESSES => Glyphs.Processes;

    public static Glyph PERFORMANCE => Glyphs.Performance;

    public static Glyph APP_HISTORY => Glyphs.AppHistory;

    public static Glyph STARTUP_APPS => Glyphs.StartupApps;

    public static Glyph USERS => Glyphs.Users;

    public static Glyph SERVICES => Glyphs.Services;

    public static Glyph UPDATE => Glyphs.Update;

    public static Glyph SELECTED => Glyphs.Selected;

    /// <summary>UAC shield shown on context-menu actions that require administrator approval.</summary>
    public static Glyph SHIELD => Glyphs.Shield;

    public static Glyph SORT_ASCENDING => Glyphs.SortAscending;

    public static Glyph SORT_DESCENDING => Glyphs.SortDescending;

    public static Glyph STATUS_SUSPENDED => Glyphs.StatusSuspended;

    public static Glyph STATUS_EFFICIENCY_MODE => Glyphs.StatusEfficiencyMode;

    public static Glyph STATUS_NOT_RESPONDING => Glyphs.StatusNotResponding;

    /// <summary>North-facing chevron drawn in the icon slot of a semantic group's Root line.</summary>
    public static Glyph ROOT_LINE => Glyphs.RootLine;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}

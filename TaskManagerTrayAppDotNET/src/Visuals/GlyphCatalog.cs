using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Visuals;

/// <summary>
/// Task Manager glyph objects and runtime composites shared by app renderers.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
#if DEBUG
    private static readonly GlyphCatalogHotReloadStore<GlyphCatalogResources> Resources =
        GlyphCatalogHotReloadStore<GlyphCatalogResources>.Create(
            "TaskManager",
            static () => new GlyphCatalogResources());
#else
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());
#endif

    public static Glyph MORE => Glyph("More");

    public static Glyph SAVE => Glyph("Save");

    public static Glyph CLOSE => CHROME_CLOSE;

    public new static Glyph CHEVRON_UP_BIG => Glyph("ChevronUpBig");

    public new static Glyph CHEVRON_DOWN_BIG => Glyph("ChevronDownBig");

    public static Glyph PROCESSES => Glyph("Processes");

    public static Glyph PERFORMANCE => Glyph("Performance");

    public static Glyph APP_HISTORY => Glyph("AppHistory");

    public static Glyph STARTUP_APPS => Glyph("StartupApps");

    public static Glyph USERS => Glyph("Users");

    public static Glyph SERVICES => Glyph("Services");

    public static Glyph GLOBAL_NAVIGATION => Glyph("GlobalNavigation");

    public static Glyph SELECTED => Glyph("Selected");

    public static Glyph SORT_ASCENDING => Glyph("SortAscending");

    public static Glyph SORT_DESCENDING => Glyph("SortDescending");

    private static Glyph Glyph(string name)
    {
#if DEBUG
        return Resources.Current.Glyph(name);
#else
        return Resources.Value.Glyph(name);
#endif
    }
}

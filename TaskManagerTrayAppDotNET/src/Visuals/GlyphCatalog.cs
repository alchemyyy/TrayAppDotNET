using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.Visuals;

/// <summary>
/// Task Manager glyph objects shared by app renderers.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
#if DEBUG
    private static readonly GlyphCatalogHotReloadStore<GlyphCatalogResources> Resources =
        GlyphCatalogHotReloadStore<GlyphCatalogResources>.Create(
            catalogName: "TaskManager",
            static () => new GlyphCatalogResources());
#else
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());
#endif

    public static Glyph MORE => Glyph("More");

    public static Glyph SAVE => Glyph("Save");

    public static Glyph CLOSE => CHROME_CLOSE;

    public new static Glyph CHEVRON_UP_BIG => Glyph("ChevronUpBig");

    public new static Glyph CHEVRON_DOWN_BIG => Glyph("ChevronDownBig");

    public static Glyph CARET_LEFT => Glyph("CaretLeft");

    public static Glyph CARET_RIGHT => Glyph("CaretRight");

    public static Glyph PROCESSES => Glyph("Processes");

    public static Glyph PERFORMANCE => Glyph("Performance");

    public static Glyph APP_HISTORY => Glyph("AppHistory");

    public static Glyph STARTUP_APPS => Glyph("StartupApps");

    public static Glyph USERS => Glyph("Users");

    public static Glyph SERVICES => Glyph("Services");

    public static Glyph SELECTED => Glyph("Selected");

    public static Glyph SORT_ASCENDING => Glyph("SortAscending");

    public static Glyph SORT_DESCENDING => Glyph("SortDescending");

    private static GlyphCatalogResources CurrentResources
    {
        get
        {
#if DEBUG
            return Resources.Current;
#else
            return Resources.Value;
#endif
        }
    }

    private static Glyph Glyph(string name) => CurrentResources.Glyph(name);
}

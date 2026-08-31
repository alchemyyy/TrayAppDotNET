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

    public static Glyph PC1 => Glyph("PC1");

    public static Glyph TASK_MANAGER_APP => Glyph("TaskManagerApp");

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

    /// <summary>
    /// Returns the PC1 glyph with TaskManagerApp fitted to its monitor frame.
    /// </summary>
    public static CompositeGlyph TASK_MANAGER_APP_COMPOSITE
    {
        get
        {
            GlyphCatalogResources resources = CurrentResources;
            CompositeGlyphLayer[] layers = new CompositeGlyphLayer[2];
            layers[0] = new CompositeGlyphLayer(PC1);
            layers[1] = new CompositeGlyphLayer(
                TASK_MANAGER_APP,
                resources.AxamlGlyphCatalog.TaskManagerAppScaleX,
                resources.AxamlGlyphCatalog.TaskManagerAppScaleY,
                resources.AxamlGlyphCatalog.TaskManagerAppTranslateX,
                resources.AxamlGlyphCatalog.TaskManagerAppTranslateY);
            return new CompositeGlyph(
                resources.AxamlGlyphCatalog.AppIconDesignCanvasSize,
                resources.AxamlGlyphCatalog.AppIconOuterMarginFraction,
                layers);
        }
    }

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

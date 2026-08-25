using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Visuals;

/// <summary>
/// Task Manager glyph objects and runtime composites shared by app renderers.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private const int AppIconDesignCanvasSize = 1024;
    private const double AppIconOuterMarginFraction = 0.055;
    private const double TaskManagerAppScaleX = 0.625;
    private const double TaskManagerAppScaleY = 2.0 / 3.0;
    private const double TaskManagerAppTranslateX = 384.0;
    private const double TaskManagerAppTranslateY = 704.0 / 3.0;

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

    /// <summary>
    /// Returns the PC1 glyph with TaskManagerApp fitted to its monitor frame.
    /// </summary>
    public static CompositeGlyph TASK_MANAGER_APP_COMPOSITE
    {
        get
        {
            CompositeGlyphLayer[] layers = new CompositeGlyphLayer[2];
            layers[0] = new CompositeGlyphLayer(PC1);
            layers[1] = new CompositeGlyphLayer(
                TASK_MANAGER_APP,
                TaskManagerAppScaleX,
                TaskManagerAppScaleY,
                TaskManagerAppTranslateX,
                TaskManagerAppTranslateY);
            return new CompositeGlyph(AppIconDesignCanvasSize, AppIconOuterMarginFraction, layers);
        }
    }

    private static Glyph Glyph(string name)
    {
#if DEBUG
        return Resources.Current.Glyph(name);
#else
        return Resources.Value.Glyph(name);
#endif
    }
}

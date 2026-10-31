#if DEBUG
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>Notifies live UI and tray renderers when any glyph catalog reloads.</summary>
public static class GlyphCatalogHotReload
{
    public static event Action? ResourcesReloaded;

    /// <summary>Raises <see cref="ResourcesReloaded"/> for a reloaded glyph or theme-color catalog.</summary>
    public static void NotifyResourcesReloaded(string label) => AXAMLHotReload.NotifyEach(ResourcesReloaded, label);
}
#endif

#if DEBUG
using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>Notifies live UI when a default theme-color catalog reloads.</summary>
public static class AppThemeHotReload
{
    public static event Action? ResourcesReloaded;

    /// <summary>Raises <see cref="ResourcesReloaded"/>, then the glyph consumers' rebuild path.</summary>
    public static void NotifyResourcesReloaded(string label)
    {
        AXAMLHotReload.NotifyEach(ResourcesReloaded, label);

        // Existing glyph consumers already own the correct rebuild or redraw behavior for
        // code-created visual trees, so theme reloads share that invalidation path
        GlyphCatalogHotReload.NotifyResourcesReloaded(label);
    }

    /// <summary>
    /// Applies present reloaded entries while preserving existing <see cref="ThemeColor"/> references, which theme
    /// objects hold as their defaults, and keeping omitted entries as fallbacks.
    /// </summary>
    public static void SynchronizeColors(ResourceDictionary currentResources, ResourceDictionary candidateResources)
    {
        foreach ((object key, object? candidateValue) in candidateResources)
        {
            if (currentResources[key] is ThemeColor currentColor && candidateValue is ThemeColor candidateColor)
            {
                currentColor.LightHex = candidateColor.LightHex;
                currentColor.DarkHex = candidateColor.DarkHex;
                continue;
            }

            currentResources[key] = candidateValue;
        }
    }
}
#endif

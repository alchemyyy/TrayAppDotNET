#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>Notifies live UI when a default theme-color catalog reloads.</summary>
public static class AppThemeHotReload
{
    public static event Action? ResourcesReloaded;

    internal static void NotifyResourcesReloaded(string catalogName)
    {
        Action? handlers = ResourcesReloaded;
        if (handlers != null)
        {
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception exception)
                {
                    TADNLog.LogDebug(
                        $"{catalogName} theme color hot-reload notification failed: {exception.Message}");
                }
            }
        }

        // Existing glyph consumers already own the correct rebuild or redraw behavior for
        // code-created visual trees, so theme reloads share that invalidation path
        GlyphCatalogHotReload.NotifyResourcesReloaded($"{catalogName} theme color");
    }
}

/// <summary>Keeps the latest successfully loaded theme-color dictionary.</summary>
public sealed class AppThemeHotReloadStore<TResource>
    where TResource : ResourceDictionary
{
    private readonly AXAMLResourceHotReloadStore<TResource> _resources;

    private AppThemeHotReloadStore(AXAMLResourceHotReloadStore<TResource> resources) =>
        _resources = resources;

    public TResource Current => _resources.Current;

    /// <summary>Reloads the source immediately on the calling UI thread.</summary>
    internal void ReloadNow() => _resources.ReloadNow();

    /// <summary>Creates a store for an AppTheme.axaml file adjacent to the caller.</summary>
    public static AppThemeHotReloadStore<TResource> Create(
        string catalogName,
        Func<TResource> resourceFactory,
        string sourceFileName = "AppTheme.axaml",
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        AXAMLResourceHotReloadStore<TResource> resources =
            AXAMLResourceHotReloadStore<TResource>.Create(
                $"{catalogName} theme color catalog",
                resourceFactory,
                () => AppThemeHotReload.NotifyResourcesReloaded(catalogName),
                sourceFileName,
                callerFilePath,
                AppThemeResourceReader.SynchronizeColors);
        return new AppThemeHotReloadStore<TResource>(resources);
    }
}
#endif

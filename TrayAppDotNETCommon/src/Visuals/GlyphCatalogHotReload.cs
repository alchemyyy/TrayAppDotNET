using System.Runtime.CompilerServices;
using TrayAppDotNETCommon.UI;
#if DEBUG
using Avalonia.Controls;
#endif

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Notifies live UI and tray renderers when any glyph catalog reloads.
/// </summary>
public static class GlyphCatalogHotReload
{
#if DEBUG
    public static event Action? ResourcesReloaded;
#else
    public static event Action? ResourcesReloaded
    {
        add { }
        remove { }
    }
#endif

#if DEBUG
    /// <summary>
    /// Notifies each consumer independently so one failed refresh does not block the others.
    /// </summary>
    internal static void NotifyResourcesReloaded(string catalogName)
    {
        Action? handlers = ResourcesReloaded;
        if (handlers == null) return;

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                TADNLog.LogDebug(
                    $"{catalogName} glyph catalog hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}

#if DEBUG
/// <summary>
/// Keeps a compiled glyph dictionary and replaces it after successful source AXAML reloads.
/// </summary>
public sealed class GlyphCatalogHotReloadStore<TResource>
    where TResource : ResourceDictionary
{
    private readonly AXAMLResourceHotReloadStore<TResource> _resources;

    private GlyphCatalogHotReloadStore(AXAMLResourceHotReloadStore<TResource> resources) =>
        _resources = resources;

    /// <summary>
    /// Gets the latest successfully loaded dictionary.
    /// </summary>
    public TResource Current => _resources.Current;

    /// <summary>
    /// Creates a store for an AXAML file adjacent to the calling catalog source file.
    /// </summary>
    public static GlyphCatalogHotReloadStore<TResource> Create(
        string catalogName,
        Func<TResource> resourceFactory,
        string sourceFileName = "GlyphCatalog.axaml",
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        AXAMLResourceHotReloadStore<TResource> resources =
            AXAMLResourceHotReloadStore<TResource>.Create(
                $"{catalogName} glyph catalog",
                resourceFactory,
                () => GlyphCatalogHotReload.NotifyResourcesReloaded(catalogName),
                sourceFileName,
                callerFilePath);
        return new GlyphCatalogHotReloadStore<TResource>(resources);
    }
}
#endif

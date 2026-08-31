#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

/// <summary>Notifies live common UI surfaces after a shared AXAML dictionary reloads.</summary>
public static class CommonAXAMLHotReload
{
    public static event Action? ResourcesReloaded;

    internal static void NotifyResourcesReloaded(string resourceName)
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
                    $"{resourceName} common AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }

    internal static void SynchronizeResources<TResource>(
        TResource currentResources,
        TResource candidateResources)
        where TResource : ResourceDictionary
    {
        List<KeyValuePair<object, object?>> resourceSnapshot = [];
        foreach (KeyValuePair<object, object?> resource in candidateResources)
            resourceSnapshot.Add(resource);

        currentResources.Clear();
        foreach (KeyValuePair<object, object?> resource in resourceSnapshot)
            currentResources[resource.Key] = resource.Value;
    }
}

/// <summary>Provides one compiled resource instance and Debug source reloads with stable identity.</summary>
internal sealed class CommonAXAMLResourceStore<TResource>
    where TResource : ResourceDictionary
{
    private readonly AXAMLResourceHotReloadStore<TResource> _resources;

    private CommonAXAMLResourceStore(AXAMLResourceHotReloadStore<TResource> resources) =>
        _resources = resources;

    public TResource Current => _resources.Current;

    public static CommonAXAMLResourceStore<TResource> Create(
        string resourceName,
        Func<TResource> resourceFactory,
        string sourceFileName,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        AXAMLResourceHotReloadStore<TResource> resources =
            AXAMLResourceHotReloadStore<TResource>.Create(
                resourceName,
                resourceFactory,
                () => CommonAXAMLHotReload.NotifyResourcesReloaded(resourceName),
                sourceFileName,
                callerFilePath,
                static (currentResources, candidateResources) =>
                    CommonAXAMLHotReload.SynchronizeResources(
                        currentResources,
                        candidateResources));
        return new CommonAXAMLResourceStore<TResource>(resources);
    }
}
#endif

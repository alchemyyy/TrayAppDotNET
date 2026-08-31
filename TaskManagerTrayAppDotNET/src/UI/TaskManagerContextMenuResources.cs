using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides the app-local visual constants for Task Manager content context menus.</summary>
public sealed partial class TaskManagerContextMenuResources : ResourceDictionary
{
#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<TaskManagerContextMenuResources> Resources =
        AXAMLResourceHotReloadStore<TaskManagerContextMenuResources>.Create(
            resourceName: "Task Manager context menu resources",
            static () => new TaskManagerContextMenuResources(),
            NotifyResourcesReloaded,
            sourceFileName: "TaskManagerContextMenuResources.axaml",
            synchronizeReload: SynchronizeResources);
#else
    private static readonly Lazy<TaskManagerContextMenuResources> Resources =
        new(static () => new TaskManagerContextMenuResources());
#endif

    /// <summary>Initializes the compiled Task Manager context-menu resource dictionary.</summary>
    public TaskManagerContextMenuResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static TaskManagerContextMenuResources Current
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

#if DEBUG
    /// <summary>Notifies live Task Manager content menus after a successful AXAML reload.</summary>
    internal static event Action? ResourcesReloaded;

    private static void SynchronizeResources(
        TaskManagerContextMenuResources currentResources,
        TaskManagerContextMenuResources candidateResources)
    {
        currentResources.Clear();
        foreach (KeyValuePair<object, object?> resource in candidateResources)
            currentResources[resource.Key] = resource.Value;
    }

    private static void NotifyResourcesReloaded()
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
                    $"Task Manager context-menu AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}

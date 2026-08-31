using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerWindowResources : ResourceDictionary
{
#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<TaskManagerWindowResources> Resources =
        AXAMLResourceHotReloadStore<TaskManagerWindowResources>.Create(
            resourceName: "Task Manager window resources",
            static () => new TaskManagerWindowResources(),
            NotifyResourcesReloaded,
            sourceFileName: "TaskManagerWindow.axaml",
            synchronizeReload: SynchronizeResources);
#else
    private static readonly Lazy<TaskManagerWindowResources> Resources =
        new(static () => new TaskManagerWindowResources());
#endif

    /// <summary>Initializes the compiled Task Manager window resource dictionary.</summary>
    public TaskManagerWindowResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded Task Manager resource dictionary.</summary>
    public static TaskManagerWindowResources Current
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
    /// <summary>Notifies code-created Task Manager controls after a successful AXAML reload.</summary>
    public static event Action? ResourcesReloaded;

    private static void SynchronizeResources(
        TaskManagerWindowResources currentResources,
        TaskManagerWindowResources candidateResources)
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
                    $"Task Manager AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}

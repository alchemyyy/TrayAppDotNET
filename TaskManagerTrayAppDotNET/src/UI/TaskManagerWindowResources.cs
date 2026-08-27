using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerWindowResources : ResourceDictionary
{
#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<TaskManagerWindowResources> Resources =
        AXAMLResourceHotReloadStore<TaskManagerWindowResources>.Create(
            "Task Manager window resources",
            static () => new TaskManagerWindowResources(),
            NotifyResourcesReloaded,
            "TaskManagerWindow.axaml",
            synchronizeReload: SynchronizeResources);
#else
    private static readonly Lazy<TaskManagerWindowResources> Resources =
        new(static () => new TaskManagerWindowResources());
#endif

    public static readonly Color ProcessGridBackgroundColor = Color.FromRgb(0x19, 0x19, 0x19);
    public static readonly Color ProcessColumnChooserDarkBackgroundColor = Color.FromRgb(0x16, 0x16, 0x16);
    public static readonly Color ProcessColumnChooserLightBackgroundColor = Color.FromRgb(0xE8, 0xE8, 0xE8);
    public static readonly Color ProcessGridScrollThumbColor = Color.FromRgb(0x8A, 0x8A, 0x8A);
    public static readonly Color ProcessGridScrollHoverThumbColor = Color.FromRgb(0xA6, 0xA6, 0xA6);
    public static readonly Color ProcessGridResizeGripColor = Color.FromRgb(0x8A, 0x8A, 0x8A);

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
#else
    public static event Action? ResourcesReloaded
    {
        add { }
        remove { }
    }
#endif
}

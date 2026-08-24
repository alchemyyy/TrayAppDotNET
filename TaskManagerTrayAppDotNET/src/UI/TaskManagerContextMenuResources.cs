using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides the app-local visual constants for Task Manager content context menus.</summary>
public sealed partial class TaskManagerContextMenuResources : ResourceDictionary
{
    private const string FontSizeKey = "TaskManagerContextMenu.FontSize";
    private const string FontWeightKey = "TaskManagerContextMenu.FontWeight";
    private const string ItemHeightKey = "TaskManagerContextMenu.ItemHeight";

#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<TaskManagerContextMenuResources> Resources =
        AXAMLResourceHotReloadStore<TaskManagerContextMenuResources>.Create(
            "Task Manager context menu resources",
            static () => new TaskManagerContextMenuResources(),
            static () => { },
            "TaskManagerContextMenuResources.axaml");
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

    internal double ItemFontSize => Get<double>(FontSizeKey);
    internal FontWeight ItemFontWeight => (FontWeight)Get<int>(FontWeightKey);
    internal double ItemHeight => Get<double>(ItemHeightKey);

    private T Get<T>(string key) =>
        this[key] is T value
            ? value
            : throw new InvalidOperationException(
                $"Task Manager context-menu resource '{key}' is missing or has the wrong type.");
}

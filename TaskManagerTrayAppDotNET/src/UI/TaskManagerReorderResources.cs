using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides app-local layout constants for Task Manager reorder lists.</summary>
public sealed partial class TaskManagerReorderResources : ResourceDictionary
{
    private const string DragThresholdKey = "TaskManagerReorder.DragThreshold";
    private const string RowMinHeightKey = "TaskManagerReorder.RowMinHeight";
    private const string RowHitSlotPaddingKey = "TaskManagerReorder.RowHitSlotPadding";
    private const string RowCornerRadiusKey = "TaskManagerReorder.RowCornerRadius";
    private const string RowHighlightPaddingKey = "TaskManagerReorder.RowHighlightPadding";
    private const string PrimaryContentMarginKey = "TaskManagerReorder.PrimaryContentMargin";
    private const string DraggingOpacityKey = "TaskManagerReorder.DraggingOpacity";
    private const string AutoScrollEdgeSizeKey = "TaskManagerReorder.AutoScrollEdgeSize";
    private const string AutoScrollStepKey = "TaskManagerReorder.AutoScrollStep";
    private const string AutoScrollIntervalMillisecondsKey =
        "TaskManagerReorder.AutoScrollIntervalMilliseconds";
    private const string NormalZIndexKey = "TaskManagerReorder.NormalZIndex";
    private const string DraggingZIndexKey = "TaskManagerReorder.DraggingZIndex";
    private const string ButtonSizeKey = "TaskManagerReorder.ButtonSize";
    private const string ButtonSpacingKey = "TaskManagerReorder.ButtonSpacing";
    private const string ButtonGlyphFontSizeKey = "TaskManagerReorder.ButtonGlyphFontSize";
    private const string ButtonPaddingKey = "TaskManagerReorder.ButtonPadding";

#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<TaskManagerReorderResources> Resources =
        AXAMLResourceHotReloadStore<TaskManagerReorderResources>.Create(
            "Task Manager reorder resources",
            static () => new TaskManagerReorderResources(),
            NotifyResourcesReloaded,
            "TaskManagerReorderResources.axaml");
#else
    private static readonly Lazy<TaskManagerReorderResources> Resources =
        new(static () => new TaskManagerReorderResources());
#endif

    /// <summary>Initializes the compiled Task Manager reorder resource dictionary.</summary>
    public TaskManagerReorderResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static TaskManagerReorderResources Current
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
    /// <summary>Notifies active reorder controls after an AXAML resource reload.</summary>
    internal static event Action? ResourcesReloaded;

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
                    $"Task Manager reorder AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }
#else
    internal static event Action? ResourcesReloaded
    {
        add { }
        remove { }
    }
#endif

    internal double DragThreshold => Get<double>(DragThresholdKey);
    internal double RowMinHeight => Get<double>(RowMinHeightKey);
    internal Thickness RowHitSlotPadding => Get<Thickness>(RowHitSlotPaddingKey);
    internal CornerRadius RowCornerRadius => Get<CornerRadius>(RowCornerRadiusKey);
    internal Thickness RowHighlightPadding => Get<Thickness>(RowHighlightPaddingKey);
    internal Thickness PrimaryContentMargin => Get<Thickness>(PrimaryContentMarginKey);
    internal double DraggingOpacity => Get<double>(DraggingOpacityKey);
    internal double AutoScrollEdgeSize => Get<double>(AutoScrollEdgeSizeKey);
    internal double AutoScrollStep => Get<double>(AutoScrollStepKey);
    internal int AutoScrollIntervalMilliseconds => Get<int>(AutoScrollIntervalMillisecondsKey);
    internal int NormalZIndex => Get<int>(NormalZIndexKey);
    internal int DraggingZIndex => Get<int>(DraggingZIndexKey);
    internal double ButtonSize => Get<double>(ButtonSizeKey);
    internal double ButtonSpacing => Get<double>(ButtonSpacingKey);
    internal double ButtonGlyphFontSize => Get<double>(ButtonGlyphFontSizeKey);
    internal Thickness ButtonPadding => Get<Thickness>(ButtonPaddingKey);

    private T Get<T>(string key) =>
        this[key] is T value
            ? value
            : throw new InvalidOperationException(
                $"Task Manager reorder resource '{key}' is missing or has the wrong type.");
}

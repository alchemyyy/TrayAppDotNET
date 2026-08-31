using Avalonia.Controls;
using Avalonia.Layout;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>One draggable entry in the Processes header-button arrangement dialog.</summary>
internal sealed class ProcessHeaderButtonArrangementItem(
    ProcessHeaderButtonKind kind,
    string label)
{
    public ProcessHeaderButtonKind Kind { get; } = kind;
    public string Label { get; } = label;
}

/// <summary>Configures the left-to-right order of Processes header buttons.</summary>
internal sealed class ProcessHeaderButtonArrangementWindow
    : TaskManagerReorderDialog<ProcessHeaderButtonArrangementItem>
{
    public ProcessHeaderButtonArrangementWindow(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<IReadOnlyList<ProcessHeaderButtonKind>> orderChanged)
        : this(
            BuildItems(
                (settings ?? throw new ArgumentNullException(nameof(settings))).ProcessHeaderButtonOrder),
            settings,
            palette,
            resources,
            orderChanged)
    {
    }

    private ProcessHeaderButtonArrangementWindow(
        List<ProcessHeaderButtonArrangementItem> items,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action<IReadOnlyList<ProcessHeaderButtonKind>> orderChanged)
        : base(
            title: "Arrange buttons",
            description: "Drag buttons or use the arrows. The top item is the leftmost header button.",
            items,
            static item => item.Label,
            (item, notifyItemChanged) => BuildLabel(item, palette),
            static () => BuildItems(ProcessHeaderButtonSettings.CreateDefault()),
            orderedItems => orderChanged(GetKinds(orderedItems)),
            palette,
            settings.EnableRoundedCorners,
            resources,
            palette.Background,
            resources.AxamlTaskManagerReorderDialog.HeaderWindowWidth,
            resources.AxamlTaskManagerReorderDialog.HeaderWindowHeight,
            resources.AxamlTaskManagerReorderDialog.HeaderWindowHeight,
            showSearch: false,
            string.Empty)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(orderChanged);
    }

#if DEBUG
    // AXAML hot-reload exception: The generic reorder dialog keeps its mutable unsaved rows in
    // private visual state, so rebuilding its chrome would discard the active edit
    /// <summary>Applies safe window dimensions while retaining the current unsaved button order.</summary>
    internal void ApplyAXAMLResources(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        Width = resources.AxamlTaskManagerReorderDialog.HeaderWindowWidth;
        MinWidth = Width;
        MaxWidth = Width;
        Height = resources.AxamlTaskManagerReorderDialog.HeaderWindowHeight;
        MinHeight = Height;
        MaxHeight = Height;
    }
#endif

    private static List<ProcessHeaderButtonArrangementItem> BuildItems(
        IEnumerable<ProcessHeaderButtonKind>? order)
    {
        List<ProcessHeaderButtonKind> normalized = ProcessHeaderButtonSettings.Normalize(order);
        List<ProcessHeaderButtonArrangementItem> items = new(normalized.Count);
        foreach (ProcessHeaderButtonKind kind in normalized)
            items.Add(new ProcessHeaderButtonArrangementItem(kind, GetLabel(kind)));
        return items;
    }

    private static IReadOnlyList<ProcessHeaderButtonKind> GetKinds(
        IReadOnlyList<ProcessHeaderButtonArrangementItem> items)
    {
        List<ProcessHeaderButtonKind> kinds = new(items.Count);
        foreach (ProcessHeaderButtonArrangementItem item in items)
            kinds.Add(item.Kind);
        return kinds;
    }

    private static Control BuildLabel(
        ProcessHeaderButtonArrangementItem item,
        SettingsPalette palette)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(item.Label, palette);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static string GetLabel(ProcessHeaderButtonKind kind) => kind switch
    {
        ProcessHeaderButtonKind.RunNewTask => "Run new task",
        ProcessHeaderButtonKind.Columns => "Columns",
        ProcessHeaderButtonKind.EndTask => "End task",
        ProcessHeaderButtonKind.RestartExplorer => "Restart explorer",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: "Unknown header button kind.")
    };
}

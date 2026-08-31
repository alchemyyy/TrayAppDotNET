namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies a reorderable button in the Processes page header.</summary>
public enum ProcessHeaderButtonKind
{
    RunNewTask,
    Columns,
    EndTask,
    RestartExplorer
}

/// <summary>Creates and normalizes the persisted Processes header-button order.</summary>
internal static class ProcessHeaderButtonSettings
{
    private static readonly ProcessHeaderButtonKind[] DefaultOrder =
    [
        ProcessHeaderButtonKind.RunNewTask,
        ProcessHeaderButtonKind.Columns,
        ProcessHeaderButtonKind.EndTask,
        ProcessHeaderButtonKind.RestartExplorer
    ];

    /// <summary>Returns a new list containing the default left-to-right button order.</summary>
    public static List<ProcessHeaderButtonKind> CreateDefault() => [.. DefaultOrder];

    /// <summary>Preserves valid unique entries and appends any missing buttons in default order.</summary>
    public static List<ProcessHeaderButtonKind> Normalize(IEnumerable<ProcessHeaderButtonKind>? source)
    {
        List<ProcessHeaderButtonKind> normalized = new(DefaultOrder.Length);
        HashSet<ProcessHeaderButtonKind> used = [];
        if (source != null)
        {
            foreach (ProcessHeaderButtonKind button in source)
            {
                if (!Enum.IsDefined(button) || !used.Add(button)) continue;
                normalized.Add(button);
            }
        }

        foreach (ProcessHeaderButtonKind button in DefaultOrder)
        {
            if (used.Add(button))
                normalized.Add(button);
        }

        return normalized;
    }
}

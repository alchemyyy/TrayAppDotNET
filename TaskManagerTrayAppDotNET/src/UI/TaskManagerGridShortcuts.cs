using Avalonia.Input;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Identifies the action a Task Manager table shortcut performs.</summary>
public enum TaskManagerGridShortcutAction : byte
{
    None,
    Zoom,
    Stretch,
    ResetZoom,
    ResetStretch,
    SetZoomBaseline,
    SetStretchBaseline,
    EndTask
}

/// <summary>Identifies the input that triggers a Task Manager table shortcut.</summary>
internal enum TaskManagerGridShortcutTrigger : byte
{
    MouseWheel,
    MiddleClick,
    Key
}

/// <summary>One registered table shortcut: its trigger, required modifiers, and action.</summary>
internal readonly record struct TaskManagerGridShortcut(
    TaskManagerGridShortcutAction Action,
    TaskManagerGridShortcutTrigger Trigger,
    KeyModifiers Modifiers,
    Key Key = Key.None);

/// <summary>
/// Registry of the Task Manager table shortcuts.
/// Input dispatch and the Hotkeys settings page both read it, so settings list exactly what the tables obey.
/// </summary>
internal static class TaskManagerGridShortcuts
{
    private const string ModifierSeparator = " + ";
    private const string ControlText = "Ctrl";
    private const string ShiftText = "Shift";
    private const string AltText = "Alt";
    private const string MouseWheelText = "Mouse wheel";
    private const string MiddleClickText = "Middle click";

    // A shortcut matches while its modifiers are held, even alongside others
    // The first match wins, so entries requiring more modifiers precede those requiring a subset
    private static readonly TaskManagerGridShortcut[] Shortcuts =
    [
        new(
            TaskManagerGridShortcutAction.SetStretchBaseline,
            TaskManagerGridShortcutTrigger.MiddleClick,
            KeyModifiers.Shift | KeyModifiers.Alt),
        new(
            TaskManagerGridShortcutAction.SetZoomBaseline,
            TaskManagerGridShortcutTrigger.MiddleClick,
            KeyModifiers.Control | KeyModifiers.Alt),
        new(TaskManagerGridShortcutAction.ResetStretch, TaskManagerGridShortcutTrigger.MiddleClick, KeyModifiers.Shift),
        new(TaskManagerGridShortcutAction.ResetZoom, TaskManagerGridShortcutTrigger.MiddleClick, KeyModifiers.Control),
        new(
            TaskManagerGridShortcutAction.ResetStretch,
            TaskManagerGridShortcutTrigger.MouseWheel,
            KeyModifiers.Shift | KeyModifiers.Alt),
        new(TaskManagerGridShortcutAction.Stretch, TaskManagerGridShortcutTrigger.MouseWheel, KeyModifiers.Shift),
        new(
            TaskManagerGridShortcutAction.ResetZoom,
            TaskManagerGridShortcutTrigger.MouseWheel,
            KeyModifiers.Control | KeyModifiers.Alt),
        new(TaskManagerGridShortcutAction.Zoom, TaskManagerGridShortcutTrigger.MouseWheel, KeyModifiers.Control),
        new(TaskManagerGridShortcutAction.EndTask, TaskManagerGridShortcutTrigger.Key, KeyModifiers.None, Key.Delete)
    ];

    /// <summary>Gets every registered shortcut in match order.</summary>
    public static IReadOnlyList<TaskManagerGridShortcut> All => Shortcuts;

    /// <summary>Resolves a pointer trigger and its held modifiers to the first matching action.</summary>
    public static TaskManagerGridShortcutAction Resolve(
        TaskManagerGridShortcutTrigger trigger,
        KeyModifiers modifiers) =>
        Resolve(trigger, Key.None, modifiers);

    /// <summary>Resolves a key press and its held modifiers to the first matching action.</summary>
    public static TaskManagerGridShortcutAction ResolveKey(Key key, KeyModifiers modifiers) =>
        key == Key.None
            ? TaskManagerGridShortcutAction.None
            : Resolve(TaskManagerGridShortcutTrigger.Key, key, modifiers);

    /// <summary>Returns whether an action changes or resets zoom state, which header interactions block.</summary>
    public static bool IsZoomReset(TaskManagerGridShortcutAction action) => action is
        TaskManagerGridShortcutAction.ResetZoom
        or TaskManagerGridShortcutAction.ResetStretch
        or TaskManagerGridShortcutAction.SetZoomBaseline
        or TaskManagerGridShortcutAction.SetStretchBaseline;

    /// <summary>Formats a shortcut as it appears in settings, for example "Ctrl + Alt + Middle click".</summary>
    public static string FormatGesture(TaskManagerGridShortcut shortcut)
    {
        List<string> parts = [];
        if ((shortcut.Modifiers & KeyModifiers.Control) != 0) parts.Add(ControlText);
        if ((shortcut.Modifiers & KeyModifiers.Shift) != 0) parts.Add(ShiftText);
        if ((shortcut.Modifiers & KeyModifiers.Alt) != 0) parts.Add(AltText);
        parts.Add(shortcut.Trigger switch
        {
            TaskManagerGridShortcutTrigger.MouseWheel => MouseWheelText,
            TaskManagerGridShortcutTrigger.MiddleClick => MiddleClickText,
            _ => shortcut.Key.ToString()
        });
        return string.Join(ModifierSeparator, parts);
    }

    /// <summary>Returns the settings title for an action.</summary>
    public static string GetTitle(TaskManagerGridShortcutAction action) => action switch
    {
        TaskManagerGridShortcutAction.Zoom => "Zoom",
        TaskManagerGridShortcutAction.Stretch => "Stretch",
        TaskManagerGridShortcutAction.ResetZoom => "Reset zoom",
        TaskManagerGridShortcutAction.ResetStretch => "Reset stretch",
        TaskManagerGridShortcutAction.SetZoomBaseline => "Set baseline zoom",
        TaskManagerGridShortcutAction.SetStretchBaseline => "Set baseline stretch",
        TaskManagerGridShortcutAction.EndTask => "End task",
        _ => action.ToString()
    };

    /// <summary>Returns the settings description for an action.</summary>
    public static string GetDescription(TaskManagerGridShortcutAction action) => action switch
    {
        TaskManagerGridShortcutAction.Zoom => "Scroll up to enlarge table text and down to shrink it.",
        TaskManagerGridShortcutAction.Stretch => "Scroll up to add space between table rows and down to remove it.",
        TaskManagerGridShortcutAction.ResetZoom => "Return table text to the baseline zoom.",
        TaskManagerGridShortcutAction.ResetStretch => "Return table row spacing to the baseline stretch.",
        TaskManagerGridShortcutAction.SetZoomBaseline => "Make the current text size the zoom that resets return to.",
        TaskManagerGridShortcutAction.SetStretchBaseline =>
            "Make the current row spacing the stretch that resets return to.",
        TaskManagerGridShortcutAction.EndTask => "End the selected processes on the Processes page.",
        _ => string.Empty
    };

    private static TaskManagerGridShortcutAction Resolve(
        TaskManagerGridShortcutTrigger trigger,
        Key key,
        KeyModifiers modifiers)
    {
        foreach (TaskManagerGridShortcut shortcut in Shortcuts)
        {
            if (shortcut.Trigger != trigger
                || shortcut.Key != key
                || (modifiers & shortcut.Modifiers) != shortcut.Modifiers)
                continue;

            return shortcut.Action;
        }

        return TaskManagerGridShortcutAction.None;
    }
}

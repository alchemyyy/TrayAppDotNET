using System.Numerics;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.ControlMapping;

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

/// <summary>One table shortcut action and the control map leaf whose gestures perform it.</summary>
internal readonly record struct TaskManagerGridShortcutEntry(
    TaskManagerGridShortcutAction Action,
    ControlMapNodeID Node);

/// <summary>One gesture read from a table shortcut's map leaf: its trigger, required modifiers, and key.</summary>
internal readonly record struct TaskManagerGridShortcut(
    TaskManagerGridShortcutAction Action,
    ControlMapNodeID Node,
    TaskManagerGridShortcutTrigger Trigger,
    KeyModifiers Modifiers,
    Key Key = Key.None);

/// <summary>
/// Task Manager table shortcuts as the control map declares them. Each action names its map leaf, whose Pointer and
/// Keys attributes supply the gestures, so table pointer dispatch and the Hotkeys settings page read one source.
/// Key gestures are dispatched by the control map runtime through the tables' MapCommand handlers.
/// </summary>
internal static class TaskManagerGridShortcuts
{
    private const string ModifierSeparator = " + ";
    private const string ControlText = "Ctrl";
    private const string ShiftText = "Shift";
    private const string AltText = "Alt";
    private const string MouseWheelText = "Mouse wheel";
    private const string MiddleClickText = "Middle click";

    // Hotkeys page order; declared before Shortcuts, whose initializer reads it
    private static readonly TaskManagerGridShortcutEntry[] EntryList =
    [
        new(TaskManagerGridShortcutAction.Zoom, ControlMap.TaskManagerGrid.Zoom),
        new(TaskManagerGridShortcutAction.Stretch, ControlMap.TaskManagerGrid.Stretch),
        new(TaskManagerGridShortcutAction.ResetZoom, ControlMap.TaskManagerGrid.ResetZoom),
        new(TaskManagerGridShortcutAction.ResetStretch, ControlMap.TaskManagerGrid.ResetStretch),
        new(TaskManagerGridShortcutAction.SetZoomBaseline, ControlMap.TaskManagerGrid.SetZoomBaseline),
        new(TaskManagerGridShortcutAction.SetStretchBaseline, ControlMap.TaskManagerGrid.SetStretchBaseline),
        new(TaskManagerGridShortcutAction.EndTask, ControlMap.Main.ProcessesPage.TableViewport.Table.EndTask)
    ];

    private static readonly TaskManagerGridShortcut[] Shortcuts = ReadShortcuts();

    /// <summary>Gets every shortcut action with its map leaf, in Hotkeys page order.</summary>
    public static IReadOnlyList<TaskManagerGridShortcutEntry> Entries => EntryList;

    /// <summary>Gets every gesture the map leaves declare, grouped by action in Hotkeys page order.</summary>
    public static IReadOnlyList<TaskManagerGridShortcut> All => Shortcuts;

    /// <summary>
    /// Resolves a pointer trigger to the action whose gesture requires the most of the held modifiers; extra held
    /// modifiers do not prevent a match. Equally specific matches for different actions are ambiguous and resolve to
    /// no action, so the input falls through to the table's container.
    /// </summary>
    public static TaskManagerGridShortcutAction Resolve(
        TaskManagerGridShortcutTrigger trigger,
        KeyModifiers modifiers)
    {
        if (trigger == TaskManagerGridShortcutTrigger.Key)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trigger),
                trigger,
                message: "Key gestures are dispatched by the control map runtime.");
        }

        TaskManagerGridShortcutAction resolved = TaskManagerGridShortcutAction.None;
        int resolvedModifierCount = -1;
        bool isAmbiguous = false;
        foreach (TaskManagerGridShortcut shortcut in Shortcuts)
        {
            if (shortcut.Trigger != trigger || (modifiers & shortcut.Modifiers) != shortcut.Modifiers) continue;

            int modifierCount = BitOperations.PopCount((uint)shortcut.Modifiers);
            if (modifierCount < resolvedModifierCount) continue;
            if (modifierCount == resolvedModifierCount)
            {
                isAmbiguous |= shortcut.Action != resolved;
                continue;
            }

            resolved = shortcut.Action;
            resolvedModifierCount = modifierCount;
            isAmbiguous = false;
        }

        return isAmbiguous ? TaskManagerGridShortcutAction.None : resolved;
    }

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

    // A leaf the generated ids name but the compiled map lacks means the build is inconsistent, so fail fast
    private static TaskManagerGridShortcut[] ReadShortcuts()
    {
        List<TaskManagerGridShortcut> shortcuts = [];
        foreach (TaskManagerGridShortcutEntry entry in EntryList)
        {
            if (ControlMapCatalog.Find(entry.Node) is not Leaf leaf)
                throw new InvalidOperationException($"Control map node {entry.Node} is not a registered leaf.");

            foreach (PointerGesture gesture in leaf.PointerGestures)
            {
                // Tables dispatch only wheel and middle-click gestures; other pointer kinds stay documentation
                TaskManagerGridShortcutTrigger? trigger = gesture.Kind switch
                {
                    PointerGestureKind.Wheel => TaskManagerGridShortcutTrigger.MouseWheel,
                    PointerGestureKind.MiddleClick => TaskManagerGridShortcutTrigger.MiddleClick,
                    _ => null
                };
                if (trigger is not { } pointerTrigger) continue;

                shortcuts.Add(new TaskManagerGridShortcut(entry.Action, entry.Node, pointerTrigger, gesture.Modifiers));
            }

            foreach (KeyGesture gesture in leaf.KeyGestures)
            {
                shortcuts.Add(new TaskManagerGridShortcut(
                    entry.Action,
                    entry.Node,
                    TaskManagerGridShortcutTrigger.Key,
                    gesture.KeyModifiers,
                    gesture.Key));
            }
        }

        return [.. shortcuts];
    }
}

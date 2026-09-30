using Avalonia.Input;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Identifies the action a Task Manager table shortcut performs.</summary>
public enum DetailsGridShortcutAction : byte
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
internal enum DetailsGridShortcutTrigger : byte
{
    MouseWheel,
    MiddleClick,
    Key
}

/// <summary>One registered table shortcut: its trigger, required modifiers, and action.</summary>
internal readonly record struct DetailsGridShortcut(
    DetailsGridShortcutAction Action,
    DetailsGridShortcutTrigger Trigger,
    KeyModifiers Modifiers,
    Key Key = Key.None);

/// <summary>
/// Registry of the Task Manager table shortcuts.
/// Input dispatch and the Hotkeys settings page both read it, so settings list exactly what the tables obey.
/// </summary>
internal static class DetailsGridShortcuts
{
    private const string ModifierSeparator = " + ";
    private const string ControlText = "Ctrl";
    private const string ShiftText = "Shift";
    private const string AltText = "Alt";
    private const string MouseWheelText = "Mouse wheel";
    private const string MiddleClickText = "Middle click";

    // A shortcut matches while its modifiers are held, even alongside others
    // The first match wins, so entries requiring more modifiers precede those requiring a subset
    private static readonly DetailsGridShortcut[] Shortcuts =
    [
        new(
            DetailsGridShortcutAction.SetStretchBaseline,
            DetailsGridShortcutTrigger.MiddleClick,
            KeyModifiers.Shift | KeyModifiers.Alt),
        new(
            DetailsGridShortcutAction.SetZoomBaseline,
            DetailsGridShortcutTrigger.MiddleClick,
            KeyModifiers.Control | KeyModifiers.Alt),
        new(DetailsGridShortcutAction.ResetStretch, DetailsGridShortcutTrigger.MiddleClick, KeyModifiers.Shift),
        new(DetailsGridShortcutAction.ResetZoom, DetailsGridShortcutTrigger.MiddleClick, KeyModifiers.Control),
        new(
            DetailsGridShortcutAction.ResetStretch,
            DetailsGridShortcutTrigger.MouseWheel,
            KeyModifiers.Shift | KeyModifiers.Alt),
        new(DetailsGridShortcutAction.Stretch, DetailsGridShortcutTrigger.MouseWheel, KeyModifiers.Shift),
        new(
            DetailsGridShortcutAction.ResetZoom,
            DetailsGridShortcutTrigger.MouseWheel,
            KeyModifiers.Control | KeyModifiers.Alt),
        new(DetailsGridShortcutAction.Zoom, DetailsGridShortcutTrigger.MouseWheel, KeyModifiers.Control),
        new(DetailsGridShortcutAction.EndTask, DetailsGridShortcutTrigger.Key, KeyModifiers.None, Key.Delete)
    ];

    /// <summary>Gets every registered shortcut in match order.</summary>
    public static IReadOnlyList<DetailsGridShortcut> All => Shortcuts;

    /// <summary>Resolves a pointer trigger and its held modifiers to the first matching action.</summary>
    public static DetailsGridShortcutAction Resolve(DetailsGridShortcutTrigger trigger, KeyModifiers modifiers) =>
        Resolve(trigger, Key.None, modifiers);

    /// <summary>Resolves a key press and its held modifiers to the first matching action.</summary>
    public static DetailsGridShortcutAction ResolveKey(Key key, KeyModifiers modifiers) =>
        key == Key.None
            ? DetailsGridShortcutAction.None
            : Resolve(DetailsGridShortcutTrigger.Key, key, modifiers);

    /// <summary>Returns whether an action changes or resets zoom state, which header interactions block.</summary>
    public static bool IsZoomReset(DetailsGridShortcutAction action) => action is
        DetailsGridShortcutAction.ResetZoom
        or DetailsGridShortcutAction.ResetStretch
        or DetailsGridShortcutAction.SetZoomBaseline
        or DetailsGridShortcutAction.SetStretchBaseline;

    /// <summary>Formats a shortcut as it appears in settings, for example "Ctrl + Alt + Middle click".</summary>
    public static string FormatGesture(DetailsGridShortcut shortcut)
    {
        List<string> parts = [];
        if ((shortcut.Modifiers & KeyModifiers.Control) != 0) parts.Add(ControlText);
        if ((shortcut.Modifiers & KeyModifiers.Shift) != 0) parts.Add(ShiftText);
        if ((shortcut.Modifiers & KeyModifiers.Alt) != 0) parts.Add(AltText);
        parts.Add(shortcut.Trigger switch
        {
            DetailsGridShortcutTrigger.MouseWheel => MouseWheelText,
            DetailsGridShortcutTrigger.MiddleClick => MiddleClickText,
            _ => shortcut.Key.ToString()
        });
        return string.Join(ModifierSeparator, parts);
    }

    /// <summary>Returns the settings title for an action.</summary>
    public static string GetTitle(DetailsGridShortcutAction action) => action switch
    {
        DetailsGridShortcutAction.Zoom => "Zoom",
        DetailsGridShortcutAction.Stretch => "Stretch",
        DetailsGridShortcutAction.ResetZoom => "Reset zoom",
        DetailsGridShortcutAction.ResetStretch => "Reset stretch",
        DetailsGridShortcutAction.SetZoomBaseline => "Set baseline zoom",
        DetailsGridShortcutAction.SetStretchBaseline => "Set baseline stretch",
        DetailsGridShortcutAction.EndTask => "End task",
        _ => action.ToString()
    };

    /// <summary>Returns the settings description for an action.</summary>
    public static string GetDescription(DetailsGridShortcutAction action) => action switch
    {
        DetailsGridShortcutAction.Zoom => "Scroll up to enlarge table text and down to shrink it.",
        DetailsGridShortcutAction.Stretch => "Scroll up to add space between table rows and down to remove it.",
        DetailsGridShortcutAction.ResetZoom => "Return table text to the baseline zoom.",
        DetailsGridShortcutAction.ResetStretch => "Return table row spacing to the baseline stretch.",
        DetailsGridShortcutAction.SetZoomBaseline => "Make the current text size the zoom that resets return to.",
        DetailsGridShortcutAction.SetStretchBaseline =>
            "Make the current row spacing the stretch that resets return to.",
        DetailsGridShortcutAction.EndTask => "End the selected processes on the Processes page.",
        _ => string.Empty
    };

    private static DetailsGridShortcutAction Resolve(
        DetailsGridShortcutTrigger trigger,
        Key key,
        KeyModifiers modifiers)
    {
        foreach (DetailsGridShortcut shortcut in Shortcuts)
        {
            if (shortcut.Trigger != trigger
                || shortcut.Key != key
                || (modifiers & shortcut.Modifiers) != shortcut.Modifiers)
                continue;

            return shortcut.Action;
        }

        return DetailsGridShortcutAction.None;
    }
}

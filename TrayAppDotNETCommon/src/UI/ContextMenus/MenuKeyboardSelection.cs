using Avalonia.Controls;
using Avalonia.Input;

namespace TrayAppDotNETCommon.UI.ContextMenus;

/// <summary>A key that moves the keyboard selection of a menu.</summary>
public enum MenuSelectionMove
{
    Previous,
    Next,
    First,
    Last
}

/// <summary>
/// Keyboard selection shared by the menu windows, following Win32 popup menus: Up and Down wrap around the ends, and
/// while no row is selected Down and Home select the first row and Up and End select the last. The row holding
/// keyboard focus is the selected row.
/// </summary>
public static class MenuKeyboardSelection
{
    /// <summary>Returns the index of the row a move selects, or -1 when the menu has no selectable row.</summary>
    /// <param name="rowCount">Number of selectable rows</param>
    /// <param name="currentIndex">Index of the selected row, or -1 while no row is selected</param>
    /// <param name="move">The selection key</param>
    public static int ResolveTargetIndex(int rowCount, int currentIndex, MenuSelectionMove move)
    {
        if (rowCount <= 0) return -1;

        bool hasSelection = currentIndex >= 0 && currentIndex < rowCount;
        return move switch
        {
            MenuSelectionMove.First => 0,
            MenuSelectionMove.Last => rowCount - 1,
            MenuSelectionMove.Next => hasSelection ? (currentIndex + 1) % rowCount : 0,
            MenuSelectionMove.Previous => hasSelection ? (currentIndex - 1 + rowCount) % rowCount : rowCount - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(move), move, message: null)
        };
    }

    /// <summary>
    /// Focuses the row a move selects among the visible, enabled, focusable rows and scrolls it into view. Returns the
    /// selected row, or null when no row can take the selection.
    /// </summary>
    public static Control? Move(IEnumerable<Control> rows, MenuSelectionMove move)
    {
        List<Control> selectableRows = [];
        foreach (Control row in rows)
        {
            if (row.Focusable && row.IsEffectivelyVisible && row.IsEffectivelyEnabled)
                selectableRows.Add(row);
        }

        int currentIndex = selectableRows.FindIndex(static row => row.IsKeyboardFocusWithin);
        int targetIndex = ResolveTargetIndex(selectableRows.Count, currentIndex, move);
        if (targetIndex < 0) return null;

        Control target = selectableRows[targetIndex];
        target.Focus(NavigationMethod.Directional);
        target.BringIntoView();
        return target;
    }

    /// <summary>Returns whether focus arrived from the keyboard, which shows the selection highlight.</summary>
    public static bool IsKeyboardNavigation(NavigationMethod navigationMethod) =>
        navigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
}

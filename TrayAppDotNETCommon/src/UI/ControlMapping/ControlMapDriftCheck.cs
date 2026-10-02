#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>
/// Debug-only drift report: interactive controls a window shows without a control map node. Map nodes that no code
/// tags are reported at build time by the control map analyzer instead.
/// </summary>
public static class ControlMapDriftCheck
{
    private static readonly HashSet<string> ReportedMessages = new(StringComparer.Ordinal);
    private static readonly HashSet<TopLevel> PendingWindows = [];

    /// <summary>
    /// Returns the interactive controls under a window that carry no map node and sit inside no mapped leaf.
    /// A control counts as interactive when it is a visible, enabled tab stop or sets the hand cursor itself.
    /// </summary>
    public static List<Control> FindUnmappedControls(TopLevel topLevel)
    {
        List<Control> unmapped = [];
        Stack<Visual> pending = new();
        pending.Push(topLevel);
        while (pending.Count > 0)
        {
            Visual visual = pending.Pop();
            if (visual is Control control && !ReferenceEquals(control, topLevel))
            {
                ExpandedNode? node = ControlMapNavigator.NodeOf(control);

                // A mapped leaf owns everything inside it, such as the text box of a number box
                if (node?.Node is Leaf) continue;

                if (node == null && IsInteractive(control))
                {
                    unmapped.Add(control);
                    continue;
                }
            }

            foreach (Visual child in visual.GetVisualChildren())
                pending.Push(child);
        }

        return unmapped;
    }

    // Bindings arrive in bursts while a window builds; one check runs after the burst settles
    internal static void Schedule(TopLevel topLevel)
    {
        if (!PendingWindows.Add(topLevel)) return;

        Dispatcher.UIThread.Post(() =>
        {
            PendingWindows.Remove(topLevel);
            foreach (Control control in FindUnmappedControls(topLevel))
                ReportOnce($"Control map: unmapped {Describe(control)} in {topLevel.GetType().Name}");
        }, DispatcherPriority.Background);
    }

    internal static void ReportOnce(string message)
    {
        if (ReportedMessages.Add(message))
            TADNLog.LogDebug(message);
    }

    private static bool IsInteractive(Control control)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return false;

        bool isTabStop = control.Focusable && KeyboardNavigation.GetIsTabStop(control);

        // Cursor inherits, so only the control that sets the hand cursor is the clickable one
        return isTabStop
               || control.IsSet(InputElement.CursorProperty)
               && ReferenceEquals(control.Cursor, TrayAppDotNETCursors.Hand);
    }

    // Type, debug name, and the nearest mapped ancestor locate the control in source
    private static string Describe(Control control)
    {
        string description = string.IsNullOrEmpty(control.Name)
            ? control.GetType().Name
            : control.GetType().Name + " '" + control.Name + "'";
        for (Visual? current = ControlMapNavigator.ParentOf(control);
             current != null;
             current = ControlMapNavigator.ParentOf(current))
        {
            if (current is Control ancestor && ControlMapNavigator.NodeOf(ancestor) is { } ancestorNode)
                return description + " under " + ancestorNode.Node.NodeID;
        }

        return description;
    }
}
#endif

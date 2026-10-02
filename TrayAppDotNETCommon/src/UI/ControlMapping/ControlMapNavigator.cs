using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>
/// Applies the control map to one top-level window: resolves tagged controls to their expanded nodes, derives tab
/// order and focus behavior from the map, and dispatches the map's keys.
/// </summary>
internal sealed class ControlMapNavigator
{
    private static readonly ConditionalWeakTable<TopLevel, ControlMapNavigator> Navigators = new();
    private static readonly ConditionalWeakTable<Control, BoundControl> Bindings = new();

    private readonly TopLevel _topLevel;
    private readonly Dictionary<ExpandedNode, List<Control>> _boundControls = [];

    private ControlMapNavigator(TopLevel topLevel)
    {
        _topLevel = topLevel;

        // Bubbling and unhandled only, so the focused control's own keys win, as FocusKeys require
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>Returns the navigator of a top-level window, creating it on first use.</summary>
    public static ControlMapNavigator For(TopLevel topLevel) =>
        Navigators.GetValue(topLevel, static value => new ControlMapNavigator(value));

    /// <summary>Returns the expanded node a bound control resolved to, or null.</summary>
    public static ExpandedNode? NodeOf(Control control) =>
        Bindings.TryGetValue(control, out BoundControl? bound) ? bound.Node : null;

    /// <summary>Resolves a tagged control and applies the navigation its node describes.</summary>
    public void Bind(Control control)
    {
        Unbind(control);
        ExpandedNode? node = Resolve(control);
        if (node == null) return;

        Bindings.AddOrUpdate(control, new BoundControl(node, this));
        if (!_boundControls.TryGetValue(node, out List<Control>? controls))
        {
            controls = [];
            _boundControls.Add(node, controls);
        }

        controls.Add(control);
        Apply(control, node);
#if DEBUG
        ControlMapDriftCheck.Schedule(_topLevel);
#endif
    }

    /// <summary>
    /// Binds a control and then every tagged control below it. Tagging an attached container changes the instance
    /// its descendants resolve against, so they bind again.
    /// </summary>
    public void BindSubtree(Control root)
    {
        Bind(root);
        foreach (Visual descendant in root.GetVisualDescendants())
        {
            if (descendant is Control control && ControlMapBinding.GetNode(control) != null)
                Bind(control);
        }
    }

    /// <summary>Forgets a control that left its window.</summary>
    public static void Unbind(Control control)
    {
        if (!Bindings.TryGetValue(control, out BoundControl? bound)) return;

        Bindings.Remove(control);
        if (bound.Navigator._boundControls.TryGetValue(bound.Node, out List<Control>? controls))
            controls.Remove(control);
    }

    /// <summary>Applies the map again to a control and every bound control below it, after its variants changed.</summary>
    public void Reapply(Control root)
    {
        foreach (KeyValuePair<ExpandedNode, List<Control>> entry in _boundControls)
        {
            foreach (Control control in entry.Value)
            {
                if (ReferenceEquals(control, root) || root.IsVisualAncestorOf(control))
                    Apply(control, entry.Key);
            }
        }
    }

    /// <summary>Returns the active layout variants of a control: those set on it and on every ancestor.</summary>
    public static HashSet<string> ActiveVariants(Visual visual)
    {
        HashSet<string> variants = new(StringComparer.Ordinal);
        for (Visual? current = visual; current != null; current = ParentOf(current))
        {
            if (current is not Control control || control.GetValue(ControlMapBinding.VariantsProperty) is not
                { } declared) continue;

            foreach (string variant in declared)
                variants.Add(variant);
        }

        return variants;
    }

    /// <summary>
    /// Returns the next element up the tree. A popup's root continues to the control that owns the popup, so
    /// controls inside popups resolve against the window that opened them.
    /// </summary>
    public static Visual? ParentOf(Visual visual) =>
        visual.GetVisualParent() ?? (visual is TopLevel topLevel ? topLevel.Parent as Visual : null);

    // Tagged ancestors resolve first, outermost down, so each one's instance narrows the next
    private static ExpandedNode? Resolve(Control control)
    {
        List<Control> chain = [];
        for (Visual? current = control; current != null; current = ParentOf(current))
        {
            if (current is Control tagged && ControlMapBinding.GetNode(tagged) != null)
                chain.Add(tagged);
        }

        ExpandedNode? context = null;
        for (int chainIndex = chain.Count - 1; chainIndex > 0; chainIndex--)
        {
            ExpandedNode? ancestorNode = NodeOf(chain[chainIndex]) ?? ResolveOne(chain[chainIndex], context);
            if (ancestorNode != null)
                context = ancestorNode;
        }

        return ResolveOne(control, context);
    }

    private static ExpandedNode? ResolveOne(Control control, ExpandedNode? context)
    {
        ControlMapNodeID id = ControlMapBinding.GetNode(control)!.Value;
        ControlMapNode? node = ControlMapCatalog.Find(id);
        if (node == null)
        {
            ReportResolution(control, id, "names no node in a registered control map");
            return null;
        }

        ControlMapForest forest = ControlMapCatalog.Forest;
        IReadOnlyList<ExpandedNode> candidates = node is Template template
            ? forest.InstancesOf(template)
            : forest.Occurrences(node);
        switch (candidates.Count)
        {
            case 0:
                ReportResolution(control, id, "is not used by any surface of the registered maps");
                return null;
            case 1:
                return candidates[0];
        }

        if (context != null)
        {
            foreach (ExpandedNode candidate in candidates)
            {
                if (candidate.IsWithin(context))
                    return candidate;
            }
        }

        ReportResolution(control, id,
            "has several instances; tag the instance container with its instance id so the node can be told apart");
        return null;
    }

    private static void Apply(Control control, ExpandedNode node)
    {
        switch (node.Node)
        {
            case Surface surface:
                KeyboardNavigation.SetTabNavigation(control, surface.Tab);
                break;
            case Scope scope:
                KeyboardNavigation.SetTabNavigation(control, scope.Tab);
                if (scope.IsFocusable)
                    MakeTabStop(control);
                else
                    control.ClearValue(KeyboardNavigation.IsTabStopProperty);

                ApplyTabIndex(control, node);
                break;
            case Leaf leaf:
                ApplyLeaf(control, node, leaf);
                break;
        }
    }

    private static void ApplyLeaf(Control control, ExpandedNode node, Leaf leaf)
    {
        if (leaf.Kind is LeafKind.Region or LeafKind.Handle or LeafKind.Command || !leaf.IsTabStop)
        {
            KeyboardNavigation.SetIsTabStop(control, false);
            return;
        }

        // A text or number control that wraps a text box is one stop: the box takes focus, the wrapper orders it
        if (leaf.Kind is LeafKind.Text or LeafKind.Number && control is not TextBox && FindTextBox(control) is { } input)
        {
            KeyboardNavigation.SetTabNavigation(control, KeyboardNavigationMode.Local);
            KeyboardNavigation.SetIsTabStop(control, false);
            KeyboardNavigation.SetIsTabStop(input, true);
            ApplyTabIndex(control, node);
            return;
        }

        MakeTabStop(control);
        ApplyTabIndex(control, node);
    }

    // The tab index is the node's preorder position inside the nearest bound group, under the active variants
    private static void ApplyTabIndex(Control control, ExpandedNode node)
    {
        for (Visual? current = ParentOf(control); current != null; current = ParentOf(current))
        {
            if (current is not Control groupControl || NodeOf(groupControl) is not { } group) continue;
            if (group.Node is not (Surface or Scope) || !node.IsWithin(group)) continue;
            if (group.Node is Scope { Tab: KeyboardNavigationMode.Continue }) continue;

            int offset = ControlMapForest.OffsetWithin(group, node, ActiveVariants(control));
            if (offset < 0)
            {
                // Absent from the active layout variant
                KeyboardNavigation.SetIsTabStop(control, false);
                return;
            }

            KeyboardNavigation.SetTabIndex(control, offset);
            return;
        }
    }

    private static void MakeTabStop(Control control)
    {
        control.Focusable = true;
        KeyboardNavigation.SetIsTabStop(control, true);

        // Templated controls draw their own focus state through the theme
        if (control is TemplatedControl || control.FocusAdorner != null) return;

        control.FocusAdorner = new FuncTemplate<Control>(() => CreateFocusAdorner(control));
    }

    private static Border CreateFocusAdorner(Control adorned)
    {
        ControlMapFocusVisualResources resources = ControlMapFocusVisualResources.Current;
        double outset = resources.AxamlFocusVisual.Outset;
        Thickness primaryThickness = resources.AxamlFocusVisual.PrimaryThickness;
        CornerRadius outerRadius = adorned is Border border ? Inflate(border.CornerRadius, outset) : default;
        CornerRadius innerRadius = Inflate(outerRadius, -primaryThickness.Left);
        Border secondaryRing = new()
        {
            BorderThickness = resources.AxamlFocusVisual.SecondaryThickness,
            BorderBrush = new SolidColorBrush(resources.AxamlFocusVisual.SecondaryColor),
            CornerRadius = innerRadius,
            IsHitTestVisible = false
        };

        return new Border
        {
            Margin = new Thickness(-outset),
            BorderThickness = primaryThickness,
            BorderBrush = new SolidColorBrush(resources.AxamlFocusVisual.PrimaryColor),
            CornerRadius = outerRadius,
            IsHitTestVisible = false,
            Child = secondaryRing
        };
    }

    private static CornerRadius Inflate(CornerRadius radius, double amount) =>
        new(
            Math.Max(val1: 0, radius.TopLeft + amount),
            Math.Max(val1: 0, radius.TopRight + amount),
            Math.Max(val1: 0, radius.BottomRight + amount),
            Math.Max(val1: 0, radius.BottomLeft + amount));

    private static TextBox? FindTextBox(Control control)
    {
        foreach (Visual descendant in control.GetVisualDescendants())
        {
            if (descendant is TextBox textBox)
                return textBox;
        }

        return null;
    }

    private static Control FocusTarget(Control control) =>
        KeyboardNavigation.GetIsTabStop(control) || FindTextBox(control) is not { } input ? control : input;

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Handled) return;

        Visual origin = eventArgs.Source as Visual ?? _topLevel;
        List<(Control Control, ExpandedNode Node)> chain = BoundChain(origin);
        bool isHandled = TryActivateFocused(eventArgs, origin, chain)
                         || TryDrill(eventArgs, origin, chain)
                         || TryArrows(eventArgs, origin, chain)
                         || TryAccelerators(eventArgs, origin, chain);
        if (isHandled)
            eventArgs.Handled = true;
    }

    // Innermost first: the bound controls from the focused element up to the window
    private static List<(Control Control, ExpandedNode Node)> BoundChain(Visual origin)
    {
        List<(Control Control, ExpandedNode Node)> chain = [];
        for (Visual? current = origin; current != null; current = ParentOf(current))
        {
            if (current is Control control && NodeOf(control) is { } node)
                chain.Add((control, node));
        }

        return chain;
    }

    // Enter and Space activate a focused clickable leaf that does not handle them itself
    private static bool TryActivateFocused(
        KeyEventArgs eventArgs,
        Visual origin,
        List<(Control Control, ExpandedNode Node)> chain)
    {
        if (chain.Count == 0 || !ReferenceEquals(chain[0].Control, origin)) return false;
        if (eventArgs.Key is not (Key.Enter or Key.Space)) return false;
        if (chain[0].Node.Node is not Leaf
            {
                Kind: LeafKind.Button or LeafKind.Toggle or LeafKind.Option or LeafKind.Navigation or LeafKind.ListItem
            }) return false;
        if (ControlMapBinding.GetActivation(chain[0].Control) is not { } activate) return false;

        activate(new ControlActivation(chain[0].Control, eventArgs.KeyModifiers));
        return true;
    }

    // Entry="Enter": Enter steps from the focused container into its first child, Escape steps back out
    private static bool TryDrill(KeyEventArgs eventArgs, Visual origin, List<(Control Control, ExpandedNode Node)> chain)
    {
        if (chain.Count == 0 || eventArgs.KeyModifiers != KeyModifiers.None) return false;

        if (eventArgs.Key == Key.Enter
            && ReferenceEquals(chain[0].Control, origin)
            && chain[0].Node.Node is Scope { IsFocusable: true, Entry: ScopeEntry.Enter })
        {
            List<Control> members = Members(chain[0].Control, chain[0].Node, excludeNestedArrowScopes: false);
            return members.Count > 0 && FocusTarget(members[0]).Focus(NavigationMethod.Tab);
        }

        if (eventArgs.Key != Key.Escape) return false;

        foreach ((Control control, ExpandedNode node) in chain)
        {
            if (ReferenceEquals(control, origin)
                || node.Node is not Scope { IsFocusable: true, Entry: ScopeEntry.Enter }) continue;

            return control.Focus(NavigationMethod.Tab);
        }

        return false;
    }

    private static bool TryArrows(KeyEventArgs eventArgs, Visual origin, List<(Control Control, ExpandedNode Node)> chain)
    {
        if (eventArgs.KeyModifiers != KeyModifiers.None) return false;

        SpatialDirection? pressed = eventArgs.Key switch
        {
            Key.Left => SpatialDirection.Left,
            Key.Right => SpatialDirection.Right,
            Key.Up => SpatialDirection.Up,
            Key.Down => SpatialDirection.Down,
            _ => null
        };
        if (pressed is not { } direction) return false;

        foreach ((Control scopeControl, ExpandedNode scopeNode) in chain)
        {
            if (scopeNode.Node is not Scope scope || !MovesAlong(scope.Arrows, direction)) continue;

            List<Control> members = Members(scopeControl, scopeNode, excludeNestedArrowScopes: true);
            int currentIndex = members.FindIndex(member =>
                ReferenceEquals(member, origin) || member.IsVisualAncestorOf(origin));
            if (currentIndex < 0) continue;

            int targetIndex = scope.Arrows == ArrowNavigation.Spatial
                ? ControlMapSpatialNavigation.FindDirectionalTarget(Centers(members, scopeControl), currentIndex, direction)
                : currentIndex + (direction is SpatialDirection.Left or SpatialDirection.Up ? -1 : 1);
            if (targetIndex < 0 || targetIndex >= members.Count) return false;

            return FocusTarget(members[targetIndex]).Focus(NavigationMethod.Directional);
        }

        return false;
    }

    private static bool MovesAlong(ArrowNavigation arrows, SpatialDirection direction) => arrows switch
    {
        ArrowNavigation.Horizontal => direction is SpatialDirection.Left or SpatialDirection.Right,
        ArrowNavigation.Vertical => direction is SpatialDirection.Up or SpatialDirection.Down,
        ArrowNavigation.Spatial => true,
        _ => false
    };

    private static List<Point> Centers(List<Control> members, Control scopeControl)
    {
        List<Point> centers = [];
        foreach (Control member in members)
        {
            Point center = new(member.Bounds.Width / 2, member.Bounds.Height / 2);
            centers.Add(member.TranslatePoint(center, scopeControl) ?? center);
        }

        return centers;
    }

    // Bound focus targets below a scope in tab order: map position first, then tree order for repeated instances
    private static List<Control> Members(Control scopeControl, ExpandedNode scopeNode, bool excludeNestedArrowScopes)
    {
        List<(Control Control, int Offset, int TreeIndex)> members = [];
        int treeIndex = 0;
        foreach (Visual descendant in scopeControl.GetVisualDescendants())
        {
            treeIndex++;
            if (descendant is not Control candidate || NodeOf(candidate) is not { } candidateNode) continue;
            if (!IsFocusTarget(candidate, candidateNode)) continue;
            if (excludeNestedArrowScopes && LiesInNestedArrowScope(candidate, scopeControl)) continue;

            int offset = ControlMapForest.OffsetWithin(scopeNode, candidateNode, ActiveVariants(candidate));
            if (offset < 0) continue;

            members.Add((candidate, offset, treeIndex));
        }

        members.Sort(static (left, right) => left.Offset != right.Offset
            ? left.Offset.CompareTo(right.Offset)
            : left.TreeIndex.CompareTo(right.TreeIndex));

        List<Control> ordered = [];
        foreach ((Control control, _, _) in members)
            ordered.Add(control);

        return ordered;
    }

    private static bool IsFocusTarget(Control control, ExpandedNode node)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return false;

        return node.Node switch
        {
            Leaf leaf => leaf.IsTabStop && leaf.Kind is not (LeafKind.Region or LeafKind.Handle or LeafKind.Command),
            Scope scope => scope.IsFocusable,
            _ => false
        };
    }

    private static bool LiesInNestedArrowScope(Control candidate, Control scopeControl)
    {
        for (Visual? current = ParentOf(candidate);
             current != null && !ReferenceEquals(current, scopeControl);
             current = ParentOf(current))
        {
            if (current is Control control && NodeOf(control)?.Node is Scope { Arrows: not ArrowNavigation.None })
                return true;
        }

        return false;
    }

    // Accelerators of every expanded scope around the focus, innermost first, so nearer scopes shadow outer ones
    private bool TryAccelerators(
        KeyEventArgs eventArgs,
        Visual origin,
        List<(Control Control, ExpandedNode Node)> chain)
    {
        if (chain.Count == 0) return false;

        // A key that types into the focused text box is the box's character, not an accelerator
        if (origin is TextBox { IsReadOnly: false } && TypesText(eventArgs.Key, eventArgs.KeyModifiers)) return false;

        ControlMapForest forest = ControlMapCatalog.Forest;
        for (ExpandedNode? scope = chain[0].Node; scope != null; scope = scope.Parent)
        {
            foreach (ControlMapAccelerator accelerator in forest.AcceleratorsAt(scope))
            {
                if (!accelerator.Gesture.Matches(eventArgs)) continue;
                if (ContextFor(chain, scope) is not { } context) continue;
                if (ActivateTarget(accelerator.Target, context, eventArgs.KeyModifiers)) return true;
            }
        }

        return false;
    }

    // Letters, digits, symbols, and Space type a character alone or with Shift; Ctrl, Alt, and Win combinations do not
    private static bool TypesText(Key key, KeyModifiers modifiers)
    {
        if ((modifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return false;

        return key is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide
            or Key.OemSemicolon or Key.OemPlus or Key.OemComma or Key.OemMinus or Key.OemPeriod or Key.OemQuestion
            or Key.OemTilde or Key.OemOpenBrackets or Key.OemPipe or Key.OemCloseBrackets or Key.OemQuotes
            or Key.Oem8 or Key.OemBackslash;
    }

    // The bound control standing for a scope: the scope's own control, or the nearest bound control around it
    private static Control? ContextFor(List<(Control Control, ExpandedNode Node)> chain, ExpandedNode scope)
    {
        foreach ((Control control, ExpandedNode node) in chain)
        {
            if (ReferenceEquals(node, scope) || scope.IsWithin(node))
                return control;
        }

        return null;
    }

    private bool ActivateTarget(ExpandedNode target, Control context, KeyModifiers modifiers)
    {
        if (target.Node is Leaf { Kind: LeafKind.Command })
            return RunCommand(target.Node.NodeID, context, modifiers);

        if (!_boundControls.TryGetValue(target, out List<Control>? controls)) return false;

        foreach (Control candidate in controls)
        {
            if (!ReferenceEquals(candidate, context) && !context.IsVisualAncestorOf(candidate)) continue;
            if (!candidate.IsEffectivelyVisible || !candidate.IsEffectivelyEnabled) continue;

            if (ControlMapBinding.GetActivation(candidate) is { } activate)
            {
                activate(new ControlActivation(candidate, modifiers));
                return true;
            }

            Control focusTarget = FocusTarget(candidate);
            if (focusTarget.Focusable)
                return focusTarget.Focus(NavigationMethod.Tab);
        }

        return false;
    }

    // A command handler lives on the scope's control, an ancestor such as the window, or a control inside the scope
    private static bool RunCommand(ControlMapNodeID command, Control context, KeyModifiers modifiers)
    {
        for (Visual? current = context; current != null; current = ParentOf(current))
        {
            if (current is not Control owner || ControlMapBinding.GetCommand(owner, command) is not { } handler) continue;

            handler(new ControlActivation(owner, modifiers));
            return true;
        }

        foreach (Visual descendant in context.GetVisualDescendants())
        {
            if (descendant is not Control owner || ControlMapBinding.GetCommand(owner, command) is not { } handler)
                continue;

            handler(new ControlActivation(owner, modifiers));
            return true;
        }

        return false;
    }

    private static void ReportResolution(Control control, ControlMapNodeID id, string problem)
    {
#if DEBUG
        ControlMapDriftCheck.ReportOnce($"Control map: {control.GetType().Name} tagged {id} {problem}");
#endif
    }

    private sealed class BoundControl(ExpandedNode node, ControlMapNavigator navigator)
    {
        public readonly ExpandedNode Node = node;
        public readonly ControlMapNavigator Navigator = navigator;
    }
}

using Avalonia.Input;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>
/// One node of the expanded control map: every template inlined at each instance, every slot filled with the
/// instance's children. A template node appears once per instance, so the runtime can tell instances apart.
/// </summary>
internal sealed class ExpandedNode(ControlMapNode node, ExpandedNode? parent, Slot? fromSlot, Template? template)
{
    public readonly ControlMapNode Node = node;
    public readonly ExpandedNode? Parent = parent;

    // The template slot that placed this node, so a variant order can name the slot
    public readonly Slot? FromSlot = fromSlot;

    // The template this node instantiates; its children come from the template
    public readonly Template? Template = template;

    public readonly List<ExpandedNode> Children = [];
    public readonly List<Variant> Variants = [];

    /// <summary>Returns whether this node is the given ancestor or lies below it.</summary>
    public bool IsWithin(ExpandedNode ancestor)
    {
        for (ExpandedNode? current = this; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }
}

/// <summary>An accelerator gesture and the expanded leaf it activates.</summary>
internal readonly record struct ControlMapAccelerator(KeyGesture Gesture, ExpandedNode Target);

/// <summary>The expanded trees of every registered map, with the lookups the runtime binding needs.</summary>
internal sealed class ControlMapForest
{
    private static readonly IReadOnlyList<ExpandedNode> NoNodes = [];
    private static readonly IReadOnlyList<ControlMapAccelerator> NoAccelerators = [];

    private readonly Dictionary<ControlMapNode, List<ExpandedNode>> _occurrences = [];
    private readonly Dictionary<Template, List<ExpandedNode>> _instances = [];
    private readonly Dictionary<ExpandedNode, List<ControlMapAccelerator>> _accelerators = [];

    /// <summary>Gets the expanded root surfaces of every map.</summary>
    public List<ExpandedNode> Roots { get; } = [];

    /// <summary>Returns every expanded occurrence of a schema node; template nodes occur once per instance.</summary>
    public IReadOnlyList<ExpandedNode> Occurrences(ControlMapNode node) =>
        _occurrences.TryGetValue(node, out List<ExpandedNode>? occurrences) ? occurrences : NoNodes;

    /// <summary>Returns every expanded node that instantiates a template.</summary>
    public IReadOnlyList<ExpandedNode> InstancesOf(Template template) =>
        _instances.TryGetValue(template, out List<ExpandedNode>? instances) ? instances : NoNodes;

    /// <summary>Returns the accelerators active while focus is within the given expanded node.</summary>
    public IReadOnlyList<ControlMapAccelerator> AcceleratorsAt(ExpandedNode scope) =>
        _accelerators.TryGetValue(scope, out List<ControlMapAccelerator>? accelerators) ? accelerators : NoAccelerators;

    /// <summary>Expands every surface of the given maps; templates resolve across all of them.</summary>
    public static ControlMapForest Build(IReadOnlyList<ControlMap> maps)
    {
        ControlMapForest forest = new();
        Dictionary<string, Template> templates = new(StringComparer.Ordinal);
        foreach (ControlMap map in maps)
        {
            // Indexing assigns the paths, parents, and owning map that ids and key scopes rely on
            _ = map.Nodes;
            foreach (ControlMapNode child in map.Children)
            {
                if (child is Template template)
                    templates.TryAdd(template.ID, template);
            }
        }

        Stack<(ExpandedNode Node, SlotContext? Context)> pending = new();
        foreach (ControlMap map in maps)
        {
            foreach (ControlMapNode child in map.Children)
            {
                if (child is not Surface surface) continue;

                ExpandedNode root = forest.Add(surface, parent: null, fromSlot: null, templates);
                forest.Roots.Add(root);
                pending.Push((root, null));
            }
        }

        while (pending.Count > 0)
        {
            (ExpandedNode expanded, SlotContext? context) = pending.Pop();
            if (expanded.Node is not ControlMapContainer container) continue;

            // An instance takes its structure from the template and hands its own children to the template's slots
            List<ControlMapNode> sourceChildren = expanded.Template?.Children ?? container.Children;
            SlotContext? childContext = expanded.Template != null
                ? new SlotContext(container.Children, context)
                : context;

            foreach (ControlMapNode child in sourceChildren)
            {
                if (child is Variant variant)
                {
                    expanded.Variants.Add(variant);
                    continue;
                }

                foreach ((ControlMapNode node, SlotContext? nodeContext, Slot? fromSlot) in
                         SlotContext.Resolve(child, childContext))
                {
                    ExpandedNode expandedChild = forest.Add(node, expanded, fromSlot, templates);
                    expanded.Children.Add(expandedChild);
                    pending.Push((expandedChild, nodeContext));
                }
            }
        }

        forest.IndexAccelerators();
        return forest;
    }

    /// <summary>
    /// Returns a container's children in tab order: the first active variant's order when the container declares one,
    /// otherwise document order. A variant omits the children absent in its layout.
    /// </summary>
    public static IReadOnlyList<ExpandedNode> OrderedChildren(
        ExpandedNode container,
        IReadOnlyCollection<string> activeVariants)
    {
        Variant? activeVariant = null;
        foreach (Variant variant in container.Variants)
        {
            if (!activeVariants.Contains(variant.ID)) continue;

            activeVariant = variant;
            break;
        }

        if (activeVariant == null) return container.Children;

        List<ExpandedNode> ordered = [];
        foreach (string orderID in activeVariant.OrderIDs)
        {
            foreach (ExpandedNode child in container.Children)
            {
                bool isNamed = child.FromSlot == null
                    ? string.Equals(child.Node.ID, orderID, StringComparison.Ordinal)
                    : string.Equals(child.FromSlot.ID, orderID, StringComparison.Ordinal);
                if (isNamed)
                    ordered.Add(child);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Returns the preorder position of a node inside a group's subtree, which is the node's tab index in that group.
    /// Positions count every node between, so a scope without a container of its own still orders its children
    /// correctly against their cousins. Returns -1 when the node is absent under the active variants.
    /// </summary>
    public static int OffsetWithin(
        ExpandedNode group,
        ExpandedNode target,
        IReadOnlyCollection<string> activeVariants)
    {
        // Children of an arranged scope share one position, so their tab order falls back to tree order
        ExpandedNode lookup = target;
        if (target.Parent?.Node is Scope { IsArranged: true })
        {
            IReadOnlyList<ExpandedNode> siblings = OrderedChildren(target.Parent, activeVariants);
            if (siblings.Count > 0 && siblings.Contains(target))
                lookup = siblings[0];
        }

        int offset = 0;
        Stack<ExpandedNode> pending = new();
        PushOrdered(pending, group, activeVariants);
        while (pending.Count > 0)
        {
            ExpandedNode node = pending.Pop();
            if (ReferenceEquals(node, lookup)) return offset;

            offset++;
            PushOrdered(pending, node, activeVariants);
        }

        return -1;
    }

    private static void PushOrdered(
        Stack<ExpandedNode> pending,
        ExpandedNode container,
        IReadOnlyCollection<string> activeVariants)
    {
        IReadOnlyList<ExpandedNode> children = OrderedChildren(container, activeVariants);
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            pending.Push(children[childIndex]);
    }

    private ExpandedNode Add(
        ControlMapNode node,
        ExpandedNode? parent,
        Slot? fromSlot,
        Dictionary<string, Template> templates)
    {
        string? templateID = node switch
        {
            Surface surface => surface.Template,
            Scope scope => scope.Template,
            _ => null
        };

        Template? template = templateID != null && templates.TryGetValue(templateID, out Template? found)
            ? found
            : null;
        ExpandedNode expanded = new(node, parent, fromSlot, template);
        if (!_occurrences.TryGetValue(node, out List<ExpandedNode>? occurrences))
        {
            occurrences = [];
            _occurrences.Add(node, occurrences);
        }

        occurrences.Add(expanded);
        if (template == null) return expanded;

        if (!_instances.TryGetValue(template, out List<ExpandedNode>? instances))
        {
            instances = [];
            _instances.Add(template, instances);
        }

        instances.Add(expanded);
        return expanded;
    }

    // An accelerator is active within its leaf's key scope, the parent unless KeyScope names an ancestor
    private void IndexAccelerators()
    {
        foreach (List<ExpandedNode> occurrences in _occurrences.Values)
        {
            foreach (ExpandedNode expanded in occurrences)
            {
                if (expanded.Node is not Leaf leaf || leaf.KeyGestures.Count == 0) continue;

                ExpandedNode? scope = FindKeyScope(expanded, leaf.KeyScope);
                if (scope == null) continue;

                if (!_accelerators.TryGetValue(scope, out List<ControlMapAccelerator>? accelerators))
                {
                    accelerators = [];
                    _accelerators.Add(scope, accelerators);
                }

                foreach (KeyGesture gesture in leaf.KeyGestures)
                    accelerators.Add(new ControlMapAccelerator(gesture, expanded));
            }
        }
    }

    // Inside a template, the template root's id names the instance that stands in for it
    private static ExpandedNode? FindKeyScope(ExpandedNode leaf, string? keyScopeID)
    {
        if (keyScopeID == null) return leaf.Parent;

        for (ExpandedNode? ancestor = leaf.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (string.Equals(ancestor.Node.ID, keyScopeID, StringComparison.Ordinal)
                || string.Equals(ancestor.Template?.ID, keyScopeID, StringComparison.Ordinal))
                return ancestor;
        }

        return null;
    }
}

/// <summary>
/// Children of one template instance, grouped by the slot each names, plus the context the instance itself was
/// expanded in. Slot content expands in that outer context, so a slot forwarded into an inner template resolves
/// against the outer instance.
/// </summary>
internal sealed class SlotContext(List<ControlMapNode> instanceChildren, SlotContext? outer)
{
    public SlotContext? Outer { get; } = outer;

    /// <summary>
    /// Replaces a slot with its content, following forwarded slots, and returns each resulting node with the context
    /// it expands in and the slot that placed it. Any other node passes through unchanged.
    /// </summary>
    public static List<(ControlMapNode Node, SlotContext? Context, Slot? FromSlot)> Resolve(
        ControlMapNode node,
        SlotContext? context)
    {
        List<(ControlMapNode Node, SlotContext? Context, Slot? FromSlot)> resolved = [];
        if (node is not Slot placingSlot)
        {
            resolved.Add((node, context, null));
            return resolved;
        }

        // Content may itself be a slot forwarded from an outer template, resolved against the next context out
        Stack<(ControlMapNode Node, SlotContext? Context)> pending = new();
        pending.Push((node, context));
        List<(ControlMapNode Node, SlotContext? Context)> ordered = [];
        while (pending.Count > 0)
        {
            (ControlMapNode current, SlotContext? currentContext) = pending.Pop();
            if (current is not Slot slot)
            {
                ordered.Add((current, currentContext));
                continue;
            }

            if (currentContext == null) continue;

            List<ControlMapNode> content = currentContext.Content(slot.ID);
            for (int contentIndex = content.Count - 1; contentIndex >= 0; contentIndex--)
                pending.Push((content[contentIndex], currentContext.Outer));
        }

        foreach ((ControlMapNode resolvedNode, SlotContext? resolvedContext) in ordered)
            resolved.Add((resolvedNode, resolvedContext, placingSlot));

        return resolved;
    }

    private List<ControlMapNode> Content(string slotID)
    {
        List<ControlMapNode> content = [];
        foreach (ControlMapNode child in instanceChildren)
        {
            string childSlot = child switch
            {
                Leaf leaf => leaf.Slot ?? string.Empty,
                Scope scope => scope.Slot ?? string.Empty,
                _ => string.Empty
            };
            if (string.Equals(childSlot, slotID, StringComparison.Ordinal))
                content.Add(child);
        }

        return content;
    }
}

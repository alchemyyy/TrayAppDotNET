using Avalonia.Metadata;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>Identifies one control map node by its owning map and dot-separated path.</summary>
public readonly record struct ControlMapNodeID(string Map, string Path)
{
    public override string ToString() => Map + ":" + Path;
}

/// <summary>
/// Root of a control map AXAML file: root-level templates and surfaces in document order.
/// Each file's x:Class derives from this type; the control map generator emits that class, its constructor,
/// and one typed <see cref="ControlMapNodeID"/> per node.
/// </summary>
public class ControlMap
{
    private const char PathSeparator = '.';

    private Dictionary<string, ControlMapNode>? _nodesByPath;

    /// <summary>Initializes a map whose node ids carry the given map name.</summary>
    protected ControlMap(string name)
    {
        Name = name;
    }

    /// <summary>Initializes an unnamed map; AXAML tooling requires a parameterless constructor on the root type.</summary>
    public ControlMap()
    {
        Name = string.Empty;
    }

    /// <summary>Gets the map name used by node ids, which is the x:Class simple name.</summary>
    public string Name { get; }

    /// <summary>Gets the root-level templates and surfaces in document order.</summary>
    [Content]
    public List<ControlMapNode> Children { get; } = [];

    /// <summary>Gets every addressable node keyed by path. Slots and variants describe positions and are absent.</summary>
    public IReadOnlyDictionary<string, ControlMapNode> Nodes => _nodesByPath ??= BuildIndex();

    /// <summary>Returns the node for an id owned by this map, or null when the id names another map or no node.</summary>
    public ControlMapNode? Find(ControlMapNodeID id)
    {
        if (!string.Equals(id.Map, Name, StringComparison.Ordinal)) return null;

        return Nodes.GetValueOrDefault(id.Path);
    }

    /// <summary>Returns the root-level template with the given id, or null when the map defines none.</summary>
    public Template? FindTemplate(string id)
    {
        foreach (ControlMapNode child in Children)
        {
            if (child is Template template && string.Equals(template.ID, id, StringComparison.Ordinal))
                return template;
        }

        return null;
    }

    // Assigns parent, index and path to every node in one iterative pass
    private Dictionary<string, ControlMapNode> BuildIndex()
    {
        Dictionary<string, ControlMapNode> nodesByPath = new(StringComparer.Ordinal);
        Stack<ControlMapNode> pending = new();
        for (int childIndex = Children.Count - 1; childIndex >= 0; childIndex--)
        {
            ControlMapNode child = Children[childIndex];
            child.Parent = null;
            child.Index = childIndex;
            child.Path = child.ID;
            pending.Push(child);
        }

        while (pending.Count > 0)
        {
            ControlMapNode node = pending.Pop();
            node.Map = this;
            bool isPosition = node is Slot or Variant;
            if (!isPosition) nodesByPath[node.Path] = node;
            if (node is not ControlMapContainer container) continue;

            for (int childIndex = container.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                ControlMapNode child = container.Children[childIndex];
                child.Parent = container;
                child.Index = childIndex;
                child.Path = child is Slot or Variant ? container.Path : container.Path + PathSeparator + child.ID;
                pending.Push(child);
            }
        }

        return nodesByPath;
    }
}

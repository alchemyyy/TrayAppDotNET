namespace BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;

public class GroupValueNode : INode
{
    public string? Value { get; set; }

    public IEnumerable<INode>? Nodes { get; set; }

    public INode? Parent { get; set; }

    public override string ToString() =>
        Parent?.ToString() != null
            ? Parent + "_" + Value
            : Value ?? string.Empty;
}

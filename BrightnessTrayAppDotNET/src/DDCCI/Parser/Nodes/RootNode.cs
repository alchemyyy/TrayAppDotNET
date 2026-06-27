namespace BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;

public class RootNode : INode
{
    public IEnumerable<INode>? Nodes { get; set; }

    public INode? Parent { get; set; }

    public string? Value { get; set; }

    public override string? ToString() => null;
}

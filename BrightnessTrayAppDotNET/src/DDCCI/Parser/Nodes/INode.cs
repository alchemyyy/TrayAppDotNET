namespace BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;

public interface INode
{
    IEnumerable<INode>? Nodes { get; }

    INode? Parent { get; }

    string? Value { get; }
}

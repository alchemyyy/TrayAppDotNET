using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;

namespace BrightnessTrayAppDotNET.DDCCI;

public interface INodeFormatter
{
    string? FormatNode(INode node);
}

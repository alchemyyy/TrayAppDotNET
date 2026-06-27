using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;

namespace BrightnessTrayAppDotNET.DDCCI.Parser;

/// <summary>
/// Parser for MCCS capability strings.
/// Converts a flat token stream into a tree where <see cref="GroupValueNode"/> represents a parenthesized list
/// and <see cref="ValueNode"/> represents a leaf word.
/// The node immediately preceding an open-paren becomes the group's label
/// (e.g. <c>vcp</c> followed by <c>(10 12 60)</c>
/// yields a GroupValueNode named <c>vcp</c> with three ValueNode children).
/// </summary>
public class CapabilitiesParser
{
    public INode Parse(IEnumerable<IToken> tokens)
    {
        Queue<IToken> queue = new(tokens);
        Stack<INode> stack = new();

        RootNode root = new();
        ParseTokens(queue, stack, root);

        List<INode> reversedNodes = [.. stack];
        reversedNodes.Reverse();
        root.Nodes = reversedNodes;

        return root;
    }

    private void ParseTokens(Queue<IToken> queue, Stack<INode> stack, INode parent)
    {
        while (queue.Count > 0)
        {
            IToken token = queue.Dequeue();
            switch (token)
            {
                case WordToken word:
                    stack.Push(ParseWordToken(word, parent));
                    break;

                case OpenToken open:
                    stack.Push(ParseOpenToken(open, queue, stack, parent));
                    break;

                case CloseToken:
                    return;
            }
        }
    }

    private GroupValueNode ParseOpenToken(OpenToken _, Queue<IToken> queue, Stack<INode> stack, INode parent)
    {
        Stack<INode> children = new();

        INode? previous = null;
        if (stack.Count > 0) previous = stack.Pop();

        GroupValueNode node = new() { Value = previous?.Value, Parent = parent };

        ParseTokens(queue, children, node);

        List<INode> orderedChildren = [.. children];
        orderedChildren.Reverse();
        node.Nodes = orderedChildren;

        return node;
    }

    private static ValueNode ParseWordToken(WordToken word, INode parent) => new()
    {
        Value = word.Value, Parent = parent
    };
}

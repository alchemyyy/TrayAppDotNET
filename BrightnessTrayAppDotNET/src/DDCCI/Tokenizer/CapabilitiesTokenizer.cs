using System.Text.RegularExpressions;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;

namespace BrightnessTrayAppDotNET.DDCCI.Tokenizer;

/// <summary>
/// Splits a raw MCCS capability string such as <c>(prot(monitor)type(lcd)vcp(10 12 60(01 11)))</c>
/// into whitespace, parenthesis, and word tokens.
/// Unrecognized characters are skipped so vendor-specific punctuation does not break parsing.
/// </summary>
public class CapabilitiesTokenizer
{
    private readonly IEnumerable<ITokenFilter<IToken>> _filters = new List<ITokenFilter<IToken>>
    {
        new TokenFilter<WhitespaceToken>(@"^\s+$"),
        new TokenFilter<OpenToken>(@"^\($"),
        new TokenFilter<CloseToken>(@"^\)$"),
        new TokenFilter<WordToken>(@"^\w+$"),
    };

    public IEnumerable<IToken> GetTokens(string inputString)
    {
        Queue<char> queue = new(inputString);
        while (queue.Count > 0)
        {
            IToken? token = ReadNext(queue);
            if (token != null) yield return token;
        }
    }

    private IToken? ReadNext(Queue<char> queue)
    {
        foreach (ITokenFilter<IToken> filter in _filters)
        {
            char peek = queue.Peek();
            if (!Regex.IsMatch(peek.ToString(), filter.Pattern)) continue;

            string buffer = queue.Dequeue().ToString();
            while (queue.Count > 0 && Regex.IsMatch(buffer + queue.Peek(), filter.Pattern))
                buffer += queue.Dequeue();

            return filter.GetToken(buffer);
        }

        queue.Dequeue();
        return null;
    }
}

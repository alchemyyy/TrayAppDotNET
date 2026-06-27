using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;

namespace BrightnessTrayAppDotNET.DDCCI.Tokenizer;

public interface ITokenFilter<out T> where T : IToken
{
    string Pattern { get; set; }

    T GetToken(string value);
}

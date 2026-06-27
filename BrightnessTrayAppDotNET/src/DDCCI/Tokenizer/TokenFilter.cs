using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;

namespace BrightnessTrayAppDotNET.DDCCI.Tokenizer;

public class TokenFilter<T>(string pattern) : ITokenFilter<T>
    where T : IToken, new()
{
    public string Name { get; set; } = typeof(T).Name;

    public string Pattern { get; set; } = pattern;

    public T GetToken(string value) => new() { Type = Name, Value = value };
}

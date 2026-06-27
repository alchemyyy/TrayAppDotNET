namespace BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;

public class Token : IToken
{
    public string Type { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public override string ToString() => $"{Type}: {Value}";
}

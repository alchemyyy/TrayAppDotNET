namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal enum AXAMLMarkupExtensionKind
{
    Binding,
    ResourceReference
}

internal sealed class AXAMLMarkupExtension(
    AXAMLMarkupExtensionKind kind,
    string name,
    string? resourceKey)
{
    public readonly AXAMLMarkupExtensionKind Kind = kind;
    public readonly string Name = name;
    public readonly string? ResourceKey = resourceKey;
}

internal static class AXAMLMarkupExtensionParser
{
    private static readonly string[] BindingNames =
    [
        "Binding",
        "CompiledBinding",
        "ReflectionBinding",
        "TemplateBinding"
    ];

    private static readonly string[] ResourceReferenceNames =
    [
        "StaticResource",
        "DynamicResource"
    ];

    public static IReadOnlyList<AXAMLMarkupExtension> Find(string expression)
    {
        List<AXAMLMarkupExtension> extensions = [];
        for (int index = 0; index < expression.Length; index++)
        {
            if (expression[index] != '{') continue;

            int nameStart = index + 1;
            while (nameStart < expression.Length && char.IsWhiteSpace(expression[nameStart]))
                nameStart++;

            int nameEnd = nameStart;
            while (nameEnd < expression.Length && IsExtensionNameCharacter(expression[nameEnd]))
                nameEnd++;

            if (nameEnd == nameStart) continue;

            string qualifiedName = expression[nameStart..nameEnd];
            int prefixSeparatorIndex = qualifiedName.LastIndexOf(':');
            string name = prefixSeparatorIndex >= 0
                ? qualifiedName[(prefixSeparatorIndex + 1)..]
                : qualifiedName;

            AXAMLMarkupExtensionKind? kind = ExtensionKind(name);
            if (kind == null) continue;

            int closingBraceIndex = FindClosingBrace(expression, index);
            if (closingBraceIndex < 0) closingBraceIndex = expression.Length;

            string body = expression[nameEnd..closingBraceIndex];
            string? resourceKey = kind == AXAMLMarkupExtensionKind.ResourceReference
                ? ParseResourceKey(body)
                : null;
            extensions.Add(new AXAMLMarkupExtension(kind.Value, name, resourceKey));
        }

        return extensions;
    }

    private static AXAMLMarkupExtensionKind? ExtensionKind(string name)
    {
        foreach (string bindingName in BindingNames)
        {
            if (string.Equals(name, bindingName, StringComparison.Ordinal))
                return AXAMLMarkupExtensionKind.Binding;
        }

        foreach (string resourceReferenceName in ResourceReferenceNames)
        {
            if (string.Equals(name, resourceReferenceName, StringComparison.Ordinal))
                return AXAMLMarkupExtensionKind.ResourceReference;
        }

        return null;
    }

    private static int FindClosingBrace(string expression, int openingBraceIndex)
    {
        int depth = 0;
        char quote = '\0';
        for (int index = openingBraceIndex; index < expression.Length; index++)
        {
            char character = expression[index];
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }

            switch (character)
            {
                case '\'':
                case '"':
                    quote = character;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0) return index;
                    break;
            }
        }

        return -1;
    }

    private static string? ParseResourceKey(string body)
    {
        string candidate = body.Trim();
        const string ResourceKeyPrefix = "ResourceKey=";
        if (candidate.StartsWith(ResourceKeyPrefix, StringComparison.OrdinalIgnoreCase))
            candidate = candidate[ResourceKeyPrefix.Length..].TrimStart();

        if (candidate.Length == 0) return null;

        int depth = 0;
        char quote = '\0';
        int endIndex = candidate.Length;
        for (int index = 0; index < candidate.Length; index++)
        {
            char character = candidate[index];
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }

            switch (character)
            {
                case '\'':
                case '"':
                    quote = character;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    if (depth > 0) depth--;
                    break;
                case ',':
                    if (depth == 0) endIndex = index;
                    break;
            }

            if (endIndex != candidate.Length) break;
        }

        string key = candidate[..endIndex].Trim();
        if (key.Length >= 2 &&
            ((key[0] == '\'' && key[^1] == '\'') || (key[0] == '"' && key[^1] == '"')))
            key = key[1..^1];

        return key.Length == 0 ? null : key;
    }

    private static bool IsExtensionNameCharacter(char character) =>
        character == ':' || character == '_' || char.IsLetterOrDigit(character);
}

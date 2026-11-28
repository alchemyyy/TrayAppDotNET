using System.Text;
using Avalonia.Media;
using TrayAppDotNETCommon.Utils;
using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>One value a card line format inserts by name, such as <c>{utilization}</c>; null when not reported.</summary>
internal readonly record struct PerformanceCardValue(string Name, string? Value);

/// <summary>Names of the card values every device kind provides.</summary>
internal static class PerformanceCardValueNames
{
    /// <summary>The device description, after the card's own name and replacements.</summary>
    public const string Description = "description";

    /// <summary>Why the device has no sample yet, e.g. <c>Collecting throughput...</c>; null once it has one.</summary>
    public const string Status = "status";
}

/// <summary>
/// A parsed card line format. <c>{name}</c> inserts a value, or <c>Unavailable</c> when the device did not report
/// it. <c>[...]</c> is optional: it shows only when every value inside it was reported. <c>{{</c>, <c>}}</c>,
/// <c>[[</c> and <c>]]</c> are literal characters, and an unknown <c>{name}</c> stays as typed.
/// </summary>
internal sealed class PerformanceCardLineFormat
{
    [ThreadStatic] private static StringBuilder? t_builder;

    private readonly Part[] _parts;

    private PerformanceCardLineFormat(Part[] parts) => _parts = parts;

    public static PerformanceCardLineFormat Empty { get; } = new([]);

    public static PerformanceCardLineFormat Parse(string? format)
    {
        if (string.IsNullOrEmpty(format)) return Empty;

        List<Part> parts = [];
        List<Part>? optionalParts = null;
        StringBuilder literal = new();
        int index = 0;
        while (index < format.Length)
        {
            char character = format[index];
            if (character is '{' or '}' or '[' or ']'
                && index + 1 < format.Length
                && format[index + 1] == character)
            {
                literal.Append(character);
                index += 2;
                continue;
            }

            if (character == '{')
            {
                int end = format.IndexOf('}', index + 1);
                if (end > index + 1)
                {
                    FlushLiteral(literal, optionalParts ?? parts);
                    (optionalParts ?? parts).Add(Part.Value(format[(index + 1)..end]));
                    index = end + 1;
                    continue;
                }
            }
            else if (character == '[' && optionalParts == null && format.IndexOf(']', index + 1) > index)
            {
                FlushLiteral(literal, parts);
                optionalParts = [];
                index++;
                continue;
            }
            else if (character == ']' && optionalParts != null)
            {
                FlushLiteral(literal, optionalParts);
                if (optionalParts.Count > 0) parts.Add(Part.Optional(optionalParts.ToArray()));
                optionalParts = null;
                index++;
                continue;
            }

            literal.Append(character);
            index++;
        }

        FlushLiteral(literal, optionalParts ?? parts);
        return new PerformanceCardLineFormat(parts.ToArray());
    }

    /// <summary>Renders the line, inserting <paramref name="description"/> for the description value.</summary>
    public string Render(ReadOnlySpan<PerformanceCardValue> values, string description)
    {
        if (_parts.Length == 0) return string.Empty;

        StringBuilder builder = t_builder ??= new StringBuilder();
        builder.Clear();
        AppendParts(builder, _parts, values, description, isOptional: false);
        return builder.ToString();
    }

    private static void AppendParts(
        StringBuilder builder,
        Part[] parts,
        ReadOnlySpan<PerformanceCardValue> values,
        string description,
        bool isOptional)
    {
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            Part part = parts[partIndex];
            switch (part.Kind)
            {
                case PartKind.Literal:
                    builder.Append(part.Text);
                    break;
                case PartKind.Value:
                    if (!TryResolve(part.Text, values, description, out string? value))
                        builder.Append('{').Append(part.Text).Append('}');
                    else
                        builder.Append(value ?? (isOptional ? string.Empty : Unavailable));
                    break;
                case PartKind.Optional:
                    if (AreAllReported(part.Children, values, description))
                        AppendParts(builder, part.Children, values, description, isOptional: true);
                    break;
            }
        }
    }

    private static bool AreAllReported(
        Part[] parts,
        ReadOnlySpan<PerformanceCardValue> values,
        string description)
    {
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            Part part = parts[partIndex];
            if (part.Kind == PartKind.Value
                && TryResolve(part.Text, values, description, out string? value)
                && value == null)
                return false;
        }

        return true;
    }

    /// <summary>Finds a value by name, ignoring case; false for a name the device does not provide.</summary>
    private static bool TryResolve(
        string name,
        ReadOnlySpan<PerformanceCardValue> values,
        string description,
        out string? value)
    {
        if (string.Equals(name, PerformanceCardValueNames.Description, StringComparison.OrdinalIgnoreCase))
        {
            value = description;
            return true;
        }

        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            if (!string.Equals(values[valueIndex].Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            value = values[valueIndex].Value;
            return true;
        }

        value = null;
        return false;
    }

    private static void FlushLiteral(StringBuilder literal, List<Part> parts)
    {
        if (literal.Length == 0) return;

        parts.Add(Part.Literal(literal.ToString()));
        literal.Clear();
    }

    private enum PartKind
    {
        Literal,
        Value,
        Optional
    }

    private readonly record struct Part(PartKind Kind, string Text, Part[] Children)
    {
        public static Part Literal(string text) => new(PartKind.Literal, text, []);

        public static Part Value(string name) => new(PartKind.Value, name, []);

        public static Part Optional(Part[] children) => new(PartKind.Optional, string.Empty, children);
    }
}

/// <summary>What one device card shows: its title and up to two lines under it.</summary>
internal readonly record struct PerformanceDeviceCardText(string Title, string FirstLine, string SecondLine);

/// <summary>
/// The universal device card: a title, then zero to two lines, each rendered from a line format. Built from the
/// device kind's defaults with the user's customization of one card laid over them.
/// </summary>
internal sealed class PerformanceDeviceCardLayout
{
    private const string LogContext = "Performance card description";

    private readonly string _title;
    private readonly string _description;
    private readonly CompiledRegexReplacement[] _descriptionReplacements;
    private readonly PerformanceCardLineFormat[] _lines;

    private PerformanceDeviceCardLayout(
        string title,
        string description,
        CompiledRegexReplacement[] descriptionReplacements,
        int lineCount,
        PerformanceCardLineFormat[] lines,
        Color? accent)
    {
        _title = title;
        _description = description;
        _descriptionReplacements = descriptionReplacements;
        LineCount = lineCount;
        _lines = lines;
        Accent = accent;
    }

    /// <summary>Lines shown under the title.</summary>
    public int LineCount { get; }

    /// <summary>The user's base graph color, or null for the device kind's.</summary>
    public Color? Accent { get; }

    /// <summary>Whether the user renamed the description or added replacements to it.</summary>
    public bool CustomizesDescription => _description.Length > 0 || _descriptionReplacements.Length > 0;

    /// <summary>Returns the default number of lines under a device kind's title.</summary>
    public static int GetDefaultLineCount(PerformanceDeviceKind kind) =>
        kind == PerformanceDeviceKind.CPU ? 1 : PerformanceDeviceCardCustomization.MaximumLineCount;

    /// <summary>
    /// Returns a device kind's default format for a line, chosen so the card reads as it always has: the device
    /// description first, then its live values. The CPU shows only its values by default, so its description
    /// comes second.
    /// </summary>
    public static string GetDefaultLineFormat(PerformanceDeviceKind kind, int lineIndex) =>
        (kind, lineIndex) switch
        {
            (PerformanceDeviceKind.CPU, 0) => "{utilization}[  {speed}]",
            (PerformanceDeviceKind.CPU, _) => "{description}",
            (PerformanceDeviceKind.Memory, 0) => "[{used} / {total}][{status}]",
            (PerformanceDeviceKind.Memory, _) => L(nameof(AppStrings.Perf_Card_MemoryFreeFormat)),
            (_, 0) => "{description}",
            (PerformanceDeviceKind.GPU, _) => "{utilization}[  {temperature}]",
            (PerformanceDeviceKind.Network, _) => "[S: {send}  R: {receive}][{status}]",
            (PerformanceDeviceKind.Disk, _) => "[{activeTime}][{status}]",
            _ => string.Empty
        };

    public static PerformanceDeviceCardLayout Create(
        PerformanceDeviceKind kind,
        PerformanceDeviceCardCustomization? customization)
    {
        int lineCount = customization?.LineCount ?? PerformanceDeviceCardCustomization.DefaultLineCount;
        if (lineCount == PerformanceDeviceCardCustomization.DefaultLineCount) lineCount = GetDefaultLineCount(kind);

        PerformanceCardLineFormat[] lines = new PerformanceCardLineFormat[PerformanceDeviceCardCustomization.MaximumLineCount];
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string format = customization?.GetLineFormat(lineIndex) ?? string.Empty;
            lines[lineIndex] = PerformanceCardLineFormat.Parse(
                format.Length > 0 ? format : GetDefaultLineFormat(kind, lineIndex));
        }

        List<CompiledRegexReplacement> replacements = [];
        if (customization != null)
        {
            foreach (PerformanceTextReplacementRule rule in customization.DescriptionReplacements)
            {
                if (CompiledRegexReplacement.TryCreate(
                        rule.MatchPattern,
                        rule.Replacement,
                        LogContext,
                        out CompiledRegexReplacement? compiled))
                    replacements.Add(compiled);
            }
        }

        return new PerformanceDeviceCardLayout(
            customization?.Title ?? string.Empty,
            customization?.Description ?? string.Empty,
            replacements.ToArray(),
            lineCount,
            lines,
            ColorMath.TryParseHexOrNull(customization?.AccentColor));
    }

    /// <summary>The card title: the user's, or the device's own.</summary>
    public string ResolveTitle(PerformanceDevicePresentation device) =>
        _title.Length > 0 ? _title : device.Title;

    /// <summary>The device description: the user's, or the device's own, then the card's replacements.</summary>
    public string ResolveDescription(PerformanceDevicePresentation device)
    {
        string description = _description.Length > 0 ? _description : device.Description;
        for (int ruleIndex = 0; ruleIndex < _descriptionReplacements.Length; ruleIndex++)
            description = _descriptionReplacements[ruleIndex].Apply(description);
        return description;
    }

    public PerformanceDeviceCardText Format(PerformanceDevicePresentation device)
    {
        string description = ResolveDescription(device);
        ReadOnlySpan<PerformanceCardValue> values = device.CardValues.Span;
        return new PerformanceDeviceCardText(
            ResolveTitle(device),
            LineCount > 0 ? _lines[0].Render(values, description) : string.Empty,
            LineCount > 1 ? _lines[1].Render(values, description) : string.Empty);
    }
}

/// <summary>A device's graph colors: the base trace and the color of a second trace drawn beside it.</summary>
internal readonly record struct PerformanceDeviceAccent(Color Primary, Color Secondary)
{
    /// <summary>
    /// Returns a device kind's colors, or ones built from the user's base color, where the second trace is a
    /// lighter shade of it.
    /// </summary>
    public static PerformanceDeviceAccent Resolve(PerformanceDeviceKind kind, Color? customAccent)
    {
        TaskManagerWindowResources resources = TaskManagerWindowResources.Current;
        if (customAccent is { } accent)
        {
            double lightening = resources.AxamlTaskManagerPerformance.CustomAccentSecondaryLightening;
            return new PerformanceDeviceAccent(
                accent,
                PageFileHistoryView.GetShade(accent, index: 1, lightening, lightening));
        }

        Color primary = PerformanceDevicePresentationFactory.GetAccent(kind);
        return new PerformanceDeviceAccent(
            primary,
            kind == PerformanceDeviceKind.GPU
                ? resources.AxamlTaskManagerPerformance.GPUSecondaryAccentColor
                : primary);
    }
}

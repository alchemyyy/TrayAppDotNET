using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One device-type-specific regex replacement for a Performance hardware name.</summary>
public sealed class PerformanceHardwareNameReplacementRule
{
    [XmlAttribute]
    public PerformanceDeviceKind DeviceKind { get; set; } = PerformanceDeviceKind.Network;

    [XmlAttribute]
    [AllowNull]
    public string MatchPattern
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    [AllowNull]
    public string Replacement
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Gets whether the Performance page shows a hardware name for <paramref name="kind"/>.</summary>
    /// <remarks>Memory has none: its header shows the memory error count instead.</remarks>
    public static bool SupportsDeviceKind(PerformanceDeviceKind kind) =>
        Enum.IsDefined(kind) && kind != PerformanceDeviceKind.Memory;
}

/// <summary>Normalizes persisted Performance hardware-name replacement rules.</summary>
internal static class PerformanceHardwareNameReplacementRuleCollection
{
    public static List<PerformanceHardwareNameReplacementRule> Normalize(
        IEnumerable<PerformanceHardwareNameReplacementRule?>? rules)
    {
        List<PerformanceHardwareNameReplacementRule> normalized = [];
        if (rules == null) return normalized;

        foreach (PerformanceHardwareNameReplacementRule? rule in rules)
        {
            if (rule == null || !PerformanceHardwareNameReplacementRule.SupportsDeviceKind(rule.DeviceKind)) continue;
            normalized.Add(new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = rule.DeviceKind,
                MatchPattern = rule.MatchPattern,
                Replacement = rule.Replacement
            });
        }

        return normalized;
    }
}

/// <summary>Applies compiled user regex replacements to Performance hardware names.</summary>
internal sealed class PerformanceHardwareNameResolver
{
    private const string LogContext = "Performance hardware-name";

    private readonly List<(PerformanceDeviceKind DeviceKind, CompiledRegexReplacement Rule)> _rules;

    private PerformanceHardwareNameResolver(List<(PerformanceDeviceKind, CompiledRegexReplacement)> rules) =>
        _rules = rules;

    public static PerformanceHardwareNameResolver Empty { get; } = new([]);

    /// <summary>Compiles valid, non-empty rules while preserving their configured order.</summary>
    public static PerformanceHardwareNameResolver Create(
        IEnumerable<PerformanceHardwareNameReplacementRule?>? rules)
    {
        List<(PerformanceDeviceKind, CompiledRegexReplacement)> compiledRules = [];
        if (rules == null) return new PerformanceHardwareNameResolver(compiledRules);

        foreach (PerformanceHardwareNameReplacementRule? rule in rules)
        {
            if (rule == null
                || !PerformanceHardwareNameReplacementRule.SupportsDeviceKind(rule.DeviceKind)
                || !CompiledRegexReplacement.TryCreate(
                    rule.MatchPattern,
                    rule.Replacement,
                    LogContext,
                    out CompiledRegexReplacement? compiled))
                continue;

            compiledRules.Add((rule.DeviceKind, compiled));
        }

        return new PerformanceHardwareNameResolver(compiledRules);
    }

    /// <summary>Applies every matching rule for the requested device kind from top to bottom.</summary>
    public string Resolve(PerformanceDeviceKind deviceKind, string? hardwareName)
    {
        string resolvedName = hardwareName ?? string.Empty;
        for (int ruleIndex = 0; ruleIndex < _rules.Count; ruleIndex++)
        {
            (PerformanceDeviceKind ruleKind, CompiledRegexReplacement rule) = _rules[ruleIndex];
            if (ruleKind == deviceKind) resolvedName = rule.Apply(resolvedName);
        }

        return resolvedName;
    }
}

/// <summary>
/// One user regex replacement, compiled case-insensitively with a match timeout. A rule that fails or times out
/// while running is switched off, so a bad pattern costs one log line instead of a stall every sample.
/// </summary>
internal sealed class CompiledRegexReplacement
{
    private const int RegexTimeoutMilliseconds = 100;

    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds);

    private readonly Regex _regex;
    private readonly string _replacement;
    private readonly string _logContext;
    private bool _isDisabled;

    private CompiledRegexReplacement(Regex regex, string replacement, string logContext)
    {
        _regex = regex;
        _replacement = replacement;
        _logContext = logContext;
    }

    /// <summary>Compiles a non-empty pattern; an invalid one is logged and yields no rule.</summary>
    public static bool TryCreate(
        string? matchPattern,
        string? replacement,
        string logContext,
        [NotNullWhen(true)] out CompiledRegexReplacement? compiled)
    {
        compiled = null;
        if (string.IsNullOrEmpty(matchPattern)) return false;

        try
        {
            Regex regex = new(
                matchPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
            compiled = new CompiledRegexReplacement(regex, replacement ?? string.Empty, logContext);
            return true;
        }
        catch (ArgumentException exception)
        {
            TADNLog.Log($"{logContext} regex ignored: {exception.Message}");
            return false;
        }
    }

    /// <summary>Returns <paramref name="text"/> with every match replaced, or unchanged once the rule is off.</summary>
    public string Apply(string text)
    {
        if (_isDisabled) return text;

        try
        {
            return _regex.Replace(text, _replacement);
        }
        catch (RegexMatchTimeoutException exception)
        {
            Disable(exception);
        }
        catch (ArgumentException exception)
        {
            Disable(exception);
        }

        return text;
    }

    private void Disable(Exception exception)
    {
        _isDisabled = true;
        TADNLog.Log($"{_logContext} replacement disabled: {exception.Message}");
    }
}

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
            if (rule == null || !Enum.IsDefined(rule.DeviceKind)) continue;
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
    private const int RegexTimeoutMilliseconds = 100;

    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds);

    private readonly List<CompiledReplacementRule> _rules;

    private PerformanceHardwareNameResolver(List<CompiledReplacementRule> rules) =>
        _rules = rules;

    public static PerformanceHardwareNameResolver Empty { get; } = new([]);

    /// <summary>Compiles valid, non-empty rules while preserving their configured order.</summary>
    public static PerformanceHardwareNameResolver Create(
        IEnumerable<PerformanceHardwareNameReplacementRule?>? rules)
    {
        List<CompiledReplacementRule> compiledRules = [];
        if (rules == null) return new PerformanceHardwareNameResolver(compiledRules);

        foreach (PerformanceHardwareNameReplacementRule? rule in rules)
        {
            if (rule == null
                || !Enum.IsDefined(rule.DeviceKind)
                || string.IsNullOrEmpty(rule.MatchPattern))
                continue;

            try
            {
                Regex regex = new(
                    rule.MatchPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout);
                compiledRules.Add(new CompiledReplacementRule(
                    rule.DeviceKind,
                    regex,
                    rule.Replacement));
            }
            catch (ArgumentException exception)
            {
                TADNLog.Log($"Performance hardware-name regex ignored: {exception.Message}");
            }
        }

        return new PerformanceHardwareNameResolver(compiledRules);
    }

    /// <summary>Applies every matching rule for the requested device kind from top to bottom.</summary>
    public string Resolve(PerformanceDeviceKind deviceKind, string? hardwareName)
    {
        string resolvedName = hardwareName ?? string.Empty;
        for (int ruleIndex = 0; ruleIndex < _rules.Count; ruleIndex++)
        {
            CompiledReplacementRule rule = _rules[ruleIndex];
            if (rule.DeviceKind != deviceKind || rule.IsDisabled) continue;

            try
            {
                resolvedName = rule.Regex.Replace(resolvedName, rule.Replacement);
            }
            catch (RegexMatchTimeoutException exception)
            {
                rule.Disable(exception);
            }
            catch (ArgumentException exception)
            {
                rule.Disable(exception);
            }
        }

        return resolvedName;
    }

    private sealed class CompiledReplacementRule(
        PerformanceDeviceKind deviceKind,
        Regex regex,
        string replacement)
    {
        public PerformanceDeviceKind DeviceKind { get; } = deviceKind;

        public Regex Regex { get; } = regex;

        public string Replacement { get; } = replacement;

        public bool IsDisabled { get; private set; }

        /// <summary>Disables a rule after its first runtime failure to avoid repeated UI stalls or logs.</summary>
        public void Disable(Exception exception)
        {
            IsDisabled = true;
            TADNLog.Log($"Performance hardware-name replacement disabled: {exception.Message}");
        }
    }
}

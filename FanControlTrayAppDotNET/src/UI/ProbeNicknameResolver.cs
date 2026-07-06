using System.Text.RegularExpressions;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Resolves user-configured probe-name nicknames.
/// </summary>
internal sealed class ProbeNicknameResolver
{
    private readonly IReadOnlyList<CompiledProbeNicknameRule> _rules;

    private ProbeNicknameResolver(IReadOnlyList<CompiledProbeNicknameRule> rules) => _rules = rules;

    public static ProbeNicknameResolver Empty { get; } = new([]);

    /// <summary>
    /// Builds a resolver from the current settings.
    /// </summary>
    public static ProbeNicknameResolver Create(AppSettings settings) =>
        new(CompileRules(settings.ProbeNicknameRules));

    /// <summary>
    /// Resolves the display name for a probe.
    /// </summary>
    public string Resolve(string? probeName)
    {
        if (string.IsNullOrWhiteSpace(probeName)) return string.Empty;

        string resolved = probeName;
        foreach (CompiledProbeNicknameRule rule in _rules)
            resolved = rule.Regex.Replace(resolved, rule.ReplacementString);

        return resolved.Trim();
    }

    /// <summary>
    /// Compiles user rules and skips invalid regex entries.
    /// </summary>
    private static List<CompiledProbeNicknameRule> CompileRules(IEnumerable<DeviceNicknameRule> rules)
    {
        List<CompiledProbeNicknameRule> compiled = [];
        foreach (DeviceNicknameRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.TargetRegex)) continue;

            try
            {
                Regex regex = new(rule.TargetRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                compiled.Add(new CompiledProbeNicknameRule(regex, rule.ReplacementString ?? string.Empty));
            }
            catch (ArgumentException ex)
            {
                TADNLog.Log($"Probe nickname regex ignored: {ex.Message}");
            }
        }

        return compiled;
    }

    private sealed record CompiledProbeNicknameRule(Regex Regex, string ReplacementString);
}

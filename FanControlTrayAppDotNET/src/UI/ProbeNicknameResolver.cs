using System.Text.RegularExpressions;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Resolves user-configured probe-name nicknames.
/// </summary>
internal sealed class ProbeNicknameResolver
{
    // The last compiled rule set, reused while the settings hold the same rules
    private static ProbeNicknameResolver? s_cached;

    private readonly string _signature;
    private readonly IReadOnlyList<CompiledProbeNicknameRule> _rules;

    private ProbeNicknameResolver(string signature, IReadOnlyList<CompiledProbeNicknameRule> rules)
    {
        _signature = signature;
        _rules = rules;
    }

    public static ProbeNicknameResolver Empty { get; } = new(string.Empty, []);

    /// <summary>
    /// Returns the resolver for the current settings, compiling the rules only when they changed.
    /// </summary>
    public static ProbeNicknameResolver Create(AppSettings settings)
    {
        string signature = NicknameRegex.Signature(settings.ProbeNicknameRules);
        ProbeNicknameResolver? cached = Volatile.Read(ref s_cached);
        if (cached != null && string.Equals(cached._signature, signature, StringComparison.Ordinal)) return cached;

        ProbeNicknameResolver resolver = new(signature, CompileRules(settings.ProbeNicknameRules));
        Volatile.Write(ref s_cached, resolver);
        return resolver;
    }

    /// <summary>
    /// Resolves the display name for a probe.
    /// </summary>
    public string Resolve(string? probeName)
    {
        if (string.IsNullOrWhiteSpace(probeName)) return string.Empty;

        string resolved = probeName;
        foreach (CompiledProbeNicknameRule rule in _rules)
            resolved = NicknameRegex.Replace(rule.Regex, resolved, rule.ReplacementString);

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
            if (NicknameRegex.TryCreate(rule.TargetRegex, "Probe", out Regex? regex))
                compiled.Add(new CompiledProbeNicknameRule(regex, rule.ReplacementString));
        }

        return compiled;
    }

    private sealed record CompiledProbeNicknameRule(Regex Regex, string ReplacementString);
}

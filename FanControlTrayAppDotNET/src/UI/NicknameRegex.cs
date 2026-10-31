using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Builds and runs user nickname patterns with a match timeout, so a backtracking pattern cannot stall the UI thread
/// that also drives curve control. A timed-out match counts as no match.
/// </summary>
internal static class NicknameRegex
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(TimeConstants.NicknameRegexMatchTimeoutMs);

    /// <summary>Compiles one user pattern; invalid patterns are logged and skipped.</summary>
    public static bool TryCreate(string? pattern, string ruleKind, [NotNullWhen(true)] out Regex? regex)
    {
        regex = null;
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            TADNLog.Log($"{ruleKind} nickname regex ignored: {ex.Message}");
            return false;
        }
    }

    public static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            LogTimeout(regex);
            return false;
        }
    }

    /// <summary>Replaces matches, or returns the input unchanged when matching timed out.</summary>
    public static string Replace(Regex regex, string input, string replacement)
    {
        try
        {
            return regex.Replace(input, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            LogTimeout(regex);
            return input;
        }
    }

    /// <summary>Identifies a rule list by content, since the settings page edits rules in place.</summary>
    public static string Signature(IReadOnlyList<DeviceNicknameRule> rules)
    {
        StringBuilder builder = new();
        foreach (DeviceNicknameRule rule in rules)
        {
            builder.Append(rule.TargetRegex).Append('\u0000');
            builder.Append(rule.ReplacementString).Append('\u0001');
        }

        return builder.ToString();
    }

    private static void LogTimeout(Regex regex) =>
        TADNLog.Log($"Nickname regex timed out after {TimeConstants.NicknameRegexMatchTimeoutMs} ms: {regex}");
}

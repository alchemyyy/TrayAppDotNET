using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// One install or uninstall progress update. The line form is the wire format shared by the standard output of
/// headless installs, the elevated helper progress pipes and the .NET Framework installer, which links this file as
/// source; keep it free of APIs that .NET Framework 4.8 lacks.
/// </summary>
public sealed record TrayAppDotNETInstallProgress(int Percent, string Message, bool IsFailure = false)
{
    public const int CompletePercent = 100;
    private const string FailureToken = "FAIL";
    private const char LineOpen = '[';
    private const char LineClose = ']';
    private const char PercentSign = '%';

    /// <summary>True for the final successful update of an operation.</summary>
    public bool IsComplete => !IsFailure && Percent >= CompletePercent;

    /// <summary>Creates a clamped success update.</summary>
    public static TrayAppDotNETInstallProgress At(int percent, string message) =>
        new(ClampPercent(percent), message);

    /// <summary>Creates the terminal failure update.</summary>
    public static TrayAppDotNETInstallProgress Failed(string message) =>
        new(CompletePercent, message, IsFailure: true);

    /// <summary>Formats the update as one wire line: "[42%] message" or "[FAIL] message".</summary>
    public string ToLine()
    {
        string message = SingleLine(Message);
        if (IsFailure) return $"{LineOpen}{FailureToken}{LineClose} {message}";

        return $"{LineOpen}{ClampPercent(Percent).ToString(CultureInfo.InvariantCulture)}{PercentSign}{LineClose} {message}";
    }

    /// <summary>Parses one wire line. Non-progress lines return false so callers can pass them through.</summary>
    public static bool TryParseLine(string? line, [NotNullWhen(true)] out TrayAppDotNETInstallProgress? progress)
    {
        progress = null;
        if (line == null) return false;

        string trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed[0] != LineOpen) return false;

        int closeIndex = trimmed.IndexOf(LineClose);
        if (closeIndex <= 1) return false;

        string token = trimmed.Substring(1, closeIndex - 1);
        string message = trimmed.Substring(closeIndex + 1).Trim();
        if (string.Equals(token, FailureToken, StringComparison.Ordinal))
        {
            progress = Failed(message);
            return true;
        }

        if (token[token.Length - 1] != PercentSign) return false;
        if (!TryParseDigits(token, token.Length - 1, out int percent)) return false;

        progress = At(percent, message);
        return true;
    }

    /// <summary>Collapses line breaks so a value always travels as one wire line.</summary>
    internal static string SingleLine(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    // A hand-written digit parse: the span overload of int.TryParse does not exist on the .NET Framework, and the
    // apps' analyzers reject the Substring overload. Values beyond int range are not progress lines.
    private static bool TryParseDigits(string text, int length, out int value)
    {
        value = 0;
        if (length == 0) return false;

        for (int index = 0; index < length; index++)
        {
            int digit = text[index] - '0';
            if (digit is < 0 or > 9 || value > (int.MaxValue - digit) / 10) return false;
            value = value * 10 + digit;
        }

        return true;
    }

    private static int ClampPercent(int percent) =>
        percent < 0 ? 0 : percent > CompletePercent ? CompletePercent : percent;
}

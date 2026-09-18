using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// One progress update in the wire format shared with the apps' headless installers and the elevated worker:
/// "[42%] message" or "[FAIL] message".
/// NOTE: This mirrors TrayAppDotNETCommon/src/Services/Install/InstallProgress.cs and must stay in sync with it;
/// the installer intentionally does not reference TrayAppDotNETCommon.
/// </summary>
public sealed record InstallProgressLine(int Percent, string Message, bool IsFailure = false)
{
    public const int CompletePercent = 100;
    private const string FailureToken = "FAIL";
    private const char LineOpen = '[';
    private const char LineClose = ']';
    private const char PercentSign = '%';

    /// <summary>True for the final successful update of an operation.</summary>
    public bool IsComplete => !IsFailure && Percent >= CompletePercent;

    /// <summary>Creates a clamped success update.</summary>
    // FrameworkCompatibility.Clamp stands in for Math.Clamp, which the .NET Framework does not define
    public static InstallProgressLine At(int percent, string message) =>
        new(FrameworkCompatibility.Clamp(percent, min: 0, CompletePercent), message);

    /// <summary>Creates the terminal failure update.</summary>
    public static InstallProgressLine Failed(string message) =>
        new(CompletePercent, message, IsFailure: true);

    /// <summary>Formats the update as one wire line: "[42%] message" or "[FAIL] message".</summary>
    public string ToLine()
    {
        string message = SingleLine(Message);
        if (IsFailure) return $"{LineOpen}{FailureToken}{LineClose} {message}";

        int percent = FrameworkCompatibility.Clamp(Percent, min: 0, CompletePercent);
        return $"{LineOpen}{percent.ToString(CultureInfo.InvariantCulture)}{PercentSign}{LineClose} {message}";
    }

    /// <summary>Parses one wire line. Non-progress lines return false so callers can pass them through.</summary>
    public static bool TryParseLine(string? line, [NotNullWhen(true)] out InstallProgressLine? progress)
    {
        progress = null;
        // The FrameworkCompatibility guard restores the nullable flow the unannotated framework method loses
        if (FrameworkCompatibility.IsNullOrWhiteSpace(line)) return false;

        string trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed[0] != LineOpen) return false;

        int closeIndex = trimmed.IndexOf(LineClose);
        if (closeIndex <= 1) return false;

        string token = trimmed[1..closeIndex];
        string message = trimmed[(closeIndex + 1)..].Trim();
        if (string.Equals(token, FailureToken, StringComparison.Ordinal))
        {
            progress = Failed(message);
            return true;
        }

        if (token[^1] != PercentSign) return false;
        if (!int.TryParse(token[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int percent))
            return false;

        progress = At(percent, message);
        return true;
    }

    // The .NET Framework string.Replace(string, string) is already ordinal, so the comparison argument the
    // modern overload took is simply dropped
    private static string SingleLine(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : message.Replace(oldValue: "\r", newValue: " ")
                .Replace(oldValue: "\n", newValue: " ")
                .Trim();
}

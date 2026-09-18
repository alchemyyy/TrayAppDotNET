using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// One install or uninstall progress update. The line form is the wire format shared by the
/// standard output of headless installs, the elevated helper progress pipe, and the installer app.
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
        new(Math.Clamp(percent, min: 0, CompletePercent), message);

    /// <summary>Creates the terminal failure update.</summary>
    public static TrayAppDotNETInstallProgress Failed(string message) =>
        new(CompletePercent, message, IsFailure: true);

    /// <summary>Formats the update as one wire line: "[42%] message" or "[FAIL] message".</summary>
    public string ToLine()
    {
        string message = SingleLine(Message);
        if (IsFailure) return $"{LineOpen}{FailureToken}{LineClose} {message}";

        int percent = Math.Clamp(Percent, min: 0, CompletePercent);
        return $"{LineOpen}{percent.ToString(CultureInfo.InvariantCulture)}{PercentSign}{LineClose} {message}";
    }

    /// <summary>Parses one wire line. Non-progress lines return false so callers can pass them through.</summary>
    public static bool TryParseLine(string? line, [NotNullWhen(true)] out TrayAppDotNETInstallProgress? progress)
    {
        progress = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

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

    private static string SingleLine(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : message.Replace(oldValue: "\r", newValue: " ", StringComparison.Ordinal)
                .Replace(oldValue: "\n", newValue: " ", StringComparison.Ordinal)
                .Trim();
}

/// <summary>Writes progress lines to the invoking console or redirected standard output.</summary>
public sealed class TrayAppDotNETConsoleProgress : IProgress<TrayAppDotNETInstallProgress>
{
    public static readonly TrayAppDotNETConsoleProgress Instance = new();

    public void Report(TrayAppDotNETInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Failure lines stay on standard output so consumers see the protocol in order
        _ = TrayAppDotNETConsoleOutput.TryWriteLine(value.ToLine(), error: false);
    }
}

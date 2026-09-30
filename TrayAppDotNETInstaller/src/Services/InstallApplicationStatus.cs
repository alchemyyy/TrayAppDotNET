using System.Diagnostics.CodeAnalysis;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Where one application of a run stands. The window shows it at the end of the application's row.</summary>
public enum InstallApplicationState
{
    /// <summary>Selected, and not reached yet.</summary>
    Waiting,

    /// <summary>Being extracted or installed.</summary>
    Installing,

    /// <summary>Installed by this run.</summary>
    Installed,

    /// <summary>The run stopped on this application.</summary>
    Failed,

    /// <summary>The run ended before it reached this application.</summary>
    NotInstalled
}

/// <summary>
/// One change in an application's state.
/// The engine reports Installing when it reaches an application, and Installed or Failed when it is done with it.
/// The window derives Waiting and NotInstalled from the plan and the outcome.
///
/// The elevated worker relays these to the window as "[APP:Installing] VolumeTrayAppDotNET" lines between its progress lines.
/// That line belongs to the installer and its worker alone: no app writes it,
/// and the shared "[42%] message" / "[FAIL] message" parsers, here and in TrayAppDotNETCommon, both reject it.
/// </summary>
public sealed record InstallApplicationStatus(string ApplicationName, InstallApplicationState State)
{
    private const string LinePrefix = "[APP:";
    private const char LineClose = ']';
    private const string WaitingToken = "Waiting";
    private const string InstallingToken = "Installing";
    private const string InstalledToken = "Installed";
    private const string FailedToken = "Failed";
    private const string NotInstalledToken = "NotInstalled";

    /// <summary>Formats the status as one worker line: "[APP:Installing] VolumeTrayAppDotNET".</summary>
    public string ToLine() =>
        $"{LinePrefix}{StateToToken(State)}{LineClose} {InstallProgressLine.SingleLine(ApplicationName)}";

    /// <summary>Parses one worker line. Progress lines and anything else malformed return false.</summary>
    public static bool TryParseLine(string? line, [NotNullWhen(true)] out InstallApplicationStatus? status)
    {
        status = null;
        // The FrameworkCompatibility guard restores the nullable flow the unannotated framework method loses
        if (FrameworkCompatibility.IsNullOrWhiteSpace(line)) return false;

        string trimmed = line.Trim();
        if (!trimmed.StartsWith(LinePrefix, StringComparison.Ordinal)) return false;

        // The prefix holds no closing bracket, so the first one found ends the state token
        int closeIndex = trimmed.IndexOf(LineClose);
        if (closeIndex < 0) return false;

        string applicationName = trimmed[(closeIndex + 1)..].Trim();
        if (applicationName.Length == 0) return false;
        if (!TryParseToken(trimmed[LinePrefix.Length..closeIndex], out InstallApplicationState state)) return false;

        status = new InstallApplicationStatus(applicationName, state);
        return true;
    }

    private static string StateToToken(InstallApplicationState state) => state switch
    {
        InstallApplicationState.Waiting => WaitingToken,
        InstallApplicationState.Installing => InstallingToken,
        InstallApplicationState.Installed => InstalledToken,
        InstallApplicationState.Failed => FailedToken,
        InstallApplicationState.NotInstalled => NotInstalledToken,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, message: "Unsupported application state.")
    };

    private static bool TryParseToken(string token, out InstallApplicationState state)
    {
        switch (token)
        {
            case WaitingToken:
                state = InstallApplicationState.Waiting;
                return true;
            case InstallingToken:
                state = InstallApplicationState.Installing;
                return true;
            case InstalledToken:
                state = InstallApplicationState.Installed;
                return true;
            case FailedToken:
                state = InstallApplicationState.Failed;
                return true;
            case NotInstalledToken:
                state = InstallApplicationState.NotInstalled;
                return true;
            default:
                state = InstallApplicationState.Waiting;
                return false;
        }
    }
}

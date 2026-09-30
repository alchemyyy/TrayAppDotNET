namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// The state of every application in one run.
/// Each starts Waiting, follows the statuses the engine reports, and is settled by the outcome when the run ends.
/// A settled state never changes again, so a report that arrives after the outcome cannot undo it.
/// </summary>
public sealed class InstallApplicationTracker(IEnumerable<string> applicationNames)
{
    // Application names are matched the way the payload catalog matches them
    private readonly Dictionary<string, InstallApplicationState> _states = CreateWaitingStates(applicationNames);

    /// <summary>The current state of an application, or null when the run does not include it.</summary>
    public InstallApplicationState? StateOf(string applicationName) =>
        _states.TryGetValue(applicationName, out InstallApplicationState state) ? state : null;

    /// <summary>
    /// Applies one reported status and returns whether it changed anything.
    /// These change nothing: a status for an application outside the run, a repeat of the current state,
    /// a reported Waiting or NotInstalled, and any report about an application that already settled.
    /// Waiting and NotInstalled come only from the plan and from <see cref="Finish"/>.
    /// </summary>
    public bool Apply(InstallApplicationStatus status)
    {
        FrameworkCompatibility.ThrowIfNull(status, nameof(status));

        if (!_states.TryGetValue(status.ApplicationName, out InstallApplicationState currentState)) return false;
        if (currentState == status.State || IsSettled(currentState)) return false;
        if (status.State is InstallApplicationState.Waiting or InstallApplicationState.NotInstalled) return false;

        _states[status.ApplicationName] = status.State;
        return true;
    }

    /// <summary>
    /// Settles every application still open once the run has ended.
    /// A successful outcome means the engine installed the whole plan, so anything a lost report left open was installed.
    /// A failed outcome fails the application the run was working on, and leaves the ones it never reached not installed.
    /// </summary>
    public void Finish(bool success)
    {
        // Copied first: the .NET Framework dictionary invalidates its key enumerator on every value overwrite
        List<string> applicationNames = [];
        applicationNames.AddRange(_states.Keys);
        foreach (string applicationName in applicationNames)
        {
            switch (_states[applicationName])
            {
                case InstallApplicationState.Waiting:
                    _states[applicationName] = success ? InstallApplicationState.Installed : InstallApplicationState.NotInstalled;
                    break;
                case InstallApplicationState.Installing:
                    _states[applicationName] = success ? InstallApplicationState.Installed : InstallApplicationState.Failed;
                    break;
                default:
                    // Installed, Failed and NotInstalled are already settled
                    break;
            }
        }
    }

    private static bool IsSettled(InstallApplicationState state) =>
        state is InstallApplicationState.Installed
            or InstallApplicationState.Failed
            or InstallApplicationState.NotInstalled;

    private static Dictionary<string, InstallApplicationState> CreateWaitingStates(IEnumerable<string> applicationNames)
    {
        FrameworkCompatibility.ThrowIfNull(applicationNames, nameof(applicationNames));

        Dictionary<string, InstallApplicationState> states = new(StringComparer.OrdinalIgnoreCase);
        foreach (string applicationName in applicationNames) states[applicationName] = InstallApplicationState.Waiting;

        return states;
    }
}

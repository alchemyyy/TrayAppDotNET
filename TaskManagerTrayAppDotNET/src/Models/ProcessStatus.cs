namespace TaskManagerTrayAppDotNET.Models;

/// <summary>
/// Processes-page status in Task Manager display precedence; a higher value hides every lower one.
/// </summary>
internal enum ProcessStatus : byte
{
    None,
    Suspended,
    EfficiencyMode,
    NotResponding
}

/// <summary>Applies Task Manager's per-process status rules and labels.</summary>
internal static class ProcessStatusFunctions
{
    public const string SuspendedText = "Suspended";
    public const string EfficiencyModeText = "Efficiency mode";
    public const string NotRespondingText = "Not responding";

    /// <summary>Resolves the one status a process row shows from its independently sampled facts.</summary>
    /// <remarks>
    /// Task Manager skips the hung-window check for a suspended process, whose windows stop pumping messages,
    /// and draws the efficiency glyph in preference to the suspended glyph.
    /// </remarks>
    public static ProcessStatus Resolve(bool isSuspended, bool isNotResponding, bool isEfficiencyMode)
    {
        if (isSuspended) return isEfficiencyMode ? ProcessStatus.EfficiencyMode : ProcessStatus.Suspended;
        if (isNotResponding) return ProcessStatus.NotResponding;
        return isEfficiencyMode ? ProcessStatus.EfficiencyMode : ProcessStatus.None;
    }

    /// <summary>Converts a stored status value, treating unknown values as no status.</summary>
    public static ProcessStatus FromStoredValue(long value) =>
        value is >= (long)ProcessStatus.None and <= (long)ProcessStatus.NotResponding
            ? (ProcessStatus)value
            : ProcessStatus.None;

    /// <summary>Returns Task Manager's status text, or empty for a process with no status.</summary>
    public static string GetText(ProcessStatus status) => status switch
    {
        ProcessStatus.Suspended => SuspendedText,
        ProcessStatus.EfficiencyMode => EfficiencyModeText,
        ProcessStatus.NotResponding => NotRespondingText,
        _ => string.Empty
    };

    /// <summary>Maps displayed status text back to its status.</summary>
    public static bool TryGetStatus(string text, out ProcessStatus status)
    {
        switch (text)
        {
            case SuspendedText:
                status = ProcessStatus.Suspended;
                return true;
            case EfficiencyModeText:
                status = ProcessStatus.EfficiencyMode;
                return true;
            case NotRespondingText:
                status = ProcessStatus.NotResponding;
                return true;
            default:
                status = ProcessStatus.None;
                return false;
        }
    }

    /// <summary>Orders an ascending sort from the most urgent status down, with status-free rows last.</summary>
    public static int GetSortOrder(ProcessStatus status) => status switch
    {
        ProcessStatus.NotResponding => 0,
        ProcessStatus.EfficiencyMode => 1,
        ProcessStatus.Suspended => 2,
        _ => 3
    };
}

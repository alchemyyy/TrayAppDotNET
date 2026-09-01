namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Controls how the Processes page organizes live process instances.</summary>
public enum ProcessGroupingStyle
{
    None,
    ParentProcess,
    Semantic
}

/// <summary>Controls the initial state assigned to each newly encountered process tree.</summary>
public enum ProcessTreeDefaultState
{
    Collapsed,
    Expanded
}

/// <summary>Resolves whether a new process tree should enter the collapsed-state set.</summary>
internal static class ProcessTreeExpansionPolicy
{
    public static bool StartsCollapsed(
        ProcessTreeDefaultState defaultState,
        bool isSemanticSection,
        bool expandSemanticSectionsByDefault) =>
        defaultState == ProcessTreeDefaultState.Collapsed
        && (!isSemanticSection || !expandSemanticSectionsByDefault);
}

/// <summary>Records whether an independent user-facing window query produced a usable fact.</summary>
internal enum ProcessIndependentWindowState : byte
{
    Unknown,
    None,
    Qualifying
}

/// <summary>Immutable process identity and UI facts consumed by semantic grouping.</summary>
internal readonly record struct ProcessGroupingFacts(
    ProcessInstanceKey InstanceKey,
    bool IsCreationTimeKnown,
    int ParentProcessID,
    string ExecutableName,
    string? ExecutablePath,
    string? UserSID,
    int SessionID,
    string? PackageFullName,
    string? ApplicationUserModelID,
    bool IsApplicationUserModelIDAmbiguous,
    ProcessIndependentWindowState IndependentWindowState,
    bool IsCriticalOrProtected,
    ProcessInstanceKey? ExplicitOwnerInstanceKey = null);

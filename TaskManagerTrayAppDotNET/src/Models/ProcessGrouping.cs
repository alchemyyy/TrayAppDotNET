namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Controls how the Processes page organizes live process instances.</summary>
public enum ProcessGroupingStyle
{
    None,
    ParentProcess,
    Semantic
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

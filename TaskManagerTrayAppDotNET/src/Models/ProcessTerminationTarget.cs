namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies one process instance without relying on a reusable PID alone.</summary>
internal readonly record struct ProcessTerminationTarget(
    int ProcessID,
    long CreationTimeFileTime);

/// <summary>Pairs one identity-checked process instance with its confirmation display name.</summary>
internal readonly record struct ProcessEndTaskRequest(
    ProcessTerminationTarget Target,
    string ProcessName);

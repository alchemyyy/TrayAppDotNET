namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies one process instance without relying on a reusable PID alone.</summary>
internal readonly record struct ProcessTerminationTarget(
    int ProcessID,
    long CreationTimeFileTime);

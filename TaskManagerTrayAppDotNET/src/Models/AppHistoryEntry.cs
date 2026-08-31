namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Session-scoped resource usage retained for one executable identity.</summary>
internal sealed record AppHistoryEntry(
    string Key,
    string Name,
    string ExecutablePath,
    ProcessIconSource IconSource,
    long CPUTimeTicks,
    double NetworkBytes,
    bool NotificationsAvailable,
    long NotificationCount);

/// <summary>Immutable view of the current session-scoped app history.</summary>
internal sealed record AppHistorySnapshot(
    DateTimeOffset StartedAt,
    long Version,
    IReadOnlyList<AppHistoryEntry> Entries);

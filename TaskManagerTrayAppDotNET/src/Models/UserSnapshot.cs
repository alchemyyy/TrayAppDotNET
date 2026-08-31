namespace TaskManagerTrayAppDotNET.Models;

internal enum UserSessionState : byte
{
    Active,
    Connected,
    Disconnected,
    Idle,
    Unknown
}

internal enum UserSessionActionError : byte
{
    None,
    UnsupportedPlatform,
    NotEligible,
    NativeFailure
}

/// <summary>Stable identity for one Windows logon session.</summary>
internal readonly record struct UserSessionKey(int SessionID);

/// <summary>Describes one interactive Windows logon session.</summary>
internal sealed record UserSessionInfo(
    int SessionID,
    string UserName,
    string DomainName,
    string StationName,
    UserSessionState State)
{
    public UserSessionKey Key => new(SessionID);

    public string AccountName => string.IsNullOrWhiteSpace(DomainName)
        ? UserName
        : string.Concat(DomainName, str1: "\\", UserName);

    public bool CanDisconnect => UserSessionActions.CanDisconnect(this);
}

/// <summary>Result from a requested Windows user-session action.</summary>
internal readonly record struct UserSessionActionResult(
    bool Succeeded,
    UserSessionActionError Error,
    int NativeErrorCode,
    string ErrorMessage)
{
    public static UserSessionActionResult Success() =>
        new(Succeeded: true, UserSessionActionError.None, NativeErrorCode: 0, string.Empty);

    public static UserSessionActionResult Failure(
        UserSessionActionError error,
        string errorMessage,
        int nativeErrorCode = 0) =>
        new(Succeeded: false, error, nativeErrorCode, errorMessage);
}

/// <summary>Pure capability decisions for user-session actions.</summary>
internal static class UserSessionActions
{
    public static bool CanDisconnect(UserSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.SessionID < 0 || string.IsNullOrWhiteSpace(session.UserName)) return false;

        return session.State switch
        {
            UserSessionState.Active or UserSessionState.Connected or UserSessionState.Idle => true,
            _ => false
        };
    }
}

/// <summary>One process child displayed beneath its owning user session.</summary>
internal sealed record UserProcessSnapshot(
    ProcessInstanceKey Key,
    string ImageKey,
    string Name,
    ProcessIconSource IconSource,
    int SessionID,
    double CPUPercent,
    long WorkingSetBytes,
    bool HasDiskUsage,
    double DiskBytesPerSecond,
    bool HasNetworkUsage,
    double NetworkBytesPerSecond);

/// <summary>One user-session row and its process children.</summary>
internal sealed record UserGroupSnapshot(
    UserSessionInfo Session,
    IReadOnlyList<UserProcessSnapshot> Processes,
    double CPUPercent,
    long WorkingSetBytes,
    bool HasDiskUsage,
    double DiskBytesPerSecond,
    bool HasNetworkUsage,
    double NetworkBytesPerSecond)
{
    public UserSessionKey Key => Session.Key;
    public int ProcessCount => Processes.Count;
    public bool CanDisconnect => Session.CanDisconnect;
}

/// <summary>Deterministically ordered user groups and process children.</summary>
internal sealed record UserSnapshot(IReadOnlyList<UserGroupSnapshot> Groups)
{
    public static UserSnapshot Empty { get; } = new([]);
}

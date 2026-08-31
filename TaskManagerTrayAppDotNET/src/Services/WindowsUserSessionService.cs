using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads and disconnects local interactive sessions through WTS APIs.</summary>
internal sealed class WindowsUserSessionService(Action<string>? log = null)
{
    private const int WTSCurrentServerVersion = 1;
    private const int WTSUserNameInformationClass = 5;
    private const int WTSDomainNameInformationClass = 7;

    private readonly Action<string> _log = log ?? TADNLog.Log;

    public unsafe IReadOnlyList<UserSessionInfo> ReadSessions()
    {
        if (!OperatingSystem.IsWindows()) return [];

        IntPtr sessionBuffer = IntPtr.Zero;
        try
        {
            if (!WTSEnumerateSessionsW(
                    IntPtr.Zero,
                    reserved: 0,
                    WTSCurrentServerVersion,
                    out sessionBuffer,
                    out int sessionCount))
            {
                int errorCode = Marshal.GetLastPInvokeError();
                _log($"WTSEnumerateSessionsW failed ({errorCode}): {GetErrorMessage(errorCode)}");
                return [];
            }

            if (sessionCount <= 0 || sessionBuffer == IntPtr.Zero)
                return [];

            List<UserSessionInfo> sessions = new(sessionCount);
            WTSSessionInfo* nativeSessions = (WTSSessionInfo*)sessionBuffer;
            for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
            {
                WTSSessionInfo nativeSession = nativeSessions[sessionIndex];
                string userName = ReadSessionString(
                    nativeSession.SessionID,
                    WTSUserNameInformationClass);
                if (string.IsNullOrWhiteSpace(userName)) continue;

                string domainName = ReadSessionString(
                    nativeSession.SessionID,
                    WTSDomainNameInformationClass);
                string stationName = nativeSession.StationName == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUni(nativeSession.StationName) ?? string.Empty;
                sessions.Add(new UserSessionInfo(
                    nativeSession.SessionID,
                    userName,
                    domainName,
                    stationName,
                    MapState(nativeSession.State)));
            }

            sessions.Sort(CompareSessions);
            return sessions.ToArray();
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            _log($"Windows user-session enumeration is unavailable: {exception.Message}");
            return [];
        }
        finally
        {
            if (sessionBuffer != IntPtr.Zero) WTSFreeMemory(sessionBuffer);
        }
    }

    public UserSessionActionResult Disconnect(UserSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OperatingSystem.IsWindows())
        {
            return UserSessionActionResult.Failure(
                UserSessionActionError.UnsupportedPlatform,
                errorMessage: "Disconnecting Windows sessions is unavailable on this platform.");
        }

        if (!session.CanDisconnect)
        {
            return UserSessionActionResult.Failure(
                UserSessionActionError.NotEligible,
                errorMessage: "The selected session cannot be disconnected in its current state.");
        }

        try
        {
            if (WTSDisconnectSession(IntPtr.Zero, session.SessionID, wait: false))
                return UserSessionActionResult.Success();

            int errorCode = Marshal.GetLastPInvokeError();
            string errorMessage = GetErrorMessage(errorCode);
            _log($"WTSDisconnectSession failed for session {session.SessionID} ({errorCode}): {errorMessage}");
            return UserSessionActionResult.Failure(
                UserSessionActionError.NativeFailure,
                errorMessage,
                errorCode);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            _log($"Windows user-session disconnect is unavailable: {exception.Message}");
            return UserSessionActionResult.Failure(
                UserSessionActionError.UnsupportedPlatform,
                exception.Message);
        }
    }

    private string ReadSessionString(int sessionID, int informationClass)
    {
        IntPtr valueBuffer = IntPtr.Zero;
        try
        {
            if (!WTSQuerySessionInformationW(
                    IntPtr.Zero,
                    sessionID,
                    informationClass,
                    out valueBuffer,
                    out int byteCount))
            {
                int errorCode = Marshal.GetLastPInvokeError();
                _log(
                    $"WTSQuerySessionInformationW failed for session {sessionID}, class {informationClass} "
                    + $"({errorCode}): {GetErrorMessage(errorCode)}");
                return string.Empty;
            }

            if (valueBuffer == IntPtr.Zero || byteCount <= sizeof(char)) return string.Empty;

            int characterCount = Math.Max(val1: 0, byteCount / sizeof(char) - 1);
            return Marshal.PtrToStringUni(valueBuffer, characterCount).TrimEnd('\0');
        }
        finally
        {
            if (valueBuffer != IntPtr.Zero) WTSFreeMemory(valueBuffer);
        }
    }

    private static UserSessionState MapState(WTSConnectState state) => state switch
    {
        WTSConnectState.Active => UserSessionState.Active,
        WTSConnectState.Connected
            or WTSConnectState.ConnectQuery
            or WTSConnectState.Shadow => UserSessionState.Connected,
        WTSConnectState.Disconnected => UserSessionState.Disconnected,
        WTSConnectState.Idle => UserSessionState.Idle,
        _ => UserSessionState.Unknown
    };

    private static int CompareSessions(UserSessionInfo left, UserSessionInfo right)
    {
        int userComparison = StringComparer.OrdinalIgnoreCase.Compare(left.UserName, right.UserName);
        if (userComparison != 0) return userComparison;

        int domainComparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.DomainName,
            right.DomainName);
        return domainComparison != 0
            ? domainComparison
            : left.SessionID.CompareTo(right.SessionID);
    }

    private static string GetErrorMessage(int errorCode) => new Win32Exception(errorCode).Message;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WTSSessionInfo
    {
        public readonly int SessionID;
        public readonly IntPtr StationName;
        public readonly WTSConnectState State;
    }

    private enum WTSConnectState
    {
        Active,
        Connected,
        ConnectQuery,
        Shadow,
        Disconnected,
        Idle,
        Listen,
        Reset,
        Down,
        Init
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessionsW(
        IntPtr serverHandle,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr serverHandle,
        int sessionID,
        int informationClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSDisconnectSession(
        IntPtr serverHandle,
        int sessionID,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}

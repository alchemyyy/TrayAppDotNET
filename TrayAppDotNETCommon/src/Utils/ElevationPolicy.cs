using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// The one over-the-shoulder (OTS) elevation policy for install, update and uninstall in the apps and, as linked
/// source, in the .NET Framework installer: elevated work runs only as the signed-in user. A standard user cannot
/// elevate their own account; UAC would ask for a different administrator's credentials and run the work as that
/// account, so per-user parts (shortcuts, the startup entry, settings, the progress pipe) would land in, or be
/// refused by, the wrong profile. Every route checks <see cref="ElevationWouldUseAnotherAccount"/> before asking
/// UAC and refuses with one message instead.
/// </summary>
public static class TrayAppDotNETElevationPolicy
{
    // TOKEN_INFORMATION_CLASS.TokenElevationType and its TOKEN_ELEVATION_TYPE values
    private const int TokenElevationTypeInformationClass = 18;
    private const int TokenElevationTypeDefault = 1;
    private const int TokenElevationTypeFull = 2;

    /// <summary>
    /// True when this process is not elevated and elevating it would run as another account: the token is not a
    /// split UAC token and the user is not an administrator. False when this process is already elevated, when the
    /// user is an administrator whose elevation stays in the same account, or when the token cannot be read.
    /// </summary>
    public static bool ElevationWouldUseAnotherAccount(Action<string>? log = null)
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (GetTokenInformation(identity.Token, TokenElevationTypeInformationClass, out int elevationType,
                    sizeof(int), out int _) == 0)
            {
                log?.Invoke($"TrayAppDotNETElevationPolicy: GetTokenInformation failed with {Marshal.GetLastWin32Error()}");
                return false;
            }

            // A split token (limited or full) belongs to an administrator, so elevation keeps the account. A default
            // token is either a full administrator with UAC off or a standard user, who can only elevate as someone else
            if (elevationType != TokenElevationTypeDefault) return false;
            return !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            log?.Invoke($"TrayAppDotNETElevationPolicy: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// True when this process runs elevated under UAC, so the signed-in user also holds the unelevated token the
    /// desktop runs with and apps should be started through the desktop shell. False with UAC off, for a standard
    /// user, and when the token cannot be read.
    /// </summary>
    public static bool IsSplitTokenElevated(Action<string>? log = null)
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (GetTokenInformation(identity.Token, TokenElevationTypeInformationClass, out int elevationType,
                    sizeof(int), out int _) != 0)
                return elevationType == TokenElevationTypeFull;

            log?.Invoke($"TrayAppDotNETElevationPolicy: GetTokenInformation failed with {Marshal.GetLastWin32Error()}");
            return false;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            log?.Invoke($"TrayAppDotNETElevationPolicy: {exception.Message}");
            return false;
        }
    }

    // DllImport with blittable arguments works under Native AOT and on the .NET Framework alike
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int GetTokenInformation(
        IntPtr token,
        int informationClass,
        out int information,
        int informationLength,
        out int returnLength);
}

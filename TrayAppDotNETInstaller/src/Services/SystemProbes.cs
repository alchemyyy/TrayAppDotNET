using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Cheap environment checks that shape the installer UI.</summary>
public static class SystemProbes
{
    private const string Kernel32 = "kernel32.dll";
    private const string Advapi32 = "advapi32.dll";

    // SYSTEM_POWER_STATUS.BatteryFlag: 128 means no system battery, 255 means unknown
    private const byte BatteryFlagNoSystemBattery = 128;

    // TOKEN_INFORMATION_CLASS.TokenElevationType, and TOKEN_ELEVATION_TYPE.TokenElevationTypeFull
    private const int TokenElevationTypeInformationClass = 18;
    private const int TokenElevationTypeFull = 2;

    public static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            InstallerLog.Write("SystemProbes.IsElevated", exception);
            return false;
        }
    }

    /// <summary>
    /// True when this process runs elevated under UAC, so the signed-in user also holds the unelevated token the
    /// desktop runs with. False with UAC turned off, where every process already runs with the full token, and
    /// for a standard user. When the token cannot be read this falls back to <see cref="IsElevated"/>.
    /// </summary>
    public static bool IsSplitTokenElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (GetTokenInformation(
                    identity.Token,
                    TokenElevationTypeInformationClass,
                    out int elevationType,
                    sizeof(int),
                    out int _))
            {
                return elevationType == TokenElevationTypeFull;
            }

            InstallerLog.Write($"SystemProbes.IsSplitTokenElevated: GetTokenInformation failed with {Marshal.GetLastWin32Error()}");
        }
        catch (Exception exception)
        {
            InstallerLog.Write("SystemProbes.IsSplitTokenElevated", exception);
        }

        return IsElevated();
    }

    /// <summary>False only when Windows positively reports no system battery; API failure counts as present.</summary>
    public static bool HasSystemBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                InstallerLog.Write($"SystemProbes.HasSystemBattery: GetSystemPowerStatus failed with {Marshal.GetLastWin32Error()}");
                return true;
            }

            return InterpretBatteryFlag(status.BatteryFlag);
        }
        catch (Exception exception)
        {
            InstallerLog.Write("SystemProbes.HasSystemBattery", exception);
            return true;
        }
    }

    /// <summary>Unknown (255) counts as present so the battery app stays selectable.</summary>
    public static bool InterpretBatteryFlag(byte batteryFlag) => batteryFlag != BatteryFlagNoSystemBattery;

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop
    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int informationClass,
        out int information,
        int informationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}

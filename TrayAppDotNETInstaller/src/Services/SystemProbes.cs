using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Cheap environment checks that shape the installer UI.</summary>
public static class SystemProbes
{
    private const string Kernel32 = "kernel32.dll";

    // SYSTEM_POWER_STATUS.BatteryFlag: 128 means no system battery, 255 means unknown
    private const byte BatteryFlagNoSystemBattery = 128;

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

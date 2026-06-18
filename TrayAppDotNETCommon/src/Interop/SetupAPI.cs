using System.Runtime.InteropServices;
using System.Text;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// P/Invoke surface for class-based device enumeration via SetupAPI.
/// Callers pass the setup-class GUID for the device class they want to enumerate
/// (monitors, displays, ports, etc.).
/// </summary>
public static class SetupAPI
{
    public const int DIGCF_PRESENT = 0x00000002;

    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumeratorHandle, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr devInfoSet, int memberIndex, ref SP_DEVINFO_DATA devInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr devInfoSet,
        ref SP_DEVINFO_DATA devInfoData,
        [Out] StringBuilder deviceInstanceID,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfoSet);

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
}

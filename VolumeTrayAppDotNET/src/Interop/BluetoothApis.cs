using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Interop;

/// <summary>Native Bluetooth radio enumeration and device-control calls.</summary>
internal static class BluetoothApis
{
    // CTL_CODE(FILE_DEVICE_BLUETOOTH, 3, METHOD_BUFFERED, FILE_ANY_ACCESS).
    public const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x0041000C;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    // TrayAppDotNET is x64-only. These explicit layouts match bluetoothapis.h under the Windows
    // x64 ABI while avoiding marshalling the unused 248-character device-name buffer.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        [FieldOffset(0)] public uint dwSize;
        [FieldOffset(4)] public int fReturnAuthenticated;
        [FieldOffset(8)] public int fReturnRemembered;
        [FieldOffset(12)] public int fReturnUnknown;
        [FieldOffset(16)] public int fReturnConnected;
        [FieldOffset(20)] public int fIssueInquiry;
        [FieldOffset(24)] public byte cTimeoutMultiplier;
        [FieldOffset(32)] public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Explicit, Size = 560)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        [FieldOffset(0)] public uint dwSize;
        [FieldOffset(8)] public ulong Address;
        [FieldOffset(16)] public uint ulClassofDevice;
        [FieldOffset(20)] public int fConnected;
        [FieldOffset(24)] public int fRemembered;
        [FieldOffset(28)] public int fAuthenticated;
    }

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextDevice(
        IntPtr hFind,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp,
        out IntPtr phRadio);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        ref ulong lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}

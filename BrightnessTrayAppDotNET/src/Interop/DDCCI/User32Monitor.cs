using System.Runtime.InteropServices;

namespace BrightnessTrayAppDotNET.Interop.DDCCI;

/// <summary>
/// Subset of user32.dll needed for monitor enumeration.
/// Kept separate from the app-wide Win32 helpers so DDCCI stays self-contained
/// and the struct marshalling stays compatible with the Monitor Configuration API.
/// </summary>
internal static class User32Monitor
{
    /// <summary>
    /// Passed to <see cref="EnumDisplayDevices"/> to receive the device-interface path
    /// (e.g. <c>\\?\DISPLAY#LGE1234#5&amp;abc&amp;0&amp;UID123#{GUID}</c>)
    /// in <see cref="DisplayDevice.DeviceID"/> instead of the <c>MONITOR\LGE1234\{GUID}\0001</c> form.
    /// The interface path maps one-to-one onto the
    /// <c>HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\...</c> registry subtree where EDID is stored.
    /// </summary>
    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(HandleRef hMonitor, [In, Out] MonitorInfoEx lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DisplayDevice lpDisplayDevice,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    public class MonitorInfoEx
    {
        internal int cbSize = Marshal.SizeOf<MonitorInfoEx>();
        internal Rect rcMonitor;
        internal Rect rcWork;
        internal int dwFlags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal char[] szDevice = new char[32];
    }

    /// <summary>
    /// Mirror of Win32 <c>DISPLAY_DEVICEW</c>.
    /// Used with <see cref="EnumDisplayDevices"/> to map an adapter device name (e.g. <c>\\.\DISPLAY1</c>)
    /// to the attached monitor's stable DeviceID (e.g. <c>MONITOR\LGE1234\{GUID}\0001</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}

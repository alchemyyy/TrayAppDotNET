using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// P/Invoke surface for <c>RegisterDeviceNotification</c>.
/// Callers supply the device-interface class GUID they care about,
/// so this helper can scope WM_DEVICECHANGE to any specific class
/// (monitors, USB HID, audio endpoints, etc.) without hardcoding a feature.
/// </summary>
public static class DeviceNotification
{
    public const int WM_DEVICECHANGE = 0x0219;

    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;

    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;

        public Guid dbcc_classguid;
        // dbcc_name follows as a variable-length UTF-16 string; not needed here.
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "RegisterDeviceNotificationW")]
    public static extern IntPtr RegisterDeviceNotification(
        IntPtr hRecipient, IntPtr deviceFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterDeviceNotification(IntPtr registrationHandle);

    /// <summary>
    /// Registers <paramref name="hwnd"/> for <c>WM_DEVICECHANGE</c> notifications scoped to
    /// the device interface class identified by <paramref name="interfaceClassGuid"/>.
    /// Returns the registration handle, or <see cref="IntPtr.Zero"/> on failure
    /// (logged via <see cref="TADNLog.Log(string)"/>).
    /// <paramref name="ownerLabel"/> is the diagnostic log prefix;
    /// <paramref name="failureModeSuffix"/> describes the caller's fallback behavior.
    /// </summary>
    public static IntPtr RegisterForDeviceInterface(
        IntPtr hwnd, Guid interfaceClassGuid, string ownerLabel, string failureModeSuffix)
    {
        DEV_BROADCAST_DEVICEINTERFACE filter = new()
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = interfaceClassGuid,
        };

        IntPtr buffer = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            IntPtr handle = RegisterDeviceNotification(hwnd, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);

            if (handle == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"{ownerLabel}: RegisterDeviceNotification failed " +
                    $"({Marshal.GetLastWin32Error()}) - {failureModeSuffix}");
            }

            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

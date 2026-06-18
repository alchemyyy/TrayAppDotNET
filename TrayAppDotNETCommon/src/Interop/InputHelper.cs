using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// Raw Input subscription helpers.
/// Used by the shell tray icon host to receive WM_INPUT (mouse wheel) only while the cursor is over the tray icon -
/// no global hooks.
/// </summary>
public static class InputHelper
{
    public static void RegisterForMouseInput(IntPtr handle)
    {
        User32.RAWINPUTDEVICE device = new()
        {
            usUsagePage = User32.HID_USAGE_PAGE_GENERIC,
            usUsage = User32.HID_USAGE_GENERIC_MOUSE,
            dwFlags = User32.RIDEV_INPUTSINK,
            hwndTarget = handle,
        };

        if (!RegisterRawInputDevice(device))
            TADNLog.Log($"InputHelper.RegisterForMouseInput failed: {Marshal.GetLastWin32Error()}");
    }

    public static void UnregisterForMouseInput()
    {
        User32.RAWINPUTDEVICE device = new()
        {
            usUsagePage = User32.HID_USAGE_PAGE_GENERIC,
            usUsage = User32.HID_USAGE_GENERIC_MOUSE,
            dwFlags = User32.RIDEV_REMOVE,
            hwndTarget = IntPtr.Zero,
        };

        if (!RegisterRawInputDevice(device))
            TADNLog.Log($"InputHelper.UnregisterForMouseInput failed: {Marshal.GetLastWin32Error()}");
    }

    private static bool RegisterRawInputDevice(User32.RAWINPUTDEVICE device)
    {
        IntPtr nativeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(device));
        try
        {
            Marshal.StructureToPtr(device, nativeBuffer, false);
            return User32.RegisterRawInputDevices(nativeBuffer, 1, (uint)Marshal.SizeOf(device));
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    /// <summary>
    /// Parses a WM_INPUT lParam.
    /// Returns true if the packet is a mouse event;
    /// sets <paramref name="wheelDelta"/> when the packet carries a wheel rotation.
    /// </summary>
    public static bool ProcessMouseInputMessage(IntPtr lParam, out int wheelDelta)
    {
        wheelDelta = 0;

        uint headerSize = (uint)Marshal.SizeOf<User32.RAWINPUTHEADER>();
        uint inputDataSize = 0;
        if (User32.GetRawInputData(lParam, User32.RID_INPUT, IntPtr.Zero, ref inputDataSize, headerSize) != 0)
            return false;

        IntPtr buffer = Marshal.AllocHGlobal((int)inputDataSize);
        try
        {
            uint written = User32.GetRawInputData(lParam, User32.RID_INPUT, buffer, ref inputDataSize, headerSize);
            if (written != inputDataSize) return false;

            User32.RAWINPUT raw = Marshal.PtrToStructure<User32.RAWINPUT>(buffer);
            if (raw.header.dwType != User32.RIM_TYPEMOUSE) return false;

            if ((raw.mouse.usButtonFlags & User32.RI_MOUSE_WHEEL) == User32.RI_MOUSE_WHEEL)
                wheelDelta = raw.mouse.usButtonData;

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// Raw Input subscription helpers.
/// Used by the shell tray icon host to receive WM_INPUT (mouse wheel) only while the cursor is over the tray icon -
/// no global hooks.
/// </summary>
public static class InputHelper
{
    private static readonly RawInputRegistration[] RawInputRegistrations =
    [
        new("generic-mouse", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_MOUSE),
        new("generic-pointer", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_POINTER),
        new("generic-wheel", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_WHEEL),
        new("digitizer-touchpad", User32.HID_USAGE_PAGE_DIGITIZER, User32.HID_USAGE_DIGITIZER_TOUCH_PAD)
    ];

    public static bool RegisterForMouseInput(IntPtr handle)
    {
        bool anyRegistered = false;
        foreach (RawInputRegistration registration in RawInputRegistrations)
        {
            User32.RAWINPUTDEVICE device = new()
            {
                usUsagePage = registration.UsagePage,
                usUsage = registration.Usage,
                dwFlags = User32.RIDEV_INPUTSINK | User32.RIDEV_DEVNOTIFY,
                hwndTarget = handle
            };

            bool registered = RegisterRawInputDevice(device);
            if (registered)
            {
                anyRegistered = true;
                TADNLog.Log(
                    "TrayInputDiag.RawInput.Register: "
                    + $"{registration.Name} page=0x{registration.UsagePage:X2} usage=0x{registration.Usage:X2}");
            }
            else
            {
                TADNLog.Log(
                    "TrayInputDiag.RawInput.Register failed: "
                    + $"{registration.Name} page=0x{registration.UsagePage:X2} usage=0x{registration.Usage:X2} "
                    + $"error={Marshal.GetLastWin32Error()}");
            }
        }

        return anyRegistered;
    }

    public static bool UnregisterForMouseInput()
    {
        bool allUnregistered = true;
        foreach (RawInputRegistration registration in RawInputRegistrations)
        {
            User32.RAWINPUTDEVICE device = new()
            {
                usUsagePage = registration.UsagePage,
                usUsage = registration.Usage,
                dwFlags = User32.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            };

            bool unregistered = RegisterRawInputDevice(device);
            if (!unregistered)
            {
                allUnregistered = false;
                TADNLog.Log(
                    "TrayInputDiag.RawInput.Unregister failed: "
                    + $"{registration.Name} page=0x{registration.UsagePage:X2} usage=0x{registration.Usage:X2} "
                    + $"error={Marshal.GetLastWin32Error()}");
            }
        }

        return allUnregistered;
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
        if (!TryReadRawInputMessage(lParam, out RawInputDiagnostics rawInput)) return false;

        wheelDelta = rawInput.WheelDelta;
        return rawInput.Type == User32.RIM_TYPEMOUSE;
    }

    public static bool TryReadRawInputMessage(IntPtr lParam, out RawInputDiagnostics diagnostics)
    {
        diagnostics = default;

        uint headerSize = (uint)Marshal.SizeOf<User32.RAWINPUTHEADER>();
        uint inputDataSize = 0;
        if (User32.GetRawInputData(lParam, User32.RID_INPUT, IntPtr.Zero, ref inputDataSize, headerSize) != 0)
            return false;

        if (inputDataSize == 0) return false;

        IntPtr buffer = Marshal.AllocHGlobal((int)inputDataSize);
        try
        {
            uint written = User32.GetRawInputData(lParam, User32.RID_INPUT, buffer, ref inputDataSize, headerSize);
            if (written != inputDataSize) return false;

            User32.RAWINPUT raw = Marshal.PtrToStructure<User32.RAWINPUT>(buffer);
            diagnostics = RawInputDiagnostics.FromRawInput(raw, buffer, inputDataSize);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed record RawInputRegistration(string Name, ushort UsagePage, ushort Usage);

    public readonly record struct RawInputDiagnostics(
        uint Type,
        IntPtr Device,
        IntPtr RawWParam,
        ushort MouseButtonFlags,
        int WheelDelta,
        int HorizontalWheelDelta,
        int LastX,
        int LastY,
        uint RawButtons,
        uint HidSize,
        uint HidCount,
        string HidData)
    {
        public bool HasWheelDelta => WheelDelta != 0 || HorizontalWheelDelta != 0;
        public bool IsMouse => Type == User32.RIM_TYPEMOUSE;
        public bool IsHid => Type == User32.RIM_TYPEHID;

        public static RawInputDiagnostics FromRawInput(User32.RAWINPUT raw, IntPtr rawInputBuffer, uint inputDataSize)
        {
            int wheelDelta = 0;
            int horizontalWheelDelta = 0;
            if (raw.header.dwType == User32.RIM_TYPEMOUSE)
            {
                if ((raw.mouse.usButtonFlags & User32.RI_MOUSE_WHEEL) == User32.RI_MOUSE_WHEEL)
                    wheelDelta = raw.mouse.usButtonData;

                if ((raw.mouse.usButtonFlags & User32.RI_MOUSE_HWHEEL) == User32.RI_MOUSE_HWHEEL)
                    horizontalWheelDelta = raw.mouse.usButtonData;
            }

            string hidData = raw.header.dwType == User32.RIM_TYPEHID
                ? ReadHidPayloadHex(raw, rawInputBuffer, inputDataSize)
                : string.Empty;

            return new RawInputDiagnostics(
                raw.header.dwType,
                raw.header.hDevice,
                raw.header.wParam,
                raw.mouse.usButtonFlags,
                wheelDelta,
                horizontalWheelDelta,
                raw.mouse.lLastX,
                raw.mouse.lLastY,
                raw.mouse.ulRawButtons,
                raw.hid.dwSizeHid,
                raw.hid.dwCount,
                hidData);
        }

        private static string ReadHidPayloadHex(User32.RAWINPUT raw, IntPtr rawInputBuffer, uint inputDataSize)
        {
            ulong byteCount = (ulong)raw.hid.dwSizeHid * raw.hid.dwCount;
            if (byteCount == 0) return string.Empty;

            int payloadOffset = Marshal.OffsetOf<User32.RAWINPUT>(nameof(User32.RAWINPUT.hid)).ToInt32()
                                + Marshal.SizeOf<User32.RAWHID>();
            if (payloadOffset < 0 || payloadOffset >= inputDataSize) return string.Empty;

            int available = checked((int)Math.Min(byteCount, inputDataSize - (uint)payloadOffset));
            int count = Math.Min(available, 64);
            byte[] bytes = new byte[count];
            Marshal.Copy(IntPtr.Add(rawInputBuffer, payloadOffset), bytes, 0, count);
            return Convert.ToHexString(bytes);
        }
    }
}

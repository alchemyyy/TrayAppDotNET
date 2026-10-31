using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// Raw Input subscription helpers.
/// Used by the shell tray icon host to receive WM_INPUT (mouse wheel and precision touchpad reports) only while the
/// cursor is over the tray icon - no global hooks.
/// </summary>
public static class InputHelper
{
    // x64 layout: RAWINPUTHEADER is 24 bytes, RAWHID's two counts follow it, then the report bytes
    private const int RawInputUnionOffset = 24;
    private const int RawHidDataOffset = RawInputUnionOffset + 8;

    private static readonly RawInputRegistration[] RawInputRegistrations =
    [
        new(Name: "generic-mouse", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_MOUSE),
        new(Name: "generic-pointer", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_POINTER),
        new(Name: "generic-wheel", User32.HID_USAGE_PAGE_GENERIC, User32.HID_USAGE_GENERIC_WHEEL),
        new(Name: "digitizer-touchpad", User32.HID_USAGE_PAGE_DIGITIZER, User32.HID_USAGE_DIGITIZER_TOUCH_PAD)
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

            if (RegisterRawInputDevice(device))
            {
                anyRegistered = true;
                continue;
            }

            TADNLog.LogDebug(
                "TrayInputDiag.RawInput.Register failed: "
                + $"{registration.Name} page=0x{registration.UsagePage:X2} usage=0x{registration.Usage:X2} "
                + $"error={Marshal.GetLastWin32Error()}");
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

            if (RegisterRawInputDevice(device)) continue;

            allUnregistered = false;
            TADNLog.LogDebug(
                "TrayInputDiag.RawInput.Unregister failed: "
                + $"{registration.Name} page=0x{registration.UsagePage:X2} usage=0x{registration.Usage:X2} "
                + $"error={Marshal.GetLastWin32Error()}");
        }

        return allUnregistered;
    }

    private static unsafe bool RegisterRawInputDevice(User32.RAWINPUTDEVICE device) =>
        User32.RegisterRawInputDevices(
            (IntPtr)(&device),
            numDevices: 1,
            (uint)sizeof(User32.RAWINPUTDEVICE));

    /// <summary>
    /// Copies one WM_INPUT packet into <paramref name="buffer"/>, growing it only when a packet is larger than any
    /// before, and decodes its header. HID report bytes stay in the buffer; read them with
    /// <see cref="RawInputPacket.HidReport"/>.
    /// </summary>
    public static unsafe bool TryReadRawInput(IntPtr lParam, ref byte[] buffer, out RawInputPacket packet)
    {
        packet = default;

        uint headerSize = (uint)sizeof(User32.RAWINPUTHEADER);
        uint inputDataSize = 0;
        if (User32.GetRawInputData(lParam, User32.RID_INPUT, IntPtr.Zero, ref inputDataSize, headerSize) != 0)
            return false;

        if (inputDataSize < headerSize) return false;
        if (buffer.Length < inputDataSize) buffer = new byte[inputDataSize];

        uint written;
        fixed (byte* data = buffer)
            written = User32.GetRawInputData(lParam, User32.RID_INPUT, (IntPtr)data, ref inputDataSize, headerSize);

        if (written != inputDataSize) return false;

        packet = RawInputPacket.Decode(buffer.AsSpan(start: 0, (int)written));
        return packet.Length > 0;
    }

    private sealed record RawInputRegistration(string Name, ushort UsagePage, ushort Usage);

    /// <summary>The decoded header of one raw input packet.</summary>
    public readonly record struct RawInputPacket(
        uint Type,
        IntPtr Device,
        ushort MouseButtonFlags,
        int WheelDelta,
        int HorizontalWheelDelta,
        int HidReportSize,
        int HidReportCount,
        int Length)
    {
        public bool IsMouse => Type == User32.RIM_TYPEMOUSE;
        public bool IsHid => Type == User32.RIM_TYPEHID;

        /// <summary>Returns one HID report of this packet from the buffer it was read into.</summary>
        public ReadOnlySpan<byte> HidReport(ReadOnlySpan<byte> buffer, int index)
        {
            if (!IsHid || (uint)index >= (uint)HidReportCount || HidReportSize <= 0) return default;

            long start = RawHidDataOffset + (long)index * HidReportSize;
            if (start + HidReportSize > Length || start + HidReportSize > buffer.Length) return default;

            return buffer.Slice((int)start, HidReportSize);
        }

        /// <summary>Decodes the header, the mouse fields and the HID report layout of one packet.</summary>
        internal static RawInputPacket Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < RawInputUnionOffset) return default;

            User32.RAWINPUTHEADER header = MemoryMarshal.Read<User32.RAWINPUTHEADER>(data);
            switch (header.dwType)
            {
                case User32.RIM_TYPEMOUSE when data.Length >= RawInputUnionOffset + Marshal.SizeOf<User32.RAWMOUSE>():
                {
                    User32.RAWMOUSE mouse = MemoryMarshal.Read<User32.RAWMOUSE>(data[RawInputUnionOffset..]);
                    int wheel = (mouse.usButtonFlags & User32.RI_MOUSE_WHEEL) != 0 ? mouse.usButtonData : 0;
                    int horizontalWheel = (mouse.usButtonFlags & User32.RI_MOUSE_HWHEEL) != 0 ? mouse.usButtonData : 0;
                    return new RawInputPacket(
                        header.dwType,
                        header.hDevice,
                        mouse.usButtonFlags,
                        wheel,
                        horizontalWheel,
                        HidReportSize: 0,
                        HidReportCount: 0,
                        data.Length);
                }
                case User32.RIM_TYPEHID when data.Length >= RawHidDataOffset:
                {
                    User32.RAWHID hid = MemoryMarshal.Read<User32.RAWHID>(data[RawInputUnionOffset..]);
                    long available = data.Length - RawHidDataOffset;
                    int reportSize = (int)Math.Min(hid.dwSizeHid, int.MaxValue);
                    int reportCount = reportSize == 0 ? 0 : (int)Math.Min(hid.dwCount, available / reportSize);
                    return new RawInputPacket(
                        header.dwType,
                        header.hDevice,
                        MouseButtonFlags: 0,
                        WheelDelta: 0,
                        HorizontalWheelDelta: 0,
                        reportSize,
                        reportCount,
                        data.Length);
                }
                default:
                    return new RawInputPacket(
                        header.dwType,
                        header.hDevice,
                        MouseButtonFlags: 0,
                        WheelDelta: 0,
                        HorizontalWheelDelta: 0,
                        HidReportSize: 0,
                        HidReportCount: 0,
                        data.Length);
            }
        }
    }
}

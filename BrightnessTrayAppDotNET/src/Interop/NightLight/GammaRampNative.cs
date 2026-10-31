using System.Runtime.InteropServices;
using BrightnessTrayAppDotNET.Interop.DDCCI;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>One active GDI display source, e.g. <c>\\.\DISPLAY1</c>.</summary>
/// <param name="DeviceName">GDI device name passed to <c>CreateDC</c>.</param>
/// <param name="IsAdvancedColor">True when Windows runs this source in advanced color (HDR), where the GDI gamma
/// ramp has no effect.</param>
internal readonly record struct GammaRampDisplay(string DeviceName, bool IsAdvancedColor);

/// <summary>
/// GDI gamma-ramp and CCD advanced-color calls used by <see cref="GammaRampNightLight"/>.
/// </summary>
internal static class GammaRampNative
{
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const uint AdvancedColorEnabledBit = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public CCD.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDCW", SetLastError = true)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "SetDeviceGammaRamp")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRampNative(IntPtr hdc, [In] ushort[] lpRamp);

    [DllImport("gdi32.dll", EntryPoint = "GetDeviceGammaRamp")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRampNative(IntPtr hdc, [Out] ushort[] lpRamp);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetAdvancedColorInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO packet);

    /// <summary>Opens a device context on one display. Returns <see cref="IntPtr.Zero"/> on failure.</summary>
    public static IntPtr OpenDisplayDC(string deviceName) =>
        CreateDC(deviceName, lpszDevice: null, lpszOutput: null, IntPtr.Zero);

    public static void CloseDisplayDC(IntPtr hdc)
    {
        if (hdc != IntPtr.Zero) DeleteDC(hdc);
    }

    /// <summary>
    /// Hands a full 3x256 ramp to the display. False when Windows or the driver refuses it, e.g. because it
    /// deviates further from identity than <c>GdiIcmGammaRange</c> allows.
    /// </summary>
    public static bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp) =>
        ramp.Length == GammaRampMath.RampLength && SetDeviceGammaRampNative(hdc, ramp);

    public static bool GetDeviceGammaRamp(IntPtr hdc, ushort[] ramp) =>
        ramp.Length == GammaRampMath.RampLength && GetDeviceGammaRampNative(hdc, ramp);

    /// <summary>
    /// Lists active display sources with their advanced-color state. A source that drives several targets (clone
    /// mode) counts as advanced color when any of its targets is. Null when CCD fails (e.g. access denied in a
    /// disconnected remote session); <see cref="CCD.TryQueryActivePaths(string, out CCD.DISPLAYCONFIG_PATH_INFO[])"/>
    /// retries the buffer-size race with a topology change first.
    /// </summary>
    public static List<GammaRampDisplay>? EnumerateActiveDisplays()
    {
        if (!CCD.TryQueryActivePaths(nameof(GammaRampNative), out CCD.DISPLAYCONFIG_PATH_INFO[] paths))
            return null;

        List<GammaRampDisplay> displays = [];

        foreach (CCD.DISPLAYCONFIG_PATH_INFO path in paths)
        {
            CCD.DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = new()
            {
                header = new CCD.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = CCD.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<CCD.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            if (CCD.DisplayConfigGetDeviceInfo(ref sourceName) != 0) continue;

            string deviceName = sourceName.viewGdiDeviceName;
            if (string.IsNullOrEmpty(deviceName)) continue;

            bool isAdvancedColor = IsTargetAdvancedColor(path.targetInfo);
            int existing = IndexOf(displays, deviceName);
            if (existing >= 0)
            {
                if (isAdvancedColor) displays[existing] = displays[existing] with { IsAdvancedColor = true };
                continue;
            }

            displays.Add(new GammaRampDisplay(deviceName, isAdvancedColor));
        }

        return displays;
    }

    private static int IndexOf(List<GammaRampDisplay> displays, string deviceName)
    {
        for (int i = 0; i < displays.Count; i++)
        {
            if (string.Equals(displays[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool IsTargetAdvancedColor(CCD.DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new()
        {
            header = new CCD.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                adapterId = target.adapterId,
                id = target.id
            }
        };

        // Older drivers and builds reject the query; treat that as SDR, which is what they can drive.
        return DisplayConfigGetAdvancedColorInfo(ref info) == 0 && (info.value & AdvancedColorEnabledBit) != 0;
    }
}

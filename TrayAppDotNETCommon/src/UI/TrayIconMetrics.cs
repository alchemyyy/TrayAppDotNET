using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETTrayIconMetrics
{
    private const string TaskbarWindowClassName = "Shell_TrayWnd";
    private const uint DefaultDPI = 96;
    private const int DefaultSmallIconSize = 16;

    public static int GetTaskbarSmallIconSize() =>
        GetSmallIconSizeForDPI(GetTaskbarDPI());

    public static uint GetTaskbarDPI()
    {
        IntPtr taskbarHwnd = User32.FindWindow(TaskbarWindowClassName, null);
        if (taskbarHwnd != IntPtr.Zero)
        {
            uint dpi = User32.GetDpiForWindow(taskbarHwnd);
            if (dpi > 0) return dpi;
        }

        IntPtr deviceContext = User32.GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return DefaultDPI;

        try
        {
            int dpi = User32.GetDeviceCaps(deviceContext, User32.LOGPIXELSX);
            return dpi > 0 ? (uint)dpi : DefaultDPI;
        }
        finally
        {
            _ = User32.ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    public static int GetSmallIconSizeForDPI(uint dpi)
    {
        int size = User32.GetSystemMetricsForDpi(User32.SM_CXSMICON, dpi);
        return size > 0 ? size : (int)Math.Round(DefaultSmallIconSize * dpi / (double)DefaultDPI);
    }
}

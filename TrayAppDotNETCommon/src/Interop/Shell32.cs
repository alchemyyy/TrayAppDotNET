using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// Shell32.dll interop declarations for system tray notification icons.
/// </summary>
public static class Shell32
{
    /// <summary>
    /// Registered message sent when the taskbar is recreated (e.g., explorer.exe restarts).
    /// </summary>
    public static readonly int WM_TASKBARCREATED = User32.RegisterWindowMessage("TaskbarCreated");

    public const int NOTIFYICON_VERSION_4 = 4;

    public enum NotifyIconMessage
    {
        NIM_ADD = 0x00000000,
        NIM_MODIFY = 0x00000001,
        NIM_DELETE = 0x00000002,
        NIM_SETVERSION = 0x00000004,
    }

    public enum NotifyIconNotification
    {
        NIN_SELECT = 0x400,
        NIN_BALLOONSHOW = 0x402,
        NIN_BALLOONHIDE = 0x403,
        NIN_BALLOONTIMEOUT = 0x404,
        NIN_BALLOONUSERCLICK = 0x405,
        NIN_POPUPOPEN = 0x406,
        NIN_POPUPCLOSE = 0x407,
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIconW(
        NotifyIconMessage message,
        ref NOTIFYICONDATAW notifyIconData);

    // Returns S_OK (0) on success.
    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier,
        out RECT iconLocation);
}

[StructLayout(LayoutKind.Sequential)]
public struct NOTIFYICONIDENTIFIER
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public Guid guidItem;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public bool Contains(User32.POINT pt) =>
        pt.X >= Left && pt.X <= Right && pt.Y >= Top && pt.Y <= Bottom;
}

[Flags]
public enum NotifyIconFlags : uint
{
    NIF_MESSAGE = 0x00000001,
    NIF_ICON = 0x00000002,
    NIF_TIP = 0x00000004,
    NIF_INFO = 0x00000010,
    NIF_GUID = 0x00000020,
    NIF_SHOWTIP = 0x00000080,
}

[Flags]
public enum NotifyIconInfoFlags : uint
{
    NIIF_NONE = 0x00000000,
    NIIF_INFO = 0x00000001,
    NIIF_WARNING = 0x00000002,
    NIIF_ERROR = 0x00000003,
    NIIF_USER = 0x00000004,
    NIIF_LARGE_ICON = 0x00000020,
    NIIF_RESPECT_QUIET_TIME = 0x00000080,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATAW
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public NotifyIconFlags uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;

    public uint dwState;
    public uint dwStateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;

    public uint uTimeoutOrVersion;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;

    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

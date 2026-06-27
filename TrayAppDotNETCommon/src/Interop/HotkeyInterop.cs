using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    public const uint NoRepeat = 0x4000;
}

internal static class HotkeyUser32
{
    public const int WM_HOTKEY = 0x0312;
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int hotKeyID, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int hotKeyID);
}

internal sealed class Win32MessageWindow : IDisposable
{
    public delegate IntPtr WindowProcedure(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    private readonly string _className;
    private readonly WndProcDelegate _wndProcDelegate;
    private readonly IntPtr _hInstance;
    private WindowProcedure? _windowProcedureCallback;
    private bool _registered;
    private bool _disposed;

    public Win32MessageWindow(string classNamePrefix)
    {
        _className = $"{classNamePrefix}.MessageWindow.{Guid.NewGuid():N}";
        _wndProcDelegate = WndProc;
        _hInstance = GetModuleHandle(null);
    }

    public IntPtr Handle { get; private set; }

    public void Initialize(WindowProcedure wndProc)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(Win32MessageWindow));
        if (Handle != IntPtr.Zero) return;

        _windowProcedureCallback = wndProc;

        WNDCLASSEX windowClass = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            hInstance = _hInstance,
            lpfnWndProc = _wndProcDelegate,
            lpszClassName = _className,
        };

        ushort atom = RegisterClassEx(ref windowClass);
        if (atom == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");
        _registered = true;

        Handle = CreateWindowEx(
            0,
            _className,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            HotkeyUser32.HWND_MESSAGE,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        bool handled = false;
        try
        {
            if (_windowProcedureCallback != null)
            {
                IntPtr result = _windowProcedureCallback(hwnd, msg, wParam, lParam, ref handled);
                if (handled) return result;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"Win32MessageWindow.WndProc: {ex.Message}");
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        if (_registered)
        {
            UnregisterClass(_className, _hInstance);
            _registered = false;
        }

        _windowProcedureCallback = null;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int width,
        int height,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

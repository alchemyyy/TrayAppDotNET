using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

public sealed class Win32Window : IDisposable
{
    public delegate IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private string? _className;
    private IntPtr _hInstance;
    private WndProcDelegate? _wndProcDelegate;
    private WindowProcedure? _windowProcedure;

    public IntPtr Handle { get; private set; }

    public void Initialize(string className, WindowProcedure wndProc, IntPtr parentWindow)
    {
        if (Handle != IntPtr.Zero) return;

        _windowProcedure = wndProc;
        _wndProcDelegate = WndProc;
        _hInstance = GetModuleHandle(null);
        _className = className + "." + Guid.NewGuid().ToString("N");

        WNDCLASSEX windowClass = new()
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = _hInstance,
            lpszClassName = _className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

        Handle = CreateWindowEx(
            0,
            _className,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            parentWindow,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        bool handled = false;
        IntPtr result = IntPtr.Zero;

        if (_windowProcedure != null)
        {
            try { result = _windowProcedure(hwnd, message, wParam, lParam, ref handled); }
            catch (Exception ex) { TADNLog.Log($"Win32Window.WndProc: {ex.Message}"); }
        }

        return handled ? result : DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        if (_className != null && _hInstance != IntPtr.Zero)
        {
            UnregisterClass(_className, _hInstance);
            _className = null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

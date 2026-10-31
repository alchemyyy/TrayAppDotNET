using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// A hidden window with its own class and window procedure, for messages the Avalonia windows do not receive
/// (device notifications, hotkeys). Create, use and dispose it on one UI thread.
/// </summary>
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
    private bool _disposed;

    public IntPtr Handle { get; private set; }

    public void Initialize(string className, WindowProcedure wndProc, IntPtr parentWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Handle != IntPtr.Zero) return;

        _windowProcedure = wndProc;
        _wndProcDelegate = WndProc;
        _hInstance = Kernel32.GetModuleHandle(lpModuleName: null);
        _className = className + "." + Guid.NewGuid().ToString("N");

        User32.WNDCLASSEX windowClass = new()
        {
            cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = _hInstance,
            lpszClassName = _className
        };

        if (User32.RegisterClassEx(ref windowClass) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            _className = null;
            _windowProcedure = null;
            throw new InvalidOperationException($"RegisterClassEx failed: {error}");
        }

        Handle = User32.CreateWindowEx(
            dwExStyle: 0,
            _className,
            string.Empty,
            dwStyle: 0,
            x: 0,
            y: 0,
            nWidth: 0,
            nHeight: 0,
            parentWindow,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            User32.UnregisterClass(_className, _hInstance);
            _className = null;
            _windowProcedure = null;
            throw new InvalidOperationException($"CreateWindowEx failed: {error}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        bool handled = false;
        IntPtr result = IntPtr.Zero;

        WindowProcedure? windowProcedure = _windowProcedure;
        if (windowProcedure != null)
        {
            try { result = windowProcedure(hwnd, message, wParam, lParam, ref handled); }
            catch (Exception ex) { TADNLog.Log($"Win32Window.WndProc: {ex}"); }
        }

        return handled ? result : User32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // No message reaches the caller's procedure once disposal starts (DestroyWindow still sends WM_DESTROY)
        _windowProcedure = null;
        if (Handle != IntPtr.Zero)
        {
            User32.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        if (_className != null && _hInstance != IntPtr.Zero)
        {
            User32.UnregisterClass(_className, _hInstance);
            _className = null;
        }
    }
}

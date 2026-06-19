using System.Runtime.InteropServices;
using Avalonia;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed class TrayAppDotNETShellTrayIcon : IDisposable
{
    private const int WM_CALLBACKMOUSEMSG = User32.WM_USER + 1024;

    private readonly Guid _iconGUID;
    private readonly Win32Window _window = new();
    private NativeIcon? _currentIcon;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private bool _hasProcessedButtonUp;
    private bool _isScrollEnabled = true;
    private bool _isListeningForInput;
    private RECT _trayIconLocation;
    private string _tooltipText = string.Empty;

    public TrayAppDotNETShellTrayIcon(string trayIconGUID, string messageWindowClassPrefix)
    {
        _iconGUID = new Guid(trayIconGUID);
        _window.Initialize(
            string.IsNullOrWhiteSpace(messageWindowClassPrefix)
                ? nameof(TrayAppDotNETShellTrayIcon)
                : messageWindowClassPrefix,
            WndProc,
            User32.HWND_MESSAGE);
    }

    public event Action? LeftMouseDown;
    public event Action? LeftClick;
    public event Action? LeftDoubleClick;
    public event Action<Point>? RightClick;
    public event Action<int>? Scrolled;
    public event Action? RefreshNeeded;
    public event Action? TooltipPopup;
    public event Action? BalloonClicked;

    public bool IsScrollEnabled
    {
        get => _isScrollEnabled;
        set
        {
            if (_isScrollEnabled == value) return;
            _isScrollEnabled = value;
            if (value) RefreshMouseInputRegistration();
            else StopListeningForInput();
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            Update();
        }
    }

    public void SetIcon(NativeIcon icon)
    {
        if (_disposed) return;

        NativeIcon clone;
        try
        {
            clone = icon.Clone();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.SetIcon: {ex.Message}");
            return;
        }

        NativeIcon? oldIcon = _currentIcon;
        _currentIcon = clone;
        Update();
        oldIcon?.Dispose();
    }

    public void SetTooltip(string text)
    {
        if (_disposed || text == _tooltipText) return;
        _tooltipText = text;
        Update();
    }

    public void ShowTooltip()
    {
        if (_disposed || !_isCreated || !_isVisible || string.IsNullOrWhiteSpace(_tooltipText)) return;

        NOTIFYICONDATAW data = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = NotifyIconFlags.NIF_TIP | NotifyIconFlags.NIF_SHOWTIP | NotifyIconFlags.NIF_GUID,
            szTip = _tooltipText.Length > 127 ? _tooltipText[..127] : _tooltipText,
            guidItem = _iconGUID,
        };

        if (!Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data))
        {
            int error = Marshal.GetLastWin32Error();
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.ShowTooltip: NIM_MODIFY failed (0x{error:X8}).");
        }
    }

    public bool TryGetIconRect(out PixelRect rect)
    {
        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _window.Handle, guidItem = _iconGUID,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref id, out RECT nativeRect) == 0)
        {
            rect = new PixelRect(
                nativeRect.Left,
                nativeRect.Top,
                Math.Max(0, nativeRect.Right - nativeRect.Left),
                Math.Max(0, nativeRect.Bottom - nativeRect.Top));
            return true;
        }

        rect = default;
        return false;
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed || !_isCreated) return;

        NOTIFYICONDATAW data = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = NotifyIconFlags.NIF_INFO | NotifyIconFlags.NIF_GUID,
            guidItem = _iconGUID,
            szInfo = message,
            szInfoTitle = title,
            dwInfoFlags = (uint)(NotifyIconInfoFlags.NIIF_USER | NotifyIconInfoFlags.NIIF_RESPECT_QUIET_TIME),
            hBalloonIcon = IntPtr.Zero,
        };

        if (!Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data))
        {
            int error = Marshal.GetLastWin32Error();
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.ShowBalloon: NIM_MODIFY failed (0x{error:X8}).");
        }
    }

    private NOTIFYICONDATAW MakeData() =>
        new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = NotifyIconFlags.NIF_MESSAGE
                     | NotifyIconFlags.NIF_ICON
                     | NotifyIconFlags.NIF_TIP
                     | NotifyIconFlags.NIF_SHOWTIP
                     | NotifyIconFlags.NIF_GUID,
            uCallbackMessage = WM_CALLBACKMOUSEMSG,
            hIcon = _currentIcon?.Handle ?? IntPtr.Zero,
            szTip = _tooltipText.Length > 127 ? _tooltipText[..127] : _tooltipText,
            guidItem = _iconGUID,
        };

    private void Update()
    {
        if (_disposed) return;

        NOTIFYICONDATAW data = MakeData();
        if (!_isVisible)
        {
            StopListeningForInput();
            if (_isCreated)
            {
                _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
                _isCreated = false;
            }

            return;
        }

        if (_isCreated && Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data))
        {
            RefreshMouseInputRegistration(forceRegistration: true);
            return;
        }

        StopListeningForInput();
        _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
        bool added = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_ADD, ref data);
        _isCreated = added;
        if (!added)
        {
            int error = Marshal.GetLastWin32Error();
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.Update: NIM_ADD failed (0x{error:X8}).");
            return;
        }

        data.uTimeoutOrVersion = Shell32.NOTIFYICON_VERSION_4;
        _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_SETVERSION, ref data);
        RefreshMouseInputRegistration(forceRegistration: true);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Shell32.WM_TASKBARCREATED)
        {
            _isCreated = false;
            Update();
            RefreshNeeded?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == User32.WM_INPUT)
        {
            if (_isScrollEnabled
                && InputHelper.ProcessMouseInputMessage(lParam, out int wheelDelta)
                && wheelDelta != 0
                && UpdateInputRegistrationForCursor(ExtractMessagePoint(User32.GetMessagePos())))
                Scrolled?.Invoke(wheelDelta);

            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_CALLBACKMOUSEMSG) return IntPtr.Zero;

        short notificationCode = (short)lParam.ToInt32();
        switch (notificationCode)
        {
            case User32.WM_LBUTTONDOWN:
                _hasProcessedButtonUp = false;
                LeftMouseDown?.Invoke();
                break;
            case (short)Shell32.NotifyIconNotification.NIN_SELECT:
            case User32.WM_LBUTTONUP:
                if (!_hasProcessedButtonUp)
                {
                    _hasProcessedButtonUp = true;
                    LeftClick?.Invoke();
                }

                break;
            case User32.WM_LBUTTONDBLCLK:
                LeftDoubleClick?.Invoke();
                break;
            case User32.WM_RBUTTONUP:
            case User32.WM_CONTEXTMENU:
                RightClick?.Invoke(ExtractScreenPoint(wParam));
                break;
            case User32.WM_MOUSEMOVE:
                OnNotifyIconMouseMove();
                break;
            case (short)Shell32.NotifyIconNotification.NIN_POPUPOPEN:
                TooltipPopup?.Invoke();
                break;
            case (short)Shell32.NotifyIconNotification.NIN_BALLOONUSERCLICK:
                BalloonClicked?.Invoke();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void OnNotifyIconMouseMove()
    {
        RefreshMouseInputRegistration(forceRegistration: true);
    }

    private void RefreshMouseInputRegistration(bool forceRegistration = false)
    {
        if (!_isScrollEnabled || !_isVisible || _disposed || _window.Handle == IntPtr.Zero)
        {
            StopListeningForInput();
            return;
        }

        if (!TryUpdateTrayIconLocation())
        {
            _trayIconLocation = default;
            StopListeningForInput();
            return;
        }

        if (!User32.GetCursorPos(out User32.POINT cursor))
        {
            StopListeningForInput();
            return;
        }

        UpdateInputRegistrationForCursor(cursor, forceRegistration);
    }

    private bool TryUpdateTrayIconLocation()
    {
        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _window.Handle, guidItem = _iconGUID,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref id, out RECT location) != 0)
            return false;

        _trayIconLocation = location;
        return true;
    }

    private bool UpdateInputRegistrationForCursor(User32.POINT cursor, bool forceRegistration = false)
    {
        bool inBounds = _trayIconLocation.Contains(cursor);
        if (inBounds) StartListeningForInput(forceRegistration);
        else StopListeningForInput();

        return inBounds;
    }

    private void StartListeningForInput(bool forceRegistration)
    {
        if (!forceRegistration && _isListeningForInput) return;
        _isListeningForInput = InputHelper.RegisterForMouseInput(_window.Handle);
    }

    private void StopListeningForInput()
    {
        if (!_isListeningForInput) return;
        _isListeningForInput = false;
        _ = InputHelper.UnregisterForMouseInput();
    }

    private static Point ExtractScreenPoint(IntPtr packedPoint)
    {
        int packed = unchecked((int)packedPoint.ToInt64());
        return new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
    }

    private static User32.POINT ExtractMessagePoint(int packedPoint) =>
        new() { X = (short)(packedPoint & 0xFFFF), Y = (short)((packedPoint >> 16) & 0xFFFF), };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopListeningForInput();

        if (_isCreated)
        {
            NOTIFYICONDATAW data = MakeData();
            _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
            _isCreated = false;
        }

        _currentIcon?.Dispose();
        _currentIcon = null;
        _window.Dispose();
    }
}

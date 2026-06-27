using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed class TrayAppDotNETShellTrayIcon : IDisposable
{
    private const int WM_CALLBACKMOUSEMSG = User32.WM_USER + 1024;
    private const int TrayIconLocationRefreshCooldownMs = 250;
    private const int TaskbarRecoveryRetryCount = 5;
    private const int TaskbarRecoveryRetryDelayMs = 500;

    private readonly Guid _iconGUID;
    private readonly Win32Window _window = new();
    private NativeIcon? _currentIcon;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private bool _hasProcessedButtonUp;
    private bool _isScrollEnabled = true;
    private bool _isListeningForInput;
    private bool _taskbarRecoveryQueued;
    private bool _trayIconLocationValid;
    private long _lastTrayIconLocationRefreshTick;
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
            // TaskbarCreated is broadcast only to top-level windows, not HWND_MESSAGE windows.
            IntPtr.Zero);
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
            CacheTrayIconLocation(nativeRect);
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
            RefreshMouseInputRegistration();
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
        RefreshMouseInputRegistration();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Shell32.WM_TASKBARCREATED)
        {
            QueueTaskbarRecovery();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == User32.WM_INPUT)
        {
            if (_isScrollEnabled
                && InputHelper.ProcessMouseInputMessage(lParam, out int wheelDelta)
                && wheelDelta != 0
                && UpdateInputRegistrationForCursor(ExtractMessagePoint(User32.GetMessagePos())))
                PostEvent(Scrolled, wheelDelta, nameof(Scrolled));

            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_CALLBACKMOUSEMSG) return IntPtr.Zero;

        short notificationCode = (short)lParam.ToInt32();
        switch (notificationCode)
        {
            case User32.WM_LBUTTONDOWN:
                _hasProcessedButtonUp = false;
                PostEvent(LeftMouseDown, nameof(LeftMouseDown));
                break;
            case (short)Shell32.NotifyIconNotification.NIN_SELECT:
            case User32.WM_LBUTTONUP:
                if (!_hasProcessedButtonUp)
                {
                    _hasProcessedButtonUp = true;
                    PostEvent(LeftClick, nameof(LeftClick));
                }

                break;
            case User32.WM_LBUTTONDBLCLK:
                PostEvent(LeftDoubleClick, nameof(LeftDoubleClick));
                break;
            case User32.WM_RBUTTONUP:
            case User32.WM_CONTEXTMENU:
                PostEvent(RightClick, ExtractScreenPoint(wParam), nameof(RightClick));
                break;
            case User32.WM_MOUSEMOVE:
                OnNotifyIconMouseMove();
                break;
            case (short)Shell32.NotifyIconNotification.NIN_POPUPOPEN:
                PostEvent(TooltipPopup, nameof(TooltipPopup));
                break;
            case (short)Shell32.NotifyIconNotification.NIN_BALLOONUSERCLICK:
                PostEvent(BalloonClicked, nameof(BalloonClicked));
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void OnNotifyIconMouseMove()
    {
        RefreshMouseInputRegistration();
    }

    private void RefreshMouseInputRegistration()
    {
        if (!_isScrollEnabled || !_isVisible || _disposed || _window.Handle == IntPtr.Zero)
        {
            StopListeningForInput();
            return;
        }

        if (!TryUpdateTrayIconLocation())
        {
            ClearTrayIconLocation();
            StopListeningForInput();
            return;
        }

        if (!User32.GetCursorPos(out User32.POINT cursor))
        {
            StopListeningForInput();
            return;
        }

        UpdateInputRegistrationForCursor(cursor);
    }

    private bool TryUpdateTrayIconLocation()
    {
        long now = Environment.TickCount64;
        if (_trayIconLocationValid
            && now - _lastTrayIconLocationRefreshTick < TrayIconLocationRefreshCooldownMs)
            return true;

        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _window.Handle, guidItem = _iconGUID,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref id, out RECT location) != 0)
            return false;

        CacheTrayIconLocation(location);
        return true;
    }

    private void CacheTrayIconLocation(RECT location)
    {
        _trayIconLocation = location;
        _trayIconLocationValid = true;
        _lastTrayIconLocationRefreshTick = Environment.TickCount64;
    }

    private void ClearTrayIconLocation()
    {
        _trayIconLocation = default;
        _trayIconLocationValid = false;
        _lastTrayIconLocationRefreshTick = 0;
    }

    private bool UpdateInputRegistrationForCursor(User32.POINT cursor)
    {
        bool inBounds = _trayIconLocation.Contains(cursor);
        if (inBounds) StartListeningForInput();
        else StopListeningForInput();

        return inBounds;
    }

    private void StartListeningForInput()
    {
        if (_isListeningForInput) return;
        _isListeningForInput = InputHelper.RegisterForMouseInput(_window.Handle);
    }

    private void StopListeningForInput()
    {
        if (!_isListeningForInput) return;
        _isListeningForInput = false;
        _ = InputHelper.UnregisterForMouseInput();
    }

    private void QueueTaskbarRecovery()
    {
        if (_disposed || _taskbarRecoveryQueued) return;

        _taskbarRecoveryQueued = true;
        PostAction(async () => await RecoverAfterTaskbarCreatedAsync(), nameof(QueueTaskbarRecovery));
    }

    private async Task RecoverAfterTaskbarCreatedAsync()
    {
        try
        {
            _isCreated = false;
            ClearTrayIconLocation();
            StopListeningForInput();

            for (int attempt = 0; attempt < TaskbarRecoveryRetryCount && !_disposed; attempt++)
            {
                Update();
                RefreshNeeded?.Invoke();
                if (_isCreated || !_isVisible)
                    return;

                await Task.Delay(TaskbarRecoveryRetryDelayMs);
            }
        }
        finally
        {
            _taskbarRecoveryQueued = false;
        }
    }

    private void PostEvent(Action? handler, string name)
    {
        if (handler == null) return;
        PostAction(handler, name);
    }

    private void PostEvent<T>(Action<T>? handler, T value, string name)
    {
        if (handler == null) return;
        PostAction(() => handler(value), name);
    }

    private void PostAction(Func<Task> action, string name)
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            if (_disposed) return;

            try { await action(); }
            catch (Exception ex) { TADNLog.Log($"TrayAppDotNETShellTrayIcon.{name}: {ex}"); }
        });
    }

    private void PostAction(Action action, string name)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            try { action(); }
            catch (Exception ex) { TADNLog.Log($"TrayAppDotNETShellTrayIcon.{name}: {ex}"); }
        });
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

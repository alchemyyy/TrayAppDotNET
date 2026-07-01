using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed class TrayAppDotNETShellTrayIcon : IDisposable
{
    private const int WM_CALLBACKMOUSEMSG = User32.WM_USER + 1024;
    private const int TrayIconLocationRefreshCooldownMs = 250;
    private const int TaskbarRecoveryRetryCount = 5;
    private const int TaskbarRecoveryRetryDelayMs = 500;

    private readonly Guid _iconGUID;
    private readonly Win32Window _window = new();
    private readonly AsyncThrottler<TrayUpdateKind> _trayUpdateThrottler = new(cooldownMs: 0);
    private readonly List<NativeIcon> _retiredIcons = [];
    private NativeIcon? _currentIcon;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private bool _hasProcessedButtonUp;
    private bool _isScrollEnabled = true;
    private bool _isListeningForInput;
    private bool _taskbarRecoveryQueued;
    private bool _trayIconLocationValid;
    private bool _forceFullIconUpdate;
    private long _lastTrayIconLocationRefreshTick;
    private RECT _trayIconLocation;
    private string _tooltipText = string.Empty;
    private bool _tooltipDirty;
    private bool _tooltipShowRequested;
    private bool _tooltipKeepOpenRequested;

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

        NativeIcon? clone = CloneIcon(icon, nameof(SetIcon));
        if (clone == null) return;

        ReplaceCurrentIcon(clone);
        RequestIconAndTooltipUpdate();
    }

    /// <summary>
    /// Applies a caller-owned tray icon without cloning it.
    /// </summary>
    public void SetOwnedIcon(NativeIcon icon)
    {
        if (_disposed)
        {
            icon.Dispose();
            return;
        }

        ReplaceCurrentIcon(icon);
        RequestIconAndTooltipUpdate();
    }

    /// <summary>
    /// Applies a tray icon and tooltip through one shell update.
    /// </summary>
    public void SetIconAndTooltip(NativeIcon icon, string text)
    {
        if (_disposed) return;

        NativeIcon? clone = CloneIcon(icon, nameof(SetIconAndTooltip));
        if (clone == null) return;

        SetTooltipText(text);
        ReplaceCurrentIcon(clone);
        RequestIconAndTooltipUpdate();
    }

    /// <summary>
    /// Applies a caller-owned tray icon and tooltip without cloning the icon.
    /// </summary>
    public void SetOwnedIconAndTooltip(NativeIcon icon, string text)
    {
        if (_disposed)
        {
            icon.Dispose();
            return;
        }

        SetTooltipText(text);
        ReplaceCurrentIcon(icon);
        RequestIconAndTooltipUpdate();
    }

    public void SetTooltip(string text)
    {
        if (_disposed || (text == _tooltipText && !_tooltipDirty)) return;
        SetTooltipText(text);
        RequestTooltipUpdate();
        RequestMouseInputRegistrationRefresh();
    }

    public void ShowTooltip()
    {
        if (_disposed || !_isVisible || string.IsNullOrWhiteSpace(_tooltipText)) return;

        _tooltipKeepOpenRequested = true;
        _tooltipShowRequested = true;
        RequestTooltipUpdate();
        RequestMouseInputRegistrationRefresh();
    }

    private Task RunTooltipShowAsync(ThrottlerContext context)
    {
        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || context.CancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetResult();
                return;
            }

            try
            {
                SyncTooltip();
                _trayUpdateThrottler.Drop(TrayUpdateKind.Tooltip);
                completionSource.TrySetResult();
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        }, DispatcherPriority.Input);

        return completionSource.Task;
    }

    public bool TryGetIconRect(out PixelRect rect)
    {
        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _window.Handle, guidItem = _iconGUID,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref id, out RECT nativeRect) == 0)
        {
            _isCreated = true;
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

    private NOTIFYICONDATAW MakeData(NotifyIconFlags flags) =>
        new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = flags | NotifyIconFlags.NIF_GUID,
            uCallbackMessage = (flags & NotifyIconFlags.NIF_MESSAGE) != 0 ? (uint)WM_CALLBACKMOUSEMSG : 0,
            hIcon = (flags & NotifyIconFlags.NIF_ICON) != 0 ? _currentIcon?.Handle ?? IntPtr.Zero : IntPtr.Zero,
            szTip = _tooltipText.Length > 127 ? _tooltipText[..127] : _tooltipText,
            guidItem = _iconGUID,
        };

    private void Update()
    {
        if (_disposed) return;

        if (!_isVisible)
        {
            DeleteTrayIcon();
            return;
        }

        RequestIconAndTooltipUpdate();
    }

    private void SyncTooltip()
    {
        if (_disposed || !_isVisible || string.IsNullOrWhiteSpace(_tooltipText)) return;
        if (!_tooltipDirty && !_tooltipShowRequested) return;

        NOTIFYICONDATAW data = MakeData(NotifyIconFlags.NIF_TIP | NotifyIconFlags.NIF_SHOWTIP);
        if (TryNotify(Shell32.NotifyIconMessage.NIM_MODIFY, ref data, out int error))
        {
            _isCreated = true;
            _tooltipDirty = false;
            _tooltipShowRequested = false;
            RequestMouseInputRegistrationRefresh();
            return;
        }

        TADNLog.Log($"TrayAppDotNETShellTrayIcon.SyncTooltip: NIM_MODIFY failed (0x{error:X8}).");
        _forceFullIconUpdate = true;
        RequestIconAndTooltipUpdate();
    }

    private void RequestTooltipUpdate()
    {
        if (_disposed || !_isVisible || string.IsNullOrWhiteSpace(_tooltipText)) return;

        _ = _trayUpdateThrottler.RunAsync(TrayUpdateKind.Tooltip, RunTooltipShowAsync);
    }

    private void SetTooltipText(string text)
    {
        if (_tooltipText == text) return;
        _tooltipText = text;
        _tooltipDirty = true;
    }

    private void RequestIconAndTooltipUpdate()
    {
        if (_disposed || !_isVisible || _currentIcon == null) return;

        _ = _trayUpdateThrottler.RunAsync(TrayUpdateKind.Icon, RunIconUpdateAsync);
    }

    private Task RunIconUpdateAsync(ThrottlerContext context)
    {
        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || context.CancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetResult();
                return;
            }

            try
            {
                NativeIcon? updateIcon = _currentIcon;
                UpdateIconAndTooltip();
                if (ReferenceEquals(updateIcon, _currentIcon))
                    _trayUpdateThrottler.Drop(TrayUpdateKind.Icon);
                completionSource.TrySetResult();
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        }, DispatcherPriority.Background);

        return completionSource.Task;
    }

    private void UpdateIconAndTooltip()
    {
        if (_disposed || !_isVisible || _currentIcon == null) return;

        bool forceFullUpdate = _forceFullIconUpdate;
        NOTIFYICONDATAW iconData = MakeData(
            forceFullUpdate
                ? NotifyIconFlags.NIF_MESSAGE
                  | NotifyIconFlags.NIF_ICON
                  | NotifyIconFlags.NIF_TIP
                  | NotifyIconFlags.NIF_SHOWTIP
                : NotifyIconFlags.NIF_ICON);

        if (_isCreated)
        {
            if (TryModifyTrayIcon(
                    ref iconData,
                    clearsTooltipDirty: forceFullUpdate,
                    setVersion: false,
                    out int modifyError))
                return;

            NOTIFYICONDATAW addData = MakeData(
                NotifyIconFlags.NIF_MESSAGE
                | NotifyIconFlags.NIF_ICON
                | NotifyIconFlags.NIF_TIP
                | NotifyIconFlags.NIF_SHOWTIP);
            if (TryAddTrayIcon(ref addData, preserveCreatedOnFailure: true, out int recoveryAddError))
                return;

            if (TryModifyTrayIcon(
                    ref addData,
                    clearsTooltipDirty: true,
                    setVersion: true,
                    out int retryModifyError))
                return;

            TADNLog.Log(
                "TrayAppDotNETShellTrayIcon.UpdateIconAndTooltip: "
                + $"NIM_MODIFY failed (0x{modifyError:X8}); recovery NIM_ADD failed (0x{recoveryAddError:X8}); "
                + $"retry NIM_MODIFY failed (0x{retryModifyError:X8}).");
            return;
        }

        if (TryModifyTrayIcon(
                ref iconData,
                clearsTooltipDirty: forceFullUpdate,
                setVersion: true,
                out int preAddModifyError))
            return;

        NOTIFYICONDATAW fullData = MakeData(
            NotifyIconFlags.NIF_MESSAGE
            | NotifyIconFlags.NIF_ICON
            | NotifyIconFlags.NIF_TIP
            | NotifyIconFlags.NIF_SHOWTIP);
        if (TryAddTrayIcon(ref fullData, preserveCreatedOnFailure: false, out int addError))
            return;

        if (TryModifyTrayIcon(
                ref fullData,
                clearsTooltipDirty: true,
                setVersion: true,
                out int modifyRecoveryError))
            return;

        _isCreated = false;

        TADNLog.Log(
            "TrayAppDotNETShellTrayIcon.UpdateIconAndTooltip: "
            + $"pre-add NIM_MODIFY failed (0x{preAddModifyError:X8}); NIM_ADD failed (0x{addError:X8}); "
            + $"recovery NIM_MODIFY failed (0x{modifyRecoveryError:X8}).");
    }

    private bool TryModifyTrayIcon(
        ref NOTIFYICONDATAW data,
        bool clearsTooltipDirty,
        bool setVersion,
        out int error)
    {
        if (!TryNotify(Shell32.NotifyIconMessage.NIM_MODIFY, ref data, out error))
            return false;

        _isCreated = true;
        _forceFullIconUpdate = false;
        if (clearsTooltipDirty)
        {
            _tooltipDirty = false;
            _tooltipShowRequested = false;
        }

        if (setVersion) SetTrayIconVersion(ref data);
        CompleteIconUpdate();
        if (!clearsTooltipDirty) RequestTooltipUpdateAfterIconChange();
        return true;
    }

    private bool TryAddTrayIcon(ref NOTIFYICONDATAW data, bool preserveCreatedOnFailure, out int error)
    {
        if (!TryNotify(Shell32.NotifyIconMessage.NIM_ADD, ref data, out error))
        {
            if (!preserveCreatedOnFailure) _isCreated = false;
            return false;
        }

        _isCreated = true;
        _forceFullIconUpdate = false;
        _tooltipDirty = false;
        _tooltipShowRequested = false;
        SetTrayIconVersion(ref data);
        RefreshMouseInputRegistration();
        CompleteIconUpdate();
        return true;
    }

    private static void SetTrayIconVersion(ref NOTIFYICONDATAW data)
    {
        data.uTimeoutOrVersion = Shell32.NOTIFYICON_VERSION_4;
        _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_SETVERSION, ref data);
    }

    private void CompleteIconUpdate()
    {
        DisposeRetiredIcons();
        InvalidateTrayIconLocationForRefresh();
        RequestMouseInputRegistrationRefresh();
    }

    private static bool TryNotify(Shell32.NotifyIconMessage message, ref NOTIFYICONDATAW data, out int error)
    {
        if (Shell32.Shell_NotifyIconW(message, ref data))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static NativeIcon? CloneIcon(NativeIcon icon, string caller)
    {
        try
        {
            return icon.Clone();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.{caller}: {ex.Message}");
            return null;
        }
    }

    private void ReplaceCurrentIcon(NativeIcon icon)
    {
        NativeIcon? oldIcon = _currentIcon;
        _currentIcon = icon;
        if (oldIcon != null) _retiredIcons.Add(oldIcon);
    }

    private void DeleteTrayIcon()
    {
        StopListeningForInput();
        ClearTrayIconLocation();
        _trayUpdateThrottler.Drop(TrayUpdateKind.Icon);
        _trayUpdateThrottler.Drop(TrayUpdateKind.MouseInput);
        _trayUpdateThrottler.Drop(TrayUpdateKind.Tooltip);
        _tooltipShowRequested = false;
        _tooltipKeepOpenRequested = false;
        if (!_isCreated) return;

        NOTIFYICONDATAW data = MakeData(0);
        _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
        _isCreated = false;
    }

    private void DisposeRetiredIcons()
    {
        foreach (NativeIcon icon in _retiredIcons)
            icon.Dispose();

        _retiredIcons.Clear();
    }

    private void RequestMouseInputRegistrationRefresh()
    {
        if (_disposed || !_isVisible || !_isCreated) return;

        _ = _trayUpdateThrottler.RunAsync(TrayUpdateKind.MouseInput, RunMouseInputRefreshAsync);
    }

    private Task RunMouseInputRefreshAsync(ThrottlerContext context)
    {
        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || context.CancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetResult();
                return;
            }

            try
            {
                SyncTooltip();
                RefreshMouseInputRegistration();
                _trayUpdateThrottler.Drop(TrayUpdateKind.MouseInput);
                completionSource.TrySetResult();
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        }, DispatcherPriority.Input);

        return completionSource.Task;
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
            if (!_isScrollEnabled || !_isVisible || _disposed || !TryUpdateTrayIconLocation())
            {
                StopListeningForInput();
                handled = true;
                return IntPtr.Zero;
            }

            if (!UpdateInputRegistrationForCursor(ExtractMessagePoint(User32.GetMessagePos())))
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (InputHelper.ProcessMouseInputMessage(lParam, out int wheelDelta) && wheelDelta != 0)
                PostEvent(Scrolled, wheelDelta, nameof(Scrolled));

            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_CALLBACKMOUSEMSG) return IntPtr.Zero;

        _isCreated = true;
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
                _tooltipKeepOpenRequested = true;
                SyncTooltip();
                PostEvent(TooltipPopup, nameof(TooltipPopup));
                break;
            case (short)Shell32.NotifyIconNotification.NIN_POPUPCLOSE:
                ClearTooltipKeepOpenIfPointerLeft();
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
        SyncTooltip();
        RefreshMouseInputRegistration();
    }

    private void RefreshMouseInputRegistration()
    {
        if (!_isScrollEnabled || !_isVisible || !_isCreated || _disposed || _window.Handle == IntPtr.Zero)
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
            return _trayIconLocationValid;

        _isCreated = true;
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

    private void InvalidateTrayIconLocationForRefresh()
    {
        _lastTrayIconLocationRefreshTick = 0;
    }

    private bool UpdateInputRegistrationForCursor(User32.POINT cursor)
    {
        bool inBounds = _trayIconLocation.Contains(cursor);
        if (inBounds) StartListeningForInput();
        else
        {
            _tooltipKeepOpenRequested = false;
            StopListeningForInput();
        }

        return inBounds;
    }

    /// <summary>
    /// Re-shows a hover-requested tooltip after a shell icon swap.
    /// </summary>
    private void RequestTooltipUpdateAfterIconChange()
    {
        if (_tooltipKeepOpenRequested)
            _tooltipShowRequested = true;

        RequestTooltipUpdate();
    }

    /// <summary>
    /// Stops preserving the tooltip once the cursor has left the tray icon.
    /// </summary>
    private void ClearTooltipKeepOpenIfPointerLeft()
    {
        if (!_tooltipKeepOpenRequested) return;
        if (!TryUpdateTrayIconLocation() || !User32.GetCursorPos(out User32.POINT cursor))
        {
            _tooltipKeepOpenRequested = false;
            return;
        }

        if (!_trayIconLocation.Contains(cursor))
            _tooltipKeepOpenRequested = false;
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
            NOTIFYICONDATAW data = MakeData(0);
            _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
            _isCreated = false;
        }

        _currentIcon?.Dispose();
        _currentIcon = null;
        DisposeRetiredIcons();
        _trayUpdateThrottler.Dispose();
        _window.Dispose();
    }

    private enum TrayUpdateKind
    {
        Icon,
        MouseInput,
        Tooltip,
    }
}

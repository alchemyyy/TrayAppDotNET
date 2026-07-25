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
    private const bool TrayInputDiagnosticsEnabled = true;
    private const int MaxTooltipTextLength = 127;
    private const uint GestureIdZoom = 3;
    private const uint GestureIdPan = 4;
    private const uint GestureIdTwoFingerTap = 6;
    private const uint GestureWantPan =
        0x00000001 | // GC_PAN
        0x00000002 | // GC_PAN_WITH_SINGLE_FINGER_VERTICALLY
        0x00000004 | // GC_PAN_WITH_SINGLE_FINGER_HORIZONTALLY
        0x00000008 | // GC_PAN_WITH_GUTTER
        0x00000010;  // GC_PAN_WITH_INERTIA
    private const int DefaultPrecisionTouchpadUnitsPerScrollStep = 90;

    private readonly Guid _iconGUID;
    private readonly Win32Window _window = new();
    private readonly AsyncThrottler<TrayUpdateKind> _trayUpdateThrottler = new(cooldownMs: 0);
    private readonly HashSet<uint> _registeredPointerTargets = [];
    private readonly PrecisionTouchpadScrollRecognizer _precisionTouchpadScroll = new(
        DefaultPrecisionTouchpadUnitsPerScrollStep);
    private NativeIcon? _currentIcon;
    private NativeIcon? _shellIcon;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private bool _hasProcessedButtonUp;
    private bool _isScrollEnabled = true;
    private bool _isPrecisionTouchpadScrollEnabled = true;
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
    private bool _tooltipHoverSyncPending = true;
    private bool _isPointerOverIcon;
    private bool _isTouchWindowRegistered;
    private bool _isGestureConfigured;

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
    public event Action<int>? PrecisionTouchpadScrolled;
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

    public bool IsPrecisionTouchpadScrollEnabled
    {
        get => _isPrecisionTouchpadScrollEnabled;
        set
        {
            if (_isPrecisionTouchpadScrollEnabled == value) return;
            _isPrecisionTouchpadScrollEnabled = value;
            _precisionTouchpadScroll.Reset();
        }
    }

    public int PrecisionTouchpadUnitsPerScrollStep
    {
        get => _precisionTouchpadScroll.UnitsPerScrollStep;
        set => _precisionTouchpadScroll.UnitsPerScrollStep = value;
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
        if (_isPointerOverIcon || _tooltipKeepOpenRequested)
            RequestTooltipUpdate();
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
        NOTIFYICONIDENTIFIER id = MakeIdentifier();

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

        NOTIFYICONDATAW data = MakeData(NotifyIconFlags.NIF_INFO);
        data.szInfo = message;
        data.szInfoTitle = title;
        data.dwInfoFlags = (uint)(NotifyIconInfoFlags.NIIF_USER | NotifyIconInfoFlags.NIIF_RESPECT_QUIET_TIME);
        data.hBalloonIcon = IntPtr.Zero;

        if (!Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data))
        {
            int error = Marshal.GetLastWin32Error();
            TADNLog.Log($"TrayAppDotNETShellTrayIcon.ShowBalloon: NIM_MODIFY failed (0x{error:X8}).");
        }
    }

    // Keep one GUID identity for the icon's full lifetime; switching to hWnd/uID can create a second shell icon
    private NOTIFYICONDATAW MakeData(NotifyIconFlags flags) =>
        new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = flags | NotifyIconFlags.NIF_GUID,
            uCallbackMessage = (flags & NotifyIconFlags.NIF_MESSAGE) != 0 ? (uint)WM_CALLBACKMOUSEMSG : 0,
            hIcon = (flags & NotifyIconFlags.NIF_ICON) != 0 ? _currentIcon?.Handle ?? IntPtr.Zero : IntPtr.Zero,
            szTip = TruncateTooltipText(_tooltipText),
            guidItem = _iconGUID
        };

    private NOTIFYICONIDENTIFIER MakeIdentifier() =>
        new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _window.Handle,
            guidItem = _iconGUID
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

    private void SyncTooltip(bool force = false)
    {
        if (_disposed || !_isVisible) return;
        if (string.IsNullOrWhiteSpace(_tooltipText) && !_tooltipDirty) return;
        if (!force && !_tooltipDirty && !_tooltipShowRequested) return;

        NOTIFYICONDATAW data = MakeData(GetTooltipShellFlags(includeEmptyTip: true));
        if (TryNotify(Shell32.NotifyIconMessage.NIM_MODIFY, ref data, out int error))
        {
            _isCreated = true;
            _tooltipDirty = false;
            _tooltipShowRequested = false;
            _tooltipHoverSyncPending = false;
            RequestMouseInputRegistrationRefresh();
            return;
        }

        TADNLog.Log($"TrayAppDotNETShellTrayIcon.SyncTooltip: NIM_MODIFY failed (0x{error:X8}).");
        _forceFullIconUpdate = true;
        RequestIconAndTooltipUpdate();
    }

    private void RequestTooltipUpdate()
    {
        if (_disposed || !_isVisible) return;
        if (string.IsNullOrWhiteSpace(_tooltipText) && !_tooltipDirty) return;

        _ = _trayUpdateThrottler.RunAsync(TrayUpdateKind.Tooltip, RunTooltipShowAsync);
    }

    private void SetTooltipText(string text)
    {
        if (_tooltipText == text) return;
        _tooltipText = text;
        _tooltipDirty = true;
        _tooltipHoverSyncPending = true;
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
                    syncsTooltip: forceFullUpdate,
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
                    syncsTooltip: true,
                    setVersion: true,
                    out int retryModifyError))
                return;

            TADNLog.Log(
                "TrayAppDotNETShellTrayIcon.UpdateIconAndTooltip: "
                + $"NIM_MODIFY failed (0x{modifyError:X8}); recovery NIM_ADD failed (0x{recoveryAddError:X8}); "
                + $"retry NIM_MODIFY failed (0x{retryModifyError:X8}).");
            _forceFullIconUpdate = true;
            QueueTaskbarRecovery();
            return;
        }

        if (TryModifyTrayIcon(
                ref iconData,
                syncsTooltip: forceFullUpdate,
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
                syncsTooltip: true,
                setVersion: true,
                out int modifyRecoveryError))
            return;

        _isCreated = false;

        TADNLog.Log(
            "TrayAppDotNETShellTrayIcon.UpdateIconAndTooltip: "
            + $"pre-add NIM_MODIFY failed (0x{preAddModifyError:X8}); NIM_ADD failed (0x{addError:X8}); "
            + $"recovery NIM_MODIFY failed (0x{modifyRecoveryError:X8}).");
        _forceFullIconUpdate = true;
        QueueTaskbarRecovery();
    }

    private bool TryModifyTrayIcon(
        ref NOTIFYICONDATAW data,
        bool syncsTooltip,
        bool setVersion,
        out int error)
    {
        if (!TryNotify(Shell32.NotifyIconMessage.NIM_MODIFY, ref data, out error))
            return false;

        bool forceShowAfterIconChange = _tooltipShowRequested || _tooltipKeepOpenRequested;
        bool shouldRefreshVisibleTooltip = forceShowAfterIconChange || _isPointerOverIcon;
        _isCreated = true;
        _forceFullIconUpdate = false;
        if (syncsTooltip)
        {
            _tooltipDirty = false;
            _tooltipShowRequested = false;
            _tooltipHoverSyncPending = false;
        }

        if (setVersion) SetTrayIconVersion(ref data);
        CompleteIconUpdate();
        if (!syncsTooltip || shouldRefreshVisibleTooltip)
            RequestTooltipUpdateAfterIconChange(forceShow: forceShowAfterIconChange);
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
        if (IncludesTooltipText(data.uFlags))
        {
            _tooltipDirty = false;
            _tooltipShowRequested = false;
            _tooltipHoverSyncPending = true;
        }

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
        CommitShellIcon();
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

    private NotifyIconFlags GetTooltipShellFlags(bool includeEmptyTip)
    {
        if (!string.IsNullOrWhiteSpace(_tooltipText))
            return NotifyIconFlags.NIF_TIP | NotifyIconFlags.NIF_SHOWTIP;

        return includeEmptyTip && _tooltipDirty ? NotifyIconFlags.NIF_TIP : 0;
    }

    private static bool IncludesTooltipText(NotifyIconFlags flags) =>
        (flags & NotifyIconFlags.NIF_TIP) != 0;

    private static string TruncateTooltipText(string text) =>
        text.Length > MaxTooltipTextLength ? text[..MaxTooltipTextLength] : text;

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
        if (oldIcon != null && !ReferenceEquals(oldIcon, _shellIcon))
            oldIcon.Dispose();
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
        _tooltipHoverSyncPending = true;
        _isPointerOverIcon = false;
        if (!_isCreated) return;

        NOTIFYICONDATAW data = MakeData(0);
        if (Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data))
        {
            _isCreated = false;
            ReleaseShellIcon();
            return;
        }

        int error = Marshal.GetLastWin32Error();
        TADNLog.Log($"TrayAppDotNETShellTrayIcon.DeleteTrayIcon: NIM_DELETE failed (0x{error:X8}).");
    }

    private void CommitShellIcon()
    {
        NativeIcon? oldShellIcon = _shellIcon;
        _shellIcon = _currentIcon;

        if (oldShellIcon != null && !ReferenceEquals(oldShellIcon, _shellIcon))
            oldShellIcon.Dispose();
    }

    private void ReleaseShellIcon()
    {
        NativeIcon? oldShellIcon = _shellIcon;
        _shellIcon = null;

        if (oldShellIcon != null && !ReferenceEquals(oldShellIcon, _currentIcon))
            oldShellIcon.Dispose();
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

            if (InputHelper.TryReadRawInputMessage(lParam, out InputHelper.RawInputDiagnostics rawInput))
            {
                LogRawInputDiagnostics(rawInput);
                if (rawInput.WheelDelta != 0)
                    PostEvent(Scrolled, rawInput.WheelDelta, nameof(Scrolled));
                else if (TryProcessPrecisionTouchpadScroll(rawInput, out int touchpadSteps))
                    PostTouchpadScrollEvents(touchpadSteps);
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (TrayInputDiagnosticsEnabled && TryHandleShotgunInputMessage(msg, wParam, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WM_CALLBACKMOUSEMSG) return IntPtr.Zero;

        _isCreated = true;
        short notificationCode = (short)lParam.ToInt32();
        LogShellCallbackDiagnostics(notificationCode, wParam, lParam);
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
                _isPointerOverIcon = true;
                _tooltipShowRequested = true;
                SyncTooltip(force: true);
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
        bool isNewHover = !_isPointerOverIcon;
        _isPointerOverIcon = true;
        SyncTooltip(force: isNewHover || _tooltipHoverSyncPending);
        RefreshMouseInputRegistration();
    }

    private bool StartShotgunInputDiagnostics()
    {
        bool anyRegistered = false;
        anyRegistered |= TryRegisterTouchWindow();
        anyRegistered |= TrySetGestureConfig();
        anyRegistered |= TryRegisterPointerTarget(User32.PT_TOUCHPAD);
        anyRegistered |= TryRegisterPointerTarget(User32.PT_TOUCH);
        anyRegistered |= TryRegisterPointerTarget(User32.PT_MOUSE);
        anyRegistered |= TryRegisterPointerTarget(User32.PT_POINTER);
        return anyRegistered;
    }

    private void StopShotgunInputDiagnostics()
    {
        if (_isTouchWindowRegistered)
        {
            try
            {
                if (!User32.UnregisterTouchWindow(_window.Handle))
                    TADNLog.Log($"TrayInputDiag.Touch.Unregister failed: error={Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                TADNLog.Log($"TrayInputDiag.Touch.Unregister threw: {ex.GetType().Name}: {ex.Message}");
            }

            _isTouchWindowRegistered = false;
        }

        if (_registeredPointerTargets.Count > 0)
        {
            uint[] pointerTargets = new uint[_registeredPointerTargets.Count];
            _registeredPointerTargets.CopyTo(pointerTargets);
            foreach (uint pointerType in pointerTargets)
                TryUnregisterPointerTarget(pointerType);
        }

        _isGestureConfigured = false;
    }

    private bool TryRegisterTouchWindow()
    {
        if (_isTouchWindowRegistered) return true;

        try
        {
            const uint twfFineTouch = 0x00000001;
            const uint twfWantPalm = 0x00000002;
            _isTouchWindowRegistered = User32.RegisterTouchWindow(_window.Handle, twfFineTouch | twfWantPalm);
            TADNLog.Log(
                _isTouchWindowRegistered
                    ? "TrayInputDiag.Touch.Register: ok"
                    : $"TrayInputDiag.Touch.Register failed: error={Marshal.GetLastWin32Error()}");
            return _isTouchWindowRegistered;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayInputDiag.Touch.Register threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool TrySetGestureConfig()
    {
        if (_isGestureConfigured) return true;

        try
        {
            User32.GESTURECONFIG[] configs =
            [
                new() { dwID = 0, dwWant = 0x00000001, dwBlock = 0 },
                new() { dwID = GestureIdPan, dwWant = GestureWantPan, dwBlock = 0 },
                new() { dwID = GestureIdZoom, dwWant = 0x00000001, dwBlock = 0 },
                new() { dwID = GestureIdTwoFingerTap, dwWant = 0x00000001, dwBlock = 0 }
            ];

            _isGestureConfigured = User32.SetGestureConfig(
                _window.Handle,
                0,
                (uint)configs.Length,
                configs,
                (uint)Marshal.SizeOf<User32.GESTURECONFIG>());

            TADNLog.Log(
                _isGestureConfigured
                    ? "TrayInputDiag.Gesture.SetConfig: ok"
                    : $"TrayInputDiag.Gesture.SetConfig failed: error={Marshal.GetLastWin32Error()}");
            return _isGestureConfigured;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayInputDiag.Gesture.SetConfig threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool TryRegisterPointerTarget(uint pointerType)
    {
        if (_registeredPointerTargets.Contains(pointerType)) return true;

        try
        {
            bool registered = User32.RegisterPointerInputTargetEx(_window.Handle, pointerType, observe: true);
            if (registered)
            {
                _registeredPointerTargets.Add(pointerType);
                TADNLog.Log($"TrayInputDiag.Pointer.Register: {PointerTypeName(pointerType)} observe=true");
                return true;
            }

            TADNLog.Log(
                $"TrayInputDiag.Pointer.Register failed: {PointerTypeName(pointerType)} "
                + $"error={Marshal.GetLastWin32Error()}");
            return false;
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"TrayInputDiag.Pointer.Register threw: {PointerTypeName(pointerType)} "
                + $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void TryUnregisterPointerTarget(uint pointerType)
    {
        try
        {
            if (!User32.UnregisterPointerInputTargetEx(_window.Handle, pointerType))
                TADNLog.Log(
                    $"TrayInputDiag.Pointer.Unregister failed: {PointerTypeName(pointerType)} "
                    + $"error={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"TrayInputDiag.Pointer.Unregister threw: {PointerTypeName(pointerType)} "
                + $"{ex.GetType().Name}: {ex.Message}");
        }

        _registeredPointerTargets.Remove(pointerType);
    }

    private bool TryHandleShotgunInputMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case User32.WM_MOUSEWHEEL:
            case User32.WM_MOUSEHWHEEL:
                LogClassicWheelDiagnostics(msg, wParam, lParam);
                return true;
            case User32.WM_POINTERWHEEL:
            case User32.WM_POINTERHWHEEL:
            case User32.WM_POINTERUPDATE:
            case User32.WM_POINTERDOWN:
            case User32.WM_POINTERUP:
            case User32.WM_POINTERENTER:
            case User32.WM_POINTERLEAVE:
            case User32.WM_NCPOINTERUPDATE:
            case User32.WM_NCPOINTERDOWN:
            case User32.WM_NCPOINTERUP:
                LogPointerDiagnostics(msg, wParam, lParam);
                return true;
            case User32.WM_GESTURE:
                LogGestureDiagnostics(lParam);
                return true;
            case User32.WM_GESTURENOTIFY:
                TADNLog.Log(
                    "TrayInputDiag.GestureNotify: "
                    + $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");
                return true;
            case User32.WM_TOUCH:
                LogTouchDiagnostics(wParam, lParam);
                return true;
            default:
                return false;
        }
    }

    private void LogRawInputDiagnostics(InputHelper.RawInputDiagnostics rawInput)
    {
        if (!TrayInputDiagnosticsEnabled || rawInput is { HasWheelDelta: false, IsHid: false }) return;

        string cursor = User32.GetCursorPos(out User32.POINT cursorPoint)
            ? $"{cursorPoint.X},{cursorPoint.Y} {DescribeTrayBounds(cursorPoint)}"
            : "unknown";

        TADNLog.Log(
            "TrayInputDiag.RawInput: "
            + $"type={RawInputTypeName(rawInput.Type)} "
            + $"device=0x{rawInput.Device.ToInt64():X} "
            + $"wParam=0x{rawInput.RawWParam.ToInt64():X} "
            + $"buttonFlags=0x{rawInput.MouseButtonFlags:X4} "
            + $"wheel={rawInput.WheelDelta} hwheel={rawInput.HorizontalWheelDelta} "
            + $"last={rawInput.LastX},{rawInput.LastY} rawButtons=0x{rawInput.RawButtons:X8} "
            + $"hidSize={rawInput.HidSize} hidCount={rawInput.HidCount} "
            + $"hidData={rawInput.HidData} "
            + $"cursor={cursor}");
    }

    private bool TryProcessPrecisionTouchpadScroll(
        InputHelper.RawInputDiagnostics rawInput,
        out int steps)
    {
        steps = 0;
        if (!_isPrecisionTouchpadScrollEnabled) return false;
        if (!rawInput.IsHid) return false;

        if (!_precisionTouchpadScroll.TryProcess(rawInput.HidData, Environment.TickCount64, out steps))
            return false;

        if (steps == 0) return true;

        TADNLog.Log(
            "TrayInputDiag.PrecisionTouchpad.Scroll: "
            + $"steps={steps} "
            + $"accumulator={_precisionTouchpadScroll.Accumulator:0.##}");
        return true;
    }

    private void PostTouchpadScrollEvents(int steps)
    {
        int direction = Math.Sign(steps);
        int count = Math.Abs(steps);
        for (int i = 0; i < count; i++)
            PostEvent(PrecisionTouchpadScrolled, direction, nameof(PrecisionTouchpadScrolled));
    }

    private void LogClassicWheelDiagnostics(int msg, IntPtr wParam, IntPtr lParam)
    {
        User32.POINT point = ExtractMessagePoint(lParam);
        TADNLog.Log(
            "TrayInputDiag.ClassicWheel: "
            + $"msg={MessageName(msg)} delta={SignedHiWord(wParam)} "
            + $"keys=0x{LoWord(wParam):X4} pt={point.X},{point.Y} {DescribeTrayBounds(point)} "
            + $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");
    }

    private void LogPointerDiagnostics(int msg, IntPtr wParam, IntPtr lParam)
    {
        uint pointerId = LoWord(wParam);
        uint flags = HiWord(wParam);
        bool typeKnown = TryGetPointerType(pointerId, out uint pointerType);
        bool infoKnown = TryGetPointerInfo(pointerId, out User32.POINTER_INFO info);
        User32.POINT point = infoKnown ? info.ptPixelLocation : ExtractMessagePoint(lParam);

        TADNLog.Log(
            "TrayInputDiag.Pointer: "
            + $"msg={MessageName(msg)} pointerId={pointerId} "
            + $"type={(typeKnown ? PointerTypeName(pointerType) : "unknown")} "
            + $"flags=0x{flags:X4} infoFlags=0x{(infoKnown ? info.pointerFlags : 0):X8} "
            + $"inputData={(infoKnown ? info.InputData : 0)} "
            + $"pt={point.X},{point.Y} {DescribeTrayBounds(point)} "
            + $"target=0x{(infoKnown ? info.hwndTarget.ToInt64() : 0):X} "
            + $"source=0x{(infoKnown ? info.sourceDevice.ToInt64() : 0):X} "
            + $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");
    }

    private static bool TryGetPointerType(uint pointerId, out uint pointerType)
    {
        try { return User32.GetPointerType(pointerId, out pointerType); }
        catch
        {
            pointerType = 0;
            return false;
        }
    }

    private static bool TryGetPointerInfo(uint pointerId, out User32.POINTER_INFO info)
    {
        try { return User32.GetPointerInfo(pointerId, out info); }
        catch
        {
            info = default;
            return false;
        }
    }

    private void LogGestureDiagnostics(IntPtr lParam)
    {
        User32.GESTUREINFO gestureInfo = new()
        {
            cbSize = (uint)Marshal.SizeOf<User32.GESTUREINFO>()
        };

        bool gotInfo = false;
        int error = 0;
        try
        {
            gotInfo = User32.GetGestureInfo(lParam, ref gestureInfo);
            if (!gotInfo) error = Marshal.GetLastWin32Error();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayInputDiag.Gesture.GetInfo threw: {ex.GetType().Name}: {ex.Message}");
        }

        if (gotInfo)
        {
            User32.POINT point = new() { X = gestureInfo.ptsLocation.X, Y = gestureInfo.ptsLocation.Y };
            TADNLog.Log(
                "TrayInputDiag.Gesture: "
                + $"id={GestureIdName(gestureInfo.dwID)} flags=0x{gestureInfo.dwFlags:X8} "
                + $"args=0x{gestureInfo.ullArguments:X16} extra={gestureInfo.cbExtraArgs} "
                + $"pt={point.X},{point.Y} {DescribeTrayBounds(point)} "
                + $"target=0x{gestureInfo.hwndTarget.ToInt64():X}");
        }
        else
        {
            TADNLog.Log($"TrayInputDiag.Gesture.GetInfo failed: error={error} lParam=0x{lParam.ToInt64():X}");
        }

        try
        {
            if (!User32.CloseGestureInfoHandle(lParam))
                TADNLog.Log($"TrayInputDiag.Gesture.Close failed: error={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayInputDiag.Gesture.Close threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LogTouchDiagnostics(IntPtr wParam, IntPtr lParam)
    {
        uint count = LoWord(wParam);
        User32.TOUCHINPUT[] inputs = count is 0 or > 256 ? [] : new User32.TOUCHINPUT[count];
        bool gotInfo = false;
        int error = 0;

        if (inputs.Length > 0)
        {
            try
            {
                gotInfo = User32.GetTouchInputInfo(
                    lParam,
                    count,
                    inputs,
                    Marshal.SizeOf<User32.TOUCHINPUT>());
                if (!gotInfo) error = Marshal.GetLastWin32Error();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"TrayInputDiag.Touch.GetInfo threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        string first = "none";
        if (gotInfo && inputs.Length > 0)
        {
            User32.POINT point = new() { X = inputs[0].x / 100, Y = inputs[0].y / 100 };
            first =
                $"id={inputs[0].dwID} flags=0x{inputs[0].dwFlags:X8} "
                + $"pt={point.X},{point.Y} {DescribeTrayBounds(point)}";
        }

        TADNLog.Log(
            "TrayInputDiag.Touch: "
            + $"count={count} gotInfo={gotInfo} error={error} first={first} "
            + $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");

        try
        {
            if (!User32.CloseTouchInputHandle(lParam))
                TADNLog.Log($"TrayInputDiag.Touch.Close failed: error={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayInputDiag.Touch.Close threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogShellCallbackDiagnostics(short notificationCode, IntPtr wParam, IntPtr lParam)
    {
        if (!IsShellCallbackDiagnosticCandidate(notificationCode)) return;

        TADNLog.Log(
            "TrayInputDiag.ShellCallback: "
            + $"notification={MessageName(notificationCode)} "
            + $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");
    }

    private static bool IsShellCallbackDiagnosticCandidate(short notificationCode) =>
        notificationCode
            is User32.WM_MOUSEWHEEL
            or User32.WM_MOUSEHWHEEL
            or User32.WM_GESTURE
            or User32.WM_GESTURENOTIFY
            or User32.WM_TOUCH
            or User32.WM_POINTERWHEEL
            or User32.WM_POINTERHWHEEL
            or User32.WM_POINTERUPDATE
            or User32.WM_NCPOINTERUPDATE;

    private string DescribeTrayBounds(User32.POINT point)
    {
        if (!TryUpdateTrayIconLocation())
            return "inBounds=unknown";

        return "inBounds=" + _trayIconLocation.Contains(point);
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

        NOTIFYICONIDENTIFIER id = MakeIdentifier();

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

    private void InvalidateTrayIconLocationForRefresh() => _lastTrayIconLocationRefreshTick = 0;

    private bool UpdateInputRegistrationForCursor(User32.POINT cursor)
    {
        bool inBounds = _trayIconLocation.Contains(cursor);
        if (inBounds) StartListeningForInput();
        else
        {
            _isPointerOverIcon = false;
            _tooltipHoverSyncPending = true;
            _tooltipKeepOpenRequested = false;
            StopListeningForInput();
        }

        return inBounds;
    }

    /// <summary>
    /// Re-shows a hover-requested tooltip after a shell icon swap.
    /// </summary>
    private void RequestTooltipUpdateAfterIconChange(bool forceShow)
    {
        if (forceShow || _tooltipKeepOpenRequested)
            _tooltipShowRequested = true;

        if (forceShow || _tooltipKeepOpenRequested || _isPointerOverIcon)
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
            _isPointerOverIcon = false;
            _tooltipHoverSyncPending = true;
            _tooltipKeepOpenRequested = false;
            return;
        }

        if (!_trayIconLocation.Contains(cursor))
        {
            _isPointerOverIcon = false;
            _tooltipHoverSyncPending = true;
            _tooltipKeepOpenRequested = false;
        }
    }

    private void StartListeningForInput()
    {
        if (_isListeningForInput) return;
        bool rawInputRegistered = InputHelper.RegisterForMouseInput(_window.Handle);
        bool shotgunRegistered = TrayInputDiagnosticsEnabled && StartShotgunInputDiagnostics();
        _isListeningForInput = rawInputRegistered || shotgunRegistered;
        if (_isListeningForInput)
            TADNLog.Log("TrayInputDiag.StartListening: tray input diagnostics active");
    }

    private void StopListeningForInput()
    {
        if (!_isListeningForInput) return;
        _isListeningForInput = false;
        _precisionTouchpadScroll.Reset();
        _ = InputHelper.UnregisterForMouseInput();
        StopShotgunInputDiagnostics();
        TADNLog.Log("TrayInputDiag.StopListening: tray input diagnostics inactive");
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
        new() { X = (short)(packedPoint & 0xFFFF), Y = (short)((packedPoint >> 16) & 0xFFFF) };

    private static User32.POINT ExtractMessagePoint(IntPtr packedPoint)
    {
        int packed = unchecked((int)packedPoint.ToInt64());
        return ExtractMessagePoint(packed);
    }

    private static ushort LoWord(IntPtr value) => unchecked((ushort)(value.ToInt64() & 0xFFFF));

    private static ushort HiWord(IntPtr value) => unchecked((ushort)((value.ToInt64() >> 16) & 0xFFFF));

    private static short SignedHiWord(IntPtr value) => unchecked((short)HiWord(value));

    private static string RawInputTypeName(uint type) =>
        type switch
        {
            User32.RIM_TYPEMOUSE => "mouse",
            User32.RIM_TYPEKEYBOARD => "keyboard",
            User32.RIM_TYPEHID => "hid",
            _ => "0x" + type.ToString("X")
        };

    private static string PointerTypeName(uint pointerType) =>
        pointerType switch
        {
            User32.PT_POINTER => "PT_POINTER",
            User32.PT_TOUCH => "PT_TOUCH",
            User32.PT_PEN => "PT_PEN",
            User32.PT_MOUSE => "PT_MOUSE",
            User32.PT_TOUCHPAD => "PT_TOUCHPAD",
            _ => "0x" + pointerType.ToString("X")
        };

    private static string GestureIdName(uint gestureId) =>
        gestureId switch
        {
            1 => "GID_BEGIN",
            2 => "GID_END",
            GestureIdZoom => "GID_ZOOM",
            GestureIdPan => "GID_PAN",
            5 => "GID_ROTATE",
            GestureIdTwoFingerTap => "GID_TWOFINGERTAP",
            7 => "GID_PRESSANDTAP",
            _ => "0x" + gestureId.ToString("X")
        };

    private static string MessageName(int msg) =>
        msg switch
        {
            User32.WM_MOUSEWHEEL => "WM_MOUSEWHEEL",
            User32.WM_MOUSEHWHEEL => "WM_MOUSEHWHEEL",
            User32.WM_INPUT => "WM_INPUT",
            User32.WM_GESTURE => "WM_GESTURE",
            User32.WM_GESTURENOTIFY => "WM_GESTURENOTIFY",
            User32.WM_TOUCH => "WM_TOUCH",
            User32.WM_NCPOINTERUPDATE => "WM_NCPOINTERUPDATE",
            User32.WM_NCPOINTERDOWN => "WM_NCPOINTERDOWN",
            User32.WM_NCPOINTERUP => "WM_NCPOINTERUP",
            User32.WM_POINTERUPDATE => "WM_POINTERUPDATE",
            User32.WM_POINTERDOWN => "WM_POINTERDOWN",
            User32.WM_POINTERUP => "WM_POINTERUP",
            User32.WM_POINTERENTER => "WM_POINTERENTER",
            User32.WM_POINTERLEAVE => "WM_POINTERLEAVE",
            User32.WM_POINTERWHEEL => "WM_POINTERWHEEL",
            User32.WM_POINTERHWHEEL => "WM_POINTERHWHEEL",
            _ => "0x" + msg.ToString("X")
        };

    private sealed class PrecisionTouchpadScrollRecognizer
    {
        private const int ReportByteCount = 12;
        private const int ReportId = 0x04;
        private const int MaxFrameGapMs = 200;
        private const int ActiveContactFlags = 0x03;

        private readonly bool[] _frameContactSeen = new bool[16];
        private bool _hasFrame;
        private ushort _frameScanTime;
        private int _frameContactCount;
        private double _frameYSum;
        private bool _hasLastFrame;
        private double _lastAverageY;
        private long _lastFrameTick;
        private double _accumulator;
        private int _unitsPerScrollStep;

        public PrecisionTouchpadScrollRecognizer(int unitsPerScrollStep) =>
            UnitsPerScrollStep = unitsPerScrollStep;

        public double Accumulator => _accumulator;

        public int UnitsPerScrollStep
        {
            get => _unitsPerScrollStep;
            set
            {
                int next = Math.Max(1, value);
                if (_unitsPerScrollStep == next) return;
                _unitsPerScrollStep = next;
                Reset();
            }
        }

        public bool TryProcess(string hidData, long now, out int notches)
        {
            notches = 0;
            if (!TryDecodeReport(hidData, out TouchpadContactReport report))
                return false;

            if (!_hasFrame || report.ScanTime != _frameScanTime)
            {
                notches += CompleteFrame(now);
                StartFrame(report.ScanTime);
            }

            AddReport(report);
            return true;
        }

        public void Reset()
        {
            ClearFrame();
            _hasLastFrame = false;
            _lastAverageY = 0;
            _lastFrameTick = 0;
            _accumulator = 0;
        }

        private void StartFrame(ushort scanTime)
        {
            ClearFrame();
            _hasFrame = true;
            _frameScanTime = scanTime;
        }

        private void AddReport(TouchpadContactReport report)
        {
            if (!report.Active) return;
            if ((uint)report.ContactId >= _frameContactSeen.Length) return;
            if (_frameContactSeen[report.ContactId]) return;

            _frameContactSeen[report.ContactId] = true;
            _frameContactCount++;
            _frameYSum += report.Y;
        }

        private int CompleteFrame(long now)
        {
            if (!_hasFrame) return 0;

            int notches = 0;
            if (_frameContactCount >= 2)
            {
                double averageY = _frameYSum / _frameContactCount;
                if (_hasLastFrame && now - _lastFrameTick <= MaxFrameGapMs)
                {
                    // Finger movement upward lowers the HID Y coordinate. Treat that as positive wheel delta.
                    _accumulator += _lastAverageY - averageY;
                    notches = DrainNotches();
                }
                else
                {
                    _accumulator = 0;
                }

                _hasLastFrame = true;
                _lastAverageY = averageY;
                _lastFrameTick = now;
            }
            else
            {
                _hasLastFrame = false;
                _accumulator = 0;
            }

            ClearFrame();
            return notches;
        }

        private int DrainNotches()
        {
            int notches = 0;
            while (_accumulator >= _unitsPerScrollStep)
            {
                notches++;
                _accumulator -= _unitsPerScrollStep;
            }

            while (_accumulator <= -_unitsPerScrollStep)
            {
                notches--;
                _accumulator += _unitsPerScrollStep;
            }

            return notches;
        }

        private void ClearFrame()
        {
            if (_hasFrame)
                Array.Clear(_frameContactSeen);

            _hasFrame = false;
            _frameScanTime = 0;
            _frameContactCount = 0;
            _frameYSum = 0;
        }

        private static bool TryDecodeReport(string hidData, out TouchpadContactReport report)
        {
            report = default;
            if (hidData.Length < ReportByteCount * 2) return false;
            Span<byte> bytes = stackalloc byte[ReportByteCount];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexNibble(hidData[i * 2]);
                int low = HexNibble(hidData[i * 2 + 1]);
                if (high < 0 || low < 0) return false;
                bytes[i] = (byte)((high << 4) | low);
            }

            if (bytes[0] != ReportId) return false;

            int contactAndFlags = bytes[1];
            int flags = contactAndFlags & 0x0F;
            report = new TouchpadContactReport(
                contactAndFlags >> 4,
                (flags & ActiveContactFlags) == ActiveContactFlags,
                (ushort)(bytes[4] | (bytes[5] << 8)),
                (ushort)(bytes[6] | (bytes[7] << 8)));
            return true;
        }

        private static int HexNibble(char c) =>
            c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => -1
            };

        private readonly record struct TouchpadContactReport(
            int ContactId,
            bool Active,
            ushort Y,
            ushort ScanTime);
    }

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

        NativeIcon? shellIcon = _shellIcon;
        _shellIcon = null;

        _currentIcon?.Dispose();
        if (shellIcon != null && !ReferenceEquals(shellIcon, _currentIcon))
            shellIcon.Dispose();

        _currentIcon = null;
        _trayUpdateThrottler.Dispose();
        _window.Dispose();
    }

    private enum TrayUpdateKind
    {
        Icon,
        MouseInput,
        Tooltip
    }
}

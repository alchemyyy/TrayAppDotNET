using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal enum WindowsTaskManagerHotkeyDecision
{
    PassThrough,
    Suppress,
    SuppressAndActivate
}

/// <summary>Overrides Ctrl+Shift+Esc while enabled and forwards activation to the application.</summary>
internal sealed class WindowsTaskManagerHotkeyOverride : IDisposable
{
    private const int LowLevelKeyboardHookID = 13;
    private const int HookActionCode = 0;
    private const int WindowsMessageKeyDown = 0x0100;
    private const int WindowsMessageKeyUp = 0x0101;
    private const int WindowsMessageSystemKeyDown = 0x0104;
    private const int WindowsMessageSystemKeyUp = 0x0105;
    private const int VirtualKeyEscape = 0x1B;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyAlt = 0x12;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;
    private const short KeyDownMask = unchecked((short)0x8000);

    private static WindowsTaskManagerHotkeyOverride? _activeOverride;

    private readonly Action _activateTaskManager;
    private readonly Action<string>? _log;
    private IntPtr _hookHandle;
    private bool _escapeIsDown;
    private bool _suppressEscapeUntilKeyUp;
    private bool _disposed;
    private int _callbackFailureLogged;

    public WindowsTaskManagerHotkeyOverride(
        Action activateTaskManager,
        Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(activateTaskManager);
        _activateTaskManager = activateTaskManager;
        _log = log;
    }

    /// <summary>Installs or removes the low-level keyboard hook for the Windows shortcut.</summary>
    public void SetEnabled(bool isEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!isEnabled)
        {
            Disable();
            return;
        }

        Enable();
    }

    private unsafe void Enable()
    {
        if (ReferenceEquals(Volatile.Read(ref _activeOverride), this)) return;

        if (_hookHandle == IntPtr.Zero)
        {
            IntPtr moduleHandle = GetModuleHandleW(null);
            if (moduleHandle == IntPtr.Zero)
            {
                LogWin32Failure("resolve the application module", Marshal.GetLastWin32Error());
                return;
            }

            _hookHandle = SetWindowsHookExW(
                LowLevelKeyboardHookID,
                &LowLevelKeyboardProcedure,
                moduleHandle,
                threadID: 0);
            if (_hookHandle == IntPtr.Zero)
            {
                LogWin32Failure("install the keyboard hook", Marshal.GetLastWin32Error());
                return;
            }
        }

        WindowsTaskManagerHotkeyOverride? existing = Interlocked.CompareExchange(
            ref _activeOverride,
            this,
            comparand: null);
        if (existing == null || ReferenceEquals(existing, this)) return;

        Log("Windows Task Manager hotkey override could not be enabled because another override is active.");
        TryUnhook();
    }

    private void Disable()
    {
        Interlocked.CompareExchange(ref _activeOverride, null, this);
        ResetEscapeState();
        TryUnhook();
    }

    private void TryUnhook()
    {
        if (_hookHandle == IntPtr.Zero) return;

        if (!UnhookWindowsHookEx(_hookHandle))
        {
            LogWin32Failure("remove the keyboard hook", Marshal.GetLastWin32Error());
            return;
        }

        _hookHandle = IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr LowLevelKeyboardProcedure(
        int code,
        IntPtr message,
        IntPtr eventData)
    {
        WindowsTaskManagerHotkeyOverride? hotkeyOverride = Volatile.Read(ref _activeOverride);
        try
        {
            if (code != HookActionCode || hotkeyOverride == null || eventData == IntPtr.Zero)
                return CallNextHookEx(IntPtr.Zero, code, message, eventData);

            long messageValue = message.ToInt64();
            bool isKeyDown = messageValue is WindowsMessageKeyDown or WindowsMessageSystemKeyDown;
            bool isKeyUp = messageValue is WindowsMessageKeyUp or WindowsMessageSystemKeyUp;
            if (!isKeyDown && !isKeyUp)
                return CallNextHookEx(hotkeyOverride._hookHandle, code, message, eventData);

            uint virtualKey = unchecked((uint)Marshal.ReadInt32(eventData));
            bool isControlDown = false;
            bool isShiftDown = false;
            bool isAltDown = false;
            bool isWindowsKeyDown = false;
            if (virtualKey == VirtualKeyEscape && isKeyDown)
            {
                isControlDown = IsKeyDown(VirtualKeyControl);
                isShiftDown = IsKeyDown(VirtualKeyShift);
                isAltDown = IsKeyDown(VirtualKeyAlt);
                isWindowsKeyDown =
                    IsKeyDown(VirtualKeyLeftWindows) || IsKeyDown(VirtualKeyRightWindows);
            }

            WindowsTaskManagerHotkeyDecision decision = hotkeyOverride.ProcessKeyboardEvent(
                virtualKey,
                isKeyDown,
                isKeyUp,
                isControlDown,
                isShiftDown,
                isAltDown,
                isWindowsKeyDown);

            switch (decision)
            {
                case WindowsTaskManagerHotkeyDecision.SuppressAndActivate:
                    if (!hotkeyOverride.TryRequestActivation())
                    {
                        hotkeyOverride.ResetEscapeState();
                        return CallNextHookEx(hotkeyOverride._hookHandle, code, message, eventData);
                    }
                    return new IntPtr(1);
                case WindowsTaskManagerHotkeyDecision.Suppress:
                    return new IntPtr(1);
                default:
                    return CallNextHookEx(hotkeyOverride._hookHandle, code, message, eventData);
            }
        }
        catch (Exception exception)
        {
            hotkeyOverride?.ResetEscapeState();
            hotkeyOverride?.LogCallbackFailure(exception);
            return CallNextHookEx(IntPtr.Zero, code, message, eventData);
        }
    }

    /// <summary>Updates Escape key state and determines whether the event belongs to the override.</summary>
    internal WindowsTaskManagerHotkeyDecision ProcessKeyboardEvent(
        uint virtualKey,
        bool isKeyDown,
        bool isKeyUp,
        bool isControlDown,
        bool isShiftDown,
        bool isAltDown,
        bool isWindowsKeyDown)
    {
        if (virtualKey != VirtualKeyEscape) return WindowsTaskManagerHotkeyDecision.PassThrough;

        if (isKeyUp)
        {
            bool suppress = _suppressEscapeUntilKeyUp;
            ResetEscapeState();
            return suppress
                ? WindowsTaskManagerHotkeyDecision.Suppress
                : WindowsTaskManagerHotkeyDecision.PassThrough;
        }

        if (!isKeyDown) return WindowsTaskManagerHotkeyDecision.PassThrough;
        if (_escapeIsDown)
        {
            return _suppressEscapeUntilKeyUp
                ? WindowsTaskManagerHotkeyDecision.Suppress
                : WindowsTaskManagerHotkeyDecision.PassThrough;
        }

        _escapeIsDown = true;
        if (!isControlDown || !isShiftDown || isAltDown || isWindowsKeyDown)
            return WindowsTaskManagerHotkeyDecision.PassThrough;

        _suppressEscapeUntilKeyUp = true;
        return WindowsTaskManagerHotkeyDecision.SuppressAndActivate;
    }

    private bool TryRequestActivation()
    {
        try
        {
            _activateTaskManager();
            return true;
        }
        catch (Exception exception)
        {
            Log($"Windows Task Manager hotkey activation failed: {exception}");
            return false;
        }
    }

    private void ResetEscapeState()
    {
        _escapeIsDown = false;
        _suppressEscapeUntilKeyUp = false;
    }

    private void LogWin32Failure(string operation, int errorCode) =>
        Log($"Windows Task Manager hotkey override failed to {operation} (Win32 error {errorCode}).");

    private void LogCallbackFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _callbackFailureLogged, value: 1) != 0) return;
        Log($"Windows Task Manager hotkey callback failed: {exception}");
    }

    private void Log(string message)
    {
        try
        {
            _log?.Invoke(message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"{message} Logging also failed: {exception}");
        }
    }

    private static bool IsKeyDown(int virtualKey) =>
        (User32.GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;

    public void Dispose()
    {
        if (_disposed) return;

        Disable();
        _disposed = true;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern unsafe IntPtr SetWindowsHookExW(
        int hookID,
        delegate* unmanaged[Stdcall]<int, IntPtr, IntPtr, IntPtr> hookProcedure,
        IntPtr moduleHandle,
        uint threadID);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr message,
        IntPtr eventData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}

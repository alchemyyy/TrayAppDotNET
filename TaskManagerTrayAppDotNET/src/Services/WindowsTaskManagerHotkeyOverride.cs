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
/// <remarks>
/// The low-level hook runs on a dedicated message thread. Hosted on the UI thread, any stall longer than
/// the LowLevelHooksTimeout registry value made Windows skip the hook and, on Windows 7 and later,
/// silently remove it, after which Ctrl+Shift+Esc opened Windows Task Manager again. It also delayed every
/// keystroke on the desktop while the UI thread was busy.
/// </remarks>
internal sealed class WindowsTaskManagerHotkeyOverride : IDisposable
{
    private const int LowLevelKeyboardHookID = 13;
    private const int HookActionCode = 0;
    private const int WindowsMessageKeyDown = 0x0100;
    private const int WindowsMessageKeyUp = 0x0101;
    private const int WindowsMessageSystemKeyDown = 0x0104;
    private const int WindowsMessageSystemKeyUp = 0x0105;
    private const uint WindowsMessageQuit = 0x0012;
    private const uint WindowsMessageUser = 0x0400;
    private const uint PeekMessageNoRemove = 0x0000;
    private const int VirtualKeyEscape = 0x1B;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyAlt = 0x12;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;
    private const short KeyDownMask = unchecked((short)0x8000);
    private const int HookThreadStartTimeoutMilliseconds = 5_000;
    private const int HookThreadStopTimeoutMilliseconds = 5_000;

    private static WindowsTaskManagerHotkeyOverride? _activeOverride;

    private readonly Action _activateTaskManager;
    private readonly Action<string>? _log;
    private Thread? _hookThread;
    private uint _hookThreadID;
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

    private void Enable()
    {
        if (_hookThread != null) return;

        WindowsTaskManagerHotkeyOverride? existing = Interlocked.CompareExchange(
            ref _activeOverride,
            this,
            comparand: null);
        if (existing != null && !ReferenceEquals(existing, this))
        {
            Log("Windows Task Manager hotkey override could not be enabled because another override is active.");
            return;
        }

        TaskCompletionSource<bool> hookInstalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread hookThread = new(() => RunHookThread(hookInstalled))
        {
            IsBackground = true,
            Name = Constants.ApplicationName + ".KeyboardHook",
            // Every keystroke on the desktop waits for this callback, which does only trivial work
            Priority = ThreadPriority.Highest
        };
        hookThread.Start();

        bool isInstalled = hookInstalled.Task.Wait(HookThreadStartTimeoutMilliseconds) && hookInstalled.Task.Result;
        if (isInstalled)
        {
            _hookThread = hookThread;
            return;
        }

        Interlocked.CompareExchange(ref _activeOverride, null, this);
        StopHookThread(hookThread);
    }

    private void Disable()
    {
        Interlocked.CompareExchange(ref _activeOverride, null, this);
        Thread? hookThread = _hookThread;
        _hookThread = null;
        if (hookThread != null) StopHookThread(hookThread);
    }

    private void StopHookThread(Thread hookThread)
    {
        uint hookThreadID = Volatile.Read(ref _hookThreadID);
        if (hookThreadID != 0 && !PostThreadMessageW(hookThreadID, WindowsMessageQuit, IntPtr.Zero, IntPtr.Zero))
            LogWin32Failure("stop the keyboard hook thread", Marshal.GetLastWin32Error());

        if (!hookThread.Join(HookThreadStopTimeoutMilliseconds))
            Log("Windows Task Manager hotkey thread did not stop within five seconds.");
    }

    /// <summary>Owns the hook for its whole lifetime; low-level callbacks arrive while it waits for messages.</summary>
    private unsafe void RunHookThread(TaskCompletionSource<bool> hookInstalled)
    {
        // Creates the thread message queue before the thread ID is published for PostThreadMessage
        _ = PeekMessageW(out _, IntPtr.Zero, WindowsMessageUser, WindowsMessageUser, PeekMessageNoRemove);
        Volatile.Write(ref _hookThreadID, GetCurrentThreadId());

        IntPtr hookHandle = IntPtr.Zero;
        try
        {
            IntPtr moduleHandle = GetModuleHandleW(null);
            if (moduleHandle == IntPtr.Zero)
            {
                LogWin32Failure("resolve the application module", Marshal.GetLastWin32Error());
                hookInstalled.TrySetResult(false);
                return;
            }

            hookHandle = SetWindowsHookExW(
                LowLevelKeyboardHookID,
                &LowLevelKeyboardProcedure,
                moduleHandle,
                threadID: 0);
            if (hookHandle == IntPtr.Zero)
            {
                LogWin32Failure("install the keyboard hook", Marshal.GetLastWin32Error());
                hookInstalled.TrySetResult(false);
                return;
            }

            Volatile.Write(ref _hookHandle, hookHandle);
            hookInstalled.TrySetResult(true);
            while (GetMessageW(out _, IntPtr.Zero, messageFilterMinimum: 0, messageFilterMaximum: 0) > 0)
            {
            }
        }
        catch (Exception exception)
        {
            Log($"Windows Task Manager hotkey thread failed: {exception}");
            hookInstalled.TrySetResult(false);
        }
        finally
        {
            if (hookHandle != IntPtr.Zero && !UnhookWindowsHookEx(hookHandle))
                LogWin32Failure("remove the keyboard hook", Marshal.GetLastWin32Error());

            Volatile.Write(ref _hookHandle, IntPtr.Zero);
            Volatile.Write(ref _hookThreadID, 0);
            ResetEscapeState();
        }
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(
        out NativeMessage message,
        IntPtr windowHandle,
        uint messageFilterMinimum,
        uint messageFilterMaximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        out NativeMessage message,
        IntPtr windowHandle,
        uint messageFilterMinimum,
        uint messageFilterMaximum,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint threadID,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }
}

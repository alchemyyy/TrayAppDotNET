using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>Foreground activation for requests that do not come from input this process received.</summary>
public static class ForegroundWindowFunctions
{
    private const uint InputMouse = 0;

    /// <summary>Brings a top-level window to the foreground, for example from a global keyboard hook.</summary>
    /// <remarks>
    /// Windows only lets the foreground process, or the process that provided the last input, take the
    /// foreground; anything else just flashes its taskbar button. An empty injected mouse input makes this
    /// process the last input provider without moving the cursor. If Windows still refuses, the switch runs
    /// while this thread shares the foreground thread's input state.
    /// </remarks>
    /// <returns>True when the window ends up as the foreground window.</returns>
    public static bool ForceForeground(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        if (User32.GetForegroundWindow() == windowHandle) return true;

        NativeInput emptyMouseInput = new() { Type = InputMouse };
        _ = SendInput(inputCount: 1, ref emptyMouseInput, Marshal.SizeOf<NativeInput>());
        if (User32.SetForegroundWindow(windowHandle) && User32.GetForegroundWindow() == windowHandle)
            return true;

        uint foregroundThreadID = GetWindowThreadProcessId(User32.GetForegroundWindow(), IntPtr.Zero);
        uint currentThreadID = GetCurrentThreadId();
        if (foregroundThreadID == 0
            || foregroundThreadID == currentThreadID
            || !AttachThreadInput(currentThreadID, foregroundThreadID, attach: true))
            return false;

        try
        {
            _ = BringWindowToTop(windowHandle);
            _ = User32.SetForegroundWindow(windowHandle);
        }
        finally
        {
            _ = AttachThreadInput(currentThreadID, foregroundThreadID, attach: false);
        }

        return User32.GetForegroundWindow() == windowHandle;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, ref NativeInput inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, IntPtr processID);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachingThreadID,
        uint attachedThreadID,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // INPUT with its MOUSEINPUT arm; 40 bytes on x64, matching the size SendInput validates
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

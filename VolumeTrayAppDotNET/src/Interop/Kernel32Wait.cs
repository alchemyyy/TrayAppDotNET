using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Interop;

internal static class Kernel32Wait
{
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_FAILED = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0 = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForMultipleObjects(
        uint nCount,
        IntPtr[] lpHandles,
        [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
        uint dwMilliseconds);
}

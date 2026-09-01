using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal static class KillHelperNativeMethods
{
    public const uint PageReadWrite = 0x00000004;
    public const uint FileMapAllAccess = 0x000F001F;
    public static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileMappingW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileMappingW(
        IntPtr fileHandle,
        IntPtr fileMappingAttributes,
        uint pageProtection,
        uint maximumSizeHigh,
        uint maximumSizeLow,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr MapViewOfFile(
        IntPtr fileMappingHandle,
        uint desiredAccess,
        uint fileOffsetHigh,
        uint fileOffsetLow,
        nuint numberOfBytesToMap);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateEventW(
        IntPtr eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool isManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetEvent(IntPtr eventHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe uint WaitForMultipleObjects(
        uint handleCount,
        IntPtr* handles,
        [MarshalAs(UnmanagedType.Bool)] bool waitForAll,
        uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualLock(IntPtr address, nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualUnlock(IntPtr address, nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnmapViewOfFile(IntPtr baseAddress);
}

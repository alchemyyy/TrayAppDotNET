using System.Runtime.InteropServices;
using System.Text;

namespace TrayAppDotNETCommon.Interop;

// Shared kernel32 P/Invokes consumed by both the icon-extraction pipeline and the per-session
// process-lifetime watchers (ProcessHelper / ProcessExitMonitor). One declaration site avoids the
// three-copy drift the pre-refactor layout shipped with.
public static class Kernel32
{
    public const uint PROCESS_TERMINATE = 0x0001;

    // PROCESS_QUERY_LIMITED_INFORMATION is the cheapest right that still resolves a PID to its
    // image path and lets us query AUMID / GetPackageId. Works against UWP and other restricted
    // processes that PROCESS_QUERY_INFORMATION would be refused on.
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);
}

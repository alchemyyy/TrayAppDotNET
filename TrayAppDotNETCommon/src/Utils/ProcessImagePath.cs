using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Resolves a process ID to its executable path through QueryFullProcessImageName. Process.MainModule
/// enumerates the target's modules and throws for every process it cannot open, which made a scan over every
/// process on the machine take seconds; this needs only PROCESS_QUERY_LIMITED_INFORMATION and reads one string.
/// </summary>
public static class ProcessImagePath
{
    // MAX_PATH covers almost every image; a long-path image grows the buffer up to the NT path limit
    private const int InitialCapacity = 260;
    private const int MaximumCapacity = 32_767;

    /// <summary>The full image path, or null when the process is gone or refuses even limited query access.</summary>
    public static string? TryGet(int processID)
    {
        // 0 is the idle process, which has no image
        if (processID <= 0) return null;

        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)processID);
        if (handle == IntPtr.Zero) return null;

        try
        {
            StringBuilder buffer = new(InitialCapacity);
            while (true)
            {
                uint characterCount = (uint)buffer.Capacity;
                if (Kernel32.QueryFullProcessImageNameW(handle, dwFlags: 0, buffer, ref characterCount))
                    return buffer.ToString(startIndex: 0, (int)characterCount);

                if (Marshal.GetLastPInvokeError() != NativeErrors.ERROR_INSUFFICIENT_BUFFER
                    || buffer.Capacity >= MaximumCapacity)
                    return null;

                buffer.EnsureCapacity(Math.Min(buffer.Capacity * 2, MaximumCapacity));
            }
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }
}

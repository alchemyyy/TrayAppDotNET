using System.Buffers;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Reads another process's command line through ProcessCommandLineInformation, which needs only
/// PROCESS_QUERY_LIMITED_INFORMATION (no PEB walk, no PROCESS_VM_READ).
/// </summary>
public static class ProcessCommandLine
{
    /// <summary>The command line, or null when the process is gone or refuses limited query access.</summary>
    public static string? TryGet(int processID)
    {
        if (processID <= 0) return null;

        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)processID);
        if (handle == IntPtr.Zero) return null;

        try
        {
            return TryRead(handle);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    /// <summary>
    /// The command line of a process opened with at least PROCESS_QUERY_LIMITED_INFORMATION; null when the query
    /// fails, empty when the process has none.
    /// </summary>
    public static unsafe string? TryRead(IntPtr processHandle)
    {
        int requiredLength = 0;
        int status = NtDll.NtQueryInformationProcess(
            processHandle,
            NtDll.ProcessCommandLineInformation,
            processInformation: null,
            processInformationLength: 0,
            &requiredLength);
        if (status != NtDll.StatusInfoLengthMismatch || requiredLength < sizeof(NtDll.UNICODE_STRING)) return null;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(requiredLength);
        try
        {
            fixed (byte* data = buffer)
            {
                status = NtDll.NtQueryInformationProcess(
                    processHandle,
                    NtDll.ProcessCommandLineInformation,
                    data,
                    requiredLength,
                    &requiredLength);
                if (status < 0) return null;

                // The returned string's buffer points into ours, right after the header
                NtDll.UNICODE_STRING* commandLine = (NtDll.UNICODE_STRING*)data;
                return commandLine->Buffer == IntPtr.Zero || commandLine->Length == 0
                    ? string.Empty
                    : new string((char*)commandLine->Buffer, startIndex: 0, commandLine->Length / sizeof(char));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

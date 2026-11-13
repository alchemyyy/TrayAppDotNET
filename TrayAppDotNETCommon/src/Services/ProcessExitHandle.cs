using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services;

/// <summary>
/// Another process opened with only SYNCHRONIZE and PROCESS_QUERY_LIMITED_INFORMATION, which is all an unelevated
/// process gets on an elevated one: enough to wait for it, read its start time and read its exit code. A
/// <see cref="Process"/> found by ID opens a PROCESS_ALL_ACCESS handle for its exit code and its exit wait, which UAC
/// refuses on an elevated helper. Holding this handle keeps the ID from being reused and the exit code readable after
/// the process exits.
/// </summary>
internal sealed class ProcessExitHandle : IDisposable
{
    private readonly SafeProcessHandle _handle;

    private ProcessExitHandle(int processID, SafeProcessHandle handle)
    {
        ProcessID = processID;
        _handle = handle;
    }

    public int ProcessID { get; }

    /// <summary>Opens <paramref name="processID"/>; throws <see cref="Win32Exception"/> when it is gone or refuses these rights.</summary>
    public static ProcessExitHandle Open(int processID)
    {
        IntPtr handle = Kernel32.OpenProcess(
            Kernel32.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            (uint)processID);
        if (handle != IntPtr.Zero) return new ProcessExitHandle(processID, new SafeProcessHandle(handle, ownsHandle: true));

        int error = Marshal.GetLastPInvokeError();
        throw new Win32Exception(error, $"Cannot open PID {processID}: {Marshal.GetPInvokeErrorMessage(error)}");
    }

    /// <summary>Reads the creation time (FILETIME, UTC); false when it cannot be read.</summary>
    public bool TryReadStartFileTime(out long startFileTime) =>
        ProcessExitWatch.TryReadStartFileTime(_handle.DangerousGetHandle(), out startFileTime);

    /// <summary>Blocks until the process exits and returns its exit code.</summary>
    public int WaitForExit()
    {
        if (Kernel32.WaitForSingleObject(_handle.DangerousGetHandle(), Kernel32.INFINITE) != Kernel32.WAIT_OBJECT_0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return ReadExitCode();
    }

    /// <summary>Completes with the exit code once the process exits, without holding a thread while it runs.</summary>
    public async Task<int> WaitForExitAsync()
    {
        // This handle keeps the ID from being reused, so the shared watch opens this same process
        ProcessExitWatchResult result = await ProcessExitWatch.WaitForExitAsync(ProcessID, expectedStartFileTime: null)
            .ConfigureAwait(false);
        if (result != ProcessExitWatchResult.Exited)
            throw new InvalidOperationException($"The exit of PID {ProcessID} cannot be watched.");

        return ReadExitCode();
    }

    public void Dispose() => _handle.Dispose();

    private int ReadExitCode() =>
        Kernel32.GetExitCodeProcess(_handle.DangerousGetHandle(), out uint exitCode)
            ? unchecked((int)exitCode)
            : throw new Win32Exception(Marshal.GetLastPInvokeError());
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Small native-backed process actions kept independent from the sampling pipeline.</summary>
internal static class CriticalProcessActions
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotFound = 1168;
    private const uint TerminationExitCode = 1;

    public static bool TryTerminate(int processID, out string errorMessage) =>
        TryTerminate(new ProcessTerminationTarget(processID, 0), out errorMessage);

    public static bool TryTerminate(ProcessTerminationTarget target, out string errorMessage)
    {
        if (target.ProcessID <= 0)
        {
            errorMessage = "The selected process cannot be terminated.";
            return false;
        }

        if (target.ProcessID == Environment.ProcessId)
        {
            errorMessage = "Task Manager cannot terminate itself from this window.";
            return false;
        }

        if (!TryOpenTerminationHandle(target, out IntPtr processHandle, out int openError))
        {
            errorMessage = openError == ErrorNotFound
                ? "The selected process exited or its PID was reused."
                : new Win32Exception(openError).Message;
            return false;
        }

        try
        {
            if (TryTerminateHandle(processHandle, out int terminateError))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = new Win32Exception(terminateError).Message;
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    /// <summary>Opens and validates a handle before the emergency termination path.</summary>
    internal static bool TryOpenTerminationHandle(
        ProcessTerminationTarget target,
        out IntPtr processHandle,
        out int errorCode)
    {
        processHandle = IntPtr.Zero;
        if (target.ProcessID <= 0 || target.ProcessID == Environment.ProcessId)
        {
            errorCode = ErrorInvalidParameter;
            return false;
        }

        uint processAccess = Kernel32.PROCESS_TERMINATE |
            Kernel32.SYNCHRONIZE |
            Kernel32.PROCESS_QUERY_LIMITED_INFORMATION;

        processHandle = Kernel32.OpenProcess(
            processAccess,
            bInheritHandle: false,
            (uint)target.ProcessID);
        if (processHandle == IntPtr.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        if (target.CreationTimeFileTime != 0)
        {
            if (!Kernel32.GetProcessTimes(
                    processHandle,
                    out Kernel32.FILETIME creationTime,
                    out _,
                    out _,
                    out _))
            {
                errorCode = Marshal.GetLastWin32Error();
                _ = Kernel32.CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
                return false;
            }

            long actualCreationTime = unchecked((long)(
                ((ulong)creationTime.HighDateTime << 32) |
                creationTime.LowDateTime));
            if (actualCreationTime != target.CreationTimeFileTime)
            {
                errorCode = ErrorNotFound;
                _ = Kernel32.CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
                return false;
            }
        }

        if (!Kernel32.IsProcessCritical(processHandle, out bool isCritical))
        {
            errorCode = Marshal.GetLastWin32Error();
            _ = Kernel32.CloseHandle(processHandle);
            processHandle = IntPtr.Zero;
            return false;
        }
        if (isCritical)
        {
            errorCode = ErrorAccessDenied;
            _ = Kernel32.CloseHandle(processHandle);
            processHandle = IntPtr.Zero;
            return false;
        }

        errorCode = 0;
        return true;
    }

    /// <summary>Sends termination through a process handle that already pins target identity.</summary>
    internal static bool TryTerminateHandle(IntPtr processHandle, out int errorCode)
    {
        if (processHandle == IntPtr.Zero)
        {
            errorCode = ErrorInvalidParameter;
            return false;
        }

        if (Kernel32.TerminateProcess(processHandle, TerminationExitCode))
        {
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    public static bool TryStart(string command, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            errorMessage = "Enter an executable, document, or URI.";
            return false;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = command.Trim(),
                UseShellExecute = true
            });
            if (process == null)
            {
                errorMessage = "Windows did not create a process for the requested target.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}

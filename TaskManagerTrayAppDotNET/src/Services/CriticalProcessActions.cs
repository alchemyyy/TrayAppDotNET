using System.ComponentModel;
using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Small native-backed process actions kept independent from the sampling pipeline.</summary>
internal static class CriticalProcessActions
{
    private const uint TerminationExitCode = 1;

    public static bool TryTerminate(int processID, out string errorMessage)
    {
        if (processID <= 0)
        {
            errorMessage = "The selected process cannot be terminated.";
            return false;
        }

        if (processID == Environment.ProcessId)
        {
            errorMessage = "Task Manager cannot terminate itself from this window.";
            return false;
        }

        IntPtr processHandle = Kernel32.OpenProcess(
            Kernel32.PROCESS_TERMINATE,
            bInheritHandle: false,
            (uint)processID);
        if (processHandle == IntPtr.Zero)
        {
            errorMessage = new Win32Exception().Message;
            return false;
        }

        try
        {
            if (Kernel32.TerminateProcess(processHandle, TerminationExitCode))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = new Win32Exception().Message;
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
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

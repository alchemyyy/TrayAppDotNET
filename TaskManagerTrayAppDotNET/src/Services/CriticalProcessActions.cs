using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal delegate bool TryTerminateProcessAction(
    ProcessTerminationTarget target,
    out string errorMessage);

internal readonly record struct ExplorerRestartResult(
    bool Succeeded,
    string ErrorMessage);

/// <summary>Small native-backed process actions kept independent from the sampling pipeline.</summary>
internal static class CriticalProcessActions
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotFound = 1168;
    private const int ExplorerExitTimeoutMilliseconds = 5_000;
    private const string ExplorerExecutableName = "explorer.exe";
    private const string ExplorerProcessName = "explorer";
    private const uint TerminationExitCode = 1;

    public static bool TryTerminate(int processID, out string errorMessage) =>
        TryTerminate(new ProcessTerminationTarget(processID, CreationTimeFileTime: 0), out errorMessage);

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
                FileName = command.Trim(), UseShellExecute = true
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

    /// <summary>Terminates every Explorer process captured at invocation, then starts a fresh shell.</summary>
    public static ExplorerRestartResult RestartExplorer(
        TryTerminateProcessAction terminateProcess)
    {
        ArgumentNullException.ThrowIfNull(terminateProcess);

        Process[] explorerProcesses;
        try
        {
            explorerProcesses = Process.GetProcessesByName(ExplorerProcessName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return new ExplorerRestartResult(
                Succeeded: false,
                $"Windows Explorer processes could not be enumerated: {exception.Message}");
        }

        List<Process> terminatedProcesses = new(explorerProcesses.Length);
        List<string> failures = [];
        try
        {
            foreach (Process process in explorerProcesses)
            {
                int processID = -1;
                try
                {
                    processID = process.Id;
                    if (process.HasExited) continue;

                    ProcessTerminationTarget target = new(
                        processID,
                        process.StartTime.ToFileTimeUtc());
                    if (!terminateProcess(target, out string errorMessage))
                    {
                        if (process.HasExited) continue;

                        failures.Add(FormatExplorerFailure(processID, errorMessage));
                        continue;
                    }

                    terminatedProcesses.Add(process);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    failures.Add(FormatExplorerFailure(processID, exception.Message));
                }
            }

            foreach (Process process in terminatedProcesses)
            {
                int processID = -1;
                try
                {
                    processID = process.Id;
                    if (!process.WaitForExit(ExplorerExitTimeoutMilliseconds))
                    {
                        failures.Add(FormatExplorerFailure(
                            processID,
                            errorMessage: "The process did not exit before the timeout."));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    failures.Add(FormatExplorerFailure(processID, exception.Message));
                }
            }
        }
        finally
        {
            foreach (Process process in explorerProcesses)
                process.Dispose();
        }

        ExplorerRestartResult startResult = StartExplorer();
        if (!startResult.Succeeded)
            failures.Add($"Starting {ExplorerExecutableName}: {startResult.ErrorMessage}");
        if (failures.Count > 0)
        {
            return new ExplorerRestartResult(
                Succeeded: false,
                string.Join(Environment.NewLine, failures));
        }

        return startResult;
    }

    private static ExplorerRestartResult StartExplorer()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return new ExplorerRestartResult(
                Succeeded: false,
                ErrorMessage: "The Windows directory could not be resolved.");
        }

        string explorerPath = Path.Combine(windowsDirectory, ExplorerExecutableName);
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath, WorkingDirectory = windowsDirectory, UseShellExecute = false
            });
            return process == null
                ? new ExplorerRestartResult(
                    Succeeded: false,
                    ErrorMessage: "Windows did not create a new Explorer process.")
                : new ExplorerRestartResult(Succeeded: true, string.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return new ExplorerRestartResult(Succeeded: false, exception.Message);
        }
    }

    private static string FormatExplorerFailure(int processID, string errorMessage)
    {
        string identity = processID > 0 ? $"explorer.exe (PID {processID})" : ExplorerExecutableName;
        string detail = string.IsNullOrWhiteSpace(errorMessage)
            ? "The process could not be terminated."
            : errorMessage;
        return $"{identity}: {detail}";
    }
}

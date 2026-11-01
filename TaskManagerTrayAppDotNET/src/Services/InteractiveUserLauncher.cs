using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Opens a command line as the signed-in user the way Windows Task Manager's Create new task does with "Create this
/// task with administrative privileges" off: wdc.dll's <c>WdcRunTaskAsInteractiveUser</c> runs the stock
/// <c>\Microsoft\Windows\Task Manager\Interactive</c> scheduled task in this session, and the task's COM handler opens
/// the command with the Run dialog's <c>ShellExecCmdLine</c> under the user's standard token. Explorer takes no part,
/// so this works while Explorer isn't running, and an elevated Task Manager never hands its token on. Success means
/// Windows started the task; a command it then can't open gets Windows' own Run error message.
/// </summary>
internal static unsafe class InteractiveUserLauncher
{
    private const string LauncherModuleName = "wdc.dll";
    private const string RunTaskExportName = "WdcRunTaskAsInteractiveUser";
    private const int HResultCodeMask = 0x0000FFFF;
    private const int HResultFacilityMask = 0x1FFF0000;
    private const int HResultFacilityWin32 = 0x00070000;

    // The ShellExecCmdLine flags Task Manager's Create new task passes: the working directory computed from the
    // command (0x01), usage logging (0x08), ID-list invocation (0x10), and documents, folders and URIs as well as
    // programs (0x20).
    private const uint RunDialogCommandFlags = 0x39;

    /// <summary>Starts <paramref name="commandLine"/>, parsed like the Run dialog, as the interactive user.</summary>
    public static bool TryRun(string commandLine, string workingDirectory, out string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string modulePath = Path.Combine(Environment.SystemDirectory, LauncherModuleName);
        if (!NativeLibrary.TryLoad(modulePath, out IntPtr module))
        {
            errorMessage = $"Windows Task Manager's {LauncherModuleName} could not be loaded.";
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(module, RunTaskExportName, out IntPtr runTaskAddress))
            {
                errorMessage = $"{LauncherModuleName} does not export {RunTaskExportName}.";
                return false;
            }

            // The export uses Task Scheduler COM without initializing it; the UI thread keeps its STA
            using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
            if (!apartment.IsUsable)
            {
                errorMessage = DescribeHResult("COM initialization failed", apartment.HResult);
                return false;
            }

            // The task's handler neither is nor was started by the foreground process, so without this what it
            // opens (the program, or Windows' error for a command it can't find) can't come in front of Task Manager
            _ = User32.AllowSetForegroundWindow(User32.ASFW_ANY);

            delegate* unmanaged[Stdcall]<char*, char*, uint, int> runTask =
                (delegate* unmanaged[Stdcall]<char*, char*, uint, int>)runTaskAddress;
            int result;
            fixed (char* command = commandLine)
            fixed (char* directory = workingDirectory)
                result = runTask(command, directory, RunDialogCommandFlags);
            if (result >= 0)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = DescribeHResult("Windows Task Manager's Interactive task did not start", result);
            return false;
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    private static string DescribeHResult(string operation, int result)
    {
        string detail = (result & HResultFacilityMask) == HResultFacilityWin32
            ? new Win32Exception(result & HResultCodeMask).Message
            : $"HRESULT 0x{unchecked((uint)result):X8}";
        return $"{operation}: {detail}.";
    }
}

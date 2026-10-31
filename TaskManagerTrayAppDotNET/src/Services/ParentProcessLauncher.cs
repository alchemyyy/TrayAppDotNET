using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

internal readonly record struct ProcessLaunchResult(
    bool Succeeded,
    int ProcessID,
    int ParentProcessID,
    string ErrorMessage);

/// <summary>Keeps the nominated Windows parent object alive across process termination.</summary>
internal sealed class ProcessParentHandle : IDisposable
{
    private IntPtr _handle;

    internal ProcessParentHandle(IntPtr handle, int processID)
    {
        _handle = handle;
        ProcessID = processID;
    }

    internal IntPtr Handle => _handle;

    public int ProcessID { get; }

    public bool IsValid => _handle != IntPtr.Zero && ProcessID > 0;

    public void Dispose()
    {
        IntPtr handleToClose = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handleToClose != IntPtr.Zero)
            _ = Kernel32.CloseHandle(handleToClose);
    }
}

/// <summary>
/// Starts a process with an explicitly nominated Windows parent (PROC_THREAD_ATTRIBUTE_PARENT_PROCESS), used to
/// restart the shell under the desktop Explorer rather than under Task Manager.
/// </summary>
internal static class ParentProcessLauncher
{
    internal const uint CreateNoWindow = 0x08000000;

    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const nuint ParentProcessAttribute = 0x00020000;
    private const uint ProcessCreateProcess = 0x0080;

    /// <summary>Creates an executable with the explicitly nominated process as its Windows parent.</summary>
    public static ProcessLaunchResult StartWithParent(
        ProcessParentHandle parent,
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        uint additionalCreationFlags = 0)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!parent.IsValid)
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The nominated parent process handle is closed.");
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The executable path is empty.");
        }

        if (!Path.IsPathFullyQualified(executablePath))
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: 0,
                ErrorMessage: "The executable path must be fully qualified.");
        }

        IntPtr parentProcessHandle = parent.Handle;
        int parentProcessID = parent.ProcessID;

        IntPtr attributeList = IntPtr.Zero;
        IntPtr parentHandleValue = IntPtr.Zero;
        bool isAttributeListInitialized = false;
        try
        {
            nuint attributeListSize = 0;
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize);
            if (attributeListSize == 0)
                return CreateWin32Failure("Sizing the process attribute list", parentProcessID);

            attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
            if (!InitializeProcThreadAttributeList(
                    attributeList,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize))
                return CreateWin32Failure("Initializing the process attribute list", parentProcessID);
            isAttributeListInitialized = true;

            parentHandleValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentHandleValue, parentProcessHandle);
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    flags: 0,
                    ParentProcessAttribute,
                    parentHandleValue,
                    (nuint)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
                return CreateWin32Failure("Assigning the process parent", parentProcessID);

            StartupInfoEx startupInfo = new()
            {
                StartupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributeList
            };
            StringBuilder commandLine = BuildCommandLine(executablePath, arguments);
            if (!CreateProcessW(
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    ExtendedStartupInfoPresent | additionalCreationFlags,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out ProcessInformation processInformation))
                return CreateWin32Failure($"Starting '{executablePath}'", parentProcessID);

            try
            {
                return new ProcessLaunchResult(
                    Succeeded: true,
                    ProcessID: checked((int)processInformation.ProcessID),
                    ParentProcessID: parentProcessID,
                    ErrorMessage: string.Empty);
            }
            finally
            {
                _ = Kernel32.CloseHandle(processInformation.ThreadHandle);
                _ = Kernel32.CloseHandle(processInformation.ProcessHandle);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException or OutOfMemoryException)
        {
            return new ProcessLaunchResult(
                Succeeded: false,
                ProcessID: 0,
                ParentProcessID: parentProcessID,
                exception.Message);
        }
        finally
        {
            if (parentHandleValue != IntPtr.Zero) Marshal.FreeHGlobal(parentHandleValue);
            if (isAttributeListInitialized) DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
        }
    }

    /// <summary>Opens the desktop Explorer process before a shell restart terminates it.</summary>
    public static bool TryOpenDesktopExplorerParent(
        out ProcessParentHandle? parent,
        out string errorMessage)
    {
        parent = null;
        IntPtr shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero)
        {
            errorMessage = "The Windows desktop Explorer process is not available.";
            return false;
        }

        _ = User32.GetWindowThreadProcessId(shellWindow, out uint processID);
        if (processID is 0 or > int.MaxValue)
        {
            errorMessage = "The Windows desktop Explorer process ID could not be resolved.";
            return false;
        }

        return TryOpenProcessAsParent(checked((int)processID), out parent, out errorMessage);
    }

    internal static bool TryOpenProcessAsParent(
        int processID,
        out ProcessParentHandle? parent,
        out string errorMessage)
    {
        parent = null;
        if (processID <= 0 || processID == Environment.ProcessId)
        {
            errorMessage = "The requested parent process is invalid.";
            return false;
        }

        IntPtr processHandle = Kernel32.OpenProcess(
            ProcessCreateProcess,
            bInheritHandle: false,
            (uint)processID);
        if (processHandle == IntPtr.Zero)
        {
            int openError = Marshal.GetLastWin32Error();
            errorMessage = $"Process {processID} cannot own a new process: " +
                           new Win32Exception(openError).Message;
            return false;
        }

        parent = new ProcessParentHandle(processHandle, processID);
        errorMessage = string.Empty;
        return true;
    }

    internal static StringBuilder BuildCommandLine(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        StringBuilder commandLine = new();
        AppendQuotedCommandLineArgument(commandLine, executablePath);
        foreach (string argument in arguments)
        {
            commandLine.Append(' ');
            AppendQuotedCommandLineArgument(commandLine, argument);
        }

        return commandLine;
    }

    private static void AppendQuotedCommandLineArgument(StringBuilder commandLine, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            commandLine.Append(argument);
            return;
        }

        commandLine.Append('"');
        int pendingBackslashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                pendingBackslashCount++;
                continue;
            }

            if (character == '"')
            {
                commandLine.Append('\\', pendingBackslashCount * 2 + 1);
                commandLine.Append('"');
                pendingBackslashCount = 0;
                continue;
            }

            commandLine.Append('\\', pendingBackslashCount);
            commandLine.Append(character);
            pendingBackslashCount = 0;
        }

        commandLine.Append('\\', pendingBackslashCount * 2);
        commandLine.Append('"');
    }

    private static ProcessLaunchResult CreateWin32Failure(string operation, int parentProcessID)
    {
        int errorCode = Marshal.GetLastWin32Error();
        return new ProcessLaunchResult(
            Succeeded: false,
            ProcessID: 0,
            ParentProcessID: parentProcessID,
            ErrorMessage: $"{operation} failed: {new Win32Exception(errorCode).Message}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountCharacters;
        public uint YCountCharacters;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessID;
        public uint ThreadID;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetShellWindow();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        uint attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);
}

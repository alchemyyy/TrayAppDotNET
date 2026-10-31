using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

internal enum ProcessPriorityLevel : uint
{
    Idle = 0x00000040,
    BelowNormal = 0x00004000,
    Normal = 0x00000020,
    AboveNormal = 0x00008000,
    High = 0x00000080,
    Realtime = 0x00000100
}

internal readonly record struct ProcessAffinityInfo(ulong ProcessMask, ulong SystemMask);

/// <summary>Performs identity-checked native actions requested by the Details row menu.</summary>
internal static class ProcessNativeActions
{
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVirtualMemoryRead = 0x0010;
    private const uint MiniDumpWithFullMemory = 0x00000002;
    private const uint MiniDumpWithUnloadedModules = 0x00000020;
    private const uint MiniDumpWithThreadInfo = 0x00001000;
    private const int MaximumPathCharacters = 32_768;
    private const int MaximumDumpNameAttempts = 100;
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;
    private const int ShowMinimized = 6;
    private const int ShowRestored = 9;

    private static readonly User32.EnumWindowsProc EnumerateWindowCallback = OnEnumerateWindow;
    private static readonly object MiniDumpLock = new();
    private static int _enumerationFailureLogged;

    /// <summary>Returns whether the process currently owns a user-facing top-level window.</summary>
    public static bool HasTopLevelWindow(int processID) => TryResolveTopLevelWindow(processID, out _);

    /// <summary>Routes descendants deepest-first through the supplied termination path.</summary>
    public static bool TryTerminateDescendants(
        ProcessTerminationTarget target,
        TryTerminateProcessAction terminateProcess,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(terminateProcess);
        if (!TryOpenValidatedProcess(
                target,
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                out IntPtr rootHandle,
                out errorMessage))
            return false;

        List<ProcessTreeNode> processes;
        try
        {
            if (!TrySnapshotProcesses(out processes, out errorMessage)) return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(rootHandle);
        }

        long rootCreationTime = target.CreationTimeFileTime;
        if (rootCreationTime == 0)
        {
            foreach (ProcessTreeNode process in processes)
            {
                if (process.ProcessID != target.ProcessID) continue;
                rootCreationTime = process.CreationTimeFileTime;
                break;
            }
        }

        if (rootCreationTime == 0)
        {
            errorMessage = "The process start time could not be read, so its child processes cannot be verified.";
            return false;
        }

        List<ProcessTreeNode> descendants = ProcessTreeDescendants.Collect(
            target.ProcessID,
            rootCreationTime,
            processes);
        if (descendants.Count == 0)
        {
            errorMessage = string.Empty;
            return true;
        }

        List<ProcessTerminationTarget> descendantTargets = new(descendants.Count);
        List<string> failures = [];
        for (int descendantIndex = 0; descendantIndex < descendants.Count; descendantIndex++)
        {
            ProcessTreeNode descendant = descendants[descendantIndex];
            int processID = descendant.ProcessID;
            if (processID == Environment.ProcessId)
            {
                failures.Add($"PID {processID}: Task Manager will not terminate itself.");
                continue;
            }

            IntPtr processHandle = Kernel32.OpenProcess(
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                bInheritHandle: false,
                (uint)processID);
            if (processHandle == IntPtr.Zero)
            {
                int openError = Marshal.GetLastWin32Error();
                if (!IsProcessGoneError(openError))
                    failures.Add($"PID {processID}: {DescribeWin32Error(openError)}");
                continue;
            }

            if (!TryReadCreationTime(processHandle, out long creationTime, out int timeError))
            {
                failures.Add($"PID {processID}: {DescribeWin32Error(timeError)}");
                _ = Kernel32.CloseHandle(processHandle);
                continue;
            }

            // The PID was recycled after the snapshot: the process now behind it is not part of this tree.
            if (creationTime != descendant.CreationTimeFileTime)
            {
                _ = Kernel32.CloseHandle(processHandle);
                continue;
            }

            if (!Kernel32.IsProcessCritical(processHandle, out bool isCritical))
            {
                int criticalError = Marshal.GetLastWin32Error();
                failures.Add($"PID {processID}: {DescribeWin32Error(criticalError)}");
                _ = Kernel32.CloseHandle(processHandle);
                continue;
            }

            if (isCritical)
            {
                failures.Add($"PID {processID}: Windows reports that the process is critical.");
                _ = Kernel32.CloseHandle(processHandle);
                continue;
            }

            descendantTargets.Add(new ProcessTerminationTarget(processID, creationTime));
            _ = Kernel32.CloseHandle(processHandle);
        }

        // Breadth-first discovery means reversing the list terminates children before parents.
        for (int targetIndex = descendantTargets.Count - 1; targetIndex >= 0; targetIndex--)
        {
            ProcessTerminationTarget descendantTarget = descendantTargets[targetIndex];
            if (!terminateProcess(descendantTarget, out string terminationError))
            {
                failures.Add($"PID {descendantTarget.ProcessID}: {terminationError}");
            }
        }

        errorMessage = failures.Count == 0
            ? string.Empty
            : "Some child processes could not be terminated:\n" + string.Join(separator: "\n", failures);
        return failures.Count == 0;
    }

    public static bool TryGetPriority(
        ProcessTerminationTarget target,
        out ProcessPriorityLevel priority,
        out string errorMessage)
    {
        priority = ProcessPriorityLevel.Normal;
        if (!TryOpenValidatedProcess(
                target,
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        try
        {
            uint priorityClass = NativeMethods.GetPriorityClass(processHandle);
            if (priorityClass == 0)
            {
                errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
                return false;
            }

            priority = Enum.IsDefined(typeof(ProcessPriorityLevel), priorityClass)
                ? (ProcessPriorityLevel)priorityClass
                : ProcessPriorityLevel.Normal;
            errorMessage = string.Empty;
            return true;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    public static bool TrySetPriority(
        ProcessTerminationTarget target,
        ProcessPriorityLevel priority,
        out string errorMessage) =>
        TrySetPriority(target, priority, out errorMessage, out _);

    /// <summary>Sets priority and reports the underlying Win32 code so callers can escalate on ACCESS_DENIED.</summary>
    public static bool TrySetPriority(
        ProcessTerminationTarget target,
        ProcessPriorityLevel priority,
        out string errorMessage,
        out int win32Error)
    {
        win32Error = 0;
        if (!Enum.IsDefined(priority))
        {
            errorMessage = "The requested priority is invalid.";
            return false;
        }

        if (!TryOpenValidatedProcess(
                target,
                ProcessSetInformation,
                out IntPtr processHandle,
                out errorMessage,
                out win32Error))
            return false;

        try
        {
            if (NativeMethods.SetPriorityClass(processHandle, (uint)priority))
            {
                errorMessage = string.Empty;
                return true;
            }

            win32Error = Marshal.GetLastWin32Error();
            errorMessage = DescribeWin32Error(win32Error);
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    public static bool TryGetAffinity(
        ProcessTerminationTarget target,
        out ProcessAffinityInfo affinity,
        out string errorMessage)
    {
        affinity = default;
        if (!TryOpenValidatedProcess(
                target,
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        try
        {
            if (!NativeMethods.GetProcessAffinityMask(
                    processHandle,
                    out nuint processAffinityMask,
                    out nuint systemAffinityMask))
            {
                errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
                return false;
            }

            affinity = new ProcessAffinityInfo(
                unchecked(processAffinityMask),
                unchecked(systemAffinityMask));
            errorMessage = string.Empty;
            return true;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    public static bool TrySetAffinity(
        ProcessTerminationTarget target,
        ulong affinityMask,
        out string errorMessage) =>
        TrySetAffinity(target, affinityMask, out errorMessage, out _);

    /// <summary>Sets affinity and reports the underlying Win32 code so callers can escalate on ACCESS_DENIED.</summary>
    public static bool TrySetAffinity(
        ProcessTerminationTarget target,
        ulong affinityMask,
        out string errorMessage,
        out int win32Error)
    {
        win32Error = 0;
        if (affinityMask == 0)
        {
            errorMessage = "Select at least one processor.";
            return false;
        }

        if (!TryOpenValidatedProcess(
                target,
                ProcessSetInformation,
                out IntPtr processHandle,
                out errorMessage,
                out win32Error))
            return false;

        try
        {
            if (NativeMethods.SetProcessAffinityMask(processHandle, checked((nuint)affinityMask)))
            {
                errorMessage = string.Empty;
                return true;
            }

            win32Error = Marshal.GetLastWin32Error();
            errorMessage = DescribeWin32Error(win32Error);
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// Returns true when changing the target's priority or affinity would require elevation, i.e. opening
    /// it for PROCESS_SET_INFORMATION at the current integrity level is denied. Own or same-integrity
    /// processes (and targets that failed for other reasons, such as having exited) return false.
    /// </summary>
    internal static bool RequiresElevationForModify(ProcessTerminationTarget target) =>
        RequiresElevationForAccess(target, ProcessSetInformation);

    /// <summary>Returns true when creating a memory dump of the target would require elevation.</summary>
    internal static bool RequiresElevationForDump(ProcessTerminationTarget target) =>
        RequiresElevationForAccess(target, ProcessQueryInformation | ProcessVirtualMemoryRead);

    private static bool RequiresElevationForAccess(ProcessTerminationTarget target, uint desiredAccess)
    {
        const int accessDenied = 5;
        if (TryOpenValidatedProcess(target, desiredAccess, out IntPtr processHandle, out _, out int win32Error))
        {
            _ = Kernel32.CloseHandle(processHandle);
            return false;
        }

        return win32Error == accessDenied;
    }

    /// <summary>
    /// Writes a full dump of <paramref name="target"/> into a new file in this user's temp folder. Used when this
    /// process can read the target itself; for any other target the UI still creates the file here and the elevated
    /// broker writes into it (<see cref="TryCreateDumpFile"/>, <see cref="TryWriteMemoryDump"/>).
    /// </summary>
    public static bool TryCreateMemoryDump(
        ProcessTerminationTarget target,
        out string dumpPath,
        out string errorMessage)
    {
        if (!TryCreateDumpFile(target, out dumpPath, out SafeFileHandle? file, out errorMessage)) return false;

        bool written;
        using (file)
        {
            written = TryWriteMemoryDump(target, file, out errorMessage);
        }

        if (written) return true;

        DiscardDumpFile(dumpPath);
        dumpPath = string.Empty;
        return false;
    }

    /// <summary>
    /// Creates the empty, uniquely named dump file in this user's temp folder, open for read and write. This always
    /// runs in the unelevated UI: a same-user process controls that folder (junctions included), so only a process
    /// with the same rights may create files there.
    /// </summary>
    public static bool TryCreateDumpFile(
        ProcessTerminationTarget target,
        out string dumpPath,
        [NotNullWhen(true)] out SafeFileHandle? file,
        out string errorMessage)
    {
        dumpPath = string.Empty;
        file = null;
        string processName = $"Process-{target.ProcessID}";
        if (TryOpenValidatedProcess(target, desiredAccess: 0, out IntPtr processHandle, out _))
        {
            if (TryResolveImagePath(processHandle, out string imagePath, out _))
                processName = Path.GetFileNameWithoutExtension(imagePath);
            _ = Kernel32.CloseHandle(processHandle);
        }

        try
        {
            string dumpDirectory = Path.Combine(Path.GetTempPath(), path2: "TaskManagerTrayAppDotNET");
            Directory.CreateDirectory(dumpDirectory);
            dumpPath = CreateUniqueDumpPath(dumpDirectory, processName, target.ProcessID);
            file = File.OpenHandle(
                dumpPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                FileOptions.SequentialScan);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            dumpPath = string.Empty;
            errorMessage = exception.Message;
            return false;
        }
    }

    /// <summary>Writes a full dump of <paramref name="target"/> into an open, empty file.</summary>
    public static bool TryWriteMemoryDump(ProcessTerminationTarget target, SafeFileHandle file, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(file);
        uint desiredAccess = ProcessQueryInformation | ProcessVirtualMemoryRead;
        if (!TryOpenValidatedProcess(target, desiredAccess, out IntPtr processHandle, out errorMessage))
            return false;

        try
        {
            const uint dumpFlags = MiniDumpWithFullMemory | MiniDumpWithUnloadedModules | MiniDumpWithThreadInfo;
            bool dumpCreated;
            lock (MiniDumpLock)
            {
                // DbgHelp is process-global and documents all of its functions as single-threaded
                dumpCreated = NativeMethods.MiniDumpWriteDump(
                    processHandle,
                    (uint)target.ProcessID,
                    file,
                    dumpFlags,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            if (dumpCreated)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    /// <summary>Deletes a dump file that was not written; best effort.</summary>
    public static void DiscardDumpFile(string dumpPath)
    {
        if (!string.IsNullOrEmpty(dumpPath)) TryDeleteFile(dumpPath);
    }

    public static bool TryOpenFileLocation(
        ProcessTerminationTarget target,
        out string errorMessage)
    {
        if (!TryResolveImagePath(target, out string imagePath, out errorMessage)) return false;

        return ShellFileActions.TryOpenContainingFolderAndSelect(imagePath, out errorMessage);
    }

    public static bool TryOpenProperties(
        ProcessTerminationTarget target,
        out string errorMessage)
    {
        if (!TryResolveImagePath(target, out string imagePath, out errorMessage)) return false;

        return ExplorerProcessLauncher.TryShellExecute(
            imagePath,
            arguments: null,
            workingDirectory: Path.GetDirectoryName(imagePath),
            verb: "properties",
            out _,
            out errorMessage);
    }

    public static bool TrySwitchToWindow(ProcessTerminationTarget target, out string errorMessage) =>
        TryActOnWindow(target, ProcessWindowAction.SwitchTo, out errorMessage);

    public static bool TryBringWindowToFront(ProcessTerminationTarget target, out string errorMessage) =>
        TryActOnWindow(target, ProcessWindowAction.BringToFront, out errorMessage);

    public static bool TryMinimizeWindow(ProcessTerminationTarget target, out string errorMessage) =>
        TryActOnWindow(target, ProcessWindowAction.Minimize, out errorMessage);

    public static bool TryMaximizeWindow(ProcessTerminationTarget target, out string errorMessage) =>
        TryActOnWindow(target, ProcessWindowAction.Maximize, out errorMessage);

    private static bool TryActOnWindow(
        ProcessTerminationTarget target,
        ProcessWindowAction action,
        out string errorMessage)
    {
        if (!TryValidateProcessIdentity(target, out errorMessage)) return false;
        if (!TryResolveTopLevelWindow(target.ProcessID, out IntPtr windowHandle))
        {
            errorMessage = "The process no longer has a user-facing window.";
            return false;
        }

        bool actionSucceeded;
        switch (action)
        {
            case ProcessWindowAction.SwitchTo:
                if (User32.IsIconic(windowHandle))
                    _ = User32.ShowWindowAsync(windowHandle, ShowRestored);
                actionSucceeded = User32.SetForegroundWindow(windowHandle);
                break;
            case ProcessWindowAction.BringToFront:
                if (User32.IsIconic(windowHandle))
                    _ = User32.ShowWindowAsync(windowHandle, ShowRestored);
                _ = User32.BringWindowToTop(windowHandle);
                actionSucceeded = User32.SetForegroundWindow(windowHandle);
                break;
            case ProcessWindowAction.Minimize:
                actionSucceeded = User32.ShowWindowAsync(windowHandle, ShowMinimized);
                break;
            case ProcessWindowAction.Maximize:
                actionSucceeded = User32.ShowWindowAsync(windowHandle, ShowMaximized);
                break;
            default:
                errorMessage = "The requested window action is invalid.";
                return false;
        }

        if (actionSucceeded)
        {
            errorMessage = string.Empty;
            return true;
        }

        int actionError = Marshal.GetLastWin32Error();
        errorMessage = actionError == 0
            ? "Windows rejected the requested window action."
            : DescribeWin32Error(actionError);
        return false;
    }

    private static bool TryResolveImagePath(
        ProcessTerminationTarget target,
        out string imagePath,
        out string errorMessage)
    {
        imagePath = string.Empty;
        if (!TryOpenValidatedProcess(
                target,
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        try
        {
            return TryResolveImagePath(processHandle, out imagePath, out errorMessage);
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
    }

    private static bool TryResolveImagePath(
        IntPtr processHandle,
        out string imagePath,
        out string errorMessage)
    {
        string? path = ProcessImagePath.TryGet(processHandle, new StringBuilder(MaximumPathCharacters));
        imagePath = path ?? string.Empty;
        errorMessage = path == null ? DescribeWin32Error(Marshal.GetLastPInvokeError()) : string.Empty;
        return path != null;
    }

    private static bool TryValidateProcessIdentity(
        ProcessTerminationTarget target,
        out string errorMessage)
    {
        if (!TryOpenValidatedProcess(
                target,
                Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        _ = Kernel32.CloseHandle(processHandle);
        return true;
    }

    internal static bool TryOpenValidatedProcess(
        ProcessTerminationTarget target,
        uint desiredAccess,
        out IntPtr processHandle,
        out string errorMessage) =>
        TryOpenValidatedProcess(target, desiredAccess, out processHandle, out errorMessage, out _);

    private static bool TryOpenValidatedProcess(
        ProcessTerminationTarget target,
        uint desiredAccess,
        out IntPtr processHandle,
        out string errorMessage,
        out int win32Error)
    {
        processHandle = IntPtr.Zero;
        win32Error = 0;
        if (target.ProcessID <= 0)
        {
            errorMessage = "The selected process is not available.";
            return false;
        }

        processHandle = Kernel32.OpenProcess(
            desiredAccess | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            (uint)target.ProcessID);
        if (processHandle == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            errorMessage = DescribeWin32Error(win32Error);
            return false;
        }

        if (target.CreationTimeFileTime == 0)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (!TryReadCreationTime(processHandle, out long actualCreationTime, out int timeError))
        {
            win32Error = timeError;
            errorMessage = DescribeWin32Error(timeError);
            _ = Kernel32.CloseHandle(processHandle);
            processHandle = IntPtr.Zero;
            return false;
        }

        if (actualCreationTime == target.CreationTimeFileTime)
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = "The selected process exited or its PID was reused.";
        _ = Kernel32.CloseHandle(processHandle);
        processHandle = IntPtr.Zero;
        return false;
    }

    private static bool TryReadCreationTime(
        IntPtr processHandle,
        out long creationTime,
        out int errorCode)
    {
        if (Kernel32.GetProcessTimes(
                processHandle,
                out Kernel32.FILETIME nativeCreationTime,
                out _,
                out _,
                out _))
        {
            creationTime = unchecked((long)(
                ((ulong)nativeCreationTime.HighDateTime << 32) |
                nativeCreationTime.LowDateTime));
            errorCode = 0;
            return true;
        }

        creationTime = 0;
        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    private static bool TrySnapshotProcesses(
        out List<ProcessTreeNode> processes,
        out string errorMessage)
    {
        Dictionary<int, SystemProcessData> processData = [];
        using SystemProcessSnapshot snapshot = new();
        if (!snapshot.TryCapture(processData))
        {
            processes = [];
            errorMessage = "The process list could not be read.";
            return false;
        }

        processes = new List<ProcessTreeNode>(processData.Count);
        foreach ((int processID, SystemProcessData process) in processData)
            processes.Add(new ProcessTreeNode(processID, process.ParentProcessID, process.CreationTimeTicks));

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryResolveTopLevelWindow(int processID, out IntPtr windowHandle)
    {
        WindowEnumerationState state = new(processID);
        GCHandle stateHandle = GCHandle.Alloc(state, GCHandleType.Normal);
        try
        {
            _ = User32.EnumWindows(
                EnumerateWindowCallback,
                GCHandle.ToIntPtr(stateHandle));
            windowHandle = state.WindowHandle;
            return windowHandle != IntPtr.Zero;
        }
        finally
        {
            stateHandle.Free();
        }
    }

    private static bool OnEnumerateWindow(IntPtr windowHandle, IntPtr statePointer)
    {
        // An exception must never unwind into user32: under Native AOT that terminates the process
        try
        {
            GCHandle stateHandle = GCHandle.FromIntPtr(statePointer);
            if (stateHandle.Target is not WindowEnumerationState state) return false;

            _ = User32.GetWindowThreadProcessId(windowHandle, out uint processID);
            if (processID != (uint)state.ProcessID || !IsUserFacingWindow(windowHandle)) return true;

            state.WindowHandle = windowHandle;
            return false;
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _enumerationFailureLogged, value: 1) == 0)
                TADNLog.Log($"ProcessNativeActions: window enumeration callback failed: {exception}");
            return false;
        }
    }

    // An unreadable cloak state counts as user-facing here: switching to a window that turns out to be cloaked is
    // harmless, while hiding the Switch to entry for a real window is not
    private static bool IsUserFacingWindow(IntPtr windowHandle) =>
        TopLevelWindowClassifier.Classify(windowHandle) != TopLevelWindowClass.NotQualifying;

    private static string CreateUniqueDumpPath(string directory, string processName, int processID)
    {
        string safeProcessName = SanitizeFileName(processName);
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        for (int attempt = 0; attempt < MaximumDumpNameAttempts; attempt++)
        {
            string suffix = attempt == 0 ? string.Empty : $"-{attempt}";
            string fileName = $"{safeProcessName}-{processID}-{timestamp}{suffix}.dmp";
            string candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{safeProcessName}-{processID}-{Guid.NewGuid():N}.dmp");
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        StringBuilder result = new(value.Length);
        for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            char character = value[characterIndex];
            result.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        }

        return result.Length == 0 ? "Process" : result.ToString();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not remove failed process dump '{path}': {exception.Message}");
        }
    }

    private static bool IsProcessGoneError(int errorCode) => errorCode is 87 or 1168;

    private static string DescribeWin32Error(int errorCode) =>
        errorCode == 0 ? "The native operation failed." : new Win32Exception(errorCode).Message;

    private enum ProcessWindowAction : byte
    {
        SwitchTo,
        BringToFront,
        Minimize,
        Maximize
    }

    private sealed class WindowEnumerationState(int processID)
    {
        public int ProcessID { get; } = processID;
        public IntPtr WindowHandle { get; set; }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetPriorityClass(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetPriorityClass(IntPtr processHandle, uint priorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessAffinityMask(
            IntPtr processHandle,
            out nuint processAffinityMask,
            out nuint systemAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessAffinityMask(IntPtr processHandle, nuint processAffinityMask);

        // Runs in the elevated broker too: never resolve dbghelp.dll from the application folder or PATH.
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr processHandle,
            uint processID,
            SafeFileHandle fileHandle,
            uint dumpType,
            IntPtr exceptionParameters,
            IntPtr userStreamParameters,
            IntPtr callbackParameters);
    }
}

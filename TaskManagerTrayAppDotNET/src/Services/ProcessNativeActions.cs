using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

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
    private const uint ToolhelpSnapshotProcesses = 0x00000002;
    private const uint MiniDumpWithFullMemory = 0x00000002;
    private const uint MiniDumpWithUnloadedModules = 0x00000020;
    private const uint MiniDumpWithThreadInfo = 0x00001000;
    private const int MaximumPathCharacters = 32_768;
    private const int MaximumDumpNameAttempts = 100;
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;
    private const int ShowMinimized = 6;
    private const int ShowRestored = 9;
    private const uint GetWindowOwner = 4;
    private const int WindowExtendedStyle = -20;
    private const long WindowStyleToolWindow = 0x00000080L;
    private const uint DwmWindowAttributeCloaked = 14;

    private static readonly NativeMethods.EnumWindowsCallback EnumerateWindowCallback = OnEnumerateWindow;
    private static readonly object MiniDumpLock = new();

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

        List<ProcessTreeEntry> processEntries = [];
        try
        {
            if (!TrySnapshotProcesses(processEntries, out errorMessage)) return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(rootHandle);
        }

        List<int> descendantProcessIDs = FindDescendantProcessIDs(target.ProcessID, processEntries);
        if (descendantProcessIDs.Count == 0)
        {
            errorMessage = string.Empty;
            return true;
        }

        List<ProcessTerminationTarget> descendantTargets = new(descendantProcessIDs.Count);
        List<string> failures = [];
        for (int descendantIndex = 0; descendantIndex < descendantProcessIDs.Count; descendantIndex++)
        {
            int processID = descendantProcessIDs[descendantIndex];
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

            // A child cannot predate its root. This rejects stale parent PIDs after PID reuse.
            if (target.CreationTimeFileTime != 0 && creationTime < target.CreationTimeFileTime)
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
        out string errorMessage)
    {
        if (!Enum.IsDefined(priority))
        {
            errorMessage = "The requested priority is invalid.";
            return false;
        }

        if (!TryOpenValidatedProcess(
                target,
                ProcessSetInformation,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        try
        {
            if (NativeMethods.SetPriorityClass(processHandle, (uint)priority))
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
        out string errorMessage)
    {
        if (affinityMask == 0)
        {
            errorMessage = "Select at least one processor.";
            return false;
        }

        if (!TryOpenValidatedProcess(
                target,
                ProcessSetInformation,
                out IntPtr processHandle,
                out errorMessage))
            return false;

        try
        {
            if (NativeMethods.SetProcessAffinityMask(processHandle, checked((nuint)affinityMask)))
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

    public static bool TryCreateMemoryDump(
        ProcessTerminationTarget target,
        out string dumpPath,
        out string errorMessage)
    {
        dumpPath = string.Empty;
        uint desiredAccess = ProcessQueryInformation | ProcessVirtualMemoryRead;
        if (!TryOpenValidatedProcess(target, desiredAccess, out IntPtr processHandle, out errorMessage))
            return false;

        try
        {
            string processName = TryResolveImagePath(processHandle, out string imagePath, out _)
                ? Path.GetFileNameWithoutExtension(imagePath)
                : $"Process-{target.ProcessID}";
            string dumpDirectory = Path.Combine(Path.GetTempPath(), path2: "TaskManagerTrayAppDotNET");
            Directory.CreateDirectory(dumpDirectory);
            dumpPath = CreateUniqueDumpPath(dumpDirectory, processName, target.ProcessID);

            using FileStream output = new(
                dumpPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.SequentialScan);
            const uint dumpFlags = MiniDumpWithFullMemory | MiniDumpWithUnloadedModules | MiniDumpWithThreadInfo;
            bool dumpCreated;
            lock (MiniDumpLock)
            {
                // DbgHelp is process-global and documents all of its functions as single-threaded
                dumpCreated = NativeMethods.MiniDumpWriteDump(
                    processHandle,
                    (uint)target.ProcessID,
                    output.SafeFileHandle.DangerousGetHandle(),
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

            int dumpError = Marshal.GetLastWin32Error();
            output.Dispose();
            TryDeleteFile(dumpPath);
            dumpPath = string.Empty;
            errorMessage = DescribeWin32Error(dumpError);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!string.IsNullOrEmpty(dumpPath)) TryDeleteFile(dumpPath);
            dumpPath = string.Empty;
            errorMessage = exception.Message;
            return false;
        }
        finally
        {
            _ = Kernel32.CloseHandle(processHandle);
        }
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
                if (NativeMethods.IsIconic(windowHandle))
                    _ = NativeMethods.ShowWindowAsync(windowHandle, ShowRestored);
                actionSucceeded = NativeMethods.SetForegroundWindow(windowHandle);
                break;
            case ProcessWindowAction.BringToFront:
                if (NativeMethods.IsIconic(windowHandle))
                    _ = NativeMethods.ShowWindowAsync(windowHandle, ShowRestored);
                _ = NativeMethods.BringWindowToTop(windowHandle);
                actionSucceeded = NativeMethods.SetForegroundWindow(windowHandle);
                break;
            case ProcessWindowAction.Minimize:
                actionSucceeded = NativeMethods.ShowWindowAsync(windowHandle, ShowMinimized);
                break;
            case ProcessWindowAction.Maximize:
                actionSucceeded = NativeMethods.ShowWindowAsync(windowHandle, ShowMaximized);
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
        StringBuilder pathBuffer = new(MaximumPathCharacters);
        uint characterCount = (uint)pathBuffer.Capacity;
        if (Kernel32.QueryFullProcessImageNameW(processHandle, dwFlags: 0, pathBuffer, ref characterCount))
        {
            imagePath = pathBuffer.ToString(startIndex: 0, checked((int)characterCount));
            errorMessage = string.Empty;
            return true;
        }

        imagePath = string.Empty;
        errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
        return false;
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

    private static bool TryOpenValidatedProcess(
        ProcessTerminationTarget target,
        uint desiredAccess,
        out IntPtr processHandle,
        out string errorMessage)
    {
        processHandle = IntPtr.Zero;
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
            errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
            return false;
        }

        if (target.CreationTimeFileTime == 0)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (!TryReadCreationTime(processHandle, out long actualCreationTime, out int timeError))
        {
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
        List<ProcessTreeEntry> processEntries,
        out string errorMessage)
    {
        IntPtr snapshotHandle = NativeMethods.CreateToolhelp32Snapshot(ToolhelpSnapshotProcesses, processID: 0);
        if (snapshotHandle == new IntPtr(-1))
        {
            errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
            return false;
        }

        try
        {
            NativeMethods.ProcessEntry processEntry = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>()
            };
            if (!NativeMethods.Process32First(snapshotHandle, ref processEntry))
            {
                errorMessage = DescribeWin32Error(Marshal.GetLastWin32Error());
                return false;
            }

            do
            {
                processEntries.Add(new ProcessTreeEntry(
                    checked((int)processEntry.ProcessID),
                    checked((int)processEntry.ParentProcessID)));
                processEntry.Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>();
            } while (NativeMethods.Process32Next(snapshotHandle, ref processEntry));

            errorMessage = string.Empty;
            return true;
        }
        finally
        {
            _ = Kernel32.CloseHandle(snapshotHandle);
        }
    }

    private static List<int> FindDescendantProcessIDs(
        int rootProcessID,
        IReadOnlyList<ProcessTreeEntry> processEntries)
    {
        Dictionary<int, List<int>> childrenByParent = new(processEntries.Count);
        for (int entryIndex = 0; entryIndex < processEntries.Count; entryIndex++)
        {
            ProcessTreeEntry processEntry = processEntries[entryIndex];
            if (!childrenByParent.TryGetValue(processEntry.ParentProcessID, out List<int>? children))
            {
                children = [];
                childrenByParent.Add(processEntry.ParentProcessID, children);
            }

            children.Add(processEntry.ProcessID);
        }

        List<int> descendants = [];
        HashSet<int> visited = [rootProcessID];
        int parentIndex = -1;
        while (true)
        {
            int parentProcessID = parentIndex < 0 ? rootProcessID : descendants[parentIndex];
            if (childrenByParent.TryGetValue(parentProcessID, out List<int>? children))
            {
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    int childProcessID = children[childIndex];
                    if (childProcessID > 0 && visited.Add(childProcessID))
                        descendants.Add(childProcessID);
                }
            }

            parentIndex++;
            if (parentIndex >= descendants.Count) break;
        }

        return descendants;
    }

    private static bool TryResolveTopLevelWindow(int processID, out IntPtr windowHandle)
    {
        WindowEnumerationState state = new(processID);
        GCHandle stateHandle = GCHandle.Alloc(state, GCHandleType.Normal);
        try
        {
            _ = NativeMethods.EnumWindows(
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
        GCHandle stateHandle = GCHandle.FromIntPtr(statePointer);
        if (stateHandle.Target is not WindowEnumerationState state) return false;

        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processID);
        if (processID != (uint)state.ProcessID || !IsUserFacingWindow(windowHandle)) return true;

        state.WindowHandle = windowHandle;
        return false;
    }

    private static bool IsUserFacingWindow(IntPtr windowHandle)
    {
        if (!NativeMethods.IsWindow(windowHandle) || !NativeMethods.IsWindowVisible(windowHandle)) return false;
        if (NativeMethods.GetWindow(windowHandle, GetWindowOwner) != IntPtr.Zero) return false;
        if (NativeMethods.GetWindowTextLength(windowHandle) <= 0) return false;

        long extendedStyle = NativeMethods.GetWindowLongPtr(windowHandle, WindowExtendedStyle).ToInt64();
        if ((extendedStyle & WindowStyleToolWindow) != 0) return false;

        int result = NativeMethods.DwmGetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCloaked,
            out int isCloaked,
            sizeof(int));
        return result != 0 || isCloaked == 0;
    }

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

    private readonly record struct ProcessTreeEntry(int ProcessID, int ParentProcessID);

    private sealed class WindowEnumerationState(int processID)
    {
        public int ProcessID { get; } = processID;
        public IntPtr WindowHandle { get; set; }
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ProcessEntry
        {
            public uint Size;
            public uint UsageCount;
            public uint ProcessID;
            public nuint DefaultHeapID;
            public uint ModuleID;
            public uint ThreadCount;
            public uint ParentProcessID;
            public int BasePriority;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string? ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processID);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

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

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr processHandle,
            uint processID,
            IntPtr fileHandle,
            uint dumpType,
            IntPtr exceptionParameters,
            IntPtr userStreamParameters,
            IntPtr callbackParameters);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processID);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr windowHandle,
            uint attribute,
            out int value,
            uint valueSize);
    }
}

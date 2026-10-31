using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>Finds, inspects and ends the processes a launch test starts.</summary>
public static class TestProcesses
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>Waits until a process named <paramref name="processName"/> runs and returns it.</summary>
    public static Process WaitForProcess(string processName, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + checked((long)timeout.TotalMilliseconds);
        while (Environment.TickCount64 < deadline)
        {
            Process[] candidates = Process.GetProcessesByName(processName);
            if (candidates.Length > 0)
            {
                Process result = candidates[0];
                for (int candidateIndex = 1; candidateIndex < candidates.Length; candidateIndex++)
                    candidates[candidateIndex].Dispose();
                return result;
            }

            Thread.Sleep(millisecondsTimeout: 50);
        }

        throw new TimeoutException($"Process '{processName}' did not start within {timeout}.");
    }

    /// <summary>Reads the parent process ID Windows recorded when <paramref name="processID"/> was created.</summary>
    public static int ReadParentProcessID(int processID)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, processID: 0);
        if (snapshot == InvalidHandleValue)
            throw new InvalidOperationException($"Process snapshot failed with error {Marshal.GetLastWin32Error()}.");

        try
        {
            ProcessEntry entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32FirstW(snapshot, ref entry))
                throw new InvalidOperationException($"Process enumeration failed with error {Marshal.GetLastWin32Error()}.");

            do
            {
                if (entry.ProcessID == processID)
                    return checked((int)entry.ParentProcessID);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
            } while (Process32NextW(snapshot, ref entry));

            throw new InvalidOperationException($"Process {processID} was not found in the process snapshot.");
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }
    }

    /// <summary>Ends a process the test started, with its children, and waits for it to exit.</summary>
    public static void TerminateIfRunning(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(milliseconds: 5_000));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint UsageCount;
        public int ProcessID;
        public nuint DefaultHeapID;
        public uint ModuleID;
        public uint ThreadCount;
        public uint ParentProcessID;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFile;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processID);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using TrayAppDotNETCommon.Interop;
using Xunit;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>Finds, inspects and ends the processes a launch test starts.</summary>
public static class TestProcesses
{
    private const uint SnapshotProcesses = 0x00000002;
    private const uint DaclSecurityInformation = 0x00000004;
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

    /// <summary>Starts cmd.exe reading commands from its redirected input, so the test decides when it exits and how.</summary>
    public static Process StartWaitingCommandProcessor() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/d /k")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");

    /// <summary>
    /// Leaves this account only SYNCHRONIZE and PROCESS_QUERY_LIMITED_INFORMATION on <paramref name="process"/>, what
    /// an unelevated process gets on an elevated one, so opening it with PROCESS_ALL_ACCESS is refused as under UAC.
    /// Handles opened before, such as the one <paramref name="process"/> holds, keep their rights.
    /// </summary>
    public static void GrantOnlyWaitAndQuery(Process process)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        const uint access = Kernel32.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION;
        RawSecurityDescriptor descriptor = new($"D:(A;;0x{access:X};;;{identity.User!.Value})");
        byte[] binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, offset: 0);
        Assert.True(
            SetKernelObjectSecurity(process.SafeHandle, DaclSecurityInformation, binary),
            $"SetKernelObjectSecurity failed with {Marshal.GetLastPInvokeError()}.");
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

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeHandle handle, uint securityInformation, byte[] securityDescriptor);
}

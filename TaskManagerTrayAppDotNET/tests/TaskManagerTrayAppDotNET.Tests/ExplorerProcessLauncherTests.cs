using System.Diagnostics;
using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ExplorerProcessLauncherTests
{
    private const string ParentSmokeTestEnvironmentVariable =
        "TASK_MANAGER_RUN_EXPLORER_PARENT_TEST";

    [Fact]
    public void BuildCommandLineQuotesExecutableAndArgumentsForCreateProcess()
    {
        string executablePath = @"C:\Program Files\Example\example.exe";
        string[] arguments =
        [
            "plain",
            "two words",
            "embedded\"quote",
            string.Empty,
            @"C:\trailing\"
        ];

        string commandLine = ExplorerProcessLauncher
            .BuildCommandLine(executablePath, arguments)
            .ToString();

        Assert.Equal(
            "\"C:\\Program Files\\Example\\example.exe\" plain \"two words\" " +
            "\"embedded\\\"quote\" \"\" C:\\trailing\\",
            commandLine);
    }

    [Fact]
    public void ExplicitExplorerParentLaunchDoesNotUseTheCallingProcessAsParent()
    {
        if (!IsParentSmokeTestEnabled()) return;

        string powerShellPath = GetPowerShellPath();
        Assert.True(
            ExplorerProcessLauncher.TryOpenDesktopExplorerParent(
                out ProcessParentHandle? parent,
                out string parentErrorMessage),
            parentErrorMessage);
        using ProcessParentHandle explorerParent = Assert.IsType<ProcessParentHandle>(parent);
        ProcessLaunchResult launchResult = ExplorerProcessLauncher.StartWithParent(
            explorerParent,
            powerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            Path.GetDirectoryName(powerShellPath)!,
            ExplorerProcessLauncher.CreateNoWindow);
        Assert.True(launchResult.Succeeded, launchResult.ErrorMessage);
        Assert.NotEqual(Environment.ProcessId, launchResult.ParentProcessID);

        using Process process = Process.GetProcessById(launchResult.ProcessID);
        try
        {
            Thread.Sleep(millisecondsTimeout: 100);
            Assert.False(
                process.HasExited,
                process.HasExited ? $"Launched process exited with code {process.ExitCode}." : string.Empty);
            int actualParentProcessID = ReadParentProcessID(process.Id);
            Assert.Equal(launchResult.ParentProcessID, actualParentProcessID);
            using Process parentProcess = Process.GetProcessById(actualParentProcessID);
            Assert.Equal("explorer", parentProcess.ProcessName, ignoreCase: true);
        }
        finally
        {
            TerminateIfRunning(process);
        }
    }

    [Fact]
    public void TerminatedParentHandleStillOwnsTheLaunchedProcess()
    {
        if (!IsParentSmokeTestEnabled()) return;

        using Process terminatedParent = StartSleepingPowerShell();
        int terminatedParentProcessID = terminatedParent.Id;
        Assert.True(
            ExplorerProcessLauncher.TryOpenProcessAsParent(
                terminatedParentProcessID,
                out ProcessParentHandle? parent,
                out string parentErrorMessage),
            parentErrorMessage);
        using ProcessParentHandle retainedParent = Assert.IsType<ProcessParentHandle>(parent);
        TerminateIfRunning(terminatedParent);

        string powerShellPath = GetPowerShellPath();
        ProcessLaunchResult launchResult = ExplorerProcessLauncher.StartWithParent(
            retainedParent,
            powerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            Path.GetDirectoryName(powerShellPath)!,
            ExplorerProcessLauncher.CreateNoWindow);
        Assert.True(launchResult.Succeeded, launchResult.ErrorMessage);

        using Process process = Process.GetProcessById(launchResult.ProcessID);
        try
        {
            Assert.Equal(terminatedParentProcessID, ReadParentProcessID(process.Id));
            Assert.NotEqual(Environment.ProcessId, terminatedParentProcessID);
        }
        finally
        {
            TerminateIfRunning(process);
        }
    }

    [Fact]
    public void ExplorerShellLaunchUsesExplorerAsTheProcessParent()
    {
        if (!IsParentSmokeTestEnabled()) return;

        DirectoryInfo testDirectory = Directory.CreateTempSubdirectory("TmtadnParent-");
        string processName = "TmtadnProbe" + Guid.NewGuid().ToString(format: "N")[..8];
        string executablePath = Path.Combine(testDirectory.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, path2: "ping.exe"), executablePath);

        Process? process = null;
        try
        {
            bool launched = false;
            int launchError = 0;
            string launchErrorMessage = string.Empty;
            Thread launchThread = new(() =>
            {
                launched = ExplorerProcessLauncher.TryShellExecute(
                    executablePath,
                    arguments: "127.0.0.1 -n 30",
                    workingDirectory: testDirectory.FullName,
                    verb: "open",
                    out launchError,
                    out launchErrorMessage);
            })
            {
                IsBackground = true,
                Name = "Explorer parent smoke test launcher"
            };
            launchThread.SetApartmentState(ApartmentState.STA);
            launchThread.Start();
            Assert.True(launchThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(launched, $"{launchError}: {launchErrorMessage}");

            process = WaitForProcess(processName, TimeSpan.FromSeconds(10));
            int parentProcessID = ReadParentProcessID(process.Id);
            Assert.NotEqual(Environment.ProcessId, parentProcessID);
            using Process parentProcess = Process.GetProcessById(parentProcessID);
            Assert.Equal("explorer", parentProcess.ProcessName, ignoreCase: true);
        }
        finally
        {
            if (process != null)
            {
                TerminateIfRunning(process);
                process.Dispose();
            }

            testDirectory.Delete(recursive: true);
        }
    }

    private static bool IsParentSmokeTestEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(ParentSmokeTestEnvironmentVariable),
            b: "1",
            StringComparison.Ordinal);

    private static Process StartSleepingPowerShell()
    {
        string powerShellPath = GetPowerShellPath();
        ProcessStartInfo startInfo = new()
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("The parent probe process could not be started.");
    }

    private static string GetPowerShellPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            path2: @"WindowsPowerShell\v1.0\powershell.exe");

    private static Process WaitForProcess(string processName, TimeSpan timeout)
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

    private static int ReadParentProcessID(int processID)
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

    private static void TerminateIfRunning(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(milliseconds: 5_000));
    }

    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

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

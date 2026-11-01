using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class InteractiveUserLauncherTests
{
    // Opt-in: the test runs Windows Task Manager's Interactive task, which opens a console window in this session
    private const string InteractiveLaunchSmokeTestEnvironmentVariable =
        "TASK_MANAGER_RUN_INTERACTIVE_LAUNCH_TEST";

    private const int TokenElevationTypeFull = 2;
    private const int TokenElevationTypeLimited = 3;

    [EnvironmentFact(InteractiveLaunchSmokeTestEnvironmentVariable)]
    public void InteractiveTaskStartsTheCommandAsTheSessionUserWithoutThisProcessToken()
    {
        DirectoryInfo testDirectory = Directory.CreateTempSubdirectory("TmtadnInteractive-");
        string processName = "TmtadnProbe" + Guid.NewGuid().ToString(format: "N")[..8];
        string executablePath = Path.Combine(testDirectory.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, path2: "ping.exe"), executablePath);

        Process? process = null;
        try
        {
            // Without its arguments the probe would print its usage and exit at once
            Assert.True(
                InteractiveUserLauncher.TryRun(
                    $"\"{executablePath}\" 127.0.0.1 -n 30",
                    testDirectory.FullName,
                    out string errorMessage),
                errorMessage);

            process = TestProcesses.WaitForProcess(processName, TimeSpan.FromSeconds(10));
            using Process currentProcess = Process.GetCurrentProcess();
            Assert.Equal(currentProcess.SessionId, process.SessionId);
            Assert.NotEqual(Environment.ProcessId, TestProcesses.ReadParentProcessID(process.Id));

            Thread.Sleep(millisecondsTimeout: 1_000);
            Assert.False(
                process.HasExited,
                process.HasExited ? $"The probe exited with code {process.ExitCode}." : string.Empty);
            if (ReadElevationType(Environment.ProcessId) == TokenElevationTypeFull)
                Assert.Equal(TokenElevationTypeLimited, ReadElevationType(process.Id));
        }
        finally
        {
            if (process != null)
            {
                TestProcesses.TerminateIfRunning(process);
                process.Dispose();
            }

            testDirectory.Delete(recursive: true);
        }
    }

    private static int ReadElevationType(int processID)
    {
        const uint processQueryLimitedInformation = 0x1000;
        const uint tokenQuery = 0x0008;
        const int tokenElevationTypeClass = 18;

        IntPtr processHandle = NativeMethods.OpenProcess(
            processQueryLimitedInformation,
            inheritHandle: false,
            (uint)processID);
        Assert.NotEqual(IntPtr.Zero, processHandle);
        try
        {
            Assert.True(NativeMethods.OpenProcessToken(processHandle, tokenQuery, out IntPtr tokenHandle));
            try
            {
                Assert.True(NativeMethods.GetTokenInformation(
                    tokenHandle,
                    tokenElevationTypeClass,
                    out int elevationType,
                    sizeof(int),
                    out _));
                return elevationType;
            }
            finally
            {
                _ = NativeMethods.CloseHandle(tokenHandle);
            }
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processHandle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int informationClass,
            out int information,
            int informationLength,
            out int returnLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processID);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
    }
}

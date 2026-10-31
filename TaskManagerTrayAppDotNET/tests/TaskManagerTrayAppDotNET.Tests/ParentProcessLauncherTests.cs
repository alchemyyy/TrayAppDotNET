using System.Diagnostics;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ParentProcessLauncherTests
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

        string commandLine = ParentProcessLauncher
            .BuildCommandLine(executablePath, arguments)
            .ToString();

        Assert.Equal(
            "\"C:\\Program Files\\Example\\example.exe\" plain \"two words\" " +
            "\"embedded\\\"quote\" \"\" C:\\trailing\\",
            commandLine);
    }

    [EnvironmentFact(ParentSmokeTestEnvironmentVariable)]
    public void ExplicitExplorerParentLaunchDoesNotUseTheCallingProcessAsParent()
    {
        string powerShellPath = GetPowerShellPath();
        Assert.True(
            ParentProcessLauncher.TryOpenDesktopExplorerParent(
                out ProcessParentHandle? parent,
                out string parentErrorMessage),
            parentErrorMessage);
        using ProcessParentHandle explorerParent = Assert.IsType<ProcessParentHandle>(parent);
        ProcessLaunchResult launchResult = ParentProcessLauncher.StartWithParent(
            explorerParent,
            powerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            Path.GetDirectoryName(powerShellPath)!,
            ParentProcessLauncher.CreateNoWindow);
        Assert.True(launchResult.Succeeded, launchResult.ErrorMessage);
        Assert.NotEqual(Environment.ProcessId, launchResult.ParentProcessID);

        using Process process = Process.GetProcessById(launchResult.ProcessID);
        try
        {
            Thread.Sleep(millisecondsTimeout: 100);
            Assert.False(
                process.HasExited,
                process.HasExited ? $"Launched process exited with code {process.ExitCode}." : string.Empty);
            int actualParentProcessID = TestProcesses.ReadParentProcessID(process.Id);
            Assert.Equal(launchResult.ParentProcessID, actualParentProcessID);
            using Process parentProcess = Process.GetProcessById(actualParentProcessID);
            Assert.Equal("explorer", parentProcess.ProcessName, ignoreCase: true);
        }
        finally
        {
            TestProcesses.TerminateIfRunning(process);
        }
    }

    [EnvironmentFact(ParentSmokeTestEnvironmentVariable)]
    public void TerminatedParentHandleStillOwnsTheLaunchedProcess()
    {
        using Process terminatedParent = StartSleepingPowerShell();
        int terminatedParentProcessID = terminatedParent.Id;
        Assert.True(
            ParentProcessLauncher.TryOpenProcessAsParent(
                terminatedParentProcessID,
                out ProcessParentHandle? parent,
                out string parentErrorMessage),
            parentErrorMessage);
        using ProcessParentHandle retainedParent = Assert.IsType<ProcessParentHandle>(parent);
        TestProcesses.TerminateIfRunning(terminatedParent);

        string powerShellPath = GetPowerShellPath();
        ProcessLaunchResult launchResult = ParentProcessLauncher.StartWithParent(
            retainedParent,
            powerShellPath,
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            Path.GetDirectoryName(powerShellPath)!,
            ParentProcessLauncher.CreateNoWindow);
        Assert.True(launchResult.Succeeded, launchResult.ErrorMessage);

        using Process process = Process.GetProcessById(launchResult.ProcessID);
        try
        {
            Assert.Equal(terminatedParentProcessID, TestProcesses.ReadParentProcessID(process.Id));
            Assert.NotEqual(Environment.ProcessId, terminatedParentProcessID);
        }
        finally
        {
            TestProcesses.TerminateIfRunning(process);
        }
    }

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
}

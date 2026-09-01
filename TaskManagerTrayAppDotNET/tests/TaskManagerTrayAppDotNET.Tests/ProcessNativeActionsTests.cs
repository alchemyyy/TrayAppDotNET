using System.Diagnostics;
using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessNativeActionsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DescendantsUseTheSuppliedTerminationPath()
    {
        using Process parentProcess = StartParentWithSleepingChild();
        string? childProcessIDText = await parentProcess.StandardOutput
            .ReadLineAsync()
            .WaitAsync(TestTimeout);
        Assert.True(
            int.TryParse(childProcessIDText, NumberStyles.None, CultureInfo.InvariantCulture,
                out int childProcessID),
            $"The child PID was invalid: {childProcessIDText}");
        using Process childProcess = Process.GetProcessById(childProcessID);
        List<ProcessTerminationTarget> requestedTargets = [];

        try
        {
            ProcessTerminationTarget parentTarget = new(
                parentProcess.Id,
                parentProcess.StartTime.ToFileTimeUtc());

            bool succeeded = ProcessNativeActions.TryTerminateDescendants(
                parentTarget,
                CaptureTermination,
                out string errorMessage);

            Assert.True(succeeded, errorMessage);
            Assert.Contains(requestedTargets, target => target.ProcessID == childProcessID);
            Assert.False(childProcess.HasExited);
        }
        finally
        {
            if (!childProcess.HasExited)
                childProcess.Kill();
            if (!parentProcess.HasExited)
                parentProcess.Kill(entireProcessTree: true);
        }

        bool CaptureTermination(ProcessTerminationTarget target, out string errorMessage)
        {
            requestedTargets.Add(target);
            errorMessage = string.Empty;
            return true;
        }
    }

    private static Process StartParentWithSleepingChild()
    {
        string powerShellPath = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        ProcessStartInfo startInfo = new()
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$childProcess = Start-Process -FilePath \"$env:SystemRoot\\System32\\ping.exe\" " +
            "-ArgumentList @('127.0.0.1', '-t') -WindowStyle Hidden -PassThru; " +
            "[Console]::Out.WriteLine($childProcess.Id); [Console]::Out.Flush(); " +
            "Wait-Process -Id $childProcess.Id");
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException(
            "The test could not start a parent process.");
    }
}

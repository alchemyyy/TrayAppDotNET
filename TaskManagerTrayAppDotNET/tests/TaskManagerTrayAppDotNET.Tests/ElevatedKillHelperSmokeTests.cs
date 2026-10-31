using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ElevatedKillHelperSmokeTests
{
    private const string SmokeTestEnvironmentVariable = "TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST";

    [Fact]
    public void StandardHelperTerminatesThroughTheSharedMailbox()
    {
        List<string> logMessages = [];
        ElevatedKillHelperStartResult startResult = ElevatedKillHelperClient.TryStart(
            elevate: false,
            log: logMessages.Add);
        Assert.Equal(ElevatedKillHelperStartOutcome.Ready, startResult.Outcome);
        using ElevatedKillHelperSession helperSession = Assert.IsType<ElevatedKillHelperSession>(
            startResult.Session);
        Assert.Equal(
            KillHelperProtocol.RequiredReliabilityFlags,
            helperSession.HardeningFlags & KillHelperProtocol.RequiredReliabilityFlags);

        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
            Assert.True(helperSession.TryArm(target, generation: 1));
            Assert.True(helperSession.TryRequestTermination(
                target,
                generation: 1,
                out long requestSequence));
            Assert.True(helperSession.TryWaitForResponse(
                requestSequence,
                timeoutMilliseconds: 5_000,
                out int result,
                out int errorCode));
            Assert.Equal(KillHelperProtocol.ResultSuccess, result);
            Assert.Equal(expected: 0, errorCode);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [EnvironmentFact(SmokeTestEnvironmentVariable)]
    public void ElevatedHelperTerminatesThroughTheSharedMailbox()
    {
        List<string> logMessages = [];
        ElevatedKillHelperStartResult startResult = ElevatedKillHelperClient.TryStart(
            elevate: true,
            log: logMessages.Add);
        Assert.Equal(ElevatedKillHelperStartOutcome.Ready, startResult.Outcome);
        using ElevatedKillHelperSession helperSession = Assert.IsType<ElevatedKillHelperSession>(startResult.Session);
        Assert.Equal(
            KillHelperProtocol.RequiredHardeningFlags,
            helperSession.HardeningFlags & KillHelperProtocol.RequiredHardeningFlags);

        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
            Assert.True(helperSession.TryArm(target, generation: 1));
            Assert.True(helperSession.TryRequestTermination(target, generation: 1, out long requestSequence));
            Assert.True(helperSession.TryWaitForResponse(
                requestSequence,
                timeoutMilliseconds: 5_000,
                out int result,
                out int errorCode));
            Assert.Equal(KillHelperProtocol.ResultSuccess, result);
            Assert.Equal(expected: 0, errorCode);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }

        using Process identityMismatchProcess = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget mismatchedTarget = new(
                identityMismatchProcess.Id,
                identityMismatchProcess.StartTime.ToFileTimeUtc() + 1);
            Assert.True(helperSession.TryArm(mismatchedTarget, generation: 2));
            Assert.True(helperSession.TryRequestTermination(
                mismatchedTarget,
                generation: 2,
                out long requestSequence));
            Assert.True(helperSession.TryWaitForResponse(
                requestSequence,
                timeoutMilliseconds: 5_000,
                out int result,
                out int errorCode));
            Assert.Equal(KillHelperProtocol.ResultIdentityMismatch, result);
            Assert.NotEqual(expected: 0, errorCode);
            Assert.False(identityMismatchProcess.HasExited);
        }
        finally
        {
            if (!identityMismatchProcess.HasExited)
                identityMismatchProcess.Kill();
        }
    }

    private static Process StartSleepingProcess()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, path2: "ping.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("The helper smoke test could not start ping.exe.");
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CriticalProcessActionsTests
{
    [Fact]
    public void TryTerminateRefusesTheTaskManagerProcess()
    {
        bool result = CriticalProcessActions.TryTerminate(Environment.ProcessId, out string errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void TryTerminateUsesTheExactProcessIdentity()
    {
        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());

            bool result = CriticalProcessActions.TryTerminate(target, out string errorMessage);

            Assert.True(result, errorMessage);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            TerminateIfRunning(process);
        }
    }

    [Fact]
    public void TryTerminateRejectsAChangedCreationTime()
    {
        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc() + 1);

            bool result = CriticalProcessActions.TryTerminate(target, out string errorMessage);

            Assert.False(result);
            Assert.NotEmpty(errorMessage);
            Assert.False(process.HasExited);
        }
        finally
        {
            TerminateIfRunning(process);
        }
    }

    [Fact]
    public void KillHelperMailboxLayoutMatchesTheNativeProtocol()
    {
        Assert.Equal(KillHelperProtocol.MailboxSize, Marshal.SizeOf<KillHelperMailbox>());
        Assert.Equal(expected: 64,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.ArmPayloadSequence)).ToInt32());
        Assert.Equal(expected: 128,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.FirePayloadSequence)).ToInt32());
        Assert.Equal(expected: 192,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.FireResponseSequence)).ToInt32());
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
        return process ?? throw new InvalidOperationException("The process identity test could not start ping.exe.");
    }

    private static void TerminateIfRunning(Process process)
    {
        if (!process.HasExited)
            process.Kill();
    }
}

using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTerminationServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ConstructionDoesNotLaunchTheElevatedHelper()
    {
        int launchCount = 0;
        using ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                Interlocked.Increment(ref launchCount);
                return FailedStart("Unexpected launch");
            });

        ElevatedHelperStatus status = service.GetElevatedHelperStatus();

        Assert.Equal(expected: 0, Volatile.Read(ref launchCount));
        Assert.Equal(ElevatedHelperState.NotRequested, status.State);
    }

    [Fact]
    public void LowLevelLauncherRejectsAZeroOwnerHandle()
    {
        List<string> logMessages = [];

        ElevatedKillHelperStartResult startResult = ElevatedKillHelperClient.TryStart(
            IntPtr.Zero,
            logMessages.Add);

        Assert.Equal(ElevatedKillHelperStartOutcome.Failed, startResult.Outcome);
        Assert.Null(startResult.Session);
        Assert.Contains(expectedSubstring: "window is not ready", startResult.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(logMessages);
    }

    [Fact]
    public async Task RepeatedEnableRequestsShareOnePendingSTAThreadLaunch()
    {
        using ManualResetEventSlim launcherEntered = new(false);
        using ManualResetEventSlim releaseLauncher = new(false);
        int launchCount = 0;
        IntPtr receivedOwnerWindowHandle = IntPtr.Zero;
        string? launcherThreadName = null;
        ApartmentState launcherApartmentState = ApartmentState.Unknown;
        bool isLauncherBackground = false;
        using ProcessTerminationService service = new(
            log: null,
            (ownerWindowHandle, _) =>
            {
                Interlocked.Increment(ref launchCount);
                receivedOwnerWindowHandle = ownerWindowHandle;
                launcherThreadName = Thread.CurrentThread.Name;
                launcherApartmentState = Thread.CurrentThread.GetApartmentState();
                isLauncherBackground = Thread.CurrentThread.IsBackground;
                launcherEntered.Set();
                Assert.True(releaseLauncher.Wait(TestTimeout));
                return FailedStart("Expected test failure");
            });

        IntPtr ownerWindowHandle = new(0x2468);
        Task<ElevatedHelperStatus> firstAttempt = service.EnableElevatedHelperAsync(ownerWindowHandle);
        Assert.True(launcherEntered.Wait(TestTimeout));
        Task<ElevatedHelperStatus> duplicateAttempt = service.EnableElevatedHelperAsync(ownerWindowHandle);

        Assert.Same(firstAttempt, duplicateAttempt);
        Assert.False(firstAttempt.IsCompleted);
        Assert.Equal(expected: 1, Volatile.Read(ref launchCount));
        Assert.Equal(ownerWindowHandle, receivedOwnerWindowHandle);
        Assert.Equal(expected: "Task Manager elevation launcher", launcherThreadName);
        Assert.Equal(ApartmentState.STA, launcherApartmentState);
        Assert.True(isLauncherBackground);

        releaseLauncher.Set();
        ElevatedHelperStatus completedStatus = await firstAttempt.WaitAsync(TestTimeout);
        Assert.Equal(ElevatedHelperState.Failed, completedStatus.State);
    }

    [Fact]
    public async Task ReadySessionRearmsTheLatestTargetSelectedDuringLaunch()
    {
        using ManualResetEventSlim launcherEntered = new(false);
        using ManualResetEventSlim releaseLauncher = new(false);
        List<(ProcessTerminationTarget? Target, long Generation)> armRequests = [];
        int disposeCount = 0;
        ElevatedKillHelperSession readySession = new(
            static () => true,
            (target, generation) =>
            {
                armRequests.Add((target, generation));
                return true;
            },
            () => Interlocked.Increment(ref disposeCount));
        using ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                launcherEntered.Set();
                if (!releaseLauncher.Wait(TestTimeout))
                    return FailedStart("The test launcher timed out.");
                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    readySession,
                    string.Empty);
            });
        ProcessTerminationTarget firstTarget = new(ProcessID: 2_000_000_001, CreationTimeFileTime: 100);
        ProcessTerminationTarget latestTarget = new(ProcessID: 2_000_000_002, CreationTimeFileTime: 200);
        service.Arm(firstTarget);

        Task<ElevatedHelperStatus> pendingAttempt = service.EnableElevatedHelperAsync(new IntPtr(1));
        Assert.True(launcherEntered.Wait(TestTimeout));
        service.Arm(latestTarget);
        releaseLauncher.Set();

        ElevatedHelperStatus completedStatus = await pendingAttempt.WaitAsync(TestTimeout);
        Assert.Equal(ElevatedHelperState.Ready, completedStatus.State);
        (ProcessTerminationTarget? Target, long Generation) armRequest = Assert.Single(armRequests);
        Assert.Equal(latestTarget, armRequest.Target);
        Assert.Equal(expected: 2, armRequest.Generation);

        service.Dispose();
        Assert.Equal(expected: 1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public async Task ReadyStateMakesSubsequentEnableRequestsIdempotent()
    {
        int launchCount = 0;
        int disposeCount = 0;
        ElevatedKillHelperSession readySession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref disposeCount));
        ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                Interlocked.Increment(ref launchCount);
                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    readySession,
                    string.Empty);
            });
        try
        {
            ElevatedHelperStatus firstStatus = await service
                .EnableElevatedHelperAsync(new IntPtr(1))
                .WaitAsync(TestTimeout);
            Task<ElevatedHelperStatus> secondAttempt = service.EnableElevatedHelperAsync(new IntPtr(2));
            ElevatedHelperStatus secondStatus = await secondAttempt;

            Assert.Equal(ElevatedHelperState.Ready, firstStatus.State);
            Assert.Equal(ElevatedHelperState.Ready, secondStatus.State);
            Assert.True(secondAttempt.IsCompletedSuccessfully);
            Assert.Equal(expected: 1, Volatile.Read(ref launchCount));
            Assert.Equal(expected: 0, Volatile.Read(ref disposeCount));
        }
        finally
        {
            service.Dispose();
        }

        Assert.Equal(expected: 1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public async Task DeclinedInitialAttemptCanBeRetriedManually()
    {
        int launchCount = 0;
        using ProcessTerminationService service = new(
            log: null,
            (_, _) => Interlocked.Increment(ref launchCount) switch
            {
                1 => new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Declined,
                    Session: null,
                    ErrorMessage: "Windows administrator approval was canceled."),
                _ => FailedStart("Second attempt failed")
            });

        ElevatedHelperStatus declinedStatus = await service
            .EnableElevatedHelperAsync(new IntPtr(1))
            .WaitAsync(TestTimeout);
        ElevatedHelperStatus failedStatus = await service
            .EnableElevatedHelperAsync(new IntPtr(1))
            .WaitAsync(TestTimeout);

        Assert.Equal(ElevatedHelperState.Declined, declinedStatus.State);
        Assert.Equal(ElevatedHelperState.Failed, failedStatus.State);
        Assert.Equal(expected: "Second attempt failed", failedStatus.ErrorMessage);
        Assert.Equal(expected: 2, Volatile.Read(ref launchCount));
    }

    [Fact]
    public async Task ZeroOwnerHandleFailsWithoutLaunching()
    {
        int launchCount = 0;
        using ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                Interlocked.Increment(ref launchCount);
                return FailedStart("Unexpected launch");
            });

        ElevatedHelperStatus status = await service.EnableElevatedHelperAsync(IntPtr.Zero);

        Assert.Equal(ElevatedHelperState.Failed, status.State);
        Assert.Contains(expectedSubstring: "window is not ready", status.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected: 0, Volatile.Read(ref launchCount));
    }

    [Fact]
    public async Task DisposeDoesNotWaitAndDisposesALateReadySessionExactlyOnce()
    {
        using ManualResetEventSlim launcherEntered = new(false);
        using ManualResetEventSlim releaseLauncher = new(false);
        using ManualResetEventSlim sessionDisposed = new(false);
        int disposeCount = 0;
        ElevatedKillHelperSession readySession = new(
            static () => true,
            static (_, _) => true,
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                sessionDisposed.Set();
            });
        ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                launcherEntered.Set();
                if (!releaseLauncher.Wait(TestTimeout))
                    return FailedStart("The test launcher timed out.");
                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    readySession,
                    string.Empty);
            });

        Task<ElevatedHelperStatus> pendingAttempt = service.EnableElevatedHelperAsync(new IntPtr(1));
        Assert.True(launcherEntered.Wait(TestTimeout));
        Stopwatch stopwatch = Stopwatch.StartNew();

        service.Dispose();

        stopwatch.Stop();
        ElevatedHelperStatus disposedStatus = await pendingAttempt.WaitAsync(TestTimeout);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(ElevatedHelperState.Disposed, disposedStatus.State);
        Assert.Equal(ElevatedHelperState.Disposed, service.GetElevatedHelperStatus().State);

        releaseLauncher.Set();
        Assert.True(sessionDisposed.Wait(TestTimeout));
        Assert.Equal(ElevatedHelperState.Disposed, service.GetElevatedHelperStatus().State);
        Assert.Equal(expected: 1, Volatile.Read(ref disposeCount));
        service.Dispose();
        Assert.Equal(expected: 1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public async Task LocalTerminationWorksWhileElevationIsPending()
    {
        using ManualResetEventSlim launcherEntered = new(false);
        using ManualResetEventSlim releaseLauncher = new(false);
        using ProcessTerminationService service = new(
            log: null,
            (_, _) =>
            {
                launcherEntered.Set();
                Assert.True(releaseLauncher.Wait(TestTimeout));
                return FailedStart("Expected test failure");
            });
        using Process process = StartSleepingProcess();
        try
        {
            Task<ElevatedHelperStatus> pendingAttempt = service.EnableElevatedHelperAsync(new IntPtr(1));
            Assert.True(launcherEntered.Wait(TestTimeout));
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());

            bool terminated = service.TryTerminate(target, out string errorMessage);

            Assert.True(terminated, errorMessage);
            Assert.True(process.WaitForExit(5_000));
            releaseLauncher.Set();
            _ = await pendingAttempt.WaitAsync(TestTimeout);
        }
        finally
        {
            releaseLauncher.Set();
            if (!process.HasExited)
                process.Kill();
        }
    }

    private static ElevatedKillHelperStartResult FailedStart(string errorMessage) =>
        new(ElevatedKillHelperStartOutcome.Failed, Session: null, errorMessage);

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
        return process ?? throw new InvalidOperationException("The test could not start ping.exe.");
    }
}

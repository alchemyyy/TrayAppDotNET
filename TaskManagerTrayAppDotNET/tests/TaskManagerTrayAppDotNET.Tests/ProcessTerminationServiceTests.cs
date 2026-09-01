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
            (_, _, _) =>
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
            elevate: true,
            log: logMessages.Add);

        Assert.Equal(ElevatedKillHelperStartOutcome.Failed, startResult.Outcome);
        Assert.Null(startResult.Session);
        Assert.Contains(expectedSubstring: "window is not ready", startResult.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(logMessages);
    }

    [Fact]
    public async Task StandardHelperLaunchUsesStandardIntegrityWithoutAnOwner()
    {
        int launchCount = 0;
        int disposeCount = 0;
        IntPtr receivedOwnerWindowHandle = new(1);
        bool? requestedElevation = null;
        string? launcherThreadName = null;
        ApartmentState launcherApartmentState = ApartmentState.Unknown;
        bool isLauncherBackground = false;
        ElevatedKillHelperSession readySession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref disposeCount));
        ProcessTerminationService service = new(
            log: null,
            (ownerWindowHandle, elevate, _) =>
            {
                Interlocked.Increment(ref launchCount);
                receivedOwnerWindowHandle = ownerWindowHandle;
                requestedElevation = elevate;
                launcherThreadName = Thread.CurrentThread.Name;
                launcherApartmentState = Thread.CurrentThread.GetApartmentState();
                isLauncherBackground = Thread.CurrentThread.IsBackground;
                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    readySession,
                    string.Empty);
            });
        try
        {
            bool helperReady = await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout);

            Assert.True(helperReady);
            Assert.Equal(expected: 1, Volatile.Read(ref launchCount));
            Assert.Equal(IntPtr.Zero, receivedOwnerWindowHandle);
            Assert.False(requestedElevation);
            Assert.Equal(expected: "Task Manager native helper launcher", launcherThreadName);
            Assert.Equal(ApartmentState.STA, launcherApartmentState);
            Assert.True(isLauncherBackground);
            Assert.Equal(ElevatedHelperState.NotRequested, service.GetElevatedHelperStatus().State);
        }
        finally
        {
            service.Dispose();
        }

        Assert.Equal(expected: 1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public async Task ElevatedHelperReplacesAReadyStandardHelper()
    {
        int standardDisposeCount = 0;
        int elevatedDisposeCount = 0;
        int standardLaunchCount = 0;
        int elevatedLaunchCount = 0;
        ElevatedKillHelperSession standardSession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref standardDisposeCount));
        ElevatedKillHelperSession elevatedSession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref elevatedDisposeCount));
        ProcessTerminationService service = new(
            log: null,
            (_, elevate, _) =>
            {
                ElevatedKillHelperSession session;
                if (elevate)
                {
                    Interlocked.Increment(ref elevatedLaunchCount);
                    session = elevatedSession;
                }
                else
                {
                    Interlocked.Increment(ref standardLaunchCount);
                    session = standardSession;
                }

                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    session,
                    string.Empty);
            });
        try
        {
            Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));

            ElevatedHelperStatus status = await service
                .EnableElevatedHelperAsync(new IntPtr(1))
                .WaitAsync(TestTimeout);

            Assert.Equal(ElevatedHelperState.Ready, status.State);
            Assert.Equal(expected: 1, Volatile.Read(ref standardLaunchCount));
            Assert.Equal(expected: 1, Volatile.Read(ref elevatedLaunchCount));
            Assert.Equal(expected: 1, Volatile.Read(ref standardDisposeCount));
            Assert.Equal(expected: 0, Volatile.Read(ref elevatedDisposeCount));
        }
        finally
        {
            service.Dispose();
        }

        Assert.Equal(expected: 1, Volatile.Read(ref elevatedDisposeCount));
    }

    [Fact]
    public async Task DeclinedElevationRetainsTheReadyStandardHelper()
    {
        int standardDisposeCount = 0;
        ElevatedKillHelperSession standardSession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref standardDisposeCount));
        ProcessTerminationService service = new(
            log: null,
            (_, elevate, _) => elevate
                ? new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Declined,
                    Session: null,
                    ErrorMessage: "Windows administrator approval was canceled.")
                : new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    standardSession,
                    string.Empty));
        try
        {
            Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));

            ElevatedHelperStatus status = await service
                .EnableElevatedHelperAsync(new IntPtr(1))
                .WaitAsync(TestTimeout);

            Assert.Equal(ElevatedHelperState.Declined, status.State);
            Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));
            Assert.Equal(expected: 0, Volatile.Read(ref standardDisposeCount));
        }
        finally
        {
            service.Dispose();
        }

        Assert.Equal(expected: 1, Volatile.Read(ref standardDisposeCount));
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
            (ownerWindowHandle, elevate, _) =>
            {
                if (!elevate)
                    return FailedStart("Expected standard helper failure");

                Assert.True(elevate);
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
            (_, elevate, _) =>
            {
                if (!elevate)
                    return FailedStart("Expected standard helper failure");

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
            (_, _, _) =>
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
        int elevatedLaunchCount = 0;
        int standardLaunchCount = 0;
        using ProcessTerminationService service = new(
            log: null,
            (_, elevate, _) =>
            {
                if (!elevate)
                {
                    Interlocked.Increment(ref standardLaunchCount);
                    return FailedStart("Expected standard helper failure");
                }

                return Interlocked.Increment(ref elevatedLaunchCount) switch
                {
                    1 => new ElevatedKillHelperStartResult(
                        ElevatedKillHelperStartOutcome.Declined,
                        Session: null,
                        ErrorMessage: "Windows administrator approval was canceled."),
                    _ => FailedStart("Second attempt failed")
                };
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
        Assert.Equal(expected: 2, Volatile.Read(ref elevatedLaunchCount));
        Assert.Equal(expected: 1, Volatile.Read(ref standardLaunchCount));
    }

    [Fact]
    public async Task ZeroOwnerHandleFailsWithoutLaunching()
    {
        int launchCount = 0;
        using ProcessTerminationService service = new(
            log: null,
            (_, _, _) =>
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
            (_, _, _) =>
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
            (_, elevate, _) =>
            {
                if (!elevate)
                    return FailedStart("Expected standard helper failure");

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

    [Fact]
    public async Task NativeHelperSuccessSkipsManagedTermination()
    {
        ElevatedKillHelperSession nativeSession = new(
            static () => true,
            static (_, _) => true,
            static () => { },
            RequestTermination,
            SuccessfulResponse);
        using ProcessTerminationService service = new(
            log: null,
            (_, elevate, _) =>
            {
                Assert.False(elevate);
                return new ElevatedKillHelperStartResult(
                    ElevatedKillHelperStartOutcome.Ready,
                    nativeSession,
                    string.Empty);
            });
        Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));
        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
            service.Arm(target);

            bool terminated = service.TryTerminate(target, out string errorMessage);

            Assert.True(terminated, errorMessage);
            Assert.False(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }

        static bool RequestTermination(
            ProcessTerminationTarget target,
            long generation,
            out long requestSequence)
        {
            requestSequence = 1;
            return target.ProcessID > 0 && generation > 0;
        }

        static bool SuccessfulResponse(
            long requestSequence,
            int timeoutMilliseconds,
            out int result,
            out int errorCode)
        {
            result = KillHelperProtocol.ResultSuccess;
            errorCode = 0;
            return requestSequence == 1 && timeoutMilliseconds > 0;
        }
    }

    [Fact]
    public async Task NativeHelperFailureUsesManagedTerminationFallback()
    {
        ElevatedKillHelperSession nativeSession = new(
            static () => true,
            static (_, _) => true,
            static () => { },
            RequestTermination,
            FailedResponse);
        using ProcessTerminationService service = new(
            log: null,
            (_, _, _) => new ElevatedKillHelperStartResult(
                ElevatedKillHelperStartOutcome.Ready,
                nativeSession,
                string.Empty));
        Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));
        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
            service.Arm(target);

            bool terminated = service.TryTerminate(target, out string errorMessage);

            Assert.True(terminated, errorMessage);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }

        static bool RequestTermination(
            ProcessTerminationTarget target,
            long generation,
            out long requestSequence)
        {
            requestSequence = 1;
            return target.ProcessID > 0 && generation > 0;
        }

        static bool FailedResponse(
            long requestSequence,
            int timeoutMilliseconds,
            out int result,
            out int errorCode)
        {
            result = KillHelperProtocol.ResultTerminateFailed;
            errorCode = 5;
            return requestSequence == 1 && timeoutMilliseconds > 0;
        }
    }

    [Fact]
    public async Task DeadElevatedHelperFallsBackAndStartsAStandardReplacement()
    {
        int firstSessionReady = 1;
        int firstDisposeCount = 0;
        int replacementDisposeCount = 0;
        int launchCount = 0;
        ElevatedKillHelperSession firstSession = new(
            () => Volatile.Read(ref firstSessionReady) != 0,
            static (_, _) => true,
            () => Interlocked.Increment(ref firstDisposeCount));
        ElevatedKillHelperSession replacementSession = new(
            static () => true,
            static (_, _) => true,
            () => Interlocked.Increment(ref replacementDisposeCount));
        ProcessTerminationService service = new(
            log: null,
            (_, elevate, _) =>
            {
                int currentLaunch = Interlocked.Increment(ref launchCount);
                Assert.Equal(expected: currentLaunch == 1, actual: elevate);
                return currentLaunch switch
                {
                    1 => new ElevatedKillHelperStartResult(
                        ElevatedKillHelperStartOutcome.Ready,
                        firstSession,
                        string.Empty),
                    2 => new ElevatedKillHelperStartResult(
                        ElevatedKillHelperStartOutcome.Ready,
                        replacementSession,
                        string.Empty),
                    _ => FailedStart("Unexpected additional helper launch")
                };
            });
        try
        {
            ElevatedHelperStatus elevatedStatus = await service
                .EnableElevatedHelperAsync(new IntPtr(1))
                .WaitAsync(TestTimeout);
            Assert.Equal(ElevatedHelperState.Ready, elevatedStatus.State);
            using Process process = StartSleepingProcess();
            try
            {
                ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
                service.Arm(target);
                Volatile.Write(ref firstSessionReady, value: 0);

                bool terminated = service.TryTerminate(target, out string errorMessage);

                Assert.True(terminated, errorMessage);
                Assert.True(process.WaitForExit(5_000));
                Assert.True(await service.EnsureStandardHelperAsync().WaitAsync(TestTimeout));
                Assert.Equal(expected: 2, Volatile.Read(ref launchCount));
                Assert.Equal(expected: 1, Volatile.Read(ref firstDisposeCount));
                Assert.Equal(
                    ElevatedHelperState.Failed,
                    service.GetElevatedHelperStatus().State);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill();
            }
        }
        finally
        {
            service.Dispose();
        }

        Assert.Equal(expected: 1, Volatile.Read(ref replacementDisposeCount));
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

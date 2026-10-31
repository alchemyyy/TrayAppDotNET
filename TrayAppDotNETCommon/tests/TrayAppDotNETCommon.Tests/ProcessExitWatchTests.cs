using System.Diagnostics;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class ProcessExitWatchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void SubscribersShareOneWatchAndTheLastUnsubscribeClosesIt()
    {
        using Process child = StartWaitingChild();
        try
        {
            Assert.Equal(
                ProcessExitWatchResult.Watching,
                ProcessExitWatch.TryRegister(child.Id, expectedStartFileTime: null, static () => { }, out IDisposable? first));
            Assert.Equal(
                ProcessExitWatchResult.Watching,
                ProcessExitWatch.TryRegister(child.Id, expectedStartFileTime: null, static () => { }, out IDisposable? second));
            Assert.Equal(expected: 2, ProcessExitWatch.SubscriberCount(child.Id));

            first!.Dispose();
            Assert.Equal(expected: 1, ProcessExitWatch.SubscriberCount(child.Id));

            second!.Dispose();
            Assert.Equal(expected: 0, ProcessExitWatch.SubscriberCount(child.Id));
        }
        finally
        {
            child.Kill();
        }
    }

    [Fact]
    public async Task AnExitReachesEveryRemainingSubscriberOnce()
    {
        using Process child = StartWaitingChild();
        TaskCompletionSource firstExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int secondCalls = 0;
        bool unsubscribedCalled = false;

        ProcessExitWatch.TryRegister(child.Id, expectedStartFileTime: null, () => firstExited.TrySetResult(), out IDisposable? first);
        ProcessExitWatch.TryRegister(child.Id, expectedStartFileTime: null, () => Interlocked.Increment(ref secondCalls), out IDisposable? second);
        ProcessExitWatch.TryRegister(child.Id, expectedStartFileTime: null, () => unsubscribedCalled = true, out IDisposable? third);
        third!.Dispose();

        child.Kill();
        await firstExited.Task.WaitAsync(Timeout);
        await WaitUntil(() => Volatile.Read(ref secondCalls) == 1 && ProcessExitWatch.SubscriberCount(child.Id) == 0);

        Assert.False(unsubscribedCalled);
        first!.Dispose();
        second!.Dispose();
        Assert.Equal(expected: 1, Volatile.Read(ref secondCalls));
    }

    [Fact]
    public void AStartTimeMismatchMeansTheExpectedProcessIsGone()
    {
        long start = ProcessExitWatch.CurrentProcessStartFileTime;

        Assert.Equal(
            ProcessExitWatchResult.Exited,
            ProcessExitWatch.TryRegister(Environment.ProcessId, start + 1, static () => { }, out IDisposable? mismatched));
        Assert.Null(mismatched);

        Assert.Equal(
            ProcessExitWatchResult.Watching,
            ProcessExitWatch.TryRegister(Environment.ProcessId, start, static () => { }, out IDisposable? matched));
        matched!.Dispose();
    }

    [Fact]
    public void OnlyAnUnknownProcessIdCountsAsExited()
    {
        Assert.Equal(ProcessExitWatchResult.Exited, ProcessExitWatch.ClassifyOpenFailure(NativeErrors.ERROR_INVALID_PARAMETER));
        Assert.Equal(ProcessExitWatchResult.Unavailable, ProcessExitWatch.ClassifyOpenFailure(NativeErrors.ERROR_ACCESS_DENIED));
    }

    [Fact]
    public void AProcessThatAlreadyExitedIsReportedWithoutARegistration()
    {
        using Process exited = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        exited.WaitForExit();

        // The Process object still holds a handle, so the ID cannot have been reused yet
        Assert.Equal(
            ProcessExitWatchResult.Exited,
            ProcessExitWatch.TryRegister(exited.Id, expectedStartFileTime: null, static () => { }, out IDisposable? registration));
        Assert.Null(registration);
    }

    [Fact]
    public async Task WaitForExitAsyncCompletesWhenTheProcessEnds()
    {
        using Process child = StartWaitingChild();
        Assert.True(ProcessExitWatch.TryGetStartFileTime(child.Id, out long start));

        Task<ProcessExitWatchResult> wait = ProcessExitWatch.WaitForExitAsync(child.Id, start);
        Assert.False(wait.IsCompleted);

        child.Kill();
        Assert.Equal(ProcessExitWatchResult.Exited, await wait.WaitAsync(Timeout));
    }

    // cmd waits on its redirected input until the test kills it
    private static Process StartWaitingChild() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/d /k")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        })!;

    private static async Task WaitUntil(Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + (long)Timeout.TotalMilliseconds;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "The condition did not become true in time.");
            await Task.Delay(millisecondsDelay: 20);
        }
    }
}

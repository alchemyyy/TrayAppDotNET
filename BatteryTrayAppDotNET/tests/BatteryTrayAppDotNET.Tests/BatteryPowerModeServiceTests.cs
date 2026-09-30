using System.Collections.Concurrent;
using System.Threading.Channels;
using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using Xunit;

namespace BatteryTrayAppDotNET.Tests;

public sealed class BatteryPowerModeServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DesiredModeIsVisibleImmediatelyAndSuccessConfirmsIt()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.True(service.IsApplying);
        Assert.True(service.IsStateAvailable);
        ApplyCall apply = await backend.NextApplyAsync();
        apply.Complete(true);
        await Finished(request);

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.False(service.IsApplying);
        Assert.True(service.IsStateAvailable);
        Assert.Equal(0, backend.ReadCount);
    }

    [Fact]
    public async Task ActiveWriteFinishesThenOnlyLatestPendingTargetIsApplied()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task first = service.RequestAsync(BatteryPowerMode.PowerSaver);
        ApplyCall active = await backend.NextApplyAsync();
        Task replaced = service.RequestAsync(BatteryPowerMode.Balanced);
        Task latest = service.RequestAsync(BatteryPowerMode.Ultimate);
        await Finished(replaced);

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.Equal(1, backend.ApplyCount);
        active.Complete(true);
        ApplyCall final = await backend.NextApplyAsync();
        await Finished(first);

        Assert.Equal(BatteryPowerMode.Ultimate, final.Mode);
        Assert.True(service.IsApplying);
        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        final.Complete(true);
        await Finished(latest);
        Assert.False(service.IsApplying);
        Assert.Equal(new[] { BatteryPowerMode.PowerSaver, BatteryPowerMode.Ultimate }, backend.AppliedModes.ToArray());
    }

    [Fact]
    public async Task RepeatingCurrentPendingTargetDoesNotQueueAnotherWrite()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task first = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        Task repeated = service.RequestAsync(BatteryPowerMode.Ultimate);

        Assert.Same(first, repeated);
        active.Complete(true);
        await Finished(first);
        Assert.Equal(1, backend.ApplyCount);
    }

    [Fact]
    public async Task RequestingCachedModeWhileIdleReappliesExplicitUserIntent()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        await ObserveAsync(service, backend, new(true, BatteryPowerMode.Balanced));

        Task request = service.RequestAsync(BatteryPowerMode.Balanced);
        ApplyCall apply = await backend.NextApplyAsync();
        Assert.Equal(BatteryPowerMode.Balanced, apply.Mode);
        Assert.True(service.IsApplying);
        apply.Complete(true);
        await Finished(request);
        Assert.Equal(1, backend.ApplyCount);
        Assert.False(service.IsApplying);
        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
    }

    [Fact]
    public async Task OlderReadCannotOverrideExplicitSelectionOfCachedMode()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        await ObserveAsync(service, backend, new(true, BatteryPowerMode.Balanced));
        Task refresh = service.RefreshAsync();
        ReadCall stale = await backend.NextReadAsync();

        Task request = service.RequestAsync(BatteryPowerMode.Balanced);
        ApplyCall apply = await backend.NextApplyAsync();
        Assert.Equal(BatteryPowerMode.Balanced, apply.Mode);
        apply.Complete(true);
        await Finished(request);
        stale.Complete(new(true, BatteryPowerMode.PowerSaver));
        await Finished(refresh);

        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
        Assert.True(service.IsStateAvailable);
        Assert.False(service.IsApplying);
        Assert.Equal(1, backend.ApplyCount);
    }

    [Fact]
    public async Task ReturningToObservedOriginalModeStillQueuesItAfterActiveWrite()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        await ObserveAsync(service, backend, new(true, BatteryPowerMode.Balanced));
        Task away = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        Task back = service.RequestAsync(BatteryPowerMode.Balanced);

        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
        active.Complete(true);
        ApplyCall revert = await backend.NextApplyAsync();
        await Finished(away);
        Assert.Equal(BatteryPowerMode.Balanced, revert.Mode);
        Assert.True(service.IsApplying);
        revert.Complete(true);
        await Finished(back);
        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
        Assert.False(service.IsApplying);
    }

    [Fact]
    public async Task ObsoleteFailureDoesNotReadBackOrClearNewerDesiredMode()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task first = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        Task latest = service.RequestAsync(BatteryPowerMode.PowerSaver);
        active.Complete(false);
        ApplyCall final = await backend.NextApplyAsync();
        await Finished(first);

        Assert.Equal(0, backend.ReadCount);
        Assert.Equal(BatteryPowerMode.PowerSaver, service.DisplayMode);
        Assert.True(service.IsApplying);
        final.Complete(true);
        await Finished(latest);
    }

    [Fact]
    public async Task ReadStartedBeforeWriteCannotOverridePendingDesiredMode()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task refresh = service.RefreshAsync();
        ReadCall stale = await backend.NextReadAsync();
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        stale.Complete(new(true, BatteryPowerMode.Balanced));
        await Finished(refresh);

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.True(service.IsApplying);
        active.Complete(true);
        await Finished(request);
    }

    [Fact]
    public async Task ReadStartedBeforeWriteCannotOverrideConfirmedModeAfterCompletion()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task refresh = service.RefreshAsync();
        ReadCall stale = await backend.NextReadAsync();
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        (await backend.NextApplyAsync()).Complete(true);
        await Finished(request);
        stale.Complete(new(true, BatteryPowerMode.Balanced));
        await Finished(refresh);

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.True(service.IsStateAvailable);
        Assert.False(service.IsApplying);
    }

    [Fact]
    public async Task RefreshSkipsPendingWritesAndOtherwiseCoalescesReaders()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        await Finished(service.RefreshAsync());
        Assert.Equal(0, backend.ReadCount);
        active.Complete(true);
        await Finished(request);

        Task first = service.RefreshAsync();
        ReadCall read = await backend.NextReadAsync();
        Task second = service.RefreshAsync();
        Assert.Same(first, second);
        read.Complete(new(true, BatteryPowerMode.PowerSaver));
        await Finished(Task.WhenAll(first, second));
        Assert.Equal(1, backend.ReadCount);
        Assert.Equal(BatteryPowerMode.PowerSaver, service.DisplayMode);
    }

    [Fact]
    public async Task LatestFailureReconcilesWithActualModeBeforeClearingPending()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        (await backend.NextApplyAsync()).Complete(false);
        ReadCall readback = await backend.NextReadAsync();

        Assert.Equal(BatteryPowerMode.Ultimate, service.DisplayMode);
        Assert.True(service.IsApplying);
        readback.Complete(new(true, BatteryPowerMode.Balanced));
        await Finished(request);
        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
        Assert.True(service.IsStateAvailable);
        Assert.False(service.IsApplying);
    }

    [Fact]
    public async Task FailedReadbackDoesNotInventConfirmationOrLeavePendingStuck()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        await ObserveAsync(service, backend, new(true, BatteryPowerMode.PowerSaver));
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        (await backend.NextApplyAsync()).Complete(false);
        (await backend.NextReadAsync()).Complete(new(false, null));
        await Finished(request);

        Assert.Null(service.DisplayMode);
        Assert.False(service.IsStateAvailable);
        Assert.False(service.IsApplying);

        Task recovery = service.RequestAsync(BatteryPowerMode.Balanced);
        (await backend.NextApplyAsync()).Complete(true);
        await Finished(recovery);
        Assert.Equal(BatteryPowerMode.Balanced, service.DisplayMode);
        Assert.True(service.IsStateAvailable);
    }

    [Fact]
    public async Task NewRequestDuringFailureReadbackPreventsRollbackToReadResult()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task first = service.RequestAsync(BatteryPowerMode.Ultimate);
        (await backend.NextApplyAsync()).Complete(false);
        ReadCall oldReadback = await backend.NextReadAsync();
        Task latest = service.RequestAsync(BatteryPowerMode.PowerSaver);
        oldReadback.Complete(new(true, BatteryPowerMode.Balanced));
        ApplyCall final = await backend.NextApplyAsync();
        await Finished(first);

        Assert.Equal(BatteryPowerMode.PowerSaver, service.DisplayMode);
        Assert.True(service.IsApplying);
        final.Complete(true);
        await Finished(latest);
        Assert.Equal(BatteryPowerMode.PowerSaver, service.DisplayMode);
    }

    [Fact]
    public async Task SuccessfulCustomSchemeAndUnavailableReadRemainDistinct()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Assert.False(service.IsStateAvailable);
        await ObserveAsync(service, backend, new(true, null));
        Assert.Null(service.DisplayMode);
        Assert.True(service.IsStateAvailable);

        await ObserveAsync(service, backend, new(false, null));
        Assert.Null(service.DisplayMode);
        Assert.False(service.IsStateAvailable);
    }

    [Fact]
    public async Task BackendExceptionsFollowFailureReadbackAndReleasePending()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
        (await backend.NextApplyAsync()).Completion.TrySetException(new InvalidOperationException("simulated write failure"));
        (await backend.NextReadAsync()).Completion.TrySetException(new InvalidOperationException("simulated read failure"));
        await Finished(request);

        Assert.False(service.IsApplying);
        Assert.False(service.IsStateAvailable);
        Assert.Null(service.DisplayMode);
    }

    [Fact]
    public async Task DisposeCancelsActiveWriteDropsPendingAndIgnoresLateCompletion()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        int notifications = 0;
        service.StateChanged += () => Interlocked.Increment(ref notifications);
        Task activeTask = service.RequestAsync(BatteryPowerMode.Ultimate);
        ApplyCall active = await backend.NextApplyAsync();
        Task pendingTask = service.RequestAsync(BatteryPowerMode.PowerSaver);
        service.Dispose();
        int afterDispose = Volatile.Read(ref notifications);
        BatteryPowerMode? displayedAfterDispose = service.DisplayMode;
        Assert.True(active.Token.IsCancellationRequested);
        await Finished(pendingTask);
        active.Complete(true);
        await Finished(activeTask);

        Assert.Equal(afterDispose, Volatile.Read(ref notifications));
        Assert.Equal(displayedAfterDispose, service.DisplayMode);
        Assert.False(service.IsApplying);
        Assert.Equal(1, backend.ApplyCount);
        await Finished(service.RefreshAsync());
        await Finished(service.RequestAsync(BatteryPowerMode.Balanced));
        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(0, backend.ReadCount);
    }

    [Fact]
    public async Task DisposeIgnoresLateReadAndSuppressesNotifications()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        int notifications = 0;
        service.StateChanged += () => Interlocked.Increment(ref notifications);
        Task refresh = service.RefreshAsync();
        ReadCall read = await backend.NextReadAsync();
        service.Dispose();
        Assert.True(read.Token.IsCancellationRequested);
        read.Complete(new(true, BatteryPowerMode.Ultimate));
        await Finished(refresh);

        Assert.Null(service.DisplayMode);
        Assert.False(service.IsStateAvailable);
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task ImmediateDisposeDoesNotStrandQueuedRequestTasks()
    {
        for (int i = 0; i < 24; i++)
        {
            using FakeBackend backend = new();
            using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
            Task request = service.RequestAsync(BatteryPowerMode.Ultimate);
            service.Dispose();
            backend.Dispose();
            await Finished(request);
            Assert.False(service.IsApplying);
        }
    }

    [Fact]
    public async Task BackendDelegatesRunOutsideCallersSynchronizationContext()
    {
        using FakeBackend backend = new();
        using BatteryPowerModeService service = new(backend.ApplyAsync, backend.ReadAsync);
        Task refresh = CallWithContext(service.RefreshAsync);
        ReadCall read = await backend.NextReadAsync();
        Assert.Null(read.Context);
        read.Complete(new(true, BatteryPowerMode.Balanced));
        await Finished(refresh);

        Task request = CallWithContext(() => service.RequestAsync(BatteryPowerMode.Ultimate));
        ApplyCall apply = await backend.NextApplyAsync();
        Assert.Null(apply.Context);
        apply.Complete(true);
        await Finished(request);
    }

    private static Task CallWithContext(Func<Task> action)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            return action();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task ObserveAsync(BatteryPowerModeService service, FakeBackend backend, BatteryPowerModeReadResult result)
    {
        Task refresh = service.RefreshAsync();
        (await backend.NextReadAsync()).Complete(result);
        await Finished(refresh);
    }

    private static Task Finished(Task task) => task.WaitAsync(TestTimeout);

    private sealed class FakeBackend : IDisposable
    {
        private readonly Channel<ApplyCall> _applies = Channel.CreateUnbounded<ApplyCall>();
        private readonly Channel<ReadCall> _reads = Channel.CreateUnbounded<ReadCall>();
        private readonly ConcurrentBag<ApplyCall> _allApplies = [];
        private readonly ConcurrentBag<ReadCall> _allReads = [];
        private int _applyCount;
        private int _readCount;
        private int _disposed;
        public ConcurrentQueue<BatteryPowerMode> AppliedModes { get; } = new();
        public int ApplyCount => Volatile.Read(ref _applyCount);
        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<bool> ApplyAsync(BatteryPowerMode mode, CancellationToken token)
        {
            ApplyCall call = new(mode, token, SynchronizationContext.Current);
            _allApplies.Add(call);
            if (Volatile.Read(ref _disposed) != 0) call.Completion.TrySetCanceled();
            AppliedModes.Enqueue(mode);
            Interlocked.Increment(ref _applyCount);
            _applies.Writer.TryWrite(call);
            return call.Completion.Task;
        }

        public Task<BatteryPowerModeReadResult> ReadAsync(CancellationToken token)
        {
            ReadCall call = new(token, SynchronizationContext.Current);
            _allReads.Add(call);
            if (Volatile.Read(ref _disposed) != 0) call.Completion.TrySetCanceled();
            Interlocked.Increment(ref _readCount);
            _reads.Writer.TryWrite(call);
            return call.Completion.Task;
        }

        public Task<ApplyCall> NextApplyAsync() => _applies.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        public Task<ReadCall> NextReadAsync() => _reads.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            foreach (ApplyCall call in _allApplies) call.Completion.TrySetCanceled();
            foreach (ReadCall call in _allReads) call.Completion.TrySetCanceled();
        }
    }

    private sealed record ApplyCall(BatteryPowerMode Mode, CancellationToken Token, SynchronizationContext? Context)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(bool success) => Completion.TrySetResult(success);
    }

    private sealed record ReadCall(CancellationToken Token, SynchronizationContext? Context)
    {
        public TaskCompletionSource<BatteryPowerModeReadResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(BatteryPowerModeReadResult result) => Completion.TrySetResult(result);
    }
}

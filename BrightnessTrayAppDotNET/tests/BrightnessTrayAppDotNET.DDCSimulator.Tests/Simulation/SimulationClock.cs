using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

/// <summary>
/// Real-time knobs that decide when the process has gone quiet enough for virtual time to advance.
/// </summary>
internal sealed class QuiescenceOptions
{
    // Every participant thread is parked or idle and nothing changed for this long
    public double IdleWindowMicroseconds = 40;

    // Some thread is busy outside the simulator's accounting (a SemaphoreSlim, lock or Task wait, or JIT). After this
    // long without observable progress, CPU cycle counters decide whether it is blocked or still computing
    public double ProbeWindowMicroseconds = 300;

    // Other threads using less than this share of one core over the probe window are treated as blocked
    public double BlockedCPUFraction = 0.05;

    // A single wait for quiescence that lasts this long is a hang; the run fails with a state dump
    public double WatchdogSeconds = 30;
}

/// <summary>Virtual time stopped advancing: a deadlock between the app and the simulator, reported with a state dump.</summary>
internal sealed class SimulationHangException(string state)
    : InvalidOperationException("Simulation stopped making progress: " + state);

/// <summary>Which bookkeeping a parked thread belongs to.</summary>
internal enum ParkedThreadKind
{
    None,
    Pool,
    Dispatcher,
    Other
}

/// <summary>
/// Virtual time for one simulation run. Timers created through this provider fire only when <see cref="RunUntil"/>
/// advances the clock, and the clock advances only after every participant thread is parked inside a simulator wait,
/// idle, or demonstrably stalled. App code under test sees ordinary <see cref="TimeProvider"/> semantics while a
/// minute of DDC traffic costs milliseconds of real time.
/// </summary>
internal sealed class SimulationClock : TimeProvider
{
    private const long MaximumTimerFiresPerRun = 20_000_000;

    private readonly Lock _gate = new();
    private readonly PriorityQueue<TimerEntry, TimerOrder> _timers = new();
    private readonly DateTimeOffset _epochUTC;
    private long _nowTicks;
    private long _timerSequence;
    private long _activity;
    private int _parkedPoolThreads;
    private int _backgroundBusyPoolThreads;
    private long _stallAdvances;
    private long _timerFires;

    public SimulationClock(DateTimeOffset epochUTC, QuiescenceOptions? options = null)
    {
        _epochUTC = epochUTC;
        Options = options ?? new QuiescenceOptions();
    }

    public QuiescenceOptions Options { get; }

    /// <summary>The simulated UI thread, consulted for quiescence. Set once by the world.</summary>
    public SimulationDispatcher? Dispatcher { get; set; }

    /// <summary>Receives exceptions thrown by scheduled callbacks, which would crash a real timer thread.</summary>
    public Action<Exception>? UnhandledCallbackException { get; set; }

    public TimeSpan Elapsed => TimeSpan.FromTicks(Volatile.Read(ref _nowTicks));

    public long ElapsedTicks => Volatile.Read(ref _nowTicks);

    /// <summary>Advances taken while some thread was blocked outside the simulator's accounting.</summary>
    public long StallAdvances => Interlocked.Read(ref _stallAdvances);

    public long TimerFires => Interlocked.Read(ref _timerFires);

    public override DateTimeOffset GetUtcNow() => _epochUTC.AddTicks(Volatile.Read(ref _nowTicks));

    public override long GetTimestamp() => Volatile.Read(ref _nowTicks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        SimulationTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Runs <paramref name="action"/> on the thread pool once <paramref name="dueTime"/> of virtual time passes.</summary>
    public ITimer Schedule(TimeSpan dueTime, Action action) =>
        CreateTimer(static state => ((Action)state!)(), action, dueTime, Timeout.InfiniteTimeSpan);

    /// <summary>Schedules <paramref name="action"/> at an absolute virtual time, or now when that time has passed.</summary>
    public ITimer ScheduleAt(long ticks, Action action) =>
        Schedule(TimeSpan.FromTicks(Math.Max(val1: 0, ticks - ElapsedTicks)), action);

    public void NoteActivity() => Interlocked.Increment(ref _activity);

    /// <summary>
    /// Measures thread pool threads that stay busy while no simulation runs (test runner infrastructure), so the
    /// quiescence test does not mistake them for app work.
    /// </summary>
    public void CalibrateBackgroundThreads()
    {
        int driverPoolThreads = Thread.CurrentThread.IsThreadPoolThread ? 1 : 0;
        int lowest = int.MaxValue;
        Stopwatch sampling = Stopwatch.StartNew();
        while (sampling.Elapsed < TimeSpan.FromMilliseconds(5))
        {
            if (ThreadPool.PendingWorkItemCount == 0)
                lowest = Math.Min(lowest, CountBusyPoolThreads() - driverPoolThreads);

            Thread.Yield();
        }

        _backgroundBusyPoolThreads = lowest == int.MaxValue ? 0 : Math.Max(val1: 0, lowest);
    }

    /// <summary>
    /// Moves time forward without firing timers in between, the way a suspended machine experiences sleep.
    /// Everything that came due during the jump fires on the next <see cref="RunUntil"/> pass, late, in due order.
    /// </summary>
    public void Jump(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;

        lock (_gate) _nowTicks += duration.Ticks;
        NoteActivity();
    }

    /// <summary>
    /// Advances virtual time to <paramref name="target"/>, firing every timer that comes due one at a time and waiting
    /// for the process to go quiet before each step.
    /// </summary>
    public void RunUntil(TimeSpan target)
    {
        long limitTicks = target.Ticks;
        int driverPoolThreads = Thread.CurrentThread.IsThreadPoolThread ? 1 : 0;
        long fires = 0;
        while (true)
        {
            WaitForQuiescence(driverPoolThreads);
            if (TryFireNextDue(limitTicks))
            {
                if (++fires > MaximumTimerFiresPerRun)
                    throw new InvalidOperationException("Simulation livelock: timer fire budget exhausted.");

                continue;
            }

            lock (_gate)
            {
                if (_nowTicks < limitTicks) _nowTicks = limitTicks;
            }

            WaitForQuiescence(driverPoolThreads);
            return;
        }
    }

    public void RunFor(TimeSpan duration) => RunUntil(Elapsed + duration);

    internal ParkedThreadKind ClassifyCurrentThread()
    {
        if (Dispatcher?.CheckAccess() == true) return ParkedThreadKind.Dispatcher;
        return Thread.CurrentThread.IsThreadPoolThread ? ParkedThreadKind.Pool : ParkedThreadKind.Other;
    }

    internal void OnParked(ParkedThreadKind kind)
    {
        switch (kind)
        {
            case ParkedThreadKind.Pool:
                Interlocked.Increment(ref _parkedPoolThreads);
                break;
            case ParkedThreadKind.Dispatcher:
                Dispatcher?.MarkParked();
                break;
        }

        NoteActivity();
    }

    internal void OnUnparked(ParkedThreadKind kind)
    {
        switch (kind)
        {
            case ParkedThreadKind.Pool:
                Interlocked.Decrement(ref _parkedPoolThreads);
                break;
            case ParkedThreadKind.Dispatcher:
                Dispatcher?.MarkRunning();
                break;
        }

        NoteActivity();
    }

    private bool ChangeTimer(SimulationTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            if (timer.IsDisposed) return false;

            timer.Version++;
            timer.PeriodTicks = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? 0 : period.Ticks;
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                long dueTicks = _nowTicks + Math.Max(val1: 0, dueTime.Ticks);
                _timers.Enqueue(new TimerEntry(timer, timer.Version), new TimerOrder(dueTicks, ++_timerSequence));
            }
        }

        NoteActivity();
        return true;
    }

    private void DisposeTimer(SimulationTimer timer)
    {
        lock (_gate)
        {
            timer.IsDisposed = true;
            timer.Version++;
        }

        NoteActivity();
    }

    private bool TryFireNextDue(long limitTicks)
    {
        SimulationTimer? due = null;
        lock (_gate)
        {
            while (_timers.TryPeek(out TimerEntry entry, out TimerOrder order))
            {
                if (entry.Timer.IsDisposed || entry.Timer.Version != entry.Version)
                {
                    _timers.Dequeue();
                    continue;
                }

                if (order.DueTicks > limitTicks) return false;

                _timers.Dequeue();
                if (order.DueTicks > _nowTicks) _nowTicks = order.DueTicks;
                if (entry.Timer.PeriodTicks > 0)
                {
                    _timers.Enqueue(
                        entry,
                        new TimerOrder(Math.Max(_nowTicks, order.DueTicks + entry.Timer.PeriodTicks), ++_timerSequence));
                }

                due = entry.Timer;
                break;
            }
        }

        if (due == null) return false;

        Interlocked.Increment(ref _timerFires);
        NoteActivity();
        ThreadPool.UnsafeQueueUserWorkItem(InvokeTimer, due, preferLocal: false);
        return true;
    }

    private void InvokeTimer(SimulationTimer timer)
    {
        try
        {
            timer.Callback(timer.State);
        }
        catch (Exception exception)
        {
            UnhandledCallbackException?.Invoke(exception);
        }
        finally
        {
            NoteActivity();
        }
    }

    private void WaitForQuiescence(int driverPoolThreads)
    {
        long waitStart = Stopwatch.GetTimestamp();
        long lastActivity = long.MinValue;
        long lastCompleted = long.MinValue;
        long quietSince = waitStart;
        ulong othersAtQuietStart = OtherThreadsCycles();
        while (true)
        {
            long activity = Volatile.Read(ref _activity);
            long completed = ThreadPool.CompletedWorkItemCount;

            // Work queued behind a parked UI thread cannot run until virtual time moves, so it is not busy
            bool hardBusy = ThreadPool.PendingWorkItemCount > 0
                            || (Dispatcher is { HasQueuedWork: true, IsParked: false });
            if (hardBusy || activity != lastActivity || completed != lastCompleted)
            {
                lastActivity = activity;
                lastCompleted = completed;
                quietSince = Stopwatch.GetTimestamp();
                othersAtQuietStart = OtherThreadsCycles();
                YieldOrFail(waitStart, driverPoolThreads);
                continue;
            }

            double quietMicroseconds = Stopwatch.GetElapsedTime(quietSince).TotalMicroseconds;
            if (CountUnknownBusy(driverPoolThreads) <= 0)
            {
                if (quietMicroseconds >= Options.IdleWindowMicroseconds) return;
            }
            else if (quietMicroseconds >= Options.ProbeWindowMicroseconds)
            {
                // Busy threads that burned almost no CPU over the window are blocked, not computing
                ulong othersNow = OtherThreadsCycles();
                double busyMicroseconds = (othersNow - othersAtQuietStart) / CyclesPerMicrosecond.Value;
                if (busyMicroseconds < quietMicroseconds * Options.BlockedCPUFraction)
                {
                    Interlocked.Increment(ref _stallAdvances);
                    return;
                }

                quietSince = Stopwatch.GetTimestamp();
                othersAtQuietStart = othersNow;
            }

            YieldOrFail(waitStart, driverPoolThreads);
        }
    }

    private int CountUnknownBusy(int driverPoolThreads)
    {
        int unknownBusy = CountBusyPoolThreads()
                          - Volatile.Read(ref _parkedPoolThreads)
                          - driverPoolThreads
                          - _backgroundBusyPoolThreads;
        if (Dispatcher?.IsRunningUnparked == true) unknownBusy++;
        return unknownBusy;
    }

    private void YieldOrFail(long waitStart, int driverPoolThreads)
    {
        if (Stopwatch.GetElapsedTime(waitStart).TotalSeconds >= Options.WatchdogSeconds)
            throw new SimulationHangException(DescribeState(driverPoolThreads));

        Thread.Yield();
    }

    /// <summary>Clock, timer queue, dispatcher and thread pool state for hang reports.</summary>
    public string DescribeState(int driverPoolThreads = 0)
    {
        StringBuilder builder = new();
        lock (_gate)
        {
            builder.Append(CultureInfo.InvariantCulture, $"virtual time {Elapsed.TotalSeconds:F6}s, ");
            builder.Append(CultureInfo.InvariantCulture, $"{_timers.Count} queued timer entries");
            if (_timers.TryPeek(out _, out TimerOrder next))
                builder.Append(CultureInfo.InvariantCulture, $", next due at {TimeSpan.FromTicks(next.DueTicks).TotalSeconds:F6}s");
        }

        builder.Append(CultureInfo.InvariantCulture, $"; dispatcher queued={Dispatcher?.HasQueuedWork} parked={Dispatcher?.IsParked} running={Dispatcher?.IsRunningUnparked}");
        builder.Append(CultureInfo.InvariantCulture, $"; pool busy={CountBusyPoolThreads()} parked={Volatile.Read(ref _parkedPoolThreads)} ");
        builder.Append(CultureInfo.InvariantCulture, $"background={_backgroundBusyPoolThreads} driver={driverPoolThreads} pending={ThreadPool.PendingWorkItemCount}");
        builder.Append(CultureInfo.InvariantCulture, $"; fires={TimerFires} blocked advances={StallAdvances}");
        return builder.ToString();
    }

    private static int CountBusyPoolThreads()
    {
        ThreadPool.GetMaxThreads(out int maximumWorkers, out _);
        ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
        return maximumWorkers - availableWorkers;
    }

    /// <summary>CPU cycles consumed by every thread in this process except the calling driver thread.</summary>
    private static ulong OtherThreadsCycles()
    {
        QueryProcessCycleTime(CurrentProcessPseudoHandle, out ulong processCycles);
        QueryThreadCycleTime(CurrentThreadPseudoHandle, out ulong driverCycles);
        return processCycles - driverCycles;
    }

    // Cycle counters tick at the invariant TSC rate; measure it once against the high-resolution timer
    private static readonly Lazy<double> CyclesPerMicrosecond = new(() =>
    {
        QueryThreadCycleTime(CurrentThreadPseudoHandle, out ulong startCycles);
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2) Thread.SpinWait(iterations: 50);
        QueryThreadCycleTime(CurrentThreadPseudoHandle, out ulong endCycles);
        return Math.Max(val1: 100.0, (endCycles - startCycles) / Stopwatch.GetElapsedTime(start).TotalMicroseconds);
    });

    private static readonly IntPtr CurrentProcessPseudoHandle = new(-1);
    private static readonly IntPtr CurrentThreadPseudoHandle = new(-2);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryProcessCycleTime(IntPtr processHandle, out ulong cycleTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr threadHandle, out ulong cycleTime);

    private readonly record struct TimerEntry(SimulationTimer Timer, long Version);

    private readonly record struct TimerOrder(long DueTicks, long Sequence) : IComparable<TimerOrder>
    {
        public int CompareTo(TimerOrder other)
        {
            int byDue = DueTicks.CompareTo(other.DueTicks);
            return byDue != 0 ? byDue : Sequence.CompareTo(other.Sequence);
        }
    }

    private sealed class SimulationTimer(SimulationClock clock, TimerCallback callback, object? state) : ITimer
    {
        // Guarded by the clock gate
        public long Version;
        public long PeriodTicks;
        public bool IsDisposed;

        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;

        public bool Change(TimeSpan dueTime, TimeSpan period) => clock.ChangeTimer(this, dueTime, period);

        public void Dispose() => clock.DisposeTimer(this);

        public ValueTask DisposeAsync()
        {
            clock.DisposeTimer(this);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// One-shot release signal for a thread blocked inside the simulation. The releasing side updates the clock's
/// parked-thread accounting before waking the waiter, so the clock never sees a gap where the woken thread is
/// runnable but uncounted.
/// </summary>
internal sealed class SimulationWaitHandle(SimulationClock clock)
{
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _released = new(false);
    private ParkedThreadKind _waiterKind;
    private bool _isReleased;

    public bool IsReleased
    {
        get
        {
            lock (_gate) return _isReleased;
        }
    }

    /// <summary>Blocks the calling thread until <see cref="Release"/>, counting it as parked meanwhile.</summary>
    public void Wait()
    {
        lock (_gate)
        {
            if (_isReleased) return;

            _waiterKind = clock.ClassifyCurrentThread();
            clock.OnParked(_waiterKind);
        }

        _released.Wait();
    }

    /// <summary>Wakes the waiter. Later calls are ignored.</summary>
    public bool Release()
    {
        lock (_gate)
        {
            if (_isReleased) return false;

            _isReleased = true;
            if (_waiterKind != ParkedThreadKind.None) clock.OnUnparked(_waiterKind);
        }

        _released.Set();
        return true;
    }
}

/// <summary>FIFO mutual exclusion whose waiters park with simulation accounting. Ownership passes directly to the next waiter.</summary>
internal sealed class SimulationGate(SimulationClock clock)
{
    private readonly Lock _gate = new();
    private readonly Queue<SimulationWaitHandle> _waiters = new();
    private bool _isHeld;

    public void Enter()
    {
        SimulationWaitHandle handle;
        lock (_gate)
        {
            if (!_isHeld)
            {
                _isHeld = true;
                return;
            }

            handle = new SimulationWaitHandle(clock);
            _waiters.Enqueue(handle);
        }

        handle.Wait();
    }

    /// <summary>Takes the gate only when nobody holds it.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_isHeld) return false;

            _isHeld = true;
            return true;
        }
    }

    public void Exit()
    {
        SimulationWaitHandle? next = null;
        lock (_gate)
        {
            if (_waiters.Count > 0) next = _waiters.Dequeue();
            else _isHeld = false;
        }

        next?.Release();
    }
}

/// <summary>Parking helpers for threads that must wait out virtual time.</summary>
internal static class SimulationClockExtensions
{
    /// <summary>Parks the calling thread for <paramref name="duration"/> of virtual time.</summary>
    public static void Sleep(this SimulationClock clock, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;

        SimulationWaitHandle handle = new(clock);
        using ITimer _ = clock.Schedule(duration, () => handle.Release());
        handle.Wait();
    }
}

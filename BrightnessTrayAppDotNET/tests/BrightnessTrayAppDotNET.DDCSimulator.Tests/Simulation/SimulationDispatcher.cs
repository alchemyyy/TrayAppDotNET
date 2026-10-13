using System.Runtime.ExceptionServices;
using BrightnessTrayAppDotNET.Services;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

/// <summary>
/// Single-threaded stand-in for the Avalonia UI dispatcher. Work runs in posting order on one dedicated thread that
/// carries a <see cref="SynchronizationContext"/>, so <c>await</c> continuations inside <see cref="MonitorService"/>
/// resume on it exactly as they resume on the real UI thread.
/// </summary>
internal sealed class SimulationDispatcher : IMonitorServiceDispatcher, IDisposable
{
    private const int StateIdle = 0;
    private const int StateRunning = 1;
    private const int StateParked = 2;

    private readonly SimulationClock _clock;
    private readonly Action<Exception> _unhandledException;

    // Monitor.Wait and Pulse need an object gate
    private readonly object _sync = new();
    private readonly Queue<Action> _queue = new();
    private readonly Thread _thread;
    private int _state;
    private bool _stopping;

    public SimulationDispatcher(SimulationClock clock, Action<Exception> unhandledException)
    {
        _clock = clock;
        _unhandledException = unhandledException;
        _thread = new Thread(Run) { IsBackground = true, Name = "DDCSimulator.UIThread" };
        _thread.Start();
    }

    public bool HasQueuedWork
    {
        get
        {
            lock (_sync) return _queue.Count > 0;
        }
    }

    public bool IsRunningUnparked => Volatile.Read(ref _state) == StateRunning;

    /// <summary>The UI thread is inside a work item, blocked on virtual time; its queue cannot drain until time moves.</summary>
    public bool IsParked => Volatile.Read(ref _state) == StateParked;

    public bool CheckAccess() => Thread.CurrentThread == _thread;

    public void Post(Action action)
    {
        lock (_sync)
        {
            if (_stopping) return;

            _queue.Enqueue(action);
            Monitor.Pulse(_sync);
        }

        _clock.NoteActivity();
    }

    public void Invoke(Action action) =>
        Invoke(() =>
        {
            action();
            return true;
        });

    public T Invoke<T>(Func<T> action)
    {
        if (CheckAccess()) return action();

        lock (_sync)
        {
            if (_stopping) throw new InvalidOperationException("The simulated UI dispatcher has stopped.");
        }

        T result = default!;
        Exception? failure = null;
        SimulationWaitHandle completed = new(_clock);
        Post(() =>
        {
            try { result = action(); }
            catch (Exception exception) { failure = exception; }
            finally { completed.Release(); }
        });
        completed.Wait();

        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _stopping = true;
            Monitor.PulseAll(_sync);
        }

        if (!CheckAccess()) _thread.Join(TimeSpan.FromSeconds(10));
    }

    internal void MarkParked() => Volatile.Write(ref _state, StateParked);

    internal void MarkRunning() => Volatile.Write(ref _state, StateRunning);

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(this));
        while (true)
        {
            Action work;
            lock (_sync)
            {
                while (_queue.Count == 0)
                {
                    Volatile.Write(ref _state, StateIdle);
                    if (_stopping) return;

                    Monitor.Wait(_sync);
                }

                work = _queue.Dequeue();
                Volatile.Write(ref _state, StateRunning);
            }

            try
            {
                work();
            }
            catch (Exception exception)
            {
                // Avalonia routes these to Dispatcher.UnhandledException, which tadn logs and swallows
                _unhandledException(exception);
            }

            _clock.NoteActivity();
        }
    }

    private sealed class DispatcherSynchronizationContext(SimulationDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            dispatcher.Post(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state) =>
            dispatcher.Invoke(() => callback(state));

        public override SynchronizationContext CreateCopy() => this;
    }
}

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services;

/// <summary>What <see cref="ProcessExitWatch.TryRegister"/> found.</summary>
public enum ProcessExitWatchResult
{
    /// <summary>The process is running; the registration's callback runs once when it exits.</summary>
    Watching,

    /// <summary>
    /// The process has exited, or its ID now belongs to a process started at another time than the caller expects.
    /// </summary>
    Exited,

    /// <summary>
    /// The process exists but cannot be watched (access denied, or its start time cannot be read for validation).
    /// Nothing is registered and the caller must not treat the process as gone.
    /// </summary>
    Unavailable
}

/// <summary>
/// Process-exit notifications without a thread per process: one SYNCHRONIZE handle per watched process ID, waited on
/// through <see cref="ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, int, bool)"/>
/// and shared by every subscriber. The handle keeps the ID from being reused while anyone watches it, and it is
/// closed when the last subscriber leaves or the process exits. Callbacks run on a thread-pool thread.
/// </summary>
public static class ProcessExitWatch
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<int, Entry> Entries = [];
    private static long _currentProcessStartFileTime;

    /// <summary>The creation time (FILETIME, UTC) of this process, for a child's <c>--parent-start</c>.</summary>
    public static long CurrentProcessStartFileTime
    {
        get
        {
            long cached = Volatile.Read(ref _currentProcessStartFileTime);
            if (cached != 0) return cached;

            if (!TryGetStartFileTime(Environment.ProcessId, out long startFileTime))
                throw new InvalidOperationException("The creation time of the current process cannot be read.");

            Volatile.Write(ref _currentProcessStartFileTime, startFileTime);
            return startFileTime;
        }
    }

    /// <summary>
    /// Registers <paramref name="onExit"/> for the exit of <paramref name="processId"/>. When
    /// <paramref name="expectedStartFileTime"/> is set, a process with another creation time (FILETIME, UTC) counts
    /// as <see cref="ProcessExitWatchResult.Exited"/>: the expected one is gone and its ID was reused. Only
    /// <see cref="ProcessExitWatchResult.Watching"/> returns a registration; disposing it unsubscribes without
    /// invoking the callback. The callback is never invoked synchronously.
    /// </summary>
    public static ProcessExitWatchResult TryRegister(
        int processId,
        long? expectedStartFileTime,
        Action onExit,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        registration = null;
        if (processId <= 0) return ProcessExitWatchResult.Exited;

        ProcessExitWatchResult opened = TryOpen(processId, expectedStartFileTime, out SafeWaitHandle? handle);
        if (opened != ProcessExitWatchResult.Watching) return opened;

        Subscription subscription = new(onExit);
        lock (Gate)
        {
            // An entry's open handle keeps the ID from being reused, so an existing entry is this same process
            if (Entries.TryGetValue(processId, out Entry? entry))
            {
                handle!.Dispose();
            }
            else
            {
                entry = new Entry(processId, handle!);
                Entries[processId] = entry;
                entry.Start();
            }

            subscription.Entry = entry;
            entry.Subscriptions.Add(subscription);
        }

        registration = subscription;
        return ProcessExitWatchResult.Watching;
    }

    /// <summary>
    /// Completes with <see cref="ProcessExitWatchResult.Exited"/> once the process exits (at once when it already
    /// has), or with <see cref="ProcessExitWatchResult.Unavailable"/> when it cannot be watched.
    /// </summary>
    public static async Task<ProcessExitWatchResult> WaitForExitAsync(
        int processId,
        long? expectedStartFileTime,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessExitWatchResult result = TryRegister(
            processId,
            expectedStartFileTime,
            () => exited.TrySetResult(),
            out IDisposable? registration);
        if (result != ProcessExitWatchResult.Watching) return result;

        using (registration)
        {
            await exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return ProcessExitWatchResult.Exited;
        }
    }

    /// <summary>Reads a process's creation time (FILETIME, UTC); false when it is gone or refuses limited query access.</summary>
    public static bool TryGetStartFileTime(int processId, out long startFileTime)
    {
        startFileTime = 0;
        if (processId <= 0) return false;

        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)processId);
        if (handle == IntPtr.Zero) return false;

        try
        {
            return TryReadStartFileTime(handle, out startFileTime);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Maps an OpenProcess failure: only ERROR_INVALID_PARAMETER means no process has the ID; anything else, access
    /// denied above all, means the process may well be running.
    /// </summary>
    internal static ProcessExitWatchResult ClassifyOpenFailure(int win32Error) =>
        win32Error == NativeErrors.ERROR_INVALID_PARAMETER
            ? ProcessExitWatchResult.Exited
            : ProcessExitWatchResult.Unavailable;

    /// <summary>Subscriber count of the shared watch on <paramref name="processId"/>; 0 when nothing watches it.</summary>
    internal static int SubscriberCount(int processId)
    {
        lock (Gate) return Entries.TryGetValue(processId, out Entry? entry) ? entry.Subscriptions.Count : 0;
    }

    private static ProcessExitWatchResult TryOpen(int processId, long? expectedStartFileTime, out SafeWaitHandle? handle)
    {
        handle = null;
        IntPtr raw = Kernel32.OpenProcess(
            Kernel32.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            (uint)processId);
        bool canQuery = raw != IntPtr.Zero;
        if (raw == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != NativeErrors.ERROR_ACCESS_DENIED || expectedStartFileTime.HasValue) return ClassifyOpenFailure(error);

            // Some protected processes grant SYNCHRONIZE without limited query access
            raw = Kernel32.OpenProcess(Kernel32.SYNCHRONIZE, bInheritHandle: false, (uint)processId);
            if (raw == IntPtr.Zero) return ClassifyOpenFailure(Marshal.GetLastPInvokeError());
        }

        SafeWaitHandle opened = new(raw, ownsHandle: true);
        if (expectedStartFileTime is { } expected)
        {
            if (!canQuery || !TryReadStartFileTime(raw, out long actual))
            {
                opened.Dispose();
                return ProcessExitWatchResult.Unavailable;
            }

            if (actual != expected)
            {
                opened.Dispose();
                return ProcessExitWatchResult.Exited;
            }
        }

        // A process that exited while someone still holds a handle to it opens fine but is already signaled
        if (Kernel32.WaitForSingleObject(raw, dwMilliseconds: 0) == Kernel32.WAIT_OBJECT_0)
        {
            opened.Dispose();
            return ProcessExitWatchResult.Exited;
        }

        handle = opened;
        return ProcessExitWatchResult.Watching;
    }

    /// <summary>Reads the creation time (FILETIME, UTC) of a process opened with PROCESS_QUERY_LIMITED_INFORMATION.</summary>
    internal static bool TryReadStartFileTime(IntPtr processHandle, out long startFileTime)
    {
        startFileTime = 0;
        if (!Kernel32.GetProcessTimes(processHandle, out Kernel32.FILETIME creation, out _, out _, out _)) return false;

        startFileTime = ((long)creation.HighDateTime << 32) | creation.LowDateTime;
        return startFileTime != 0;
    }

    private static void OnSignaled(object? state, bool timedOut)
    {
        Entry entry = (Entry)state!;
        Subscription[] subscriptions;
        lock (Gate)
        {
            if (entry.Closed) return;

            subscriptions = [.. entry.Subscriptions];
            entry.Subscriptions.Clear();
            entry.Close();
        }

        foreach (Subscription subscription in subscriptions)
        {
            try
            {
                subscription.Invoke();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessExitWatch: exit callback for PID {entry.ProcessId} failed: {exception}");
            }
        }
    }

    private sealed class Entry(int processId, SafeWaitHandle handle)
    {
        private readonly ProcessWaitHandle _waitHandle = new(handle);
        private RegisteredWaitHandle? _registeredWait;

        public int ProcessId { get; } = processId;
        public List<Subscription> Subscriptions { get; } = [];
        public bool Closed { get; private set; }

        // Called under Gate, so OnSignaled cannot observe the entry before the registration is stored
        public void Start() =>
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _waitHandle,
                OnSignaled,
                this,
                Timeout.Infinite,
                executeOnlyOnce: true);

        // Called under Gate
        public void Close()
        {
            if (Closed) return;

            Closed = true;
            if (Entries.TryGetValue(ProcessId, out Entry? current) && ReferenceEquals(current, this))
                Entries.Remove(ProcessId);
            _registeredWait?.Unregister(waitObject: null);
            _waitHandle.Dispose();
        }
    }

    private sealed class Subscription(Action onExit) : IDisposable
    {
        private Action? _onExit = onExit;

        public Entry? Entry { get; set; }

        public void Invoke() => Interlocked.Exchange(ref _onExit, value: null)?.Invoke();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _onExit, value: null) == null) return;

            lock (Gate)
            {
                Entry? entry = Entry;
                if (entry == null || entry.Closed) return;

                entry.Subscriptions.Remove(this);
                if (entry.Subscriptions.Count == 0) entry.Close();
            }
        }
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
}

using VolumeTrayAppDotNET.Interop;


namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Single background thread that fires per-PID callbacks when a watched process exits.
/// Mirrors EarTrumpet's ProcessWatcherService - opens a SYNCHRONIZE handle per PID and
/// blocks in WaitForMultipleObjects until any of them signals. An auto-reset wake event
/// in slot 0 lets the foreground add / remove watches without tearing down the wait.
///
/// The audio engine sometimes leaves a session as Inactive long after the owning process
/// is gone (force-kill, crash) instead of firing Expired promptly. Watching the process
/// handle directly closes that gap because the OS signals the handle within microseconds
/// of the process record being torn down.
/// </summary>
internal sealed class ProcessExitMonitor : IDisposable
{
    // WaitForMultipleObjects caps at MAXIMUM_WAIT_OBJECTS (64). Slot 0 is reserved for the
    // wake event, so the watch set is capped at 63. Beyond that the overflow PIDs simply
    // miss handle-driven detection and have to wait for the audio-session Expired callback.
    private const int MAXIMUM_WAIT_OBJECTS = 64;

    // OpenProcess / CloseHandle live in the common Kernel32 helper shared with ProcessHelper and
    // AppIconResolver.

    private sealed class WatchEntry
    {
        public IntPtr Handle;
        public Action Callback = static () => { };
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<uint, WatchEntry> _watches = [];
    private readonly Thread _thread;
    private readonly IntPtr _wakeEvent;
    private volatile bool _disposed;

    public ProcessExitMonitor()
    {
        // Auto-reset (bManualReset = false) so a SetEvent fires exactly one Wait wake-up and
        // resets atomically when observed - no chance of a Watch's SetEvent being lost to a
        // foreign ResetEvent racing inside the loop.
        _wakeEvent = Kernel32Wait.CreateEventW(IntPtr.Zero, false, false, null);
        if (_wakeEvent == IntPtr.Zero) throw new InvalidOperationException("CreateEventW failed");

        _thread = new Thread(WaitLoop) { IsBackground = true, Name = "VolumeTrayApp.ProcessExitMonitor", };
        _thread.Start();
    }

    /// <summary>
    /// Register a callback that fires on the watcher thread when <paramref name="pid"/> exits.
    /// If the process is already gone (or pid is zero, or the monitor is disposed), invokes
    /// <paramref name="onExit"/> synchronously and returns false. Multiple watches for the same
    /// PID chain into a single handle so Discord / Chromium child processes that share a PID
    /// (rare, but possible) all get notified.
    /// </summary>
    public bool Watch(uint pid, Action onExit)
    {
        if (_disposed || pid == 0)
        {
            try { onExit(); }
            catch { }

            return false;
        }

        IntPtr handle = Kernel32.OpenProcess(Kernel32Wait.SYNCHRONIZE, false, pid);
        if (handle == IntPtr.Zero)
        {
            // Process gone between session creation and our subscribe, or denied. Fire now.
            try { onExit(); }
            catch { }

            return false;
        }

        bool wake;
        lock (_gate)
        {
            if (_watches.TryGetValue(pid, out WatchEntry? existing))
            {
                // Already watching this PID - fold the new callback into the existing watch
                // and drop the redundant handle. SYNCHRONIZE handles refcount on the kernel
                // side anyway; one is enough to drive the wait.
                Kernel32.CloseHandle(handle);
                Action prev = existing.Callback;
                existing.Callback = () =>
                {
                    try { prev(); }
                    catch { }

                    try { onExit(); }
                    catch { }
                };
                return true;
            }

            _watches[pid] = new WatchEntry { Handle = handle, Callback = onExit };
            wake = true;
        }

        if (wake) Kernel32Wait.SetEvent(_wakeEvent);
        return true;
    }

    /// <summary>
    /// Stop watching <paramref name="pid"/>. Safe to call multiple times or for unwatched PIDs.
    /// Does not invoke any pending callback - the caller is unsubscribing because they no longer care.
    /// </summary>
    public void Unwatch(uint pid)
    {
        if (_disposed || pid == 0) return;

        IntPtr toClose = IntPtr.Zero;
        lock (_gate)
        {
            if (_watches.TryGetValue(pid, out WatchEntry? w))
            {
                toClose = w.Handle;
                _watches.Remove(pid);
            }
        }

        if (toClose != IntPtr.Zero)
        {
            Kernel32.CloseHandle(toClose);
            Kernel32Wait.SetEvent(_wakeEvent);
        }
    }

    private void WaitLoop()
    {
        while (!_disposed)
        {
            // Snapshot the watch set into parallel pids[] / handles[]. Slot 0 of handles[] is
            // the wake event; slots 1..N mirror pids[0..N-1]. Allocating per-iteration is fine -
            // process-exit events are rare and the array is at most 64 IntPtrs.
            uint[] pids;
            IntPtr[] handles;
            lock (_gate)
            {
                int count = _watches.Count;
                if (count > MAXIMUM_WAIT_OBJECTS - 1) count = MAXIMUM_WAIT_OBJECTS - 1;

                pids = new uint[count];
                handles = new IntPtr[count + 1];
                handles[0] = _wakeEvent;

                int i = 0;
                foreach (KeyValuePair<uint, WatchEntry> kvp in _watches)
                {
                    if (i >= count) break;
                    pids[i] = kvp.Key;
                    handles[i + 1] = kvp.Value.Handle;
                    i++;
                }
            }

            uint result = Kernel32Wait.WaitForMultipleObjects(
                (uint)handles.Length,
                handles,
                false,
                Kernel32Wait.INFINITE);
            if (_disposed) return;

            if (result == Kernel32Wait.WAIT_FAILED)
            {
                // Most commonly fires when a handle was just closed by Unwatch. Re-snapshot.
                Thread.Sleep(TimeConstants.ProcessExitWatchRetryDelayMs);
                continue;
            }

            int idx = (int)(result - Kernel32Wait.WAIT_OBJECT_0);
            if (idx < 0 || idx >= handles.Length) continue;
            if (idx == 0) continue; // wake event - re-snapshot the watch set

            uint signaledPID = pids[idx - 1];
            Action? callback = null;
            IntPtr toClose = IntPtr.Zero;
            lock (_gate)
            {
                if (_watches.TryGetValue(signaledPID, out WatchEntry? w))
                {
                    callback = w.Callback;
                    toClose = w.Handle;
                    _watches.Remove(signaledPID);
                }
            }

            if (toClose != IntPtr.Zero) Kernel32.CloseHandle(toClose);
            try { callback?.Invoke(); }
            catch
            {
                /* never let a callback bring down the watcher */
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kernel32Wait.SetEvent(_wakeEvent);
        try { _thread.Join(500); }
        catch { }

        lock (_gate)
        {
            foreach (WatchEntry w in _watches.Values) Kernel32.CloseHandle(w.Handle);
            _watches.Clear();
        }

        Kernel32.CloseHandle(_wakeEvent);
    }
}

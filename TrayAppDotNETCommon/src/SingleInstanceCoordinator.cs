using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace TrayAppDotNETCommon;

public sealed record SingleInstanceIdentity(string ApplicationName, string AppGuid)
{
    public string SingleInstanceMutexName => $"Local\\{ApplicationName}-Watcher-{AppGuid}";
    public string ApplicationInstanceMutexName => $"Local\\{ApplicationName}-Application-{AppGuid}";
    public string PIDMmfName => $"Local\\{ApplicationName}-WatcherPID-{AppGuid}";
}

/// <summary>
/// Owns the single-instance Mutex and PID-bulletin MMF for the current process lifetime.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const int MmfSize = 12;
    private const int OffsetGeneration = 0;
    private const int OffsetWatcherPID = 4;
    private const int OffsetMonitoredPID = 8;

    private readonly Mutex _mutex;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private bool _disposed;

    private SingleInstanceCoordinator(
        Mutex mutex,
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor view)
    {
        _mutex = mutex;
        _mmf = mmf;
        _view = view;
    }

    public static SingleInstanceCoordinator AcquireOrTakeover(SingleInstanceIdentity identity) =>
        AcquireOrTakeover(identity, Environment.ProcessId, monitoredPID: 0);

    /// <summary>
    /// Returns whether a watcher of this app holds the single-instance mutex in this session, without taking it.
    /// </summary>
    public static bool IsRunning(SingleInstanceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        try
        {
            if (!Mutex.TryOpenExisting(identity.SingleInstanceMutexName, out Mutex? mutex)) return false;

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated owner's mutex exists but denies this unelevated process
            return true;
        }
    }

    public static SingleInstanceCoordinator AcquireOrTakeover(
        SingleInstanceIdentity identity,
        int watcherPID,
        int monitoredPID,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(watcherPID);
        ArgumentOutOfRangeException.ThrowIfNegative(monitoredPID);

        Mutex mutex = new(initiallyOwned: true, identity.SingleInstanceMutexName, out bool createdNew);
        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;

        try
        {
            if (!createdNew) TakeoverFromExistingOwner(identity, mutex, log);

            mmf = MemoryMappedFile.CreateOrOpen(identity.PIDMmfName, MmfSize);
            view = mmf.CreateViewAccessor(offset: 0, MmfSize);

            int generation = view.ReadInt32(OffsetGeneration);
            view.Write(OffsetWatcherPID, watcherPID);
            view.Write(OffsetMonitoredPID, monitoredPID);
            Interlocked.MemoryBarrier();
            view.Write(OffsetGeneration, NextGeneration(generation));
            view.Flush();

            SingleInstanceCoordinator coordinator = new(mutex, mmf, view);
            mmf = null;
            view = null;
            return coordinator;
        }
        catch
        {
            try { view?.Dispose(); }
            catch
            {
                /* ignored */
            }

            try { mmf?.Dispose(); }
            catch
            {
                /* ignored */
            }

            try { mutex.ReleaseMutex(); }
            catch
            {
                /* ignored */
            }

            try { mutex.Dispose(); }
            catch
            {
                /* ignored */
            }

            throw;
        }
    }

    public void RecordMonitoredPID(int pid)
    {
        if (_disposed) return;
        ArgumentOutOfRangeException.ThrowIfNegative(pid);

        _view.Write(OffsetMonitoredPID, pid);
        Interlocked.MemoryBarrier();
        _view.Flush();
    }

    /// <summary>
    /// Takes the single instance over from the running owner. The running app is first asked to exit through
    /// <see cref="TrayAppDotNETExitRequest"/>, so it runs its normal exit (hardware handed back, settings saved) and its
    /// watcher follows; only when the mutex is still held after a bounded wait are the old processes killed, each one
    /// proven by image name and start time first.
    /// </summary>
    private static void TakeoverFromExistingOwner(
        SingleInstanceIdentity identity,
        Mutex mutex,
        Action<string>? log)
    {
        TrayAppDotNETProcessIdentity? oldWatcher = null;
        TrayAppDotNETProcessIdentity? oldMonitored = null;
        if (TryReadExistingOwner(identity, out int oldWatcherPID, out int oldMonitoredPID))
        {
            oldWatcher = CaptureOwner(identity, oldWatcherPID, log);
            if (oldMonitoredPID != oldWatcherPID) oldMonitored = CaptureOwner(identity, oldMonitoredPID, log);
        }

        if (oldMonitored != null && TrayAppDotNETExitRequest.TryRequest(oldMonitored, log))
        {
            log?.Invoke($"SingleInstanceCoordinator: asked PID {oldMonitored.ProcessId} to exit");
            if (WaitForMutex(mutex, TimeConstants.GracefulExitRequestTimeoutMs)) return;

            log?.Invoke($"SingleInstanceCoordinator: PID {oldMonitored.ProcessId} did not exit in time");
        }

        if (oldWatcher != null) TrayAppDotNETExitRequest.TryKill(oldWatcher, entireProcessTree: true, log);
        if (oldMonitored != null) TrayAppDotNETExitRequest.TryKill(oldMonitored, entireProcessTree: true, log);

        if (!WaitForMutex(mutex, TimeConstants.SingleInstanceMutexAcquireTimeoutMs))
            throw new InvalidOperationException("Timed out waiting for single-instance mutex.");

        // A killed watcher can die after CreateProcess but before it publishes the child PID. Sweep same-name
        // processes only after owning the mutex, when the old watcher can no longer spawn.
        StopUnrecordedApplicationProcesses(identity.ApplicationName, log);
    }

    private static bool WaitForMutex(Mutex mutex, int timeoutMs)
    {
        try
        {
            return mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner was killed or crashed; this process now owns the mutex
            return true;
        }
    }

    /// <summary>
    /// Captures a PID from the owner bulletin, or null when it is gone or now names a process that is not this app.
    /// </summary>
    private static TrayAppDotNETProcessIdentity? CaptureOwner(
        SingleInstanceIdentity identity,
        int processID,
        Action<string>? log)
    {
        if (processID <= 0 || processID == Environment.ProcessId) return null;

        TrayAppDotNETProcessIdentity? owner = TrayAppDotNETProcessIdentity.TryCapture(processID);
        if (owner == null) return null;
        if (IsApplicationImage(owner, identity.ApplicationName)) return owner;

        log?.Invoke($"SingleInstanceCoordinator: PID {processID} is {owner.ImagePath}, not {identity.ApplicationName}");
        return null;
    }

    private static bool IsApplicationImage(TrayAppDotNETProcessIdentity process, string applicationName) =>
        string.Equals(
            Path.GetFileNameWithoutExtension(process.ImagePath),
            Path.GetFileNameWithoutExtension(applicationName),
            StringComparison.OrdinalIgnoreCase);

    private static void StopUnrecordedApplicationProcesses(string applicationName, Action<string>? log)
    {
        string processName = Path.GetFileNameWithoutExtension(applicationName);
        if (string.IsNullOrWhiteSpace(processName)) return;

        int currentSessionID;
        Process[] processes;
        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            currentSessionID = currentProcess.SessionId;
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception exception)
        {
            log?.Invoke($"SingleInstanceCoordinator: process sweep enumeration failed: {exception.Message}");
            return;
        }

        List<TrayAppDotNETProcessIdentity> unrecorded = [];
        foreach (Process process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != currentSessionID) continue;

                    TrayAppDotNETProcessIdentity? captured = TrayAppDotNETProcessIdentity.TryCapture(process.Id);
                    if (captured != null && IsApplicationImage(captured, applicationName)) unrecorded.Add(captured);
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                      or System.ComponentModel.Win32Exception)
                {
                    log?.Invoke($"SingleInstanceCoordinator: process sweep skipped a process: {exception.Message}");
                }
            }
        }

        if (unrecorded.Count == 0) return;

        // A transient launcher can be the current watcher's parent, so never kill its tree.
        IReadOnlyList<TrayAppDotNETProcessIdentity> remaining = TrayAppDotNETExitRequest.StopProcesses(
            unrecorded,
            TimeConstants.GracefulExitRequestTimeoutMs,
            killEntireProcessTree: false,
            log);
        foreach (TrayAppDotNETProcessIdentity process in remaining)
            log?.Invoke($"SingleInstanceCoordinator: unrecorded {applicationName} PID {process.ProcessId} did not exit.");
    }

    private static bool TryReadExistingOwner(
        SingleInstanceIdentity identity,
        out int oldWatcherPID,
        out int oldMonitoredPID)
    {
        long deadline = Environment.TickCount64 + TimeConstants.SingleInstancePidBulletinReadTimeoutMs;

        do
        {
            try
            {
                using MemoryMappedFile existing = MemoryMappedFile.OpenExisting(identity.PIDMmfName);
                using MemoryMappedViewAccessor view =
                    existing.CreateViewAccessor(offset: 0, MmfSize, MemoryMappedFileAccess.Read);
                int generation = view.ReadInt32(OffsetGeneration);
                Interlocked.MemoryBarrier();

                if (generation != 0)
                {
                    oldWatcherPID = view.ReadInt32(OffsetWatcherPID);
                    oldMonitoredPID = view.ReadInt32(OffsetMonitoredPID);
                    return true;
                }
            }
            catch
            {
                // The owner may still be publishing its PID bulletin, or it may be exiting.
            }

            Thread.Sleep(TimeConstants.SingleInstancePidBulletinReadRetryMs);
        } while (Environment.TickCount64 < deadline);

        oldWatcherPID = 0;
        oldMonitoredPID = 0;
        return false;
    }

    private static int NextGeneration(int generation)
    {
        int next = generation == int.MaxValue ? 1 : generation + 1;
        return next == 0 ? 1 : next;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try { _view.Dispose(); }
        catch
        {
            /* ignored */
        }

        try { _mmf.Dispose(); }
        catch
        {
            /* ignored */
        }

        try { _mutex.ReleaseMutex(); }
        catch
        {
            /* ignored */
        }

        try { _mutex.Dispose(); }
        catch
        {
            /* ignored */
        }
    }
}

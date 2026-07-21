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
        AcquireOrTakeover(identity, Environment.ProcessId, 0);

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
            view = mmf.CreateViewAccessor(0, MmfSize);

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

    private static void TakeoverFromExistingOwner(
        SingleInstanceIdentity identity,
        Mutex mutex,
        Action<string>? log)
    {
        if (TryReadExistingOwner(identity, out int oldWatcherPID, out int oldMonitoredPID))
        {
            KillByPID(oldWatcherPID);
            if (oldMonitoredPID != 0 && oldMonitoredPID != oldWatcherPID) KillByPID(oldMonitoredPID);
        }

        try
        {
            if (!mutex.WaitOne(TimeConstants.SingleInstanceMutexAcquireTimeoutMs))
                throw new InvalidOperationException("Timed out waiting for single-instance mutex.");
        }
        catch (AbandonedMutexException)
        {
            // Expected - previous owner was killed. We now own the mutex.
        }

        // The previous watcher can be killed after CreateProcess but before it publishes the child PID.
        // Sweep same-name processes only after owning the mutex, when the old watcher can no longer spawn.
        TerminateUnrecordedApplicationProcesses(identity.ApplicationName, log);
    }

    private static void TerminateUnrecordedApplicationProcesses(string applicationName, Action<string>? log)
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

        foreach (Process process in processes)
        {
            using (process)
            {
                int processID = 0;
                try
                {
                    processID = process.Id;
                    if (processID == Environment.ProcessId ||
                        process.SessionId != currentSessionID ||
                        process.HasExited)
                        continue;

                    // A transient launcher can be the current watcher's parent, so never kill its tree.
                    process.Kill(entireProcessTree: false);
                    if (!process.WaitForExit(TimeConstants.SingleInstanceMutexAcquireTimeoutMs))
                    {
                        log?.Invoke(
                            $"SingleInstanceCoordinator: unrecorded {applicationName} PID {processID} did not exit.");
                    }
                }
                catch (Exception exception)
                {
                    log?.Invoke(
                        $"SingleInstanceCoordinator: unrecorded {applicationName} PID {processID} cleanup failed: "
                        + exception.Message);
                }
            }
        }
    }

    private static void KillByPID(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return;

        try
        {
            using Process proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // PID not running - stale entry, ignore.
        }
        catch (InvalidOperationException)
        {
            // Process already exited between lookup and Kill.
        }
        catch
        {
            // Access denied or other - takeover will still proceed.
        }
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
                    existing.CreateViewAccessor(0, MmfSize, MemoryMappedFileAccess.Read);
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

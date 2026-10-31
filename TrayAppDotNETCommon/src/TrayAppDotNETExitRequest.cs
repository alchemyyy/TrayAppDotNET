using System.Diagnostics;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon;

/// <summary>
/// A running process pinned by its ID, image path and start time. A process ID is reused once its process is gone,
/// so every kill first proves through <see cref="TryOpen"/> that the ID still names the process that was captured.
/// </summary>
public sealed record TrayAppDotNETProcessIdentity(int ProcessId, string ImagePath, DateTime StartTimeUtc)
{
    /// <summary>Captures a running process, or returns null when it is gone or cannot be queried.</summary>
    public static TrayAppDotNETProcessIdentity? TryCapture(int processId)
    {
        if (processId <= 0) return null;

        try
        {
            using Process process = Process.GetProcessById(processId);
            // The handle keeps the ID from being reused while the path and start time are read
            _ = process.SafeHandle;
            if (process.HasExited) return null;

            string? imagePath = ProcessImagePath.TryGet(processId);
            return imagePath == null
                ? null
                : new TrayAppDotNETProcessIdentity(processId, imagePath, process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the process again and returns it only while its ID still names the captured image and start time. The
    /// returned object holds a process handle, so the ID cannot be reused until it is disposed.
    /// </summary>
    public Process? TryOpen()
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(ProcessId);
            _ = process.SafeHandle;
            if (!process.HasExited &&
                process.StartTime.ToUniversalTime() == StartTimeUtc &&
                string.Equals(ProcessImagePath.TryGet(ProcessId), ImagePath, StringComparison.OrdinalIgnoreCase))
            {
                Process matched = process;
                process = null;
                return matched;
            }

            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>Gets whether the captured process is still running.</summary>
    public bool IsRunning()
    {
        using Process? process = TryOpen();
        return process != null;
    }
}

/// <summary>
/// The graceful exit channel between a running tray app and another process that needs it gone: a second launch
/// taking over the single instance, uninstall preparation and the update installer. The app listens on a named
/// event keyed by its process ID and turns a signal into its normal exit, which runs the app's cleanup (fans handed
/// back to firmware, Night Light tint removed, settings flushed) and ends with exit code 0, so its watcher exits too.
/// A requester signals, waits a bounded time, and only then falls back to killing what is left.
/// </summary>
public static class TrayAppDotNETExitRequest
{
    private const string EventNamePrefix = @"Local\TrayAppDotNET-ExitRequest-";

    /// <summary>Returns the session-local event name the process with <paramref name="processId"/> listens on.</summary>
    public static string GetEventName(int processId) => EventNamePrefix + processId;

    /// <summary>
    /// Starts listening for exit requests addressed to this process. <paramref name="onExitRequested"/> runs once, on
    /// a thread-pool thread; dispose the listener to stop listening.
    /// </summary>
    public static TrayAppDotNETExitRequestListener Listen(Action onExitRequested)
    {
        ArgumentNullException.ThrowIfNull(onExitRequested);
        return new TrayAppDotNETExitRequestListener(GetEventName(Environment.ProcessId), onExitRequested);
    }

    /// <summary>
    /// Signals the exit request event of <paramref name="process"/>. Returns false when the process is gone, no
    /// longer matches the captured identity, or does not listen (a watcher, or an app still starting).
    /// </summary>
    public static bool TryRequest(TrayAppDotNETProcessIdentity process, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(process);

        using Process? running = process.TryOpen();
        if (running == null) return false;

        try
        {
            if (!EventWaitHandle.TryOpenExisting(GetEventName(process.ProcessId), out EventWaitHandle? exitEvent))
                return false;

            using (exitEvent)
                return exitEvent.Set();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                              or WaitHandleCannotBeOpenedException)
        {
            log?.Invoke($"TrayAppDotNETExitRequest: could not signal PID {process.ProcessId}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stops <paramref name="processes"/>: asks every listening one to exit, waits up to
    /// <paramref name="gracefulTimeoutMs"/> for all of them to end (a watcher ends on its own once its app exits),
    /// then kills each one still running after proving it is the captured process. The current process is never
    /// killed. Returns the processes still running afterwards.
    /// </summary>
    public static IReadOnlyList<TrayAppDotNETProcessIdentity> StopProcesses(
        IReadOnlyList<TrayAppDotNETProcessIdentity> processes,
        int gracefulTimeoutMs,
        bool killEntireProcessTree,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentOutOfRangeException.ThrowIfNegative(gracefulTimeoutMs);

        bool anySignalled = false;
        foreach (TrayAppDotNETProcessIdentity process in processes)
        {
            if (process.ProcessId == Environment.ProcessId) continue;
            if (TryRequest(process, log))
            {
                anySignalled = true;
                log?.Invoke($"TrayAppDotNETExitRequest: asked PID {process.ProcessId} to exit");
            }
        }

        if (anySignalled) WaitForExit(processes, gracefulTimeoutMs);

        List<TrayAppDotNETProcessIdentity> remaining = [];
        foreach (TrayAppDotNETProcessIdentity process in processes)
        {
            if (process.ProcessId == Environment.ProcessId) continue;
            if (!TryKill(process, killEntireProcessTree, log) && process.IsRunning()) remaining.Add(process);
        }

        return remaining;
    }

    /// <summary>
    /// Kills <paramref name="process"/> when it is still the captured process and waits briefly for it to end.
    /// Returns true when it is gone afterwards (including when it was already gone).
    /// </summary>
    public static bool TryKill(TrayAppDotNETProcessIdentity process, bool entireProcessTree, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (process.ProcessId == Environment.ProcessId) return false;

        using Process? running = process.TryOpen();
        if (running == null) return true;

        try
        {
            log?.Invoke($"TrayAppDotNETExitRequest: killing PID {process.ProcessId} ({process.ImagePath})");
            running.Kill(entireProcessTree);
            return running.WaitForExit(TimeConstants.GracefulExitKillWaitMs);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            log?.Invoke($"TrayAppDotNETExitRequest: could not kill PID {process.ProcessId}: {exception.Message}");
            return running.HasExited;
        }
    }

    private static void WaitForExit(IReadOnlyList<TrayAppDotNETProcessIdentity> processes, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        foreach (TrayAppDotNETProcessIdentity process in processes)
        {
            if (process.ProcessId == Environment.ProcessId) continue;

            using Process? running = process.TryOpen();
            if (running == null) continue;

            long remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0) return;
            _ = running.WaitForExit((int)remainingMs);
        }
    }
}

/// <summary>Listens on this process's exit request event until disposed.</summary>
public sealed class TrayAppDotNETExitRequestListener : IDisposable
{
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _registration;
    private int _disposed;

    internal TrayAppDotNETExitRequestListener(string eventName, Action onExitRequested)
    {
        _exitEvent = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, eventName);
        // A request that arrived before this process got here is stale, it was addressed to an earlier owner of the ID
        _exitEvent.Reset();
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _exitEvent,
            (_, _) => onExitRequested(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) == 1) return;

        _registration.Unregister(waitObject: null);
        _exitEvent.Dispose();
    }
}

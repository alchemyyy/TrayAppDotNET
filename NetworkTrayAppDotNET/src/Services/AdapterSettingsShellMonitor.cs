using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetworkTrayAppDotNET.Services;

/// <summary>
/// Opens the Network Connections window via ncpa.cpl or the Explorer shell folder GUID,
/// then watches the resulting factory explorer.exe with a three-stage SetWinEventHook chain
/// (process spawn -> CabinetWClass shown -> CabinetWClass destroyed) and ends the process
/// once its last visible window is gone. Without this, every open leaves a phantom explorer.exe
/// in the background because the shell uses an out-of-process factory model.
/// </summary>
/// <remarks>
/// Launching through the desktop Explorer (ExplorerProcessLauncher.TryShellExecute) is not used: depending on the
/// "launch folder windows in a separate process" setting it opens the window in the desktop Explorer itself or in a
/// new factory process, so the factory still has to be watched. The factory process is only ever ended through the
/// handle opened when it was first seen (its ID cannot be recycled while that handle is open), and only when it has
/// no visible top-level window left, so other Explorer windows routed into the same process keep running.
/// </remarks>
internal static class AdapterSettingsShellMonitor
{
    // Shell factory command-line markers we recognize as "the explorer we just spawned".
    // We don't need to extract the GUIDs at runtime - just substring-match.
    private static readonly string[] ExplorerFactoryCommandLines =
    [
        "/factory,{5bd95610-9434-43c2-886c-57852cc8a120} -Embedding", // Control Panel (ncpa.cpl)
        "/factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding" // Explorer shell
    ];

    private const string TargetWindowClass = "CabinetWClass";
    private const int ClassNameCapacity = 256;
    private const int MonitorReadyTimeoutMs = 2_000;
    private const int ProcessSpawnMonitorTimeoutMs = 15_000;
    private const int MainWindowMonitorTimeoutMs = 30_000;
    private const int WindowDestroyMonitorTimeoutMs = 6 * 60 * 60 * 1_000;
    private const int OBJID_WINDOW = 0;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint PM_NOREMOVE = 0x0000;
    private const uint ProcessExitCode = 0;

    private static readonly Lock _lock = new();
    private static readonly HashSet<int> _monitoredPids = [];
    private static readonly List<CancellationTokenSource> _activeMonitorCancellationSources = [];

    public static void OpenAndMonitorControlPanel() => OpenAndMonitor(fileName: "ncpa.cpl", arguments: null);

    public static void OpenAndMonitorExplorerShell() =>
        OpenAndMonitor(fileName: "explorer.exe", arguments: "shell:::{7007acc7-3202-11d1-aad2-00805fc1270e}");

    private static void OpenAndMonitor(string fileName, string? arguments)
    {
        ProcessMonitor? monitor = null;
        try
        {
            HashSet<int> existingPids = GetExplorerFactoryPids();

            // Also exclude PIDs we're already tracking so a second open doesn't latch onto the prior window's process.
            lock (_lock) existingPids.UnionWith(_monitoredPids);

            // Start event-driven monitoring on a dedicated thread BEFORE launching,
            // so the spawn event is always observed.
            monitor = new ProcessMonitor(existingPids);
            monitor.Start();
            if (!monitor.WaitForReady(TimeSpan.FromMilliseconds(MonitorReadyTimeoutMs)))
            {
                monitor.Cancel();
                TADNLog.Log("AdapterSettingsShellMonitor: process monitor did not become ready before timeout");
                return;
            }

            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = fileName, Arguments = arguments ?? string.Empty, UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            monitor?.Cancel();
            TADNLog.Log($"AdapterSettingsShellMonitor.OpenAndMonitor({fileName}): {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        List<CancellationTokenSource> cancellationSources = [];
        lock (_lock)
        {
            cancellationSources.AddRange(_activeMonitorCancellationSources);
            _monitoredPids.Clear();
        }

        foreach (CancellationTokenSource cancellationSource in cancellationSources)
        {
            try { cancellationSource.Cancel(); }
            catch (ObjectDisposedException)
            {
                TADNLog.Log("AdapterSettingsShellMonitor.Shutdown: cancellation source was already disposed");
            }
        }
    }

    private static void AddMonitoredPid(int pid)
    {
        lock (_lock) _monitoredPids.Add(pid);
    }

    private static void RemoveMonitoredPid(int pid)
    {
        lock (_lock) _monitoredPids.Remove(pid);
    }

    private static HashSet<int> GetExplorerFactoryPids()
    {
        HashSet<int> pids = [];
        foreach (Process proc in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (IsFactoryExplorer(proc.Id)) pids.Add(proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }

        return pids;
    }

    private static bool IsFactoryExplorer(int pid) => IsFactoryCommandLine(ProcessCommandLine.TryGet(pid));

    internal static bool IsFactoryCommandLine(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return false;

        foreach (string factoryCmd in ExplorerFactoryCommandLines)
        {
            if (commandLine.Contains(factoryCmd, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static unsafe bool IsTargetWindowClass(IntPtr hwnd)
    {
        char* className = stackalloc char[ClassNameCapacity];
        int length = User32.GetClassName(hwnd, className, ClassNameCapacity);
        return length > 0 && new ReadOnlySpan<char>(className, length).SequenceEqual(TargetWindowClass);
    }

    /// <summary>True when <paramref name="processId"/> still owns a visible top-level window other than <paramref name="excluded"/>.</summary>
    private static bool HasOtherVisibleWindow(int processId, IntPtr excluded)
    {
        bool found = false;
        User32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == excluded || !User32.IsWindowVisible(hwnd)) return true;

            User32.GetWindowThreadProcessId(hwnd, out uint ownerProcessId);
            found = ownerProcessId == (uint)processId;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    private static CancellationTokenSource CreateMonitorCancellationSource(int timeoutMs)
    {
        CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.CancelAfter(timeoutMs);
        lock (_lock) _activeMonitorCancellationSources.Add(cancellationTokenSource);
        return cancellationTokenSource;
    }

    private static void CompleteMonitorCancellationSource(CancellationTokenSource cancellationTokenSource)
    {
        lock (_lock) _activeMonitorCancellationSources.Remove(cancellationTokenSource);
        cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Shared WinEvent hook loop with cancellation and deterministic unhook. Callbacks are guarded: an exception
    /// escaping into the native WinEvent dispatch would end the process.
    /// </summary>
    private abstract class WinEventMonitor
    {
        private readonly string _threadName;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private IntPtr _hook;
        private uint _threadId;
        private bool _callbackFailureLogged;

        protected WinEventMonitor(string threadName, int timeoutMs)
        {
            _threadName = threadName;
            _cancellationTokenSource = CreateMonitorCancellationSource(timeoutMs);
            WinEventProc = DispatchWinEvent;
        }

        protected User32.WinEventProc WinEventProc { get; }

        protected abstract IntPtr InstallHook();

        protected virtual void OnHookInstalled() { }

        protected virtual void OnHookFailed() { }

        /// <summary>Runs on the monitor thread after the pump stopped and the hook is gone.</summary>
        protected virtual void OnStopped() { }

        protected abstract void OnWinEvent(uint eventType, IntPtr hwnd, int idObject);

        public void Start()
        {
            Thread thread = new(RunMessageLoop) { IsBackground = true, Name = _threadName };
            thread.Start();
        }

        public void Cancel()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                TADNLog.Log($"{_threadName}: cancellation source was already disposed");
            }
        }

        protected void StopMessageLoop()
        {
            ReleaseHook();
            RequestStop();
        }

        private void RunMessageLoop()
        {
            _threadId = Kernel32.GetCurrentThreadId();
            // Creates this thread's message queue before anyone can post WM_QUIT to it
            _ = User32.PeekMessage(out User32.MSG _, IntPtr.Zero, wMsgFilterMin: 0, wMsgFilterMax: 0, PM_NOREMOVE);

            CancellationTokenRegistration cancellationRegistration =
                _cancellationTokenSource.Token.Register(static state =>
                    ((WinEventMonitor)state!).RequestStop(), this);

            try
            {
                _hook = InstallHook();
                if (_hook == IntPtr.Zero)
                {
                    OnHookFailed();
                    return;
                }

                OnHookInstalled();
                RunMessagePump();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"{_threadName}: {ex.Message}");
            }
            finally
            {
                ReleaseHook();
                cancellationRegistration.Dispose();
                CompleteMonitorCancellationSource(_cancellationTokenSource);
                try { OnStopped(); }
                catch (Exception ex) { TADNLog.Log($"{_threadName}: stop handling failed: {ex}"); }
            }
        }

        private void RunMessagePump()
        {
            while (!_cancellationTokenSource.IsCancellationRequested
                   && User32.GetMessage(out User32.MSG msg, IntPtr.Zero, wMsgFilterMin: 0, wMsgFilterMax: 0) > 0)
            {
                User32.TranslateMessage(in msg);
                User32.DispatchMessage(in msg);
            }
        }

        private void DispatchWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (_cancellationTokenSource.IsCancellationRequested) return;

                OnWinEvent(eventType, hwnd, idObject);
            }
            catch (Exception ex)
            {
                if (_callbackFailureLogged) return;

                _callbackFailureLogged = true;
                TADNLog.Log($"{_threadName}: WinEvent callback failed: {ex}");
            }
        }

        private void ReleaseHook()
        {
            IntPtr hook = _hook;
            if (hook == IntPtr.Zero) return;

            _hook = IntPtr.Zero;
            if (!User32.UnhookWinEvent(hook))
                TADNLog.Log($"{_threadName}: UnhookWinEvent failed");
        }

        private void RequestStop()
        {
            uint threadId = _threadId;
            if (threadId == 0) return;

            if (!User32.PostThreadMessage(threadId, (uint)User32.WM_QUIT, IntPtr.Zero, IntPtr.Zero))
                TADNLog.Log($"{_threadName}: PostThreadMessage(WM_QUIT) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>
    /// Phase 1: catches EVENT_OBJECT_CREATE for a new factory explorer.exe, opens it, and hands off to phase 2 once
    /// its own message loop has stopped.
    /// </summary>
    private sealed class ProcessMonitor(HashSet<int> existingPids)
        : WinEventMonitor(threadName: "AdapterSettingsProcessMonitor", ProcessSpawnMonitorTimeoutMs)
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _foundPid;
        private SafeProcessHandle? _foundProcess;

        public bool WaitForReady(TimeSpan timeout)
        {
            try
            {
                bool completed = _ready.Task.Wait(timeout);
                return completed && _ready.Task.Result;
            }
            catch (AggregateException ex)
            {
                foreach (Exception inner in ex.Flatten().InnerExceptions)
                    TADNLog.Log($"AdapterSettingsProcessMonitor.WaitForReady: {inner.Message}");
                return false;
            }
        }

        protected override IntPtr InstallHook() =>
            User32.SetWinEventHook(
                EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
                IntPtr.Zero, WinEventProc,
                idProcess: 0, idThread: 0, User32.WINEVENT_OUTOFCONTEXT);

        protected override void OnHookInstalled() => _ready.TrySetResult(true);

        protected override void OnHookFailed() => _ready.TrySetResult(false);

        protected override void OnStopped()
        {
            _ready.TrySetResult(false);
            if (_foundProcess == null) return;

            // Hand off outside the WinEvent callback, after this hook is gone
            new MainWindowMonitor(_foundPid, _foundProcess).Start();
        }

        protected override void OnWinEvent(uint eventType, IntPtr hwnd, int idObject)
        {
            if (_foundProcess != null || idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid is 0 or > int.MaxValue || existingPids.Contains((int)pid)) return;

            if (!IsFactoryExplorer((int)pid)) return;

            // The handle pins the process: its ID cannot be recycled while we hold it, so the eventual end can never
            // reach another process
            IntPtr handle = Kernel32.OpenProcess(
                Kernel32.PROCESS_TERMINATE | Kernel32.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                bInheritHandle: false,
                pid);
            if (handle == IntPtr.Zero)
            {
                TADNLog.Log($"AdapterSettingsProcessMonitor: could not open factory explorer PID {pid}: {Marshal.GetLastPInvokeError()}");
                StopMessageLoop();
                return;
            }

            _foundPid = (int)pid;
            _foundProcess = new SafeProcessHandle(handle, ownsHandle: true);
            AddMonitoredPid(_foundPid);
            StopMessageLoop();
        }
    }

    /// <summary>
    /// Phase 2: waits for a CabinetWClass window to appear in the target process, then hands off to phase 3.
    /// </summary>
    private sealed class MainWindowMonitor(int pid, SafeProcessHandle process)
        : WinEventMonitor(threadName: "AdapterSettingsMainWindowMonitor", MainWindowMonitorTimeoutMs)
    {
        private IntPtr _targetHwnd;

        protected override IntPtr InstallHook() =>
            User32.SetWinEventHook(
                EVENT_OBJECT_CREATE, User32.EVENT_OBJECT_SHOW,
                IntPtr.Zero, WinEventProc,
                (uint)pid, idThread: 0, User32.WINEVENT_OUTOFCONTEXT);

        protected override void OnStopped()
        {
            if (_targetHwnd == IntPtr.Zero)
            {
                RemoveMonitoredPid(pid);
                process.Dispose();
                return;
            }

            new WindowDestroyMonitor(pid, process, _targetHwnd).Start();
        }

        protected override void OnWinEvent(uint eventType, IntPtr hwnd, int idObject)
        {
            // Only handle CREATE and SHOW; DESTROY belongs to phase 3.
            if (eventType is not EVENT_OBJECT_CREATE and not User32.EVENT_OBJECT_SHOW) return;
            if (_targetHwnd != IntPtr.Zero || idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            if (!IsTargetWindowClass(hwnd)) return;

            _targetHwnd = hwnd;
            StopMessageLoop();
        }
    }

    /// <summary>
    /// Phase 3: waits until the process's last visible window closed, then ends the process through its handle.
    /// </summary>
    /// <remarks>
    /// Out-of-context destroy events arrive after the window is gone, so a destroyed window can no longer be told
    /// apart by its class. Once the target closed, every later destroy event from this process (the hook is scoped
    /// to it) re-checks whether a visible window remains, so another Explorer window routed into the process keeps it
    /// alive only until that window closes too.
    /// </remarks>
    private sealed class WindowDestroyMonitor(int pid, SafeProcessHandle process, IntPtr targetHwnd)
        : WinEventMonitor(threadName: "AdapterSettingsWindowDestroyMonitor", WindowDestroyMonitorTimeoutMs)
    {
        private bool _targetClosed;
        private bool _lastWindowClosed;

        protected override IntPtr InstallHook() =>
            User32.SetWinEventHook(
                User32.EVENT_OBJECT_DESTROY, User32.EVENT_OBJECT_DESTROY,
                IntPtr.Zero, WinEventProc,
                (uint)pid, idThread: 0, User32.WINEVENT_OUTOFCONTEXT);

        protected override void OnStopped()
        {
            RemoveMonitoredPid(pid);
            using (process)
            {
                if (!_lastWindowClosed) return;

                if (Kernel32.WaitForSingleObject(process.DangerousGetHandle(), dwMilliseconds: 0) == Kernel32.WAIT_OBJECT_0)
                    return;

                if (!Kernel32.TerminateProcess(process.DangerousGetHandle(), ProcessExitCode))
                    TADNLog.Log($"AdapterSettingsWindowDestroyMonitor: ending PID {pid} failed: {Marshal.GetLastPInvokeError()}");
            }
        }

        protected override void OnWinEvent(uint eventType, IntPtr hwnd, int idObject)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            bool destroyedTarget = hwnd == targetHwnd;
            bool targetClosedEarlier = _targetClosed;
            if (!destroyedTarget && !targetClosedEarlier) return;

            _targetClosed = true;
            if (!ShouldEndProcess(destroyedTarget, targetClosedEarlier, HasOtherVisibleWindow(pid, hwnd))) return;

            _lastWindowClosed = true;
            StopMessageLoop();
        }
    }

    /// <summary>
    /// The factory process may end once its target window has closed (now or by an earlier destroy event) and no
    /// other visible top-level window of it remains.
    /// </summary>
    internal static bool ShouldEndProcess(bool destroyedTarget, bool targetClosedEarlier, bool otherVisibleWindowRemains) =>
        (destroyedTarget || targetClosedEarlier) && !otherVisibleWindowRemains;
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkTrayAppDotNET.Services;

/// <summary>
/// Opens the Network Connections window via ncpa.cpl or the Explorer shell folder GUID,
/// then watches the resulting factory explorer.exe with a three-stage SetWinEventHook chain
/// (process spawn -> CabinetWClass shown -> CabinetWClass destroyed) and kills the process
/// when the user closes the window. Without this, every open leaves a phantom explorer.exe
/// in the background because the shell uses an out-of-process factory model.
/// </summary>
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
    private const int MonitorReadyTimeoutMs = 2_000;
    private const int ProcessSpawnMonitorTimeoutMs = 15_000;
    private const int MainWindowMonitorTimeoutMs = 30_000;
    private const int WindowDestroyMonitorTimeoutMs = 6 * 60 * 60 * 1_000;

    private static readonly Lock _lock = new();
    private static readonly HashSet<int> _monitoredPids = [];
    private static readonly List<CancellationTokenSource> _activeMonitorCancellationSources = [];

    public static void OpenAndMonitorControlPanel() => OpenAndMonitor("ncpa.cpl", null);

    public static void OpenAndMonitorExplorerShell() =>
        OpenAndMonitor("explorer.exe", "shell:::{7007acc7-3202-11d1-aad2-00805fc1270e}");

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
            catch (Exception ex)
            {
                TADNLog.Log($"AdapterSettingsShellMonitor.GetExplorerFactoryPids({proc.Id}): {ex.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }

        return pids;
    }

    private static bool IsFactoryExplorer(int pid)
    {
        try
        {
            string? cmdLine = GetProcessCommandLine(pid);
            if (cmdLine == null) return false;

            foreach (string factoryCmd in ExplorerFactoryCommandLines)
            {
                if (cmdLine.Contains(factoryCmd, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AdapterSettingsShellMonitor.IsFactoryExplorer({pid}): {ex.Message}");
        }

        return false;
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

    private static unsafe string? GetProcessCommandLine(int pid)
    {
        nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, (uint)pid);
        if (hProcess == 0) return null;

        try
        {
            // Get PEB address from process basic information
            PROCESS_BASIC_INFORMATION pbi;
            int status = NtQueryInformationProcess(hProcess, 0, &pbi, sizeof(PROCESS_BASIC_INFORMATION), out _);
            if (status != 0) return null;

            // Read PEB to get ProcessParameters address
            PEB peb;
            if (!ReadProcessMemory(hProcess, pbi.PebBaseAddress, &peb, (nuint)sizeof(PEB), out _))
                return null;

            // Read RTL_USER_PROCESS_PARAMETERS to get command line
            RTL_USER_PROCESS_PARAMETERS processParams;
            if (!ReadProcessMemory(hProcess, peb.ProcessParameters, &processParams,
                    (nuint)sizeof(RTL_USER_PROCESS_PARAMETERS), out _))
                return null;

            if (processParams.CommandLine.Length == 0 || processParams.CommandLine.Buffer == 0) return null;

            int byteLen = processParams.CommandLine.Length;
            char* buffer = stackalloc char[byteLen / 2 + 1];
            if (!ReadProcessMemory(hProcess, (void*)processParams.CommandLine.Buffer, buffer, (nuint)byteLen, out _))
                return null;

            buffer[byteLen / 2] = '\0';
            return new string(buffer);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    #region P/Invoke

    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;
    private const int OBJID_WINDOW = 0;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool ReadProcessMemory(
        nint hProcess, void* lpBaseAddress, void* lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

    [DllImport("ntdll.dll")]
    private static extern unsafe int NtQueryInformationProcess(
        nint ProcessHandle, int ProcessInformationClass, void* ProcessInformation,
        int ProcessInformationLength, out int ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public nint Reserved1;
        public unsafe void* PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nint UniqueProcessId;
        public nint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PEB
    {
        public byte Reserved1_0;
        public byte Reserved1_1;
        public byte BeingDebugged;
        public byte Reserved2;
        public nint Reserved3_0;
        public nint Reserved3_1;
        public unsafe void* Ldr;
        public unsafe void* ProcessParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RTL_USER_PROCESS_PARAMETERS
    {
        public uint MaximumLength;
        public uint Length;
        public uint Flags;
        public uint DebugFlags;
        public nint ConsoleHandle;
        public uint ConsoleFlags;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
        public UNICODE_STRING CurrentDirectory_DosPath;
        public nint CurrentDirectory_Handle;
        public UNICODE_STRING DllPath;
        public UNICODE_STRING ImagePathName;
        public UNICODE_STRING CommandLine;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    /// <summary>
    /// Shared WinEvent hook loop with cancellation and deterministic unhook.
    /// </summary>
    private abstract class WinEventMonitor
    {
        private readonly string _threadName;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private IntPtr _hook;
        private uint _threadId;

        protected WinEventMonitor(string threadName, int timeoutMs)
        {
            _threadName = threadName;
            _cancellationTokenSource = CreateMonitorCancellationSource(timeoutMs);
            WinEventProc = DispatchWinEvent;
        }

        protected WinEventDelegate WinEventProc
        {
            get;
        }

        protected abstract IntPtr InstallHook();

        protected virtual void OnHookInstalled() { }

        protected virtual void OnHookFailed() { }

        protected virtual void OnStopped() { }

        protected abstract void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

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
            _threadId = GetCurrentThreadId();
            PeekMessage(out MSG _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

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
                OnStopped();
                cancellationRegistration.Dispose();
                CompleteMonitorCancellationSource(_cancellationTokenSource);
            }
        }

        private void RunMessagePump()
        {
            while (!_cancellationTokenSource.IsCancellationRequested
                   && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private void DispatchWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (_cancellationTokenSource.IsCancellationRequested) return;

            OnWinEvent(hWinEventHook, eventType, hwnd, idObject, idChild, dwEventThread, dwmsEventTime);
        }

        private void ReleaseHook()
        {
            IntPtr hook = _hook;
            if (hook == IntPtr.Zero) return;

            _hook = IntPtr.Zero;
            if (!UnhookWinEvent(hook))
                TADNLog.Log($"{_threadName}: UnhookWinEvent failed");
        }

        private void RequestStop()
        {
            uint threadId = _threadId;
            if (threadId == 0) return;

            if (!PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero))
                TADNLog.Log($"{_threadName}: PostThreadMessage(WM_QUIT) failed");
        }
    }

    /// <summary>
    /// Phase 1: catches EVENT_OBJECT_CREATE for a new factory explorer.exe and hands off to phase 2.
    /// </summary>
    private sealed class ProcessMonitor(HashSet<int> existingPids)
        : WinEventMonitor("AdapterSettingsProcessMonitor", ProcessSpawnMonitorTimeoutMs)
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pin the next monitor in the chain so it can't be GC'd while its message loop runs.
        private MainWindowMonitor? _nextMonitorRef;

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
            SetWinEventHook(
                EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
                IntPtr.Zero, WinEventProc,
                0, 0, WINEVENT_OUTOFCONTEXT);

        protected override void OnHookInstalled() => _ready.TrySetResult(true);

        protected override void OnHookFailed() => _ready.TrySetResult(false);

        protected override void OnStopped() => _ready.TrySetResult(false);

        protected override void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || existingPids.Contains((int)pid)) return;

            if (!IsFactoryExplorer((int)pid)) return;

            // Found new factory explorer process - hand off to main window monitor.
            AddMonitoredPid((int)pid);

            _nextMonitorRef = new MainWindowMonitor((int)pid);
            _nextMonitorRef.Start();

            StopMessageLoop();
        }
    }

    /// <summary>
    /// Phase 2: waits for a CabinetWClass window to appear in the target process, then hands off to phase 3.
    /// </summary>
    private sealed class MainWindowMonitor(int pid)
        : WinEventMonitor("AdapterSettingsMainWindowMonitor", MainWindowMonitorTimeoutMs)
    {
        private bool _handedOff;

        // Pin the next monitor in the chain to keep it alive while its loop runs.
        private WindowDestroyMonitor? _nextMonitorRef;

        protected override IntPtr InstallHook() =>
            SetWinEventHook(
                EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW,
                IntPtr.Zero, WinEventProc,
                (uint)pid, 0, WINEVENT_OUTOFCONTEXT);

        protected override void OnHookFailed() => RemoveMonitoredPid(pid);

        protected override void OnStopped()
        {
            if (!_handedOff) RemoveMonitoredPid(pid);
        }

        protected override void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only handle CREATE and SHOW; DESTROY belongs to phase 3.
            if (eventType is not EVENT_OBJECT_CREATE and not EVENT_OBJECT_SHOW) return;
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) <= 0 || className.ToString() != TargetWindowClass) return;

            _handedOff = true;
            _nextMonitorRef = new WindowDestroyMonitor(pid, hwnd);
            _nextMonitorRef.Start();

            StopMessageLoop();
        }
    }

    /// <summary>
    /// Phase 3: waits for the CabinetWClass window to be destroyed, then kills the host process.
    /// </summary>
    private sealed class WindowDestroyMonitor(int pid, IntPtr targetHwnd)
        : WinEventMonitor("AdapterSettingsWindowDestroyMonitor", WindowDestroyMonitorTimeoutMs)
    {
        private bool _windowDestroyed;

        protected override IntPtr InstallHook() =>
            SetWinEventHook(
                EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
                IntPtr.Zero, WinEventProc,
                (uint)pid, 0, WINEVENT_OUTOFCONTEXT);

        protected override void OnStopped() => Cleanup();

        protected override void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            if (hwnd == targetHwnd)
            {
                _windowDestroyed = true;
                StopMessageLoop();
                return;
            }

            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) > 0 && className.ToString() == TargetWindowClass)
            {
                _windowDestroyed = true;
                StopMessageLoop();
            }
        }

        private void Cleanup()
        {
            RemoveMonitoredPid(pid);
            if (!_windowDestroyed) return;

            try
            {
                using Process process = Process.GetProcessById(pid);
                if (!process.HasExited) process.Kill();
            }
            catch (ArgumentException)
            {
                TADNLog.Log($"AdapterSettingsWindowDestroyMonitor.Cleanup({pid}): process already exited");
            }
            catch (Exception ex)
            {
                TADNLog.Log($"AdapterSettingsWindowDestroyMonitor.Cleanup({pid}): {ex.Message}");
            }
        }
    }
}

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
        "/factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding", // Explorer shell
    ];

    private const string TargetWindowClass = "CabinetWClass";

    private static readonly Lock _lock = new();
    private static readonly HashSet<int> _monitoredPids = [];

    public static void OpenAndMonitorControlPanel() => OpenAndMonitor("ncpa.cpl", null);

    public static void OpenAndMonitorExplorerShell() =>
        OpenAndMonitor("explorer.exe", "shell:::{7007acc7-3202-11d1-aad2-00805fc1270e}");

    private static void OpenAndMonitor(string fileName, string? arguments)
    {
        try
        {
            HashSet<int> existingPids = GetExplorerFactoryPids();

            // Also exclude PIDs we're already tracking so a second open doesn't latch onto the prior window's process.
            lock (_lock) existingPids.UnionWith(_monitoredPids);

            // Start event-driven monitoring on a dedicated thread BEFORE launching,
            // so the spawn event is always observed.
            ProcessMonitor monitor = new(existingPids);
            monitor.Start();
            monitor.WaitForReady();

            Process.Start(new ProcessStartInfo
            {
                FileName = fileName, Arguments = arguments ?? string.Empty, UseShellExecute = true,
            })?.Dispose();
        }
        catch
        {
            // best-effort
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
            catch
            {
                // ignore
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
                if (cmdLine.Contains(factoryCmd, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch
        {
            // ignore
        }

        return false;
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
    /// Phase 1: catches EVENT_OBJECT_CREATE for a new factory explorer.exe and hands off to phase 2.
    /// </summary>
    private sealed class ProcessMonitor
    {
        private readonly HashSet<int> _existingPids;
        private readonly WinEventDelegate _winEventProc;
        private readonly ManualResetEventSlim _ready = new(false);
        private IntPtr _hook;
        private uint _threadId;

        // Pin the next monitor in the chain so it can't be GC'd while its message loop runs.
        private MainWindowMonitor? _nextMonitorRef;

        public ProcessMonitor(HashSet<int> existingPids)
        {
            _existingPids = existingPids;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop) { IsBackground = true, Name = "AdapterSettingsProcessMonitor", }.Start();
        }

        public void WaitForReady() => _ready.Wait();

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                _hook = SetWinEventHook(
                    EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
                    IntPtr.Zero, _winEventProc,
                    0, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    _ready.Set();
                    return;
                }

                _ready.Set();

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _ready.Set();
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }

                _ready.Dispose();
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || _existingPids.Contains((int)pid)) return;

            if (!IsFactoryExplorer((int)pid)) return;

            // Found new factory explorer process - hand off to main window monitor.
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            AddMonitoredPid((int)pid);

            _nextMonitorRef = new MainWindowMonitor((int)pid);
            _nextMonitorRef.Start();

            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Phase 2: waits for a CabinetWClass window to appear in the target process, then hands off to phase 3.
    /// </summary>
    private sealed class MainWindowMonitor
    {
        private readonly int _pid;
        private readonly WinEventDelegate _winEventProc;
        private IntPtr _hook;
        private uint _threadId;

        // Pin the next monitor in the chain to keep it alive while its loop runs.
        private WindowDestroyMonitor? _nextMonitorRef;

        public MainWindowMonitor(int pid)
        {
            _pid = pid;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop) { IsBackground = true, Name = "AdapterSettingsMainWindowMonitor", }.Start();
        }

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                _hook = SetWinEventHook(
                    EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW,
                    IntPtr.Zero, _winEventProc,
                    (uint)_pid, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    RemoveMonitoredPid(_pid);
                    return;
                }

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only handle CREATE and SHOW; DESTROY belongs to phase 3.
            if (eventType != EVENT_OBJECT_CREATE && eventType != EVENT_OBJECT_SHOW) return;
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) <= 0 || className.ToString() != TargetWindowClass) return;

            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            _nextMonitorRef = new WindowDestroyMonitor(_pid);
            _nextMonitorRef.Start();

            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Phase 3: waits for the CabinetWClass window to be destroyed, then kills the host process.
    /// </summary>
    private sealed class WindowDestroyMonitor
    {
        private readonly int _pid;
        private readonly WinEventDelegate _winEventProc;
        private IntPtr _hook;
        private uint _threadId;

        public WindowDestroyMonitor(int pid)
        {
            _pid = pid;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop) { IsBackground = true, Name = "AdapterSettingsWindowDestroyMonitor", }.Start();
        }

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                _hook = SetWinEventHook(
                    EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
                    IntPtr.Zero, _winEventProc,
                    (uint)_pid, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    Cleanup();
                    return;
                }

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                Cleanup();
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;

            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) > 0 && className.ToString() == TargetWindowClass)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        private void Cleanup()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            RemoveMonitoredPid(_pid);

            try
            {
                using Process process = Process.GetProcessById(_pid);
                if (!process.HasExited) process.Kill();
            }
            catch
            {
                // process already exited
            }
        }
    }
}

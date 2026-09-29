using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessStatusTests
{
    private const string HungWindowTestEnvironmentVariable = "TASK_MANAGER_RUN_HUNG_WINDOW_TEST";
    private const long WindowStyleExtendedToolWindow = 0x00000080L;
    private const long WindowStyleExtendedApplicationWindow = 0x00040000L;
    private const long WindowStyleExtendedNoActivate = 0x08000000L;
    private const int PollIntervalMilliseconds = 100;

    // ProcessStatus is internal, so theory data names statuses instead of passing them
    [Theory]
    [InlineData(false, false, false, nameof(ProcessStatus.None))]
    [InlineData(false, false, true, nameof(ProcessStatus.EfficiencyMode))]
    [InlineData(false, true, false, nameof(ProcessStatus.NotResponding))]
    [InlineData(false, true, true, nameof(ProcessStatus.NotResponding))]
    [InlineData(true, false, false, nameof(ProcessStatus.Suspended))]
    [InlineData(true, false, true, nameof(ProcessStatus.EfficiencyMode))]
    [InlineData(true, true, false, nameof(ProcessStatus.Suspended))]
    [InlineData(true, true, true, nameof(ProcessStatus.EfficiencyMode))]
    public void ResolveFollowsTaskManagerPrecedence(
        bool isSuspended,
        bool isNotResponding,
        bool isEfficiencyMode,
        string expectedStatusName)
    {
        ProcessStatus status = ProcessStatusFunctions.Resolve(isSuspended, isNotResponding, isEfficiencyMode);

        Assert.Equal(Enum.Parse<ProcessStatus>(expectedStatusName), status);
    }

    [Theory]
    [InlineData(nameof(ProcessStatus.Suspended), "Suspended")]
    [InlineData(nameof(ProcessStatus.EfficiencyMode), "Efficiency mode")]
    [InlineData(nameof(ProcessStatus.NotResponding), "Not responding")]
    public void StatusTextUsesTaskManagerWordingAndMapsBack(string statusName, string expectedText)
    {
        ProcessStatus status = Enum.Parse<ProcessStatus>(statusName);

        string text = ProcessStatusFunctions.GetText(status);

        Assert.Equal(expectedText, text);
        Assert.True(ProcessStatusFunctions.TryGetStatus(text, out ProcessStatus parsedStatus));
        Assert.Equal(status, parsedStatus);
    }

    [Fact]
    public void RunningProcessesShowNoStatusText()
    {
        Assert.Empty(ProcessStatusFunctions.GetText(ProcessStatus.None));
        Assert.False(ProcessStatusFunctions.TryGetStatus(string.Empty, out _));
        Assert.False(ProcessStatusFunctions.TryGetStatus("Running", out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(long.MaxValue)]
    public void UnknownStoredValuesShowNoStatus(long storedValue) =>
        Assert.Equal(ProcessStatus.None, ProcessStatusFunctions.FromStoredValue(storedValue));

    [Fact]
    public void AscendingSortListsUrgentStatusesFirstAndRowsWithoutStatusLast()
    {
        ProcessStatus[] statuses =
        [
            ProcessStatus.None,
            ProcessStatus.Suspended,
            ProcessStatus.NotResponding,
            ProcessStatus.EfficiencyMode
        ];

        ProcessStatus[] sorted = statuses.OrderBy(ProcessStatusFunctions.GetSortOrder).ToArray();

        Assert.Equal(
            [
                ProcessStatus.NotResponding,
                ProcessStatus.EfficiencyMode,
                ProcessStatus.Suspended,
                ProcessStatus.None
            ],
            sorted);
    }

    [Theory]
    [InlineData(0L, false, false, false, true)]
    [InlineData(WindowStyleExtendedToolWindow, false, false, false, false)]
    [InlineData(WindowStyleExtendedToolWindow | WindowStyleExtendedApplicationWindow, false, false, false, true)]
    [InlineData(WindowStyleExtendedNoActivate, false, false, false, false)]
    [InlineData(WindowStyleExtendedNoActivate | WindowStyleExtendedApplicationWindow, false, false, false, true)]
    [InlineData(0L, true, false, false, false)]
    [InlineData(WindowStyleExtendedApplicationWindow, true, false, false, true)]
    [InlineData(WindowStyleExtendedApplicationWindow, false, true, false, false)]
    [InlineData(0L, false, false, true, false)]
    public void UserInteractiveWindowRuleMatchesTaskManager(
        long extendedStyle,
        bool hasVisibleOwner,
        bool isTaskbar,
        bool hasGhostWindow,
        bool expectedIncluded)
    {
        bool included = ProcessHungWindowCollector.IsUserInteractiveWindow(
            extendedStyle,
            hasVisibleOwner,
            isTaskbar,
            hasGhostWindow);

        Assert.Equal(expectedIncluded, included);
    }

    [Fact]
    public void HungWindowCaptureEnumeratesTheDesktopWithoutFlaggingThisProcess()
    {
        ProcessHungWindowCollector collector = new();

        collector.Capture();

        Assert.False(collector.IsNotResponding(Environment.ProcessId));
    }

    [Fact]
    public void EfficiencyModeAndPowerThrottlingRequireBothEcoQoSAndIdlePriority()
    {
        using Process process = StartSleepingProcess();
        try
        {
            AssertEfficiencyMode(process.Handle, isExpected: false);

            process.PriorityClass = ProcessPriorityClass.Idle;
            AssertEfficiencyMode(process.Handle, isExpected: false);

            SetExecutionSpeedThrottling(process.Handle, isThrottled: true);
            AssertEfficiencyMode(process.Handle, isExpected: true);

            process.PriorityClass = ProcessPriorityClass.Normal;
            AssertEfficiencyMode(process.Handle, isExpected: false);

            process.PriorityClass = ProcessPriorityClass.Idle;
            SetExecutionSpeedThrottling(process.Handle, isThrottled: false);
            AssertEfficiencyMode(process.Handle, isExpected: false);
        }
        finally
        {
            StopProcess(process);
        }
    }

    /// <summary>Checks the Status reader and the Power throttling column, which Task Manager derives from one state.</summary>
    private static void AssertEfficiencyMode(IntPtr processHandle, bool isExpected)
    {
        Assert.Equal(isExpected, NativeProcessInfo.ReadIsEfficiencyMode(processHandle));
        Assert.Equal(
            isExpected ? ProcessDisplayCode.Enabled : ProcessDisplayCode.Disabled,
            NativeProcessInfo.ReadPowerThrottling(processHandle));
    }

    [Fact]
    public void SuspendedProcessIsDetectedFromItsThreadWaitReasons()
    {
        using Process process = StartSleepingProcess();
        using SystemProcessSnapshot snapshot = new();
        try
        {
            ThrowIfFailed(NtSuspendProcess(process.Handle));
            Assert.True(WaitForExecutionState(snapshot, process.Id, ProcessExecutionState.Suspended));

            ThrowIfFailed(NtResumeProcess(process.Handle));
            Assert.True(WaitForExecutionState(snapshot, process.Id, ProcessExecutionState.Running));
        }
        finally
        {
            StopProcess(process);
        }
    }

    [Fact]
    public void HungWindowMarksItsOwningProcessNotResponding()
    {
        // Opt-in because it shows an off-screen window with a taskbar button for several seconds
        if (!string.Equals(
                Environment.GetEnvironmentVariable(HungWindowTestEnvironmentVariable),
                b: "1",
                StringComparison.Ordinal))
            return;

        ProcessHungWindowCollector collector = new();
        using HungTestWindow window = new();
        bool isNotResponding = false;
        Stopwatch stopwatch = Stopwatch.StartNew();

        // IsHungAppWindow reports a thread that has not checked its queue for five seconds
        while (!isNotResponding && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(PollIntervalMilliseconds);
            collector.Capture();
            isNotResponding = collector.IsNotResponding(Environment.ProcessId);
        }

        Assert.True(isNotResponding);

        // A hung query ghosts the window; the ghost belongs to dwm.exe but must charge this process
        IntPtr ghostWindowHandle = IntPtr.Zero;
        stopwatch.Restart();
        while (ghostWindowHandle == IntPtr.Zero && stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            ghostWindowHandle = GhostWindowFromHungWindow(window.Handle);
            if (ghostWindowHandle == IntPtr.Zero) Thread.Sleep(PollIntervalMilliseconds);
        }

        Assert.NotEqual(IntPtr.Zero, ghostWindowHandle);
        _ = GetWindowThreadProcessId(ghostWindowHandle, out uint ghostProcessID);
        Assert.NotEqual((uint)Environment.ProcessId, ghostProcessID);
        stopwatch.Restart();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            Thread.Sleep(PollIntervalMilliseconds);
            collector.Capture();
            Assert.True(collector.IsNotResponding(Environment.ProcessId));
            Assert.False(collector.IsNotResponding((int)ghostProcessID));
        }

        window.Release();
        stopwatch.Restart();
        while (isNotResponding && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            Thread.Sleep(PollIntervalMilliseconds);
            collector.Capture();
            isNotResponding = collector.IsNotResponding(Environment.ProcessId);
        }

        Assert.False(isNotResponding);
    }

    private static bool WaitForExecutionState(
        SystemProcessSnapshot snapshot,
        int processID,
        ProcessExecutionState expectedState)
    {
        Dictionary<int, SystemProcessData> processes = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (snapshot.TryCapture(processes)
                && processes.TryGetValue(processID, out SystemProcessData process)
                && snapshot.ReadExecutionState(process) == expectedState)
                return true;

            Thread.Sleep(PollIntervalMilliseconds);
        }

        return false;
    }

    private static Process StartSleepingProcess()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, path2: "ping.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("The status test could not start ping.exe.");
    }

    private static void StopProcess(Process process)
    {
        if (process.HasExited) return;

        process.Kill();
        _ = process.WaitForExit(5_000);
    }

    private static void SetExecutionSpeedThrottling(IntPtr processHandle, bool isThrottled)
    {
        const int ProcessPowerThrottling = 4;
        const uint ExecutionSpeed = 0x1;
        PowerThrottlingState state = new()
        {
            Version = 1,
            ControlMask = ExecutionSpeed,
            StateMask = isThrottled ? ExecutionSpeed : 0
        };
        if (!SetProcessInformation(
                processHandle,
                ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<PowerThrottlingState>()))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static void ThrowIfFailed(int status)
    {
        if (status < 0)
            throw new InvalidOperationException($"The native call failed with NTSTATUS 0x{status:X8}.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr process,
        int informationClass,
        ref PowerThrottlingState information,
        uint informationSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GhostWindowFromHungWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processID);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>Creates a visible top-level window whose thread stops reading its queue until released.</summary>
    private sealed class HungTestWindow : IDisposable
    {
        private const int OffscreenPosition = -32_000;
        private const uint WindowStyleOverlapped = 0x00CF0000;
        private const uint WindowStyleVisible = 0x10000000;
        private const uint PeekMessageRemove = 0x0001;

        private readonly ManualResetEventSlim _windowCreated = new(false);
        private readonly ManualResetEventSlim _released = new(false);
        private readonly Thread _windowThread;
        private Exception? _creationException;
        private IntPtr _windowHandle;
        private bool _disposed;

        public HungTestWindow()
        {
            _windowThread = new Thread(RunWindowThread)
            {
                IsBackground = true, Name = "Process status hung-window test"
            };

            // A CLR wait on an STA thread pumps messages, which would keep the window responsive
            _windowThread.SetApartmentState(ApartmentState.MTA);
            _windowThread.Start();
            if (!_windowCreated.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The hung test window was not created in time.");
            if (_creationException != null)
                throw new InvalidOperationException("The hung test window could not be created.", _creationException);
        }

        public IntPtr Handle => _windowHandle;

        public void Release() => _released.Set();

        private void RunWindowThread()
        {
            try
            {
                _windowHandle = CreateWindowExW(
                    extendedStyle: 0,
                    className: "STATIC",
                    windowName: "Task Manager hung-window status test",
                    WindowStyleOverlapped | WindowStyleVisible,
                    OffscreenPosition,
                    OffscreenPosition,
                    width: 200,
                    height: 80,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (_windowHandle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                PumpPendingMessages();
            }
            catch (Exception exception)
            {
                _creationException = exception;
            }
            finally
            {
                _windowCreated.Set();
            }

            if (_windowHandle == IntPtr.Zero) return;

            // Blocking without reading the queue is exactly what IsHungAppWindow detects
            _released.Wait();
            while (!Volatile.Read(ref _disposed))
            {
                PumpPendingMessages();
                Thread.Sleep(PollIntervalMilliseconds);
            }

            _ = DestroyWindow(_windowHandle);
        }

        private static void PumpPendingMessages()
        {
            while (PeekMessageW(out WindowMessage message, IntPtr.Zero, 0, 0, PeekMessageRemove))
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed)) return;

            Volatile.Write(ref _disposed, true);
            _released.Set();
            _ = _windowThread.Join(TimeSpan.FromSeconds(5));
            _windowCreated.Dispose();
            _released.Dispose();
        }

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentWindow,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(
            out WindowMessage message,
            IntPtr windowHandle,
            uint messageFilterMinimum,
            uint messageFilterMaximum,
            uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref WindowMessage message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static extern IntPtr DispatchMessageW(ref WindowMessage message);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowMessage
        {
            public IntPtr WindowHandle;
            public uint Message;
            public UIntPtr WordParameter;
            public IntPtr LongParameter;
            public uint Time;
            public WindowPoint Point;
            public uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPoint
        {
            public int X;
            public int Y;
        }
    }
}

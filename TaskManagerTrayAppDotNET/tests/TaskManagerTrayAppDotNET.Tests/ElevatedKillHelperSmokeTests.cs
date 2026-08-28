using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ElevatedKillHelperSmokeTests
{
    private const string SmokeTestEnvironmentVariable = "TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST";

    [Fact]
    public void ElevatedHelperTerminatesThroughTheSharedMailbox()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(SmokeTestEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        List<string> logMessages = new List<string>();
        using TemporaryVisibleOwnerWindow ownerWindow = new();
        Assert.NotEqual(IntPtr.Zero, ownerWindow.Handle);
        ElevatedKillHelperStartResult startResult = ElevatedKillHelperClient.TryStart(
            ownerWindow.Handle,
            logMessages.Add);
        Assert.Equal(ElevatedKillHelperStartOutcome.Ready, startResult.Outcome);
        using ElevatedKillHelperSession helperSession = Assert.IsType<ElevatedKillHelperSession>(startResult.Session);
        Assert.Equal(
            KillHelperProtocol.RequiredHardeningFlags,
            helperSession.HardeningFlags & KillHelperProtocol.RequiredHardeningFlags);

        using Process process = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget target = new(process.Id, process.StartTime.ToFileTimeUtc());
            Assert.True(helperSession.TryArm(target, generation: 1));
            Assert.True(helperSession.TryRequestTermination(target, generation: 1, out long requestSequence));
            Assert.True(helperSession.TryWaitForResponse(
                requestSequence,
                timeoutMilliseconds: 5_000,
                out int result,
                out int errorCode));
            Assert.Equal(KillHelperProtocol.ResultSuccess, result);
            Assert.Equal(0, errorCode);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }

        using Process identityMismatchProcess = StartSleepingProcess();
        try
        {
            ProcessTerminationTarget mismatchedTarget = new(
                identityMismatchProcess.Id,
                identityMismatchProcess.StartTime.ToFileTimeUtc() + 1);
            Assert.True(helperSession.TryArm(mismatchedTarget, generation: 2));
            Assert.True(helperSession.TryRequestTermination(
                mismatchedTarget,
                generation: 2,
                out long requestSequence));
            Assert.True(helperSession.TryWaitForResponse(
                requestSequence,
                timeoutMilliseconds: 5_000,
                out int result,
                out int errorCode));
            Assert.Equal(KillHelperProtocol.ResultIdentityMismatch, result);
            Assert.NotEqual(0, errorCode);
            Assert.False(identityMismatchProcess.HasExited);
        }
        finally
        {
            if (!identityMismatchProcess.HasExited)
                identityMismatchProcess.Kill();
        }
    }

    private static Process StartSleepingProcess()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("The helper smoke test could not start ping.exe.");
    }

    private sealed class TemporaryVisibleOwnerWindow : IDisposable
    {
        private const int CreateUseDefault = unchecked((int)0x80000000);
        private const int ShowWindowNormal = 5;
        private const uint WindowStyleOverlapped = 0x00CF0000;
        private const uint WindowStyleVisible = 0x10000000;
        private const uint WindowMessageQuit = 0x0012;

        private readonly ManualResetEventSlim _windowCreated = new(false);
        private readonly Thread _windowThread;
        private Exception? _creationException;
        private uint _windowThreadID;
        private bool _disposed;

        public TemporaryVisibleOwnerWindow()
        {
            _windowThread = new Thread(RunWindowMessageLoop)
            {
                IsBackground = true,
                Name = "Elevated helper smoke test owner window"
            };
            _windowThread.SetApartmentState(ApartmentState.STA);
            _windowThread.Start();
            if (!_windowCreated.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The helper smoke test owner window was not created in time.");
            if (_creationException != null)
                throw new InvalidOperationException(
                    "The helper smoke test owner window could not be created.",
                    _creationException);
        }

        public IntPtr Handle { get; private set; }

        private void RunWindowMessageLoop()
        {
            _windowThreadID = GetCurrentThreadID();
            try
            {
                Handle = CreateWindowExW(
                    0,
                    "STATIC",
                    "Task Manager elevated helper smoke test",
                    WindowStyleOverlapped | WindowStyleVisible,
                    CreateUseDefault,
                    CreateUseDefault,
                    360,
                    120,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (Handle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                _ = ShowWindow(Handle, ShowWindowNormal);
                _ = UpdateWindow(Handle);
            }
            catch (Exception exception)
            {
                _creationException = exception;
            }
            finally
            {
                _windowCreated.Set();
            }

            if (Handle == IntPtr.Zero) return;

            while (GetMessageW(out WindowMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }

            _ = DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_windowThreadID != 0)
                _ = PostThreadMessageW(_windowThreadID, WindowMessageQuit, UIntPtr.Zero, IntPtr.Zero);
            _ = _windowThread.Join(TimeSpan.FromSeconds(5));
            _windowCreated.Dispose();
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

        [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        private static extern int GetMessageW(
            out WindowMessage message,
            IntPtr windowHandle,
            uint messageFilterMinimum,
            uint messageFilterMaximum);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref WindowMessage message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static extern IntPtr DispatchMessageW(ref WindowMessage message);

        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessageW(
            uint threadID,
            uint message,
            UIntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr windowHandle, int commandShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateWindow(IntPtr windowHandle);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint GetCurrentThreadID();

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

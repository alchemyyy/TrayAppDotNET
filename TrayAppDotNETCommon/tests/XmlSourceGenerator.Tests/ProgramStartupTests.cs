using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ProgramStartupTests
{
    [Fact]
    public void LocalInstallDoesNotStopUnrelatedProcessesBeforeServiceRuns()
    {
        string uniqueID = Guid.NewGuid().ToString("N");
        string applicationName = $"TrayAppDotNETInstallTest{uniqueID}";
        SingleInstanceIdentity identity = new(applicationName, uniqueID);
        using FakeRunningApplication runningApplication = new(identity);
        List<string> logMessages = [];
        bool unrelatedProcessesWereRunning = false;

        TrayAppDotNETProgramOptions options = new(
            applicationName,
            SharedRootFolderName: "TrayAppDotNETInstallTests",
            uniqueID,
            static _ => throw new InvalidOperationException("Application mode was not expected."),
            static (_, _, _, _) => throw new InvalidOperationException("Admin install mode was not expected."),
            static (_, _) => { },
            static (_, _, _) => throw new InvalidOperationException("Uninstall preparation was not expected."),
            static (_, _, _) => throw new InvalidOperationException("Headless uninstall was not expected."),
            (_, _) =>
            {
                unrelatedProcessesWereRunning = !runningApplication.ProcessesExited;
                return new TrayAppDotNETProgramInstallResult(
                    unrelatedProcessesWereRunning,
                    unrelatedProcessesWereRunning ? null : "Unrelated processes were stopped.");
            },
            static (_, _) => throw new InvalidOperationException("System install mode was not expected."),
            () => Path.Combine(Path.GetTempPath(), applicationName + ".exe"),
            () => Path.Combine(Path.GetTempPath(), applicationName + "-system.exe"),
            logMessages.Add);

        int exitCode = TrayAppDotNETProgram.RunInstall(scope: "local", options, logMessages.Add, startInstalled: false);

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, logMessages));
        Assert.True(unrelatedProcessesWereRunning);
        Assert.False(runningApplication.ProcessesExited);
    }

    [Fact]
    public void CancelledSystemInstallLeavesRunningApplicationUntouched()
    {
        string uniqueID = Guid.NewGuid().ToString("N");
        string applicationName = $"TrayAppDotNETInstallTest{uniqueID}";
        SingleInstanceIdentity identity = new(applicationName, uniqueID);
        using FakeRunningApplication runningApplication = new(identity);
        List<string> logMessages = [];
        bool systemInstallInvoked = false;

        TrayAppDotNETProgramOptions options = new(
            applicationName,
            SharedRootFolderName: "TrayAppDotNETInstallTests",
            uniqueID,
            static _ => throw new InvalidOperationException("Application mode was not expected."),
            static (_, _, _, _) => throw new InvalidOperationException("Admin install mode was not expected."),
            static (_, _) => { },
            static (_, _, _) => throw new InvalidOperationException("Uninstall preparation was not expected."),
            static (_, _, _) => throw new InvalidOperationException("Headless uninstall was not expected."),
            static (_, _) => throw new InvalidOperationException("Local install mode was not expected."),
            (_, _) =>
            {
                systemInstallInvoked = true;
                return new TrayAppDotNETProgramInstallResult(
                    Success: false,
                    ErrorMessage: null,
                    UserCancelled: true);
            },
            () => Path.Combine(Path.GetTempPath(), applicationName + ".exe"),
            () => Path.Combine(Path.GetTempPath(), applicationName + "-system.exe"),
            logMessages.Add);

        int exitCode =
            TrayAppDotNETProgram.RunInstall(scope: "system", options, logMessages.Add, startInstalled: false);

        Assert.Equal(expected: 1, exitCode);
        Assert.True(systemInstallInvoked);
        Assert.False(runningApplication.ProcessesExited);
    }

    [Fact]
    public void ForwardedLaunchArgumentsKeepOnlyTheLaunchFlagsInAFixedOrder()
    {
        string[] args = ["--hidden", "--monitored", "--watcher-pid", "12", "--AUTOSTART"];
        string[] expected = [TrayAppDotNETProgram.AutostartArgument, TrayAppDotNETProgram.HiddenArgument];

        IReadOnlyList<string> forwarded = TrayAppDotNETProgram.SelectForwardedLaunchArguments(args, settingsDirectory: null);

        Assert.Equal(expected, forwarded);
    }

    [Fact]
    public void ForwardedLaunchArgumentsAreEmptyForAManualStart()
    {
        Assert.Empty(TrayAppDotNETProgram.SelectForwardedLaunchArguments([], settingsDirectory: null));
        Assert.Empty(TrayAppDotNETProgram.SelectForwardedLaunchArguments(
            ["--monitored", "--watcher-pid", "12"],
            settingsDirectory: null));
    }

    [Fact]
    public void ForwardedLaunchArgumentsCarryTheResolvedSettingsDirectoryLast()
    {
        const string SettingsDirectory = @"D:\Portable Settings\Volume";
        string[] args = ["--settings-dir", @"relative\folder", "--hidden"];
        string[] expected =
        [
            TrayAppDotNETProgram.HiddenArgument,
            TrayAppDotNETProgram.SettingsDirectoryArgument,
            SettingsDirectory
        ];

        IReadOnlyList<string> forwarded = TrayAppDotNETProgram.SelectForwardedLaunchArguments(args, SettingsDirectory);

        Assert.Equal(expected, forwarded);
    }

    [Fact]
    public void SettingsDirectoryIsAbsentWithoutTheArgument()
    {
        bool resolved = TrayAppDotNETProgram.TryResolveSettingsDirectory(
            ["--hidden"],
            out string? settingsDirectory,
            out string? errorMessage);

        Assert.True(resolved);
        Assert.Null(settingsDirectory);
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData(@"D:\Settings\Volume", @"D:\Settings\Volume")]
    [InlineData(@"D:\Settings\Volume\", @"D:\Settings\Volume")]
    [InlineData("D:\\Settings\\Volume\"", @"D:\Settings\Volume")]
    [InlineData(@"D:\Settings\..\Other", @"D:\Other")]
    [InlineData(@"D:\", @"D:\")]
    public void SettingsDirectoryResolvesToAnAbsoluteFolder(string value, string expected)
    {
        bool resolved = TrayAppDotNETProgram.TryResolveSettingsDirectory(
            ["--SETTINGS-DIR", value],
            out string? settingsDirectory,
            out string? errorMessage);

        Assert.True(resolved, errorMessage);
        Assert.Equal(expected, settingsDirectory);
    }

    [Fact]
    public void RelativeSettingsDirectoryResolvesAgainstTheWorkingDirectory()
    {
        string expected = Path.Combine(Environment.CurrentDirectory, path2: "portable", path3: "settings");

        bool resolved = TrayAppDotNETProgram.TryResolveSettingsDirectory(
            ["--settings-dir", @"portable\settings"],
            out string? settingsDirectory,
            out string? errorMessage);

        Assert.True(resolved, errorMessage);
        Assert.Equal(expected, settingsDirectory);
    }

    [Fact]
    public void SettingsDirectoryExpandsEnvironmentVariables()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            path2: "Custom");

        bool resolved = TrayAppDotNETProgram.TryResolveSettingsDirectory(
            ["--settings-dir", @"%LOCALAPPDATA%\Custom"],
            out string? settingsDirectory,
            out string? errorMessage);

        Assert.True(resolved, errorMessage);
        Assert.Equal(expected, settingsDirectory);
    }

    [Theory]
    [InlineData("--settings-dir")]
    [InlineData("--settings-dir", "")]
    [InlineData("--settings-dir", "  ")]
    [InlineData("--settings-dir", "--hidden")]
    [InlineData("--settings-dir", "my settings\" --hidden")]
    [InlineData("--settings-dir", @"D:\Settings\<name>")]
    [InlineData("--settings-dir", @"D:\Settings\*")]
    [InlineData("--settings-dir", "D:\\Settings\tTab")]
    public void SettingsDirectoryWithoutAUsablePathIsRejected(params string[] args)
    {
        bool resolved = TrayAppDotNETProgram.TryResolveSettingsDirectory(
            args,
            out string? settingsDirectory,
            out string? errorMessage);

        Assert.False(resolved);
        Assert.Null(settingsDirectory);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Theory]
    [InlineData(@"--hidden", @"--hidden")]
    [InlineData(@"D:\Settings", @"D:\Settings")]
    [InlineData(@"D:\Portable Settings", "\"D:\\Portable Settings\"")]
    [InlineData(@"D:\Portable Settings\", "\"D:\\Portable Settings\\\\\"")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    public void CommandLineArgumentsAreQuotedForCommandLineToArgv(string argument, string expected)
    {
        Assert.Equal(expected, TrayAppDotNETProgram.QuoteCommandLineArgument(argument));
    }

    private sealed class FakeRunningApplication : IDisposable
    {
        private const int OwnerReadyTimeoutMs = 5_000;
        private const int OwnerShutdownTimeoutMs = 10_000;

        private readonly SingleInstanceIdentity _identity;
        private readonly Process _watcherProcess;
        private readonly Process _monitoredProcess;
        private readonly ManualResetEventSlim _ownerReady = new(false);
        private readonly Thread _ownerThread;
        private Exception? _ownerException;
        private bool _processesExited;
        private bool _disposed;

        public FakeRunningApplication(SingleInstanceIdentity identity)
        {
            _identity = identity;
            _watcherProcess = StartSleeper();
            _monitoredProcess = StartSleeper();
            _ownerThread = new Thread(OwnSingleInstance) { IsBackground = true };
            _ownerThread.Start();

            if (!_ownerReady.Wait(OwnerReadyTimeoutMs))
                throw new TimeoutException("The fake watcher did not publish its PID bulletin.");

            ThrowOwnerException();
        }

        public bool ProcessesExited => Volatile.Read(ref _processesExited);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            TryKill(_watcherProcess);
            TryKill(_monitoredProcess);
            _ownerThread.Join(OwnerShutdownTimeoutMs);
            _watcherProcess.Dispose();
            _monitoredProcess.Dispose();
            _ownerReady.Dispose();
        }

        private void OwnSingleInstance()
        {
            try
            {
                using SingleInstanceCoordinator coordinator = SingleInstanceCoordinator.AcquireOrTakeover(
                    _identity,
                    _watcherProcess.Id,
                    _monitoredProcess.Id);
                _ownerReady.Set();

                bool watcherExited = _watcherProcess.WaitForExit(OwnerShutdownTimeoutMs);
                bool monitoredExited = _monitoredProcess.WaitForExit(OwnerShutdownTimeoutMs);
                if (!watcherExited || !monitoredExited)
                    throw new TimeoutException("The install coordinator did not stop both recorded processes.");

                Volatile.Write(ref _processesExited, value: true);
            }
            catch (Exception exception)
            {
                _ownerException = exception;
                _ownerReady.Set();
            }
        }

        private void ThrowOwnerException()
        {
            if (_ownerException != null)
                ExceptionDispatchInfo.Capture(_ownerException).Throw();
        }

        private static Process StartSleeper()
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = Path.Combine(Environment.SystemDirectory, path2: "ping.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("127.0.0.1");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("61");
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("1000");
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Failed to start a fake watcher process.");
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(OwnerShutdownTimeoutMs);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
        }
    }
}

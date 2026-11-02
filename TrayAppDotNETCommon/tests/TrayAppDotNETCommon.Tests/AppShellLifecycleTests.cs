using System.Diagnostics;
using System.Resources;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;
using AppThemeBase = TrayAppDotNETCommon.Visuals.AppTheme;

namespace TrayAppDotNETCommon.Tests;

/// <summary>Lifecycle behavior of the shared app shell: exit paths, exit requests, crash policy, logging, watcher.</summary>
public sealed class AppShellLifecycleTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ExitRequestIsDeliveredToTheListeningProcess()
    {
        using ManualResetEventSlim requested = new();
        using TrayAppDotNETExitRequestListener listener = TrayAppDotNETExitRequest.Listen(requested.Set);
        TrayAppDotNETProcessIdentity self = Assert.IsType<TrayAppDotNETProcessIdentity>(
            TrayAppDotNETProcessIdentity.TryCapture(Environment.ProcessId));

        Assert.True(TrayAppDotNETExitRequest.TryRequest(self));
        Assert.True(requested.Wait(WaitTimeout));
    }

    [Fact]
    public void ProcessThatIgnoresTheExitRequestIsKilledAfterTheTimeout()
    {
        using Process process = StartIdleProcess();
        TrayAppDotNETProcessIdentity identity = Assert.IsType<TrayAppDotNETProcessIdentity>(
            TrayAppDotNETProcessIdentity.TryCapture(process.Id));

        IReadOnlyList<TrayAppDotNETProcessIdentity> remaining = TrayAppDotNETExitRequest.StopProcesses(
            [identity],
            gracefulTimeoutMs: 200,
            killEntireProcessTree: false);

        Assert.Empty(remaining);
        Assert.True(process.WaitForExit((int)WaitTimeout.TotalMilliseconds));
    }

    [Fact]
    public void KillIsRefusedWhenTheProcessIdNoLongerNamesTheCapturedProcess()
    {
        using Process process = StartIdleProcess();
        try
        {
            TrayAppDotNETProcessIdentity captured = Assert.IsType<TrayAppDotNETProcessIdentity>(
                TrayAppDotNETProcessIdentity.TryCapture(process.Id));
            // Same ID and image, different start time: a reused process ID
            TrayAppDotNETProcessIdentity stale = captured with { StartTimeUtc = captured.StartTimeUtc.AddMinutes(-1) };

            TrayAppDotNETExitRequest.StopProcesses([stale], gracefulTimeoutMs: 0, killEntireProcessTree: false);

            Assert.False(process.HasExited);
        }
        finally
        {
            process.Kill();
            process.WaitForExit();
        }
    }

    [Fact]
    public void UnobservedTaskExceptionIsLoggedInFullAndObservedWithoutExiting()
    {
        List<string> lines = [];
        CrashHandlerOptions options = new(
            "Test",
            new SingleInstanceIdentity("Test", Guid.NewGuid().ToString("N")),
            lines.Add,
            static () => { });
        Exception thrown = CaptureThrown(() => throw new InvalidOperationException(
            "outer",
            new TimeoutException("inner cause")));
        UnobservedTaskExceptionEventArgs args = new(new AggregateException(thrown));

        CrashHandler.LogUnobservedTaskException(options, args);

        Assert.True(args.Observed);
        string line = Assert.Single(lines);
        Assert.Contains("inner cause", line, StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThrown), line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, nameof(WatcherExitAction.Stop))]
    [InlineData(1, false, nameof(WatcherExitAction.Stop))]
    [InlineData(CrashHandler.FatalExceptionExitCode, false, nameof(WatcherExitAction.RestartAfterCrash))]
    [InlineData(CrashHandler.SessionEndingExitCode, true, nameof(WatcherExitAction.RelaunchIfSessionContinues))]
    // The C runtime's abort also exits with 3; outside a session end it is a crash
    [InlineData(CrashHandler.SessionEndingExitCode, false, nameof(WatcherExitAction.RestartAfterCrash))]
    [InlineData(-1073741819, true, nameof(WatcherExitAction.RestartAfterCrash))]
    public void WatcherClassifiesExitCodes(int exitCode, bool sessionShuttingDown, string expected) =>
        Assert.Equal(expected, CrashHandler.ClassifyExitCode(exitCode, sessionShuttingDown).ToString());

    [Fact]
    public void RapidRelaunchesReachTheRestartLimit()
    {
        Span<long> restartTimes = stackalloc long[CrashHandler.MaxRapidRestarts];
        int restartCount = 0;

        for (int relaunch = 0; relaunch < CrashHandler.MaxRapidRestarts; relaunch++)
            CrashHandler.RecordRestart(restartTimes, ref restartCount, now: 1_000 + relaunch * 5_000, windowMs: 30_000);

        Assert.Equal(CrashHandler.MaxRapidRestarts, restartCount);
    }

    [Fact]
    public void WatcherWaitsUntilWindowsNoLongerReportsTheSessionEnding()
    {
        Queue<bool> shuttingDown = new([true, true, false]);
        List<int> sleeps = [];

        CrashHandler.WaitForSessionToContinue(shuttingDown.Dequeue, sleeps.Add);

        Assert.Empty(shuttingDown);
        Assert.Equal(
            [
                TimeConstants.SessionEndRelaunchDelayMs,
                TimeConstants.SessionEndShutdownPollIntervalMs,
                TimeConstants.SessionEndShutdownPollIntervalMs
            ],
            sleeps);
    }

    [Fact]
    public void ConcurrentFlushesLoseNoLines()
    {
        const int ThreadCount = 8;
        const int LinesPerThread = 500;
        using TempDirectory temp = new();
        string directory = temp.Path;
        TADNLogFile file = new(Path.Combine(directory, "active.log"), Path.Combine(directory, "old.log"));
        Parallel.For(0, ThreadCount, thread =>
        {
            for (int line = 0; line < LinesPerThread; line++)
            {
                file.Enqueue($"thread {thread} line {line}\n");
                if (line % 25 == 0) file.Flush();
            }
        });
        file.Flush();

        Assert.Equal(ThreadCount * LinesPerThread, File.ReadAllLines(file.ActivePath).Length);
    }

    [Fact]
    public void LinesFromAFailedWriteAreKeptForTheNextFlush()
    {
        using TempDirectory temp = new();
        string directory = temp.Path;
        TADNLogFile file = new(Path.Combine(directory, "active.log"), Path.Combine(directory, "old.log"));
        using (new FileStream(file.ActivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            file.Enqueue("written later\n");
            file.Flush();
        }

        file.Flush();

        Assert.Equal(["written later"], File.ReadAllLines(file.ActivePath));
    }

    [Fact]
    public void HelperArgumentsCarryTheSettingsFolderToTheHelper()
    {
        string settingsDirectory = Path.Combine(Path.GetTempPath(), "TADN helper settings");

        string[] helperArguments = ["--helper-mode", .. TrayAppDotNETProgram.HelperProcessArguments(settingsDirectory)];

        Assert.True(TrayAppDotNETProgram.TryResolveSettingsDirectory(
            helperArguments,
            out string? resolved,
            out string? error));
        Assert.Null(error);
        Assert.Equal(settingsDirectory, resolved);
        Assert.Empty(TrayAppDotNETProgram.HelperProcessArguments(settingsDirectory: null));
    }

    [Fact]
    public void EveryExitPathRunsTheAppCleanupExactlyOnce() => AvaloniaTestHost.Run(() =>
    {
        int cleanupRuns = 0;
        TrayAppDotNETAppHost<HostTestSettings, AppThemeBase> host = CreateHost();
        try
        {
            bool listeningDuringCleanup = false;
            host.StartCommonServices(new TrayAppDotNETAppHostStartOptions
            {
                ShutdownApp = () =>
                {
                    cleanupRuns++;
                    // A takeover arriving mid-cleanup must find the listener and wait, not kill the process
                    listeningDuringCleanup = TrayAppDotNETExitRequest.TryRequest(
                        TrayAppDotNETProcessIdentity.TryCapture(Environment.ProcessId)!);
                }
            });

            host.OnShutdownRequested(null, new ShutdownRequestedEventArgs());
            ControlledApplicationLifetimeExitEventArgs exit = new(0);
            host.OnLifetimeExit(null, exit);
            host.Exit();
            TrayAppDotNETAppLifetime.Exit();
            // Runs the request posted during cleanup, which must not start a second exit
            Dispatcher.UIThread.RunJobs();
            bool signalledAfterShutdown = TrayAppDotNETExitRequest.TryRequest(
                TrayAppDotNETProcessIdentity.TryCapture(Environment.ProcessId)!);

            Assert.Equal(expected: 1, cleanupRuns);
            Assert.True(listeningDuringCleanup, "The exit request listener stopped before the app's cleanup");
            Assert.True(host.ShutdownCompleted);
            // An ordinary exit keeps code 0, so the watcher stops
            Assert.Equal(expected: 0, exit.ApplicationExitCode);
            Assert.False(signalledAfterShutdown, "The exit request listener must be gone after shutdown");
        }
        finally
        {
            TrayAppDotNETAppLifetime.Unregister(host);
        }
    });

    [Fact]
    public void UninstallerModeReadsNothingAndWritesNothing() => AvaloniaTestHost.Run(() =>
    {
        using TestSettingsDirectory.FreshSettingsDirectory fresh = TestSettingsDirectory.UseFresh();
        bool settingsLoaded = false;
        TrayAppDotNETAppHost<HostTestSettings, AppThemeBase> host = CreateHost(() => settingsLoaded = true);
        try
        {
            string settingsDirectory = host.Install.SettingsDirectory;
            Assert.Equal(fresh.Path, settingsDirectory);
            Assert.False(Directory.Exists(settingsDirectory));

            Window? window = host.TryRunAuxiliaryMode(
                installerMode: false,
                uninstallerMode: true,
                InstallScope.LocalAppData);

            Assert.IsType<TrayAppDotNETUninstallerWindow>(window);
            window.Close();
            Assert.False(settingsLoaded, "Tray-mode settings load (which creates settings.xml) ran");
            Assert.False(Directory.Exists(settingsDirectory), "The uninstaller created the settings folder");
            Assert.Null(host.TryRunAuxiliaryMode(installerMode: false, uninstallerMode: false, InstallScope.LocalAppData));
        }
        finally
        {
            TrayAppDotNETAppLifetime.Unregister(host);
        }
    });

    private static TrayAppDotNETAppHost<HostTestSettings, AppThemeBase> CreateHost(Action? onTrayModeLoad = null)
    {
        string applicationName = "TADNHostTest" + Guid.NewGuid().ToString("N");
        TrayAppDotNETAppInstallServices install = new(new TrayAppDotNETAppInstallServicesOptions
        {
            ApplicationName = applicationName,
            AppGuid = Guid.NewGuid().ToString("D"),
            BuildNumber = 0,
            PostToUIThread = static action => Dispatcher.UIThread.Post(action)
        });
        return new TrayAppDotNETAppHost<HostTestSettings, AppThemeBase>(
            Avalonia.Application.Current!,
            new TrayAppDotNETAppHostOptions<HostTestSettings, AppThemeBase>
            {
                Install = install,
                Strings = new ResourceManager(typeof(CommonStrings)),
                SetStringsCulture = static _ => { },
                AvailableLanguages = [],
                GetSettingsPath = () => Path.Combine(install.SettingsDirectory, "settings.xml"),
                LoadSettings = _ =>
                {
                    onTrayModeLoad?.Invoke();
                    return new HostTestSettings();
                },
                ReadSettingsWithoutSaving = static _ => new HostTestSettings(),
                CreateDefaultSettings = static () => new HostTestSettings(),
                GetThemePath = () => Path.Combine(install.SettingsDirectory, "theme.xml"),
                LoadTheme = static _ => new AppThemeBase(),
                LoadAppIcon = static () => null,
                CreatePalette = static (_, _) => TestPalettes.Default(),
                SaveSettings = static _ => throw new InvalidOperationException("Nothing may save settings here."),
                SaveSettingsOnExit = static _ => { }
            });
    }

    private static Process StartIdleProcess() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
            Arguments = "-n 60 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("ping.exe did not start.");

    private static Exception CaptureThrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    public sealed class HostTestSettings : AppSettingsCommon;
}

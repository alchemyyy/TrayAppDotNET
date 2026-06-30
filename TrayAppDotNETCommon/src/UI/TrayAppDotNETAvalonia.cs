using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETAvalonia
{
    public const string DefaultFontFamilyName = "Segoe UI";

    public static AppBuilder Configure<TApp>(Func<AppBuilder, AppBuilder>? configureAfterPlatformDetect = null)
        where TApp : Application, new()
    {
        AppBuilder builder = AppBuilder.Configure<TApp>().UsePlatformDetect();
        if (configureAfterPlatformDetect != null)
            builder = configureAfterPlatformDetect(builder);

        return builder.With(DefaultFontOptions());
    }

    public static int StartWithExplicitShutdown<TApp>(
        string[] args,
        Func<AppBuilder, AppBuilder>? configureAfterPlatformDetect = null)
        where TApp : Application, new() =>
        RunOnStaThreadIfNeeded(() =>
            Configure<TApp>(configureAfterPlatformDetect)
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown));

    private static int RunOnStaThreadIfNeeded(Func<int> run)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return run();

        int exitCode = 0;
        Exception? exception = null;
        Thread staThread = new(() =>
        {
            try { exitCode = run(); }
            catch (Exception ex) { exception = ex; }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        if (exception != null) ExceptionDispatchInfo.Capture(exception).Throw();
        return exitCode;
    }

    public static FontManagerOptions DefaultFontOptions() =>
        new() { DefaultFamilyName = DefaultFontFamilyName, };

    public static void InitializeDefaults(
        Application app,
        TrayAppDotNETAnimationMode animationMode = TrayAppDotNETAnimationMode.System,
        int? toolTipShowDelayMs = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Styles.Add(new FluentTheme());
        TrayAppDotNETAnimationPolicy.Apply(app, animationMode);
        if (toolTipShowDelayMs.HasValue)
            TrayAppDotNETToolTip.ShowDelayMs = toolTipShowDelayMs.Value;
    }

    public static void ConfigureExplicitShutdown(Application app, Action shutdownServices)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(shutdownServices);

        if (app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.ShutdownRequested += (_, _) => shutdownServices();
    }

    public static void ConfigureShutdownOnLastWindowClose(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    public static void WireCrashHandlers(
        Action? processExit = null,
        Action<UnobservedTaskExceptionEventArgs>? unobservedTaskException = null)
    {
        CrashHandler.WireCrashHandlers();

        if (processExit != null)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => processExit();

        if (unobservedTaskException != null)
            TaskScheduler.UnobservedTaskException += (_, args) => unobservedTaskException(args);
    }

    public static Task InvokeOnUIThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    public static WatcherMonitor CreateWatcherMonitor(int? watcherPid, Action onWatcherDied) =>
        new(watcherPid, InvokeOnUIThreadAsync, onWatcherDied);

    public static UpdateCheckService CreateGitHubUpdateCheckService(
        ITrayAppDotNETUpdateSettings settings,
        string repositoryName,
        string applicationName,
        int currentBuild,
        string owner = "alchemyyy",
        Func<Action, Task>? invokeOnUIThread = null) =>
        new(CreateGitHubUpdateOptions(
            settings,
            repositoryName,
            applicationName,
            currentBuild,
            owner,
            invokeOnUIThread));

    public static TrayAppDotNETLoadResult<TSettings, TTheme> LoadSettingsAndTheme<TSettings, TTheme>(
        TrayAppDotNETLoadOptions<TSettings, TTheme> options)
        where TSettings : class
        where TTheme : class
    {
        ArgumentNullException.ThrowIfNull(options);

        TSettings settings;
        try
        {
            string settingsPath = options.GetSettingsPath();
            bool firstRun = !File.Exists(settingsPath);
            settings = options.LoadSettings(settingsPath);
            if (firstRun)
                options.Startup?.SetRunOnStartup(options.GetRunOnStartup(settings));
        }
        catch (Exception ex)
        {
            options.LogSettingsLoadFailed(ex);
            settings = options.CreateDefaultSettings();
        }

        options.Startup?.RemoveLegacyRunKey();
        options.Startup?.RepairShortcutIfStale();
        options.ConfigureSettings(settings);

        TTheme? theme = null;
        try
        {
            theme = options.LoadTheme(options.GetThemePath());
            options.ConfigureTheme(theme);
        }
        catch (Exception ex)
        {
            options.LogThemeLoadFailed(ex);
        }

        return new TrayAppDotNETLoadResult<TSettings, TTheme>(settings, theme);
    }

    public static UpdateCheckOptions CreateGitHubUpdateOptions(
        ITrayAppDotNETUpdateSettings settings,
        string repositoryName,
        string applicationName,
        int currentBuild,
        string owner = "alchemyyy",
        Func<Action, Task>? invokeOnUIThread = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return new UpdateCheckOptions
        {
            VersionsManifestUrl = GitHubReleaseUrls.LatestVersionsManifestUrl(owner, repositoryName),
            RepositoryOwner = owner,
            RepositoryName = repositoryName,
            ApplicationName = applicationName,
            CurrentBuild = currentBuild,
            UserAgent = applicationName + "-Updater",
            StagingDirectory = static () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp"),
            IsEnabled = () => settings.CheckForUpdatesEnabled,
            PollInterval = () => TimeSpan.FromMilliseconds(settings.UpdateCheckIntervalMs),
            InvokeOnUIThread = invokeOnUIThread ?? InvokeOnUIThreadAsync,
            StagingFilePrefix = applicationName,
        };
    }
}

public sealed class TrayAppDotNETLoadOptions<TSettings, TTheme>
    where TSettings : class
    where TTheme : class
{
    public required Func<string> GetSettingsPath { get; init; }
    public required Func<string, TSettings> LoadSettings { get; init; }
    public required Func<TSettings> CreateDefaultSettings { get; init; }
    public required Func<TSettings, bool> GetRunOnStartup { get; init; }
    public TrayAppDotNETStartupManager? Startup { get; init; }
    public required Action<TSettings> ConfigureSettings { get; init; }
    public required Action<Exception> LogSettingsLoadFailed { get; init; }
    public required Func<string> GetThemePath { get; init; }
    public required Func<string, TTheme> LoadTheme { get; init; }
    public required Action<TTheme> ConfigureTheme { get; init; }
    public required Action<Exception> LogThemeLoadFailed { get; init; }
}

public sealed record TrayAppDotNETLoadResult<TSettings, TTheme>(
    TSettings Settings,
    TTheme? Theme)
    where TSettings : class
    where TTheme : class;

using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Utils;
using AppThemeBase = TrayAppDotNETCommon.Visuals.AppTheme;

namespace TrayAppDotNETCommon.UI;

/// <summary>The app's exit path, as the Common settings pages and update prompts reach it.</summary>
public interface ITrayAppDotNETAppExit
{
    /// <summary>Runs the app's cleanup once, then ends the desktop lifetime.</summary>
    void Exit();

    /// <summary>
    /// Runs the app's cleanup once, starts <paramref name="executablePath"/> as the replacement instance, then ends
    /// the desktop lifetime. Cleanup runs first so the replacement's single-instance takeover finds nothing to stop.
    /// </summary>
    void Restart(string executablePath, string? arguments);
}

/// <summary>
/// Process-wide access to the running app's exit path. Common UI (the About page update prompt, Restart from
/// installed, the rendering restart, the uninstaller) exits through here so the app's cleanup always runs.
/// </summary>
public static class TrayAppDotNETAppLifetime
{
    private static ITrayAppDotNETAppExit? _current;

    internal static void Register(ITrayAppDotNETAppExit host) => _current = host;

    /// <summary>Forgets the registered host; tests that build a host restore the default with it.</summary>
    internal static void Unregister(ITrayAppDotNETAppExit host)
    {
        if (ReferenceEquals(_current, host)) _current = null;
    }

    /// <summary>Exits through the registered app host, or ends the desktop lifetime when none is registered.</summary>
    public static void Exit()
    {
        if (_current != null)
        {
            _current.Exit();
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>
    /// Restarts as <paramref name="executablePath"/> through the registered app host. Without a host, the
    /// replacement starts before the desktop lifetime ends. Throws when the replacement cannot start before
    /// anything was torn down.
    /// </summary>
    public static void Restart(string executablePath, string? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException(message: "The executable to restart as was not found.", executablePath);

        if (_current != null)
        {
            _current.Restart(executablePath, arguments);
            return;
        }

        if (!TryStart(executablePath, arguments, out string errorMessage))
            throw new InvalidOperationException(errorMessage);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    internal static bool TryStart(string executablePath, string? arguments, out string errorMessage) =>
        ExplorerProcessLauncher.TryShellExecute(
            executablePath,
            arguments,
            Path.GetDirectoryName(executablePath),
            verb: null,
            out _,
            out errorMessage);
}

/// <summary>Settings the host reads before Avalonia starts, in one pass over settings.xml.</summary>
public sealed record TrayAppDotNETStartupSettings(string? RenderingBackend, string? Language)
{
    public static TrayAppDotNETStartupSettings Read(Func<string> getSettingsPath, Action<string>? log = null) =>
        TrayAppDotNETAvalonia.ReadStartupSettings(getSettingsPath, log);
}

public sealed class TrayAppDotNETAppHostOptions<TSettings, TTheme>
    where TSettings : AppSettingsCommon
    where TTheme : AppThemeBase
{
    public required TrayAppDotNETAppInstallServices Install { get; init; }
    public required ResourceManager Strings { get; init; }
    public required Action<CultureInfo> SetStringsCulture { get; init; }
    public required IReadOnlyList<string> AvailableLanguages { get; init; }
    public required Func<string> GetSettingsPath { get; init; }

    /// <summary>Loads settings for normal tray mode (may create the file with defaults).</summary>
    public required Func<string, TSettings> LoadSettings { get; init; }

    /// <summary>
    /// Reads settings for the installer and uninstaller windows without writing anything: defaults when the file
    /// is missing or unreadable.
    /// </summary>
    public required Func<string, TSettings> ReadSettingsWithoutSaving { get; init; }

    public required Func<TSettings> CreateDefaultSettings { get; init; }
    public required Func<string> GetThemePath { get; init; }
    public required Func<string, TTheme> LoadTheme { get; init; }
    public required Func<WindowIcon?> LoadAppIcon { get; init; }

    /// <summary>Builds the palette of the installer and uninstaller windows from what was loaded.</summary>
    public required Func<TTheme?, TSettings?, SettingsPalette> CreatePalette { get; init; }

    /// <summary>Saves settings now, for explicit saves such as the update check persisting a skipped version.</summary>
    public required Action<TSettings> SaveSettings { get; init; }

    /// <summary>Saves settings at exit; an app with an implicit-save policy decides here whether to write.</summary>
    public required Action<TSettings> SaveSettingsOnExit { get; init; }

    /// <summary>Optional last-chance work on process exit (for example draining queued hardware writes).</summary>
    public Action? ProcessExit { get; init; }
}

/// <summary>Callbacks the host needs from the app once its own services are up.</summary>
public sealed class TrayAppDotNETAppHostStartOptions
{
    /// <summary>The app's own cleanup, run once on every exit path after the shared services stopped.</summary>
    public required Action ShutdownApp { get; init; }

    /// <summary>Shows the update notification (title, body), typically as a tray balloon.</summary>
    public Action<string, string>? ShowUpdateNotification { get; init; }
}

/// <summary>
/// The lifecycle shared by every tray app, built by composition inside the app's <see cref="Application"/>:
/// logging, localization and crash handlers; side-effect-free installer and uninstaller modes; settings and theme
/// load; the crash-watcher monitor, update checks, hotkey service creation and update notifications; the graceful
/// exit request channel; and one shutdown core that every exit path runs exactly once (tray Exit, watcher death,
/// update install, restarts, a Windows session end and an exit request from another process).
/// </summary>
public sealed class TrayAppDotNETAppHost<TSettings, TTheme> : ITrayAppDotNETAppExit
    where TSettings : AppSettingsCommon
    where TTheme : AppThemeBase
{
    private readonly Application _application;
    private readonly TrayAppDotNETAppHostOptions<TSettings, TTheme> _options;
    private TrayAppDotNETAppHostStartOptions? _startOptions;
    private IDisposable? _hotkeyService;
    private WatcherMonitor? _watcherMonitor;
    private TrayAppDotNETExitRequestListener? _exitRequestListener;
    private int _lastNotifiedUpdateVersion;
    private bool _sessionEnding;
    private bool _shutdownCompleted;

    public TrayAppDotNETAppHost(Application application, TrayAppDotNETAppHostOptions<TSettings, TTheme> options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);
        _application = application;
        _options = options;
        TrayAppDotNETAppLifetime.Register(this);
    }

    public string ApplicationName => _options.Install.ApplicationName;

    public TrayAppDotNETAppInstallServices Install => _options.Install;

    public TSettings? Settings { get; private set; }

    public TTheme? Theme { get; private set; }

    public UpdateCheckService? UpdateService { get; private set; }

    /// <summary>True from the moment the shutdown core starts.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>Wires logging, localization and crash handling. Call first.</summary>
    public void InitializeShell()
    {
        TADNLog.Log($"{ApplicationName}: framework initialization completed");
        TrayAppDotNETStartupSettings startup =
            TrayAppDotNETAppRunner.StartupSettings
            ?? TrayAppDotNETStartupSettings.Read(_options.GetSettingsPath, TADNLog.Log);
        LocalizationManager.Instance.Initialize(
            _options.Strings,
            _options.SetStringsCulture,
            LocalizationManager.ResolveCulture(
                startup.Language ?? LocalizationManager.AutoLanguage,
                _options.AvailableLanguages,
                TADNLog.Log));
        Action? processExit = _options.ProcessExit;
        TrayAppDotNETAvalonia.WireCrashHandlers(() =>
        {
            processExit?.Invoke();
            TADNLog.Shutdown();
        });
    }

    /// <summary>
    /// Runs installer or uninstaller mode when the command line asked for one, and returns true when it did. Those
    /// modes only read settings and theme to style their window: nothing is saved, no startup shortcut is created,
    /// no app service starts and the app's cleanup is not wired.
    /// </summary>
    public bool TryRunAuxiliaryMode() =>
        TryRunAuxiliaryMode(
            TrayAppDotNETProgram.IsInstallerMode,
            TrayAppDotNETProgram.IsUninstallerMode,
            TrayAppDotNETProgram.UninstallerScope) != null;

    /// <summary>Runs the requested mode and returns its window, or null in normal tray mode.</summary>
    internal Window? TryRunAuxiliaryMode(bool installerMode, bool uninstallerMode, InstallScope uninstallScope)
    {
        if (!installerMode && !uninstallerMode) return null;

        (TSettings? settings, TTheme? theme) = ReadForAuxiliaryWindow();
        SettingsPalette palette = _options.CreatePalette(theme, settings);
        bool? roundedCorners = settings?.EnableRoundedCorners;

        if (installerMode)
        {
            return TrayAppDotNETInstallerRunner.Show(_application, new TrayAppDotNETInstallerWindowOptions
            {
                Layout = Install.Layout,
                Icon = _options.LoadAppIcon(),
                Palette = palette,
                EnableRoundedCorners = roundedCorners ?? true
            });
        }

        if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

        TrayAppDotNETUninstallerWindow window = new(TrayAppDotNETUninstallerWindowOptions.Create(
            Install,
            uninstallScope,
            _options.LoadAppIcon(),
            palette,
            roundedCorners,
            progress: null) with
        {
            // The uninstaller window is its own process; when it is the copy being removed it simply ends
            ExitCurrentProcess = () =>
            {
                if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    lifetime.Shutdown();
            }
        });
        window.Show();
        return window;
    }

    /// <summary>
    /// Starts normal tray mode: hooks the shutdown core to every exit path, loads settings (creating the startup
    /// shortcut on a first run) and theme, applies the animation and tooltip settings and keeps them applied.
    /// <paramref name="configureSettings"/> and <paramref name="configureTheme"/> run before anything else uses
    /// them. Theme load failures leave <see cref="Theme"/> null.
    /// </summary>
    public void BeginTrayMode(Action<TSettings> configureSettings, Action<TTheme> configureTheme)
    {
        ArgumentNullException.ThrowIfNull(configureSettings);
        ArgumentNullException.ThrowIfNull(configureTheme);

        if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            Debugging.TrayAppDotNETControlHoverInspector.Attach(desktop);
#endif
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += OnLifetimeExit;
        }

        TSettings settings;
        try
        {
            string settingsPath = _options.GetSettingsPath();
            bool firstRun = !File.Exists(settingsPath);
            settings = _options.LoadSettings(settingsPath);
            // The startup shortcut is shared by every settings folder, so a fresh override folder leaves it alone
            if (firstRun && TrayAppDotNETProgram.SettingsDirectoryOverride == null)
                Install.Startup.SetRunOnStartup(settings.RunOnStartup);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: settings load failed: {ex}");
            settings = _options.CreateDefaultSettings();
        }

        Install.Startup.RemoveLegacyRunKey();
        Install.Startup.RepairShortcutIfStale();
        Settings = settings;
        ApplyPresentationSettings(settings);
        settings.Changed += OnSettingsChanged;
        configureSettings(settings);

        TTheme? theme = null;
        try
        {
            theme = _options.LoadTheme(_options.GetThemePath());
            Theme = theme;
            configureTheme(theme);
        }
        catch (Exception ex)
        {
            if (theme != null) Safe.Dispose(theme);
            Theme = null;
            TADNLog.Log($"{ApplicationName}: theme load failed: {ex}");
        }
    }

    /// <summary>
    /// Creates the global hotkey service. <paramref name="create"/> gets the window-class prefix of this app;
    /// <paramref name="initialize"/> initializes it, subscribes and applies the bindings. A service whose
    /// initialization throws is disposed and null is returned, so it is never published. The host disposes the
    /// returned service at shutdown.
    /// </summary>
    public THotkeys? StartHotkeyService<THotkeys>(Func<string, THotkeys> create, Action<THotkeys> initialize)
        where THotkeys : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(initialize);

        THotkeys? service = null;
        try
        {
            service = create(ApplicationName + ".HotkeySink");
            initialize(service);
            _hotkeyService = service;
            return service;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: hotkey init failed: {ex}");
            if (service != null) Safe.Dispose(service);
            return null;
        }
    }

    /// <summary>
    /// Starts the shared services once the app's own are up: the crash-watcher monitor, the update check service
    /// and its notifications, and the exit request listener.
    /// </summary>
    public void StartCommonServices(TrayAppDotNETAppHostStartOptions startOptions)
    {
        ArgumentNullException.ThrowIfNull(startOptions);
        ArgumentNullException.ThrowIfNull(startOptions.ShutdownApp);
        _startOptions = startOptions;

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(TrayAppDotNETProgram.WatcherPID, Exit);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: watcher monitor init failed: {ex}");
        }

        TSettings? settings = Settings;
        if (settings != null)
        {
            try
            {
                UpdateCheckService updateService = TrayAppDotNETAvalonia.CreateGitHubUpdateCheckService(
                    settings,
                    ApplicationName,
                    Install.BuildNumber,
                    () => _options.SaveSettings(settings),
                    TrayAppDotNETAppInstallServices.LocalAppDataRoot);
                updateService.StateChanged += OnUpdateStateChanged;
                updateService.Start();
                UpdateService = updateService;
            }
            catch (Exception ex)
            {
                TADNLog.Log($"{ApplicationName}: update service init failed: {ex}");
            }
        }

        try
        {
            _exitRequestListener = TrayAppDotNETExitRequest.Listen(() => Dispatcher.UIThread.Post(() =>
            {
                // A request that arrives while the app is already exiting is answered by that exit
                if (IsShuttingDown) return;

                TADNLog.Log($"{ApplicationName}: exit requested by another process");
                Exit();
            }));
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: exit request listener failed: {ex}");
        }
    }

    /// <summary>
    /// Opens the uninstaller for <paramref name="scope"/> without blocking its owner, and returns whether the
    /// uninstall started once the window closes.
    /// </summary>
    public Task<bool> ShowUninstallerWindow(
        InstallScope scope,
        IProgress<TrayAppDotNETInstallProgress>? progress,
        Window? owner)
    {
        TrayAppDotNETUninstallerWindow window = new(TrayAppDotNETUninstallerWindowOptions.Create(
            Install,
            scope,
            _options.LoadAppIcon(),
            _options.CreatePalette(Theme, Settings),
            Settings?.EnableRoundedCorners,
            progress));
        if (owner != null) window.Show(owner);
        else window.Show();
        return window.Completion;
    }

    /// <inheritdoc />
    public void Exit()
    {
        TADNLog.Log($"{ApplicationName}: exit");
        Shutdown();
        if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <inheritdoc />
    public void Restart(string executablePath, string? arguments)
    {
        TADNLog.Log($"{ApplicationName}: restart as {executablePath}");
        Shutdown();
        if (!TrayAppDotNETAppLifetime.TryStart(executablePath, arguments, out string errorMessage))
        {
            TADNLog.Log($"{ApplicationName}: restart failed: {errorMessage}");
            TADNLog.Flush();
            _ = User32.MessageBox(
                IntPtr.Zero,
                errorMessage,
                LocalizationManager.Instance[nameof(CommonStrings.Settings_General_RestartFailed_Title)],
                User32.MB_ICONERROR);
        }

        if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>
    /// The shutdown core. Runs once, whichever exit path gets here first: stops the shared services, runs the app's
    /// cleanup, saves settings, releases the theme and flushes the log. The exit request listener goes last: while
    /// it listens, a new instance taking over waits for this exit instead of killing the process mid-cleanup.
    /// </summary>
    public void Shutdown()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;

        RunShutdownStep("hotkey service", () =>
        {
            if (_hotkeyService != null) Safe.Dispose(_hotkeyService);
            _hotkeyService = null;
        });
        RunShutdownStep("watcher monitor", () =>
        {
            Safe.Dispose(_watcherMonitor);
            _watcherMonitor = null;
        });
        RunShutdownStep("update service", () =>
        {
            UpdateCheckService? updateService = UpdateService;
            UpdateService = null;
            if (updateService == null) return;

            updateService.StateChanged -= OnUpdateStateChanged;
            Safe.Dispose(updateService);
        });

        if (_startOptions != null) RunShutdownStep("app", _startOptions.ShutdownApp);

        RunShutdownStep("settings", () =>
        {
            TSettings? settings = Settings;
            if (settings == null) return;

            settings.Changed -= OnSettingsChanged;
            _options.SaveSettingsOnExit(settings);
        });
        RunShutdownStep("theme", () =>
        {
            if (Theme != null) Safe.Dispose(Theme);

            Theme = null;
        });
        RunShutdownStep("exit request listener", () =>
        {
            _exitRequestListener?.Dispose();
            _exitRequestListener = null;
        });

        _shutdownCompleted = true;
        TADNLog.Log($"{ApplicationName}: shutdown complete");
        TADNLog.Flush();
    }

    /// <summary>Gets whether the shutdown core ran to its end; tests use it.</summary>
    internal bool ShutdownCompleted => _shutdownCompleted;

    /// <summary>Gets whether this exit is a Windows session end; tests use it.</summary>
    internal bool IsSessionEnding => _sessionEnding;

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (e.Cancel) return;

        // Avalonia raises this for the Windows end-session query; Windows reports the session as shutting down then
        if (CrashHandler.IsSessionShuttingDown())
        {
            _sessionEnding = true;
            TADNLog.Log($"{ApplicationName}: Windows session is ending");
        }

        Shutdown();
    }

    internal void OnLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Shutdown();
        // The watcher relaunches the app after a session end the user cancelled
        if (_sessionEnding) e.ApplicationExitCode = CrashHandler.SessionEndingExitCode;
    }

    private void RunShutdownStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: shutdown ({step}) failed: {ex}");
        }
    }

    private (TSettings? Settings, TTheme? Theme) ReadForAuxiliaryWindow()
    {
        // Nothing to read in a folder that does not exist, and asking the app for paths would create it
        if (!Directory.Exists(Install.SettingsDirectory)) return (null, null);

        TSettings? settings = null;
        try
        {
            settings = _options.ReadSettingsWithoutSaving(_options.GetSettingsPath());
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: settings read failed: {ex.Message}");
        }

        TTheme? theme = null;
        try
        {
            string themePath = _options.GetThemePath();
            if (File.Exists(themePath)) theme = _options.LoadTheme(themePath);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"{ApplicationName}: theme read failed: {ex.Message}");
        }

        return (settings, theme);
    }

    private void ApplyPresentationSettings(TSettings settings)
    {
        TrayAppDotNETAnimationPolicy.Apply(_application, settings.AnimationMode);
        TrayAppDotNETToolTip.ShowDelayMs = settings.ToolTipShowDelayMs;
    }

    private void OnSettingsChanged()
    {
        TSettings? settings = Settings;
        if (settings == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsShuttingDown) ApplyPresentationSettings(settings);
        });
    }

    private void OnUpdateStateChanged()
    {
        UpdateInfo? info = UpdateService?.AvailableUpdate;
        if (info == null || Settings?.ShowUpdateNotificationsEnabled != true) return;
        if (info.Version <= _lastNotifiedUpdateVersion) return;

        _lastNotifiedUpdateVersion = info.Version;
        _startOptions?.ShowUpdateNotification?.Invoke(
            LocalizationManager.Instance[nameof(CommonStrings.UpdateNotification_Title)],
            string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance[nameof(CommonStrings.UpdateNotification_BodyFormat)],
                info.ReleaseName));
    }
}

/// <summary>Starts an app's Avalonia lifetime with the startup settings the host needs.</summary>
public static class TrayAppDotNETAppRunner
{
    /// <summary>What <see cref="Run{TApp}"/> read before Avalonia started.</summary>
    internal static TrayAppDotNETStartupSettings? StartupSettings { get; set; }

    /// <summary>
    /// Runs Avalonia with the rendering backend from the app's settings (read in the same pass as the language) on
    /// an STA thread, and returns the exit code of the desktop lifetime.
    /// </summary>
    public static int Run<TApp>(
        string[] args,
        Func<string> getSettingsPath,
        TrayAppDotNETRenderingBackend defaultBackend = TrayAppDotNETRenderingBackend.GPUPreferred,
        Func<AppBuilder, AppBuilder>? configure = null)
        where TApp : Application, new()
    {
        TrayAppDotNETStartupSettings startup = TrayAppDotNETStartupSettings.Read(getSettingsPath, TADNLog.Log);
        StartupSettings = startup;
        TrayAppDotNETRenderingBackend backend =
            Enum.TryParse(startup.RenderingBackend, ignoreCase: true, out TrayAppDotNETRenderingBackend parsed)
                ? parsed
                : defaultBackend;
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<TApp>(
            args,
            builder =>
            {
                builder = TrayAppDotNETAvalonia.UseRenderingBackend(builder, backend);
                return configure == null ? builder : configure(builder);
            });
    }
}

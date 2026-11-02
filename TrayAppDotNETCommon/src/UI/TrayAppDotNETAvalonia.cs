using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETAvalonia
{
    public const string DefaultFontFamilyName = "Segoe UI";
    private static int _dispatcherUnhandledExceptionHandlerWired;

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
        TrayAppDotNETProgram.RunOnStaThreadIfNeeded(() =>
            Configure<TApp>(configureAfterPlatformDetect)
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown));

    public static FontManagerOptions DefaultFontOptions() =>
        new() { DefaultFamilyName = DefaultFontFamilyName };

    /// <summary>Applies an explicit Avalonia rendering backend choice.</summary>
    public static AppBuilder UseRenderingBackend(AppBuilder builder, TrayAppDotNETRenderingBackend backend)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return backend switch
        {
            TrayAppDotNETRenderingBackend.Software => UseWin32SoftwareRendering(builder),
            _ => builder
        };
    }

    /// <summary>
    /// Reads the settings startup needs before the full load (rendering backend and language) in one pass over the
    /// file. Only top-level elements count; a value missing, or a file missing or unreadable, reads as null.
    /// </summary>
    internal static TrayAppDotNETStartupSettings ReadStartupSettings(Func<string> getSettingsPath, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(getSettingsPath);

        // Settings properties are the root's direct children; nested objects may reuse a property name
        const int RootSettingDepth = 1;
        const string RenderingBackendElement = nameof(ITrayAppDotNETRenderingSettings.RenderingBackend);
        const string LanguageElement = nameof(AppSettingsCommon.Language);

        string? renderingBackend = null;
        string? language = null;
        try
        {
            string settingsPath = getSettingsPath();
            if (!File.Exists(settingsPath)) return new TrayAppDotNETStartupSettings(null, null);

            XmlReaderSettings readerSettings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };

            using FileStream settingsStream = new(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            using XmlReader reader = XmlReader.Create(settingsStream, readerSettings);
            bool advanced = reader.Read();
            while (advanced && (renderingBackend == null || language == null))
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == RootSettingDepth)
                {
                    if (string.Equals(reader.LocalName, RenderingBackendElement, StringComparison.Ordinal))
                    {
                        // Reading the content moves the reader past the element, onto the next node
                        renderingBackend = reader.ReadElementContentAsString();
                        advanced = !reader.EOF;
                        continue;
                    }

                    if (string.Equals(reader.LocalName, LanguageElement, StringComparison.Ordinal))
                    {
                        language = reader.ReadElementContentAsString();
                        advanced = !reader.EOF;
                        continue;
                    }
                }

                advanced = reader.Read();
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"TrayAppDotNETAvalonia: reading startup settings failed: {ex.Message}");
        }

        return new TrayAppDotNETStartupSettings(renderingBackend, language);
    }

    /// <summary>Forces Avalonia's Win32 backend onto CPU software rendering.</summary>
    public static AppBuilder UseWin32SoftwareRendering(AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.With(new Win32PlatformOptions
        {
            RenderingMode = [Win32RenderingMode.Software],
            CompositionMode = [Win32CompositionMode.RedirectionSurface]
        });
    }

    public static void InitializeDefaults(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Styles.Add(new FluentTheme());
        TextBlockLayoutLifetime.Install(app);
        TrayAppDotNETAnimationPolicy.Apply(app, TrayAppDotNETAnimationMode.System);
    }

    /// <summary>
    /// Wires the Common crash policy (<see cref="CrashHandler"/>) and the Avalonia dispatcher handler, plus optional
    /// last-chance work on process exit.
    /// </summary>
    public static void WireCrashHandlers(Action? processExit = null)
    {
        CrashHandler.WireCrashHandlers();
        WireDispatcherUnhandledExceptionHandler();

        if (processExit != null)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => processExit();
    }

    /// <summary>
    /// Keeps a failing dispatcher callback from killing the tray message thread, and logs it in full (stack trace and
    /// inner exceptions).
    /// </summary>
    private static void WireDispatcherUnhandledExceptionHandler()
    {
        if (Interlocked.Exchange(ref _dispatcherUnhandledExceptionHandlerWired, value: 1) == 1) return;

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            try
            {
                CrashHandler.LogNonFatal(
                    $"Avalonia DispatcherUnhandledException: {CrashHandler.DescribeException(args.Exception)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "TrayAppDotNETAvalonia dispatcher exception logging failed: " + ex);
            }

            args.Handled = true;
        };
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
        string applicationName,
        int currentBuild,
        Action saveSettings,
        string sharedSettingsDirectory,
        Func<Action, Task>? invokeOnUIThread = null) =>
        new(CreateGitHubUpdateOptions(
            settings,
            applicationName,
            currentBuild,
            saveSettings,
            sharedSettingsDirectory,
            invokeOnUIThread));

    public static UpdateCheckOptions CreateGitHubUpdateOptions(
        ITrayAppDotNETUpdateSettings settings,
        string applicationName,
        int currentBuild,
        Action saveSettings,
        string sharedSettingsDirectory,
        Func<Action, Task>? invokeOnUIThread = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedSettingsDirectory);

        return new UpdateCheckOptions
        {
            VersionsManifestUrl = GitHubReleaseUrls.LatestVersionsManifestUrl(),
            VersionsManifestCachePath = Path.Combine(
                sharedSettingsDirectory,
                GitHubReleaseUrls.VersionsManifestFileName),
            RepositoryOwner = TrayAppDotNETConstants.RepositoryOwner,
            RepositoryName = TrayAppDotNETConstants.RepositoryName,
            ApplicationName = applicationName,
            CurrentBuild = currentBuild,
            UserAgent = applicationName + "-Updater",
            StagingDirectory = static () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                path2: "Temp"),
            IsEnabled = () => settings.CheckForUpdatesEnabled,
            PollInterval = () => TimeSpan.FromMilliseconds(settings.UpdateCheckIntervalMs),
            GetSkippedUpdateVersion = () => settings.SkippedUpdateVersion,
            PersistSkippedUpdateVersion = version =>
            {
                if (settings.SkippedUpdateVersion == version) return;
                settings.SkippedUpdateVersion = version;
                saveSettings();
            },
            InvokeOnUIThread = invokeOnUIThread ?? InvokeOnUIThreadAsync,
            StagingFilePrefix = applicationName
        };
    }
}

#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Replaces a compiled resource dictionary after successful Debug source AXAML reloads.
/// </summary>
public sealed class AXAMLResourceHotReloadStore<TResource>
    where TResource : ResourceDictionary
{
    private const int ReloadDebounceMilliseconds = 150;

    private readonly string _resourceName;
    private readonly string _sourcePath;
    private readonly Func<TResource> _resourceFactory;
    private readonly Action _resourcesReloaded;
    private readonly Action<TResource, TResource>? _synchronizeReload;
    private TResource? _hotReloadedResources;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;

    private AXAMLResourceHotReloadStore(
        string resourceName,
        string sourcePath,
        Func<TResource> resourceFactory,
        Action resourcesReloaded,
        Action<TResource, TResource>? synchronizeReload)
    {
        _resourceName = resourceName;
        _sourcePath = sourcePath;
        _resourceFactory = resourceFactory;
        _resourcesReloaded = resourcesReloaded;
        _synchronizeReload = synchronizeReload;
        Current = resourceFactory();
        StartWatcher();
    }

    /// <summary>Gets the latest successfully loaded dictionary.</summary>
    public TResource Current => Volatile.Read(ref _hotReloadedResources) ?? field;

    /// <summary>Reloads the source immediately on the calling UI thread.</summary>
    internal void ReloadNow() => Reload();

    /// <summary>Creates a store for an AXAML file adjacent to the supplied caller source file.</summary>
    public static AXAMLResourceHotReloadStore<TResource> Create(
        string resourceName,
        Func<TResource> resourceFactory,
        Action resourcesReloaded,
        string sourceFileName,
        [CallerFilePath] string callerFilePath = "",
        Action<TResource, TResource>? synchronizeReload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentNullException.ThrowIfNull(resourcesReloaded);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        string sourceDirectory = Path.GetDirectoryName(callerFilePath) ?? string.Empty;
        string sourcePath = Path.Combine(sourceDirectory, sourceFileName);
        return new AXAMLResourceHotReloadStore<TResource>(
            resourceName,
            sourcePath,
            resourceFactory,
            resourcesReloaded,
            synchronizeReload);
    }

    /// <summary>
    /// Watches source because HotAvalonia does not patch standalone resource dictionaries.
    /// </summary>
    private void StartWatcher()
    {
        try
        {
            string? sourceDirectory = Path.GetDirectoryName(_sourcePath);
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)) return;

            _reloadTimer = new Timer(
                QueueReloadOnUIThread,
                state: null,
                Timeout.Infinite,
                Timeout.Infinite);

            FileSystemWatcher watcher = new(sourceDirectory, Path.GetFileName(_sourcePath))
            {
                NotifyFilter = NotifyFilters.CreationTime |
                               NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };
            watcher.Changed += OnSourceFileChanged;
            watcher.Created += OnSourceFileChanged;
            watcher.Renamed += OnSourceFileRenamed;
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug($"{_resourceName} hot-reload watcher failed: {exception.Message}");
        }
    }

    private void OnSourceFileChanged(object sender, FileSystemEventArgs eventArgs) => ScheduleReload();

    private void OnSourceFileRenamed(object sender, RenamedEventArgs eventArgs) => ScheduleReload();

    private void ScheduleReload()
    {
        try
        {
            _reloadTimer?.Change(ReloadDebounceMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Process shutdown can race a final file notification
        }
    }

    private void QueueReloadOnUIThread(object? state)
    {
        try
        {
            Dispatcher.UIThread.Post(Reload);
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug($"{_resourceName} hot reload dispatch failed: {exception.Message}");
        }
    }

    private void Reload()
    {
        try
        {
            ReloadCore();
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug($"{_resourceName} hot reload failed: {exception.Message}");
        }
    }

    // Keep runtime-loader references outside Reload's JIT boundary so dependency failures are caught
    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnconditionalSuppressMessage(
        category: "Trimming",
        checkId: "IL2026",
        Justification = "This Debug-only loader uses types rooted by the matching compiled AXAML.")]
    [UnconditionalSuppressMessage(
        category: "AOT",
        checkId: "IL3050",
        Justification =
            "This Debug-only hot-reload path intentionally uses runtime XAML compilation and is excluded from AOT releases.")]
    private void ReloadCore()
    {
        string xaml = File.ReadAllText(_sourcePath);
        TResource candidateResources = _resourceFactory();
        candidateResources.Clear();

        object loadedResources = AvaloniaRuntimeXamlLoader.Load(
            xaml,
            typeof(TResource).Assembly,
            candidateResources,
            new Uri(_sourcePath, UriKind.Absolute),
            designMode: false);
        if (!ReferenceEquals(loadedResources, candidateResources))
        {
            throw new InvalidOperationException(
                $"Runtime XAML loader returned an unexpected {_resourceName} instance.");
        }

        TResource publishedResources = candidateResources;
        if (_synchronizeReload != null)
        {
            publishedResources = Current;
            _synchronizeReload(publishedResources, candidateResources);
        }

        Volatile.Write(ref _hotReloadedResources, publishedResources);
        TADNLog.LogDebug($"{_resourceName} hot reloaded");
        _resourcesReloaded();
    }
}
#endif

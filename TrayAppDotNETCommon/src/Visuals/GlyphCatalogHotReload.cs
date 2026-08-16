using System.Runtime.CompilerServices;
#if DEBUG
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
#endif

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Notifies live UI and tray renderers when any glyph catalog reloads.
/// </summary>
public static class GlyphCatalogHotReload
{
#if DEBUG
    public static event Action? ResourcesReloaded;
#else
    public static event Action? ResourcesReloaded
    {
        add { }
        remove { }
    }
#endif

#if DEBUG
    /// <summary>
    /// Notifies each consumer independently so one failed refresh does not block the others.
    /// </summary>
    internal static void NotifyResourcesReloaded(string catalogName)
    {
        Action? handlers = ResourcesReloaded;
        if (handlers == null) return;

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                TADNLog.LogDebug(
                    $"{catalogName} glyph catalog hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}

#if DEBUG
/// <summary>
/// Keeps a compiled glyph dictionary and replaces it after successful source AXAML reloads.
/// </summary>
public sealed class GlyphCatalogHotReloadStore<TResource>
    where TResource : ResourceDictionary
{
    private const int ReloadDebounceMilliseconds = 150;

    private readonly string _catalogName;
    private readonly string _sourcePath;
    private readonly Func<TResource> _resourceFactory;
    private TResource? _hotReloadedResources;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _reloadTimer;

    private GlyphCatalogHotReloadStore(
        string catalogName,
        string sourcePath,
        Func<TResource> resourceFactory)
    {
        _catalogName = catalogName;
        _sourcePath = sourcePath;
        _resourceFactory = resourceFactory;
        Current = resourceFactory();
        StartWatcher();
    }

    /// <summary>
    /// Gets the latest successfully loaded dictionary.
    /// </summary>
    public TResource Current => Volatile.Read(ref _hotReloadedResources) ?? field;

    /// <summary>
    /// Creates a store for an AXAML file adjacent to the calling catalog source file.
    /// </summary>
    public static GlyphCatalogHotReloadStore<TResource> Create(
        string catalogName,
        Func<TResource> resourceFactory,
        string sourceFileName = "GlyphCatalog.axaml",
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        string sourceDirectory = Path.GetDirectoryName(callerFilePath) ?? string.Empty;
        string sourcePath = Path.Combine(sourceDirectory, sourceFileName);
        return new GlyphCatalogHotReloadStore<TResource>(catalogName, sourcePath, resourceFactory);
    }

    /// <summary>
    /// Watches the source catalog because HotAvalonia does not patch standalone resource dictionaries.
    /// </summary>
    private void StartWatcher()
    {
        try
        {
            string? sourceDirectory = Path.GetDirectoryName(_sourcePath);
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)) return;

            _reloadTimer = new System.Threading.Timer(
                QueueReloadOnUIThread,
                null,
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
            TADNLog.LogDebug($"{_catalogName} glyph catalog hot-reload watcher failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Debounces writes from editors that emit multiple file events.
    /// </summary>
    private void OnSourceFileChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    /// <summary>
    /// Handles editors that replace the catalog through an atomic rename.
    /// </summary>
    private void OnSourceFileRenamed(object sender, RenamedEventArgs e) => ScheduleReload();

    /// <summary>
    /// Schedules parsing after the editor has finished writing the source file.
    /// </summary>
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

    /// <summary>
    /// Moves runtime XAML parsing onto Avalonia's UI thread.
    /// </summary>
    private void QueueReloadOnUIThread(object? state)
    {
        try
        {
            Dispatcher.UIThread.Post(Reload);
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug($"{_catalogName} glyph catalog hot reload dispatch failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Parses a candidate dictionary and publishes it only after a successful load.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "This Debug-only loader uses types already rooted by compiled glyph catalog AXAML.")]
    private void Reload()
    {
        try
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
                    "Runtime XAML loader returned an unexpected glyph catalog instance.");
            }

            Volatile.Write(ref _hotReloadedResources, candidateResources);
            TADNLog.LogDebug($"{_catalogName} glyph catalog hot reloaded");
            GlyphCatalogHotReload.NotifyResourcesReloaded(_catalogName);
        }
        catch (Exception exception)
        {
            TADNLog.LogDebug($"{_catalogName} glyph catalog hot reload failed: {exception.Message}");
        }
    }
}
#endif

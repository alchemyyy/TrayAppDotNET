namespace TrayAppDotNETCommon.Services;

/// <summary>
/// Watches one file for writes from any process and raises a callback on a thread-pool thread once they settle.
/// An atomic replace by rename raises several watcher events, which the quiet period folds into one callback.
/// </summary>
internal sealed class DebouncedFileWatcher(string path, TimeSpan quietPeriod, Action changed) : IDisposable
{
    private readonly string _path = Path.GetFullPath(path);

    private readonly TimeSpan _quietPeriod = quietPeriod >= TimeSpan.Zero
        ? quietPeriod
        : throw new ArgumentOutOfRangeException(nameof(quietPeriod));

    private readonly Action _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    private readonly Lock _sync = new();
    private Timer? _quietPeriodTimer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>Starts watching, creating the file's directory when it is missing.</summary>
    public void Start()
    {
        string directory = Path.GetDirectoryName(_path)
                           ?? throw new InvalidOperationException($"Watched path has no parent directory: {_path}");
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher != null) return;

            Directory.CreateDirectory(directory);
            Timer quietPeriodTimer = new(OnQuietPeriodElapsed, state: null, Timeout.Infinite, Timeout.Infinite);
            FileSystemWatcher watcher = new(directory, Path.GetFileName(_path))
            {
                // LastWrite and Size catch a write in place; FileName catches the rename that replaces the file
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnWatcherError;
            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                DisposeWatcher(watcher);
                quietPeriodTimer.Dispose();
                throw;
            }

            _quietPeriodTimer = quietPeriodTimer;
            _watcher = watcher;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs eventArgs) => ScheduleCallback();

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        Exception exception = eventArgs.GetException();

        // An overflow only drops events, so the file is read again; any other error disables the watcher
        if (exception is InternalBufferOverflowException)
        {
            ScheduleCallback();
            return;
        }

        TADNLog.Log($"DebouncedFileWatcher: stopped watching {_path}: {exception.Message}");
    }

    private void ScheduleCallback()
    {
        lock (_sync)
        {
            if (_disposed) return;

            // Every event restarts the quiet period, so a burst raises one callback after its last event
            _quietPeriodTimer?.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnQuietPeriodElapsed(object? state)
    {
        if (Volatile.Read(ref _disposed)) return;

        try
        {
            _changed();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"DebouncedFileWatcher: change handler for {_path} failed: {exception.Message}");
        }
    }

    private void DisposeWatcher(FileSystemWatcher watcher)
    {
        watcher.Changed -= OnFileChanged;
        watcher.Created -= OnFileChanged;
        watcher.Renamed -= OnFileChanged;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        Timer? quietPeriodTimer;
        lock (_sync)
        {
            if (_disposed) return;

            _disposed = true;
            watcher = _watcher;
            quietPeriodTimer = _quietPeriodTimer;
            _watcher = null;
            _quietPeriodTimer = null;
        }

        if (watcher != null) DisposeWatcher(watcher);
        quietPeriodTimer?.Dispose();
    }
}

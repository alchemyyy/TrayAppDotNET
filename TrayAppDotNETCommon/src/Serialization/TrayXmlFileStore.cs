using Timer = System.Threading.Timer;

namespace TrayAppDotNETCommon.Serialization;

/// <summary>Save operations of a persisted XML document, independent of its type.</summary>
public interface ITrayXmlFileStore
{
    string Path { get; }

    /// <summary>How the last <c>Load</c> found the file.</summary>
    DataFileLoadResult LoadResult { get; }

    /// <summary>
    /// True after the file stayed locked at load, or was corrupt and could not be moved aside: the session runs on
    /// defaults and never writes, so the unreadable file survives untouched.
    /// </summary>
    bool IsSavingDisabled { get; }

    /// <summary>Coalesces a burst of changes into one write after the quiet period.</summary>
    void ScheduleSave();

    /// <summary>Serializes the document now and writes it in the background, replacing any scheduled save.</summary>
    void SaveNow();

    /// <summary>Writes a scheduled save now and waits until every queued write has reached the disk.</summary>
    void Flush();
}

/// <summary>A document that keeps a reference to the store it was loaded from.</summary>
public interface ITrayXmlStoredDocument
{
    void AttachStore(ITrayXmlFileStore store);
}

/// <summary>
/// Owns one XML data file: load with a <see cref="DataFileLoadResult"/>, corrupt-file backup, and coalesced saves.
/// <para>
/// Live state is never serialized concurrently with the thread that mutates it: a scheduled save captures the
/// caller's <see cref="SynchronizationContext"/> (the UI thread) and serializes there once the quiet period ends;
/// with no context it serializes immediately on the calling thread. Only the finished bytes go to the thread pool,
/// where one writer replaces the file atomically through <see cref="AtomicFile"/>.
/// </para>
/// <para>
/// Load outcomes: <see cref="DataFileLoadResult.Missing"/> writes the defaults;
/// <see cref="DataFileLoadResult.Corrupt"/> moves the file to a timestamped <c>.corrupt-*</c> backup and runs on
/// defaults; <see cref="DataFileLoadResult.Locked"/> runs on defaults with saving disabled for the session.
/// </para>
/// </summary>
public sealed class TrayXmlFileStore<T> : ITrayXmlFileStore
    where T : class
{
    private readonly Func<T> _createDefault;
    private readonly Func<T, bool>? _normalize;
    private readonly Action<string> _log;
    private readonly int _debounceMs;
    private readonly Lock _gate = new();
    private readonly Timer _debounceTimer;

    private T? _value;
    private bool _saveScheduled;
    private SynchronizationContext? _scheduledContext;
    private byte[]? _scheduledBytes;
    private byte[]? _pendingWrite;
    private bool _writerRunning;
    private Task _writer = Task.CompletedTask;

    /// <param name="path">File the store reads and writes.</param>
    /// <param name="createDefault">Creates the document used when the file is missing, corrupt or locked.</param>
    /// <param name="log">Receives every load and save failure; required so no failure goes unrecorded.</param>
    /// <param name="normalize">
    /// Repairs or migrates a loaded or default document; returns true when it changed persisted state, which saves
    /// a loaded document once.
    /// </param>
    /// <param name="debounceMs">Quiet period of <see cref="ScheduleSave"/>.</param>
    public TrayXmlFileStore(
        string path,
        Func<T> createDefault,
        Action<string> log,
        Func<T, bool>? normalize = null,
        int debounceMs = TimeConstants.SettingsSaveDebounceMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(createDefault);
        ArgumentNullException.ThrowIfNull(log);

        Path = path;
        _createDefault = createDefault;
        _log = log;
        _normalize = normalize;
        _debounceMs = debounceMs;
        _debounceTimer = new Timer(_ => OnDebounceElapsed(), state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Path { get; }

    public DataFileLoadResult LoadResult { get; private set; } = DataFileLoadResult.Missing;

    public bool IsSavingDisabled { get; private set; }

    /// <summary>The document returned by the last <see cref="Load"/>.</summary>
    public T Value => _value ?? throw new InvalidOperationException($"{Path} has not been loaded.");

    /// <summary>
    /// Reads the file, or falls back to defaults as described on the type, and attaches this store to the document
    /// when it implements <see cref="ITrayXmlStoredDocument"/>.
    /// </summary>
    public T Load()
    {
        DataFileLoadResult result = TrayXmlSerializer.TryLoadFile(Path, out T? loaded, _log);
        LoadResult = result;

        T value;
        bool save;
        if (result == DataFileLoadResult.Loaded)
        {
            value = loaded!;
            save = _normalize?.Invoke(value) == true;
        }
        else
        {
            value = _createDefault();
            _normalize?.Invoke(value);
            save = result == DataFileLoadResult.Missing;

            if (result == DataFileLoadResult.Corrupt && DataFileLoader.MoveCorruptAside(Path, _log) == null)
                DisableSaving();
            else if (result == DataFileLoadResult.Locked)
                DisableSaving();
        }

        _value = value;
        if (value is ITrayXmlStoredDocument document) document.AttachStore(this);
        if (save) SaveNow();
        return value;
    }

    public void ScheduleSave()
    {
        if (_value is not { } value || IsSavingDisabled) return;

        SynchronizationContext? context = SynchronizationContext.Current;
        byte[]? bytes = context == null ? Serialize(value) : null;
        if (context == null && bytes == null) return;

        lock (_gate)
        {
            _saveScheduled = true;
            _scheduledContext = context;
            _scheduledBytes = bytes;
            _debounceTimer.Change(_debounceMs, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        if (_value is not { } value || IsSavingDisabled) return;

        CancelScheduledSave();
        if (Serialize(value) is { } bytes) QueueWrite(bytes);
    }

    public void Flush()
    {
        bool scheduled;
        lock (_gate) scheduled = _saveScheduled;
        if (scheduled) SaveNow();

        Task writer;
        lock (_gate) writer = _writer;
        writer.Wait();
    }

    private void DisableSaving()
    {
        IsSavingDisabled = true;
        _log($"{Path}: saving is disabled for this session so the existing file is not overwritten.");
    }

    private void CancelScheduledSave()
    {
        lock (_gate)
        {
            _saveScheduled = false;
            _scheduledContext = null;
            _scheduledBytes = null;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnDebounceElapsed()
    {
        SynchronizationContext? context;
        lock (_gate)
        {
            if (!_saveScheduled) return;

            if (_scheduledBytes is { } bytes)
            {
                _saveScheduled = false;
                _scheduledBytes = null;
                QueueWriteLocked(bytes);
                return;
            }

            context = _scheduledContext;
        }

        context?.Post(static state => ((TrayXmlFileStore<T>)state!).SerializeScheduledSave(), this);
    }

    private void SerializeScheduledSave()
    {
        lock (_gate)
        {
            if (!_saveScheduled) return;
            _saveScheduled = false;
            _scheduledContext = null;
        }

        if (_value is { } value && Serialize(value) is { } bytes) QueueWrite(bytes);
    }

    private byte[]? Serialize(T value)
    {
        try
        {
            using MemoryStream stream = new();
            TrayXmlSerializer.Write(stream, value);
            return stream.ToArray();
        }
        catch (Exception exception)
        {
            _log($"{Path}: serialization failed: {exception}");
            return null;
        }
    }

    private void QueueWrite(byte[] bytes)
    {
        lock (_gate) QueueWriteLocked(bytes);
    }

    private void QueueWriteLocked(byte[] bytes)
    {
        _pendingWrite = bytes;
        if (_writerRunning) return;

        _writerRunning = true;
        _writer = Task.Run(WriteLoop);
    }

    /// <summary>
    /// Writes the latest queued snapshot until none is left. A failed write (the file held by another program for
    /// longer than <see cref="AtomicFile"/>'s own retries) is retried with the same snapshot, unless a newer one
    /// replaced it, for up to <c>SettingsFileReplaceRetryCount</c> rounds before the failure is reported as final.
    /// </summary>
    private void WriteLoop()
    {
        int failedRounds = 0;
        while (true)
        {
            byte[]? bytes;
            lock (_gate)
            {
                bytes = _pendingWrite;
                _pendingWrite = null;
                if (bytes == null)
                {
                    _writerRunning = false;
                    return;
                }
            }

            try
            {
                AtomicFile.WriteAllBytes(Path, bytes);
                failedRounds = 0;
            }
            catch (Exception exception)
            {
                failedRounds++;
                bool retry;
                lock (_gate)
                {
                    retry = failedRounds < TimeConstants.SettingsFileReplaceRetryCount;
                    if (retry) _pendingWrite ??= bytes;
                }

                if (!retry)
                {
                    _log($"{Path}: save failed: {exception.Message}");
                    failedRounds = 0;
                    continue;
                }

                _log($"{Path}: save attempt {failedRounds} failed, retrying: {exception.Message}");
                Thread.Sleep(TimeConstants.SettingsFileReplaceRetryDelayMs * failedRounds);
            }
        }
    }
}

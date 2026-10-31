using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon;

/// <summary>
/// Singular file-backed logger for tray apps. Calls to <see cref="Log"/> append a timestamped line
/// to an in-memory buffer that is flushed to disk every ~2 seconds, so logging stays cheap on hot paths
/// and a process kill loses at most ~2s.
///
/// Two files live in the configured log directory:
///   * <c>active.log</c>: current.
///   * <c>old.log</c>: previous rollover.
/// A helper process the app starts (<see cref="InitializeHelper"/>) writes <c>&lt;helper&gt;.log</c> and
/// <c>&lt;helper&gt;.old.log</c> in the same folder instead, so it never competes with the app for its file.
/// An elevated process (<see cref="InitializeForElevatedProcess"/>) writes no file at all.
/// Either trigger rotates - active file at or above 10 MB, or active file at least 7 days old.
/// On rotation, old is deleted, active is renamed to old, and a fresh active starts.
///
/// The logger is best-effort: it never throws back to callers. A write that fails (the file is locked by a
/// virus scanner or another process rotating it) is retried briefly and the batch goes back to the buffer for the
/// next flush, up to a bounded backlog, so a FATAL line flushed while the timer is writing is not lost.
/// </summary>
public static class TADNLog
{
    private const string ActiveName = "active.log";
    private const string OldName = "old.log";
    private const string HelperLogExtension = ".log";
    private const string HelperOldLogSuffix = ".old.log";
    private const string TimestampFmt = "yyyy-MM-dd HH:mm:ss.fff";

    private static readonly Lock _gate = new();
    private static TADNLogFile? _file;
    private static Timer? _timer;
    private static string? _configuredLogDirectory;
    private static bool _initialized;
    private static bool _shuttingDown;

    // Set only for an elevated process: messages go here instead of a file
    private static Action<string>? _forwarder;

    /// <summary>
    /// Optional in-process observer that receives every message passed to <see cref="Log"/>, before any file
    /// buffering. Simulation tests use it to interleave app log lines with their virtual timeline.
    /// Called on the logging thread and must not throw.
    /// </summary>
    public static Action<string>? MessageObserver { get; set; }

    /// <summary>
    /// Resolves log file paths and starts the periodic flush timer.
    /// Idempotent - the first call wins and later calls are no-ops.
    /// </summary>
    public static void Initialize(string? logDirectory = null) => InitializeCore(logDirectory, ActiveName, OldName);

    /// <summary>
    /// Starts logging for a helper process to <c>&lt;helperName&gt;.log</c> in <paramref name="logDirectory"/>,
    /// which is the app's own log folder, so helper lines land beside the app's without sharing its file.
    /// </summary>
    public static void InitializeHelper(string logDirectory, string helperName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(helperName);
        InitializeCore(logDirectory, helperName + HelperLogExtension, helperName + HelperOldLogSuffix);
    }

    /// <summary>
    /// Starts logging for an elevated process without any file: every message goes to <paramref name="forwarder"/>
    /// (for example back to the unelevated app, which writes it into its own log), or is dropped when it is null.
    /// An elevated process must not create, append to or rotate files in the user's log folder: a same-user process
    /// controls that folder and could redirect the write or the rotation with a junction, turning it into an
    /// arbitrary file create or delete as administrator. Call it first, before anything can log; it throws when the
    /// logger already has a file.
    /// </summary>
    public static void InitializeForElevatedProcess(Action<string>? forwarder)
    {
        lock (_gate)
        {
            if (_file != null)
                throw new InvalidOperationException("TADNLog already writes a log file in this process.");

            _forwarder = forwarder ?? DiscardMessage;
            _initialized = true;
        }
    }

    private static void DiscardMessage(string message)
    {
    }

    private static void InitializeCore(string? logDirectory, string activeName, string oldName)
    {
        lock (_gate)
        {
            if (_initialized) return;
            try
            {
                string folder = ResolveLogDirectory(logDirectory);
                Directory.CreateDirectory(folder);
                _file = new TADNLogFile(Path.Combine(folder, activeName), Path.Combine(folder, oldName));
                _timer = new Timer(
                    OnTimerTick, state: null, TimeConstants.LogFlushIntervalMs, TimeConstants.LogFlushIntervalMs);
                _initialized = true;
            }
            catch
            {
                // Swallow. Logger stays uninitialized; subsequent Log() calls become no-ops.
            }
        }
    }

    /// <summary>
    /// Enqueues a single line for the next flush. Auto-prepends a timestamp. Never throws.
    /// </summary>
    public static void Log(string? message)
    {
        try
        {
            MessageObserver?.Invoke(message ?? string.Empty);

            if (!_initialized) Initialize();
            if (_forwarder is { } forwarder)
            {
                forwarder(message ?? string.Empty);
                return;
            }

            TADNLogFile? file = _file;
            if (file == null || _shuttingDown) return;

            string formatted = FormatLine(message);
            file.Enqueue(formatted);

#if DEBUG
            // Mirror to VS Output window during dev. Stripped at compile time in Release.
            Debug.WriteLine(formatted.TrimEnd('\n'));
#endif
        }
        catch
        {
            // Logger must never throw back to callers.
        }
    }

    /// <summary>
    /// Debug-only logging. Calls are erased at compile time in Release via <c>[Conditional("DEBUG")]</c>.
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogDebug(string? message) => Log(message);

    /// <summary>
    /// Synchronously drains the in-memory buffer to disk without tearing down the logger.
    /// Use at intermediate exit points where more code may still want to log before the process actually dies.
    /// Non-throwing.
    /// </summary>
    public static void Flush() => _file?.Flush();

    /// <summary>The file this process logs to, or null while logging is uninitialized or forwarded.</summary>
    internal static string? ActiveLogPath => _file?.ActivePath;

    /// <summary>
    /// Stops the flush timer and performs a final synchronous drain.
    /// Once called, future <see cref="Log"/> calls become no-ops,
    /// so reserve this for the very last termination point. Everywhere else, prefer <see cref="Flush"/>.
    /// Safe to call multiple times.
    /// </summary>
    public static void Shutdown()
    {
        Timer? timerToDispose;
        lock (_gate)
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            timerToDispose = _timer;
            _timer = null;
        }

        try
        {
            if (timerToDispose != null)
            {
                using ManualResetEvent done = new(false);
                if (timerToDispose.Dispose(done)) done.WaitOne(TimeConstants.LogShutdownTimerWaitMs);
            }
        }
        catch
        {
            // ignored
        }

        _file?.Flush();
    }

    internal static string FormatLine(string? message) =>
        "[" + DateTime.Now.ToString(TimestampFmt) + " pid=" + Environment.ProcessId
        + " tid=" + Environment.CurrentManagedThreadId + "] " + (message ?? string.Empty) + "\n";

    private static string ResolveLogDirectory(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            _configuredLogDirectory = explicitDirectory;
            return explicitDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_configuredLogDirectory)) return _configuredLogDirectory;

        return DefaultLogDirectory(
            TrayAppDotNETProgram.SettingsDirectoryOverride,
            Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? AppDomain.CurrentDomain.FriendlyName);
    }

    /// <summary>
    /// The folder a process that logs before any <see cref="Initialize"/> writes to: the settings folder override
    /// (<c>--settings-dir</c>) when one is set, as <c>TrayAppDotNETProgram.AppLocalAppDataDirectory</c> resolves it,
    /// otherwise a folder named after the process under the shared root in %LOCALAPPDATA%.
    /// </summary>
    internal static string DefaultLogDirectory(string? settingsDirectoryOverride, string processName) =>
        !string.IsNullOrWhiteSpace(settingsDirectoryOverride)
            ? settingsDirectoryOverride
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                TrayAppDotNETConstants.SharedRootFolderName,
                processName);

    private static void OnTimerTick(object? state)
    {
        if (_shuttingDown) return;
        _file?.Flush();
    }
}

/// <summary>
/// One log file pair and its pending lines. Flushes are serialized, and each batch is written with a single
/// append-only write, so batches from concurrent flushers and from other processes appending to the same file never
/// interleave or overwrite each other.
/// </summary>
internal sealed class TADNLogFile(string activePath, string oldPath)
{
    private const long MaxBytes = 10L * 1024 * 1024;
    private const int InitialBufferCapacity = 64;
    private const int WriteAttempts = 3;

    // Lines kept in memory while the file cannot be written; beyond this the oldest are dropped and counted
    internal const int MaxPendingLines = 10_000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _bufferGate = new();
    private readonly Lock _flushGate = new();
    private List<string> _buffer = new(InitialBufferCapacity);
    private int _droppedLines;

    public string ActivePath { get; } = activePath;

    public void Enqueue(string formattedLine)
    {
        lock (_bufferGate)
        {
            _buffer.Add(formattedLine);
            TrimBacklog();
        }
    }

    /// <summary>Writes every pending line; on failure the batch stays pending for the next flush.</summary>
    public void Flush()
    {
        lock (_flushGate)
        {
            List<string> batch;
            int dropped;
            lock (_bufferGate)
            {
                if (_buffer.Count == 0) return;
                batch = _buffer;
                _buffer = new List<string>(InitialBufferCapacity);
                dropped = _droppedLines;
                _droppedLines = 0;
            }

            if (dropped > 0)
                batch.Insert(index: 0, TADNLog.FormatLine($"TADNLog: {dropped} lines dropped while the log file was unwritable"));

            if (TryWrite(batch)) return;

            lock (_bufferGate)
            {
                // Older lines go back in front of anything logged during the failed write
                batch.AddRange(_buffer);
                _buffer = batch;
                TrimBacklog();
            }
        }
    }

    private void TrimBacklog()
    {
        int excess = _buffer.Count - MaxPendingLines;
        if (excess <= 0) return;

        _buffer.RemoveRange(index: 0, excess);
        _droppedLines += excess;
    }

    private bool TryWrite(List<string> batch)
    {
        byte[] bytes = Encode(batch);
        for (int attempt = 1; attempt <= WriteAttempts; attempt++)
        {
            try
            {
                EnsureRotated();
                if (AppendAll(bytes)) return true;
            }
            catch (IOException)
            {
                // Retried below
            }
            catch (UnauthorizedAccessException)
            {
                // Retried below
            }

            if (attempt < WriteAttempts) Thread.Sleep(TimeConstants.LogWriteRetryDelayMs);
        }

        return false;
    }

    private static byte[] Encode(List<string> batch)
    {
        int byteCount = 0;
        foreach (string line in batch) byteCount += Utf8NoBom.GetByteCount(line);

        byte[] bytes = new byte[byteCount];
        int offset = 0;
        foreach (string line in batch) offset += Utf8NoBom.GetBytes(line, charIndex: 0, line.Length, bytes, offset);
        return bytes;
    }

    /// <summary>
    /// Appends through a handle opened with append-only access, where Windows places every write at the current end
    /// of file. The handle shares read, write and delete, so a reader, another writer, or a rotation in another
    /// process never blocks this one.
    /// </summary>
    private bool AppendAll(byte[] bytes)
    {
        IntPtr handle = CreateFileW(
            ActivePath,
            FILE_APPEND_DATA | Kernel32.SYNCHRONIZE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);
        if (handle == Kernel32.INVALID_HANDLE_VALUE) return false;

        try
        {
            int written = 0;
            while (written < bytes.Length)
            {
                unsafe
                {
                    fixed (byte* data = &bytes[written])
                    {
                        if (!WriteFile(handle, data, (uint)(bytes.Length - written), out uint count, IntPtr.Zero) ||
                            count == 0)
                            return false;

                        written += (int)count;
                    }
                }
            }

            return true;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private void EnsureRotated()
    {
        if (!File.Exists(ActivePath)) return;

        FileInfo fi = new(ActivePath);
        bool sizeOver = fi.Length >= MaxBytes;
        bool ageOver = (DateTime.UtcNow - fi.CreationTimeUtc).TotalMilliseconds >= TimeConstants.LogMaxAgeMs;
        if (!sizeOver && !ageOver) return;

        try
        {
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch
        {
            // ignored
        }

        try { File.Move(ActivePath, oldPath); }
        catch { return; }

        // Recreate the active file immediately and pin its creation time so the age trigger has a stable anchor
        // even on filesystems that inherit creation time from the parent.
        try
        {
            using (File.Create(ActivePath)) { }

            File.SetCreationTimeUtc(ActivePath, DateTime.UtcNow);
        }
        catch
        {
            // ignored
        }
    }

    private const uint FILE_APPEND_DATA = 0x0004;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint FILE_SHARE_DELETE = 0x4;
    private const uint OPEN_ALWAYS = 4;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool WriteFile(
        IntPtr hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}

using System.Collections.Concurrent;
using System.Text;

namespace TrayAppDotNETCommon.Serialization;

/// <summary>
/// Crash-safe file replacement for every persisted data file: the content goes to a unique temporary file beside
/// the target, is flushed to disk, and only then replaces the target. A crash mid-write leaves either the old file or
/// the new one, never a truncated mix.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // One gate per destination (full path, case-insensitive like NTFS); the set of persisted files is small and fixed
    private static readonly ConcurrentDictionary<string, Lock> ReplaceGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes the file through <paramref name="write"/>, then atomically replaces <paramref name="path"/>.</summary>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            ReplaceWithRetry(temporaryPath, path);
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    public static void WriteAllBytes(string path, ReadOnlyMemory<byte> bytes) =>
        Write(path, stream => stream.Write(bytes.Span));

    /// <summary>Writes UTF-8 text without a byte order mark, matching <see cref="File.WriteAllText(string, string)"/>.</summary>
    public static void WriteAllText(string path, string contents) =>
        WriteAllBytes(path, Utf8NoBom.GetBytes(contents));

    /// <summary>
    /// Moves a staged file over its destination. <c>MoveFileEx(REPLACE_EXISTING)</c> fails with access denied while
    /// another replace of the same destination is still completing (the old file is briefly delete-pending), so
    /// replacements of one destination inside this process take turns. Sharing violations and access denials caused by
    /// other processes (a sibling app reading the file, antivirus or the search indexer opening the new temporary file)
    /// are retried <c>SettingsFileReplaceRetryCount</c> times, <c>SettingsFileReplaceRetryDelayMs</c> apart.
    /// </summary>
    public static void ReplaceWithRetry(string temporaryPath, string path)
    {
        Lock gate = ReplaceGates.GetOrAdd(Path.GetFullPath(path), static _ => new Lock());
        lock (gate)
        {
            for (int attempt = 1;; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, path, overwrite: true);
                    return;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException &&
                    attempt < TimeConstants.SettingsFileReplaceRetryCount)
                {
                    Thread.Sleep(TimeConstants.SettingsFileReplaceRetryDelayMs);
                }
            }
        }
    }

    /// <summary>Removes the staged file after a failed write, retrying while a scanner still has it open.</summary>
    private static void DeleteTemporaryFile(string temporaryPath)
    {
        for (int attempt = 1;; attempt++)
        {
            try
            {
                File.Delete(temporaryPath);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < TimeConstants.SettingsFileReplaceRetryCount)
                {
                    Thread.Sleep(TimeConstants.SettingsFileReplaceRetryDelayMs);
                    continue;
                }

                TADNLog.Log($"AtomicFile: temporary file cleanup failed ({temporaryPath}): {exception.Message}");
                return;
            }
        }
    }
}

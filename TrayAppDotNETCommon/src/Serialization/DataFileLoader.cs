using System.Globalization;

namespace TrayAppDotNETCommon.Serialization;

/// <summary>Outcome of reading a persisted data file.</summary>
public enum DataFileLoadResult
{
    /// <summary>The file does not exist. The only outcome that may create the file from defaults.</summary>
    Missing,

    /// <summary>The file was read and parsed.</summary>
    Loaded,

    /// <summary>The file exists but could not be parsed. It must be kept, never overwritten.</summary>
    Corrupt,

    /// <summary>The file exists but stayed unreadable (sharing violation or access denied) through every retry.</summary>
    Locked
}

/// <summary>
/// Classifies a data-file read as missing, loaded, corrupt or locked, so no caller can mistake an unreadable file for
/// an absent one and write defaults over it.
/// </summary>
public static class DataFileLoader
{
    /// <summary>
    /// Opens and parses <paramref name="path"/>. A sharing violation or access denial is retried
    /// <c>SettingsFileReplaceRetryCount</c> times, <c>SettingsFileReplaceRetryDelayMs</c> apart, before the file is
    /// reported <see cref="DataFileLoadResult.Locked"/>. Any exception thrown by <paramref name="parse"/> other than
    /// an <see cref="IOException"/> reports <see cref="DataFileLoadResult.Corrupt"/>. Nothing on disk is changed.
    /// </summary>
    public static DataFileLoadResult TryLoad<T>(
        string path,
        Func<Stream, T> parse,
        Action<string> log,
        out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(log);

        value = default;
        for (int attempt = 1;; attempt++)
        {
            if (!File.Exists(path)) return DataFileLoadResult.Missing;

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                try
                {
                    value = parse(stream);
                    return DataFileLoadResult.Loaded;
                }
                catch (Exception exception) when (exception is not IOException)
                {
                    value = default;
                    log($"{path} is corrupt: {exception}");
                    return DataFileLoadResult.Corrupt;
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return DataFileLoadResult.Missing;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < TimeConstants.SettingsFileReplaceRetryCount)
                {
                    Thread.Sleep(TimeConstants.SettingsFileReplaceRetryDelayMs);
                    continue;
                }

                log($"{path} is locked after {attempt} attempts: {exception.Message}");
                return DataFileLoadResult.Locked;
            }
        }
    }

    /// <summary>
    /// Moves a corrupt file aside to <c>{path}.corrupt-{UTC yyyyMMddHHmmss}</c> (with a numeric suffix when that name is
    /// taken) so defaults can be saved without destroying it. Returns the backup path, or null when the move failed;
    /// the caller must then leave the file alone.
    /// </summary>
    public static string? MoveCorruptAside(string path, Action<string> log, DateTime? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);

        string stamp = (utcNow ?? DateTime.UtcNow).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string backupPath = $"{path}.corrupt-{stamp}";
        for (int suffix = 1; File.Exists(backupPath); suffix++)
            backupPath = $"{path}.corrupt-{stamp}-{suffix.ToString(CultureInfo.InvariantCulture)}";

        try
        {
            File.Move(path, backupPath, overwrite: false);
            log($"{path} was moved aside to {backupPath}; defaults are used instead.");
            return backupPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log($"{path} is corrupt and could not be moved aside: {exception.Message}");
            return null;
        }
    }
}

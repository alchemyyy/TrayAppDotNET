using System.Security.Cryptography;
using System.Text;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Cross-process lock on one install directory. Every app installs into the same flat folder and writes the
/// same shared files there (native rendering DLLs, legal files, .notices), so two installs copying at once
/// would hit sharing violations. The bundle installer runs several apps' installers concurrently and relies
/// on this to serialize only their copy phase; in-app updates take it around their file transaction, since
/// several apps updating to one release at once write the same files.
/// The lock is advisory: only code that takes it is excluded, so the uninstall batch script is not.
/// </summary>
public sealed class TrayAppDotNETInstallDirectoryLock : IDisposable
{
    // Global so a system install in one session excludes one in another; the path hash keeps the name short
    // and free of the backslashes a kernel object name cannot carry
    private const string MutexNamePrefix = @"Global\TrayAppDotNET.InstallDirectory.";

    private readonly Mutex? _mutex;

    private TrayAppDotNETInstallDirectoryLock(Mutex? mutex) => _mutex = mutex;

    /// <summary>Mutex name for <paramref name="directory"/>; equal for every spelling of the same full path.</summary>
    public static string MutexName(string directory)
    {
        string normalized = PathNormalization.Normalize(directory).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return MutexNamePrefix + Convert.ToHexString(hash);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the directory. Returns null on timeout.
    /// <paramref name="waiting"/> runs once, before blocking, when another process holds the lock.
    /// A mutex this process cannot open (created by an elevated install while this one is not) is logged and
    /// skipped, which is the unlocked behaviour every install had before the lock existed.
    /// </summary>
    public static TrayAppDotNETInstallDirectoryLock? Acquire(
        string directory,
        TimeSpan timeout,
        Action? waiting,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        string name = MutexName(directory);
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
        }
        catch (UnauthorizedAccessException exception)
        {
            log($"TrayAppDotNETInstallDirectoryLock: cannot open {name} for {directory}; copying unlocked: {exception.Message}");
            return new TrayAppDotNETInstallDirectoryLock(mutex: null);
        }

        try
        {
            if (WaitOne(mutex, TimeSpan.Zero)) return new TrayAppDotNETInstallDirectoryLock(mutex);

            log($"TrayAppDotNETInstallDirectoryLock: waiting for another install into {directory}");
            waiting?.Invoke();
            if (WaitOne(mutex, timeout)) return new TrayAppDotNETInstallDirectoryLock(mutex);

            log($"TrayAppDotNETInstallDirectoryLock: timed out after {timeout.TotalSeconds:0} s waiting for {directory}");
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_mutex == null) return;

        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static bool WaitOne(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died mid-copy; the copy overwrites every file, so the next one repairs it
            return true;
        }
    }
}

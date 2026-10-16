using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class InstallDirectoryLockTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void MutexNameIsTheSameForEverySpellingOfOneDirectory()
    {
        string directory = UniqueDirectory();

        string name = TrayAppDotNETInstallDirectoryLock.MutexName(directory);

        Assert.Equal(name, TrayAppDotNETInstallDirectoryLock.MutexName(directory.ToLowerInvariant() + Path.DirectorySeparatorChar));
        Assert.Equal(name, TrayAppDotNETInstallDirectoryLock.MutexName(Path.Combine(directory, "sub", "..")));
        Assert.NotEqual(name, TrayAppDotNETInstallDirectoryLock.MutexName(directory + "-other"));
        Assert.DoesNotContain('\\', name[@"Global\".Length..]);
    }

    [Fact]
    public void ASecondInstallWaitsForTheFirstAndTimesOutWhileItHolds()
    {
        string directory = UniqueDirectory();
        // Mutex ownership is per thread, so the competing install runs on its own thread
        using TrayAppDotNETInstallDirectoryLock? held = TrayAppDotNETInstallDirectoryLock.Acquire(directory, ShortTimeout, waiting: null, Ignore);
        Assert.NotNull(held);

        bool waited = false;
        TrayAppDotNETInstallDirectoryLock? competing = RunOnThread(() =>
            TrayAppDotNETInstallDirectoryLock.Acquire(directory, ShortTimeout, () => waited = true, Ignore));

        Assert.Null(competing);
        Assert.True(waited);
    }

    [Fact]
    public void ASecondInstallProceedsOnceTheFirstReleases()
    {
        string directory = UniqueDirectory();
        TrayAppDotNETInstallDirectoryLock? held = TrayAppDotNETInstallDirectoryLock.Acquire(directory, ShortTimeout, waiting: null, Ignore);
        Assert.NotNull(held);
        held.Dispose();

        bool waited = false;
        bool acquired = RunOnThread(() =>
        {
            using TrayAppDotNETInstallDirectoryLock? next =
                TrayAppDotNETInstallDirectoryLock.Acquire(directory, LongTimeout, () => waited = true, Ignore);
            return next != null;
        });

        Assert.True(acquired);
        Assert.False(waited);
    }

    [Fact]
    public void DifferentDirectoriesDoNotExcludeEachOther()
    {
        using TrayAppDotNETInstallDirectoryLock? first =
            TrayAppDotNETInstallDirectoryLock.Acquire(UniqueDirectory(), ShortTimeout, waiting: null, Ignore);
        string otherDirectory = UniqueDirectory();

        bool acquired = RunOnThread(() =>
        {
            using TrayAppDotNETInstallDirectoryLock? second =
                TrayAppDotNETInstallDirectoryLock.Acquire(otherDirectory, ShortTimeout, waiting: null, Ignore);
            return second != null;
        });

        Assert.NotNull(first);
        Assert.True(acquired);
    }

    private static string UniqueDirectory() =>
        Path.Combine(Path.GetTempPath(), "TrayAppDotNETInstallDirectoryLockTests", Guid.NewGuid().ToString("N"));

    private static void Ignore(string message)
    {
    }

    private static T RunOnThread<T>(Func<T> action)
    {
        T result = default!;
        Thread thread = new(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }
}

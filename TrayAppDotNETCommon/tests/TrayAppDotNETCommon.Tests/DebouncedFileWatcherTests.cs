using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class DebouncedFileWatcherTests : IDisposable
{
    private const string WatchedFileName = "watched.xml";
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(10);

    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    private string WatchedPath => Path.Combine(TestDirectory, WatchedFileName);

    [Fact]
    public async Task StartCreatesTheMissingDirectoryAndReportsAFileWrittenInPlace()
    {
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using DebouncedFileWatcher watcher = new(WatchedPath, QuietPeriod, () => changed.TrySetResult());

        watcher.Start();
        Assert.True(Directory.Exists(TestDirectory));
        await File.WriteAllTextAsync(WatchedPath, contents: "<versions />");

        await changed.Task.WaitAsync(CallbackTimeout);
    }

    [Fact]
    public async Task AnAtomicReplaceRaisesOneCallbackAfterItsEventsSettle()
    {
        Directory.CreateDirectory(TestDirectory);
        await File.WriteAllTextAsync(WatchedPath, contents: "<versions />");
        string temporaryPath = WatchedPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, contents: "<versions release=\"2\" />");
        int callbackCount = 0;
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using DebouncedFileWatcher watcher = new(
            WatchedPath,
            QuietPeriod,
            () =>
            {
                Interlocked.Increment(ref callbackCount);
                changed.TrySetResult();
            });
        watcher.Start();

        // The same replace the update cache writer performs
        File.Move(temporaryPath, WatchedPath, overwrite: true);
        await changed.Task.WaitAsync(CallbackTimeout);
        await Task.Delay(QuietPeriod * 3);

        Assert.Equal(expected: 1, Volatile.Read(ref callbackCount));
    }

    public void Dispose() => _temp.Dispose();
}

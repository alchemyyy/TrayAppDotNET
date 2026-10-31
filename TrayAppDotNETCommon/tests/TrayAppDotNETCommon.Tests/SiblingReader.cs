namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// Holds a file open for reading the way a sibling app or a scanner does, and lets go a set time after a writer has
/// staged its replacement beside it (a file matching <c>stagedFilePattern</c>). The release runs on its own
/// high-priority thread: an await continuation can queue behind other tests on a loaded runner for longer than the
/// writer's whole replace-retry budget.
/// </summary>
internal sealed class SiblingReader : IDisposable
{
    // A writer that gives up deletes its staged file at once, so the wait can miss it; it then lets go without holding
    private static readonly TimeSpan StagingTimeout = TimeSpan.FromSeconds(10);

    private readonly FileStream _stream;
    private readonly Thread _release;

    public SiblingReader(string path, string stagedFilePattern, TimeSpan holdAfterStaging)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string directory = Path.GetDirectoryName(path)!;
        _release = new Thread(() =>
        {
            if (SpinWait.SpinUntil(() => Directory.EnumerateFiles(directory, stagedFilePattern).Any(), StagingTimeout))
                Thread.Sleep(holdAfterStaging);

            _stream.Dispose();
        }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = nameof(SiblingReader) };
        _release.Start();
    }

    /// <summary>Waits until the reader has let go.</summary>
    public void Dispose()
    {
        _release.Join();
        _stream.Dispose();
    }
}

using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    private string FilePath => Path.Combine(TestDirectory, "data.txt");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ConcurrentWritersToOneFileAllSucceedAndLeaveOneCompleteVersion()
    {
        const int writerCount = 64;
        using ManualResetEventSlim start = new(initialState: false);
        Task[] writers = new Task[writerCount];
        for (int index = 0; index < writerCount; index++)
        {
            string contents = $"writer {index} " + new string('x', 64 * 1024);
            writers[index] = Task.Run(() =>
            {
                start.Wait();
                AtomicFile.WriteAllText(FilePath, contents);
            });
        }

        start.Set();
        await Task.WhenAll(writers);

        string written = await File.ReadAllTextAsync(FilePath);
        Assert.StartsWith(expectedStartString: "writer ", written);
        Assert.Equal(expected: 64 * 1024, written.Length - written.IndexOf('x'));
        Assert.Equal(["data.txt"], Directory.GetFiles(TestDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ReplaceWaitsForAReaderThatBrieflyHoldsTheDestination()
    {
        AtomicFile.WriteAllText(FilePath, "old");
        Task write;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            write = Task.Run(() => AtomicFile.WriteAllText(FilePath, "new"));
            await Task.Delay(100);
        }

        await write;

        Assert.Equal(expected: "new", await File.ReadAllTextAsync(FilePath));
        Assert.Single(Directory.GetFiles(TestDirectory));
    }

    [Fact]
    public void AFailedWriteLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(TestDirectory);

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.Write(FilePath, static _ => throw new InvalidOperationException("serialization failed")));

        Assert.Empty(Directory.GetFiles(TestDirectory));
    }
}

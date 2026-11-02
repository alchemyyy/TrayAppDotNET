using System.Diagnostics;
using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class DetachedCleanupTests : IDisposable
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestRoot => _temp.Combine("root");

    [Theory]
    [InlineData("José Müller")]
    [InlineData("Tom & Jerry")]
    [InlineData("100% ^done^ (x) !bang!")]
    [InlineData("ユーザー")]
    public void RemovesPathsWhoseNamesCmdWouldMangle(string name)
    {
        string directory = Path.Combine(TestRoot, name, "staging");
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "nested", "payload.dll"), "payload");
        string file = Path.Combine(TestRoot, name, name + ".zip");
        File.WriteAllText(file, "archive");
        string keptParent = Path.Combine(TestRoot, name);
        File.WriteAllText(Path.Combine(keptParent, "keep.txt"), "keeps the parent folder");

        RunToCompletion(
        [
            new DetachedCleanupTarget(DetachedCleanupAction.DeleteDirectoryTree, directory),
            new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, file),
            new DetachedCleanupTarget(DetachedCleanupAction.RemoveDirectoryIfEmpty, keptParent)
        ]);

        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(keptParent));
    }

    [Fact]
    public void KeepsRetryingUntilALockedFileIsReleased()
    {
        Directory.CreateDirectory(TestRoot);
        string file = Path.Combine(TestRoot, "locked & busy.exe");
        File.WriteAllText(file, "in use");

        Process process;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            process = Start([new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, file)]);
            Thread.Sleep(1500);
            Assert.True(File.Exists(file));
        }

        using (process)
            Assert.True(process.WaitForExit(CleanupTimeout), "The cleanup did not finish.");
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void NeverPutsAPathIntoTheCommandLine()
    {
        string path = @"C:\Users\Jos & Co\%PATH%\x.exe";
        ProcessStartInfo startInfo = DetachedCleanup.CreateStartInfo(
            [new DetachedCleanupTarget(DetachedCleanupAction.DeleteFile, path)],
            attempts: 3,
            retryDelaySeconds: 1);

        Assert.DoesNotContain("Jos", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Equal(path, startInfo.Environment[DetachedCleanup.PathVariablePrefix + "0"]);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), startInfo.FileName);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "timeout.exe"), startInfo.Environment[DetachedCleanup.TimeoutVariable]);
    }

    public void Dispose() => _temp.Dispose();

    private static void RunToCompletion(IReadOnlyList<DetachedCleanupTarget> targets)
    {
        using Process process = Start(targets);
        Assert.True(process.WaitForExit(CleanupTimeout), "The cleanup did not finish.");
    }

    private static Process Start(IReadOnlyList<DetachedCleanupTarget> targets) =>
        Process.Start(DetachedCleanup.CreateStartInfo(targets, attempts: 20, retryDelaySeconds: 1))
        ?? throw new InvalidOperationException("cmd.exe did not start.");
}

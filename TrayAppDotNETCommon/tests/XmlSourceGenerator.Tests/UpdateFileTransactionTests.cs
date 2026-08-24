using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UpdateFileTransactionTests
{
    [Fact]
    public void BuildPlanSkipsUnchangedSharedFilesAndPlacesExecutableLast()
    {
        using TestDirectories directories = new();
        string sourceExecutable = directories.WriteSource("TestTrayAppDotNET.exe", "new app");
        string targetExecutable = directories.WriteTarget("TestTrayAppDotNET.exe", "old app");
        _ = directories.WriteSource("settings.json", "new settings");
        _ = directories.WriteTarget("settings.json", "old settings");
        _ = directories.WriteSource("libSkiaSharp.dll", "same native file");
        _ = directories.WriteTarget("libSkiaSharp.dll", "same native file");

        UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
            directories.Source,
            directories.Target,
            targetExecutable,
            static _ => { });

        Assert.False(plan.StopSiblingApps);
        Assert.DoesNotContain(
            plan.Files,
            file => file.RelativePath.Equals("libSkiaSharp.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sourceExecutable, plan.Files[^1].SourcePath);
    }

    [Fact]
    public void BuildPlanRejectsPayloadWithoutInstalledExecutable()
    {
        using TestDirectories directories = new();
        _ = directories.WriteSource("data.txt", "new data");
        string targetExecutable = directories.WriteTarget("TestTrayAppDotNET.exe", "old app");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            UpdateFileTransaction.BuildPlan(
                directories.Source,
                directories.Target,
                targetExecutable,
                static _ => { }));

        Assert.Contains("does not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyReplacesEveryFileAndRemovesTransactionArtifacts()
    {
        using TestDirectories directories = new();
        _ = directories.WriteSource("TestTrayAppDotNET.exe", "new app");
        string targetExecutable = directories.WriteTarget("TestTrayAppDotNET.exe", "old app");
        _ = directories.WriteSource("data.txt", "new data");
        _ = directories.WriteTarget("data.txt", "old data");

        UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
            directories.Source,
            directories.Target,
            targetExecutable,
            static _ => { });
        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.Succeeded, result.Status);
        Assert.Equal("new app", File.ReadAllText(targetExecutable));
        Assert.Equal("new data", File.ReadAllText(Path.Combine(directories.Target, "data.txt")));
        Assert.Empty(Directory.EnumerateFiles(directories.Target, "*.tadn-update-*"));
    }

    [Fact]
    public void ApplyRestoresFilesAlreadyReplacedWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string firstSource = directories.WriteSource("first.txt", "new first");
        string firstTarget = directories.WriteTarget("first.txt", "old first");
        string secondSource = directories.WriteSource("second.txt", "new second");
        string secondTarget = directories.WriteTarget("second.txt", "old second");
        string firstTemporary = firstTarget + ".tmp";
        string firstBackup = firstTarget + ".bak";
        string secondTemporary = secondTarget + ".tmp";

        UpdateFilePlan plan = new(
            [
                new UpdateFileOperation("first.txt", firstSource, firstTarget, firstTemporary, firstBackup),
                new UpdateFileOperation(
                    "second.txt",
                    secondSource,
                    secondTarget,
                    secondTemporary,
                    "invalid\0backup")
            ],
            StopSiblingApps: false);

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.Equal("old first", File.ReadAllText(firstTarget));
        Assert.Equal("old second", File.ReadAllText(secondTarget));
        Assert.False(File.Exists(firstTemporary));
        Assert.False(File.Exists(firstBackup));
    }

    [Fact]
    public void ApplyRemovesNewFilesWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string newSource = directories.WriteSource("new.txt", "new file");
        string newTarget = Path.Combine(directories.Target, "new.txt");
        string existingSource = directories.WriteSource("existing.txt", "new existing");
        string existingTarget = directories.WriteTarget("existing.txt", "old existing");

        UpdateFilePlan plan = new(
            [
                new UpdateFileOperation(
                    "new.txt",
                    newSource,
                    newTarget,
                    newTarget + ".tmp",
                    newTarget + ".bak"),
                new UpdateFileOperation(
                    "existing.txt",
                    existingSource,
                    existingTarget,
                    existingTarget + ".tmp",
                    "invalid\0backup")
            ],
            StopSiblingApps: false);

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.False(File.Exists(newTarget));
        Assert.Equal("old existing", File.ReadAllText(existingTarget));
    }

    private sealed class TestDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"TrayAppDotNET-update-tests-{Guid.NewGuid():N}");

        public TestDirectories()
        {
            Source = Path.Combine(_root, "source");
            Target = Path.Combine(_root, "target");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Target);
        }

        public string Source { get; }

        public string Target { get; }

        public string WriteSource(string relativePath, string content) =>
            Write(Source, relativePath, content);

        public string WriteTarget(string relativePath, string content) =>
            Write(Target, relativePath, content);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        private static string Write(string directory, string relativePath, string content)
        {
            string path = Path.Combine(directory, relativePath);
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, content);
            return path;
        }
    }
}

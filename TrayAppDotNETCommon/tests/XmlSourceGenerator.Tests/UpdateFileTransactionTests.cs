using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UpdateFileTransactionTests
{
    [Fact]
    public void BuildPlanSkipsUnchangedSharedFilesAndPlacesExecutableLast()
    {
        using TestDirectories directories = new();
        string sourceExecutable = directories.WriteSource(relativePath: "TestTrayAppDotNET.exe", content: "new app");
        string targetExecutable = directories.WriteTarget(relativePath: "TestTrayAppDotNET.exe", content: "old app");
        _ = directories.WriteSource(relativePath: "settings.json", content: "new settings");
        _ = directories.WriteTarget(relativePath: "settings.json", content: "old settings");
        _ = directories.WriteSource(relativePath: "libSkiaSharp.dll", content: "same native file");
        _ = directories.WriteTarget(relativePath: "libSkiaSharp.dll", content: "same native file");

        UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
            directories.Source,
            directories.Target,
            targetExecutable,
            static _ => { });

        Assert.False(plan.StopSiblingApps);
        Assert.DoesNotContain(
            plan.Files,
            file => file.RelativePath.Equals(value: "libSkiaSharp.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sourceExecutable, plan.Files[^1].SourcePath);
    }

    [Fact]
    public void BuildPlanRejectsPayloadWithoutInstalledExecutable()
    {
        using TestDirectories directories = new();
        _ = directories.WriteSource(relativePath: "data.txt", content: "new data");
        string targetExecutable = directories.WriteTarget(relativePath: "TestTrayAppDotNET.exe", content: "old app");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            UpdateFileTransaction.BuildPlan(
                directories.Source,
                directories.Target,
                targetExecutable,
                static _ => { }));

        Assert.Contains(expectedSubstring: "does not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyReplacesEveryFileAndRemovesTransactionArtifacts()
    {
        using TestDirectories directories = new();
        _ = directories.WriteSource(relativePath: "TestTrayAppDotNET.exe", content: "new app");
        string targetExecutable = directories.WriteTarget(relativePath: "TestTrayAppDotNET.exe", content: "old app");
        _ = directories.WriteSource(relativePath: "data.txt", content: "new data");
        _ = directories.WriteTarget(relativePath: "data.txt", content: "old data");

        UpdateFilePlan plan = UpdateFileTransaction.BuildPlan(
            directories.Source,
            directories.Target,
            targetExecutable,
            static _ => { });
        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.Succeeded, result.Status);
        Assert.Equal(expected: "new app", File.ReadAllText(targetExecutable));
        Assert.Equal(expected: "new data", File.ReadAllText(Path.Combine(directories.Target, path2: "data.txt")));
        Assert.Empty(Directory.EnumerateFiles(directories.Target, searchPattern: "*.tadn-update-*"));
    }

    [Fact]
    public void ApplyRestoresFilesAlreadyReplacedWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string firstSource = directories.WriteSource(relativePath: "first.txt", content: "new first");
        string firstTarget = directories.WriteTarget(relativePath: "first.txt", content: "old first");
        string secondSource = directories.WriteSource(relativePath: "second.txt", content: "new second");
        string secondTarget = directories.WriteTarget(relativePath: "second.txt", content: "old second");
        string firstTemporary = firstTarget + ".tmp";
        string firstBackup = firstTarget + ".bak";
        string secondTemporary = secondTarget + ".tmp";

        UpdateFilePlan plan = new(
            [
                new UpdateFileOperation(relativePath: "first.txt", firstSource, firstTarget, firstTemporary,
                    firstBackup),
                new UpdateFileOperation(
                    relativePath: "second.txt",
                    secondSource,
                    secondTarget,
                    secondTemporary,
                    backupPath: "invalid\0backup")
            ],
            StopSiblingApps: false);

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.Equal(expected: "old first", File.ReadAllText(firstTarget));
        Assert.Equal(expected: "old second", File.ReadAllText(secondTarget));
        Assert.False(File.Exists(firstTemporary));
        Assert.False(File.Exists(firstBackup));
    }

    [Fact]
    public void ApplyRemovesNewFilesWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string newSource = directories.WriteSource(relativePath: "new.txt", content: "new file");
        string newTarget = Path.Combine(directories.Target, path2: "new.txt");
        string existingSource = directories.WriteSource(relativePath: "existing.txt", content: "new existing");
        string existingTarget = directories.WriteTarget(relativePath: "existing.txt", content: "old existing");

        UpdateFilePlan plan = new(
            [
                new UpdateFileOperation(
                    relativePath: "new.txt",
                    newSource,
                    newTarget,
                    newTarget + ".tmp",
                    newTarget + ".bak"),
                new UpdateFileOperation(
                    relativePath: "existing.txt",
                    existingSource,
                    existingTarget,
                    existingTarget + ".tmp",
                    backupPath: "invalid\0backup")
            ],
            StopSiblingApps: false);

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.False(File.Exists(newTarget));
        Assert.Equal(expected: "old existing", File.ReadAllText(existingTarget));
    }

    private sealed class TestDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"TrayAppDotNET-update-tests-{Guid.NewGuid():N}");

        public TestDirectories()
        {
            Source = Path.Combine(_root, path2: "source");
            Target = Path.Combine(_root, path2: "target");
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

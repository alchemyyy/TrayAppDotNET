using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class UpdateFileTransactionTests
{
    private const string ApplicationName = "TestTrayAppDotNET";
    private const string ExecutableName = ApplicationName + ".exe";

    [Fact]
    public void BuildPlanReplacesOnlyThePayloadAndOrdersNativeDllsFirstAndTheExeLast()
    {
        using TestDirectories directories = new();
        directories.WritePayload(skiaContent: "same native file", glesContent: "new native file");
        _ = directories.WriteSource(relativePath: "install.bat", content: "batch installer");
        _ = directories.WriteSource(relativePath: "stray.txt", content: "not part of the payload");
        string targetExecutable = directories.WriteTarget(ExecutableName, content: "old app");
        _ = directories.WriteTarget(relativePath: "libSkiaSharp.dll", content: "same native file");
        _ = directories.WriteTarget(relativePath: "av_libglesv2.dll", content: "old native file");

        UpdateFilePlan plan = BuildPlan(directories, targetExecutable);

        string[] relativePaths = [.. plan.Files.Select(file => file.RelativePath)];
        Assert.True(plan.StopSiblingApps);
        Assert.DoesNotContain("install.bat", relativePaths);
        Assert.DoesNotContain("stray.txt", relativePaths);
        Assert.DoesNotContain("libSkiaSharp.dll", relativePaths);
        Assert.Contains(Path.Combine(".notices", "skia.txt"), relativePaths);
        Assert.Equal("av_libglesv2.dll", relativePaths[0]);
        Assert.Equal(ExecutableName, relativePaths[^1]);
    }

    [Fact]
    public void BuildPlanKeepsSiblingAppsRunningWhenNoNativeDllChanges()
    {
        using TestDirectories directories = new();
        directories.WritePayload(skiaContent: "same", glesContent: "same");
        string targetExecutable = directories.WriteTarget(ExecutableName, content: "old app");
        _ = directories.WriteTarget(relativePath: "libSkiaSharp.dll", content: "same");
        _ = directories.WriteTarget(relativePath: "av_libglesv2.dll", content: "same");
        _ = directories.WriteTarget(relativePath: "libHarfBuzzSharp.dll", content: "harfbuzz");

        UpdateFilePlan plan = BuildPlan(directories, targetExecutable);

        Assert.False(plan.StopSiblingApps);
    }

    [Fact]
    public void BuildPlanRejectsPayloadWithoutInstalledExecutable()
    {
        using TestDirectories directories = new();
        directories.WritePayload(skiaContent: "skia", glesContent: "gles");
        File.Delete(Path.Combine(directories.Source, ExecutableName));
        string targetExecutable = directories.WriteTarget(ExecutableName, content: "old app");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BuildPlan(directories, targetExecutable));

        Assert.Contains(ExecutableName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyReplacesEveryFileAndRemovesTheJournalAndBackups()
    {
        using TestDirectories directories = new();
        directories.WritePayload(skiaContent: "new skia", glesContent: "new gles");
        string targetExecutable = directories.WriteTarget(ExecutableName, content: "old app");
        _ = directories.WriteTarget(relativePath: "libSkiaSharp.dll", content: "old skia");

        UpdateFilePlan plan = BuildPlan(directories, targetExecutable);
        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.Succeeded, result.Status);
        Assert.Equal(expected: "new app", File.ReadAllText(targetExecutable));
        Assert.Equal(expected: "new skia", File.ReadAllText(Path.Combine(directories.Target, "libSkiaSharp.dll")));
        Assert.Empty(Directory.EnumerateFiles(directories.Target, searchPattern: "*.bak", SearchOption.AllDirectories));
        Assert.False(File.Exists(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName)));
    }

    [Fact]
    public void ApplyRestoresFilesAlreadyReplacedWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string firstSource = directories.WriteSource(relativePath: "first.txt", content: "new first");
        string firstTarget = directories.WriteTarget(relativePath: "first.txt", content: "old first");
        string secondSource = directories.WriteSource(relativePath: "second.txt", content: "new second");
        string secondTarget = directories.WriteTarget(relativePath: "second.txt", content: "old second");
        string firstBackup = firstTarget + ".bak";

        UpdateFilePlan plan = Plan(
            directories,
            new UpdateFileOperation(relativePath: "first.txt", firstSource, firstTarget, firstBackup),
            new UpdateFileOperation(relativePath: "second.txt", secondSource, secondTarget, backupPath: "invalid\0backup"));

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.Equal(expected: "old first", File.ReadAllText(firstTarget));
        Assert.Equal(expected: "old second", File.ReadAllText(secondTarget));
        Assert.False(File.Exists(firstBackup));
        Assert.False(File.Exists(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName)));
    }

    [Fact]
    public void ApplyRemovesNewFilesWhenALaterCommitFails()
    {
        using TestDirectories directories = new();
        string newSource = directories.WriteSource(relativePath: "new.txt", content: "new file");
        string newTarget = Path.Combine(directories.Target, path2: "new.txt");
        string existingSource = directories.WriteSource(relativePath: "existing.txt", content: "new existing");
        string existingTarget = directories.WriteTarget(relativePath: "existing.txt", content: "old existing");

        UpdateFilePlan plan = Plan(
            directories,
            new UpdateFileOperation(relativePath: "new.txt", newSource, newTarget, newTarget + ".bak"),
            new UpdateFileOperation(relativePath: "existing.txt", existingSource, existingTarget, backupPath: "invalid\0backup"));

        UpdateFileTransactionResult result = UpdateFileTransaction.Apply(plan, static _ => { });

        Assert.Equal(UpdateFileTransactionStatus.FailedRolledBack, result.Status);
        Assert.False(File.Exists(newTarget));
        Assert.Equal(expected: "old existing", File.ReadAllText(existingTarget));
    }

    [Fact]
    public void RecoverInterruptedRollsBackAnApplyWhoseWorkerDiedAndSweepsItsLeftovers()
    {
        using TestDirectories directories = new();
        string staging = UpdateFileTransaction.NewStagingDirectory(directories.Target, ApplicationName);
        Directory.CreateDirectory(staging);
        // The worker died after replacing the exe and moving the DLL aside, before the DLL's new copy landed
        string exeTarget = directories.WriteTarget(ExecutableName, content: "new app");
        string exeBackup = directories.WriteTarget(ExecutableName + ".tadn-update-" + ApplicationName + "-x-1.bak", "old app");
        string dllTarget = Path.Combine(directories.Target, "libSkiaSharp.dll");
        string dllBackup = directories.WriteTarget("libSkiaSharp.dll.tadn-update-" + ApplicationName + "-x-0.bak", "old skia");
        string dllStaged = Path.Combine(staging, "libSkiaSharp.dll");
        File.WriteAllText(dllStaged, "new skia");
        string addedTarget = directories.WriteTarget("added.txt", content: "added by the update");
        string addedStaged = Path.Combine(staging, "added.txt");
        File.WriteAllLines(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName),
        [
            "TrayAppDotNET update journal 2",
            string.Join('\t', "libSkiaSharp.dll", dllStaged, dllTarget, dllBackup, "1"),
            string.Join('\t', "added.txt", addedStaged, addedTarget, addedTarget + ".bak", "0"),
            string.Join('\t', ExecutableName, Path.Combine(staging, ExecutableName), exeTarget, exeBackup, "1")
        ]);

        bool recovered = UpdateFileTransaction.RecoverInterrupted(directories.Target, ApplicationName, static _ => { });

        Assert.True(recovered);
        Assert.Equal("old app", File.ReadAllText(exeTarget));
        Assert.Equal("old skia", File.ReadAllText(dllTarget));
        Assert.False(File.Exists(addedTarget));
        Assert.False(File.Exists(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName)));
        Assert.False(Directory.Exists(staging));
        Assert.Empty(Directory.EnumerateFiles(directories.Target, searchPattern: "*.bak"));
    }

    [Fact]
    public void RecoverInterruptedLeavesACompletedUpdateWhoseJournalCouldNotBeDeleted()
    {
        using TestDirectories directories = new();
        string staging = UpdateFileTransaction.NewStagingDirectory(directories.Target, ApplicationName);
        string exeTarget = directories.WriteTarget(ExecutableName, content: "new app");
        string exeBackup = directories.WriteTarget(ExecutableName + ".tadn-update-" + ApplicationName + "-x-1.bak", "old app");
        string addedTarget = directories.WriteTarget("added.txt", content: "added by the update");
        File.WriteAllLines(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName),
        [
            "TrayAppDotNET update journal 2",
            string.Join('\t', "added.txt", Path.Combine(staging, "added.txt"), addedTarget, addedTarget + ".bak", "0"),
            string.Join('\t', ExecutableName, Path.Combine(staging, ExecutableName), exeTarget, exeBackup, "1"),
            "committed"
        ]);

        bool recovered = UpdateFileTransaction.RecoverInterrupted(directories.Target, ApplicationName, static _ => { });

        Assert.True(recovered);
        Assert.Equal("new app", File.ReadAllText(exeTarget));
        Assert.True(File.Exists(addedTarget));
        Assert.False(File.Exists(exeBackup));
        Assert.False(File.Exists(UpdateFileTransaction.JournalPath(directories.Target, ApplicationName)));
    }

    private static UpdateFilePlan BuildPlan(TestDirectories directories, string targetExecutable) =>
        UpdateFileTransaction.BuildPlan(
            directories.Source,
            directories.Target,
            targetExecutable,
            TrayAppDotNETInstallPayload.NativeAOTApp(ApplicationName),
            directories.Source,
            static _ => { });

    private static UpdateFilePlan Plan(TestDirectories directories, params UpdateFileOperation[] operations) =>
        new(operations, StopSiblingApps: false, directories.Target, ApplicationName, directories.Source);

    private sealed class TestDirectories : IDisposable
    {
        private readonly TempDirectory _temp = new();

        // Created by the test or the code under test, so a test can also start from a missing folder
        private string TestRoot => _temp.Combine("root");

        public TestDirectories()
        {
            Source = Path.Combine(TestRoot, path2: "source");
            Target = Path.Combine(TestRoot, path2: "target");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Target);
        }

        public string Source { get; }

        public string Target { get; }

        /// <summary>Writes a complete NativeAOT payload into the source folder.</summary>
        public void WritePayload(string skiaContent, string glesContent)
        {
            _ = WriteSource(ExecutableName, content: "new app");
            _ = WriteSource("libSkiaSharp.dll", skiaContent);
            _ = WriteSource("av_libglesv2.dll", glesContent);
            _ = WriteSource("libHarfBuzzSharp.dll", content: "harfbuzz");
            _ = WriteSource("LICENSE.txt", content: "license");
            _ = WriteSource("NOTICE", content: "notice");
            _ = WriteSource(Path.Combine(".notices", "skia.txt"), content: "skia notice");
        }

        public string WriteSource(string relativePath, string content) =>
            Write(Source, relativePath, content);

        public string WriteTarget(string relativePath, string content) =>
            Write(Target, relativePath, content);

        public void Dispose() => _temp.Dispose();

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

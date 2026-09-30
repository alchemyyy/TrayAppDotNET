using System.IO.Compression;
using System.Text;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallEngineExtractionTests : IDisposable
{
    private const string FirstApplicationName = "FirstTrayAppDotNET";
    private const string SecondApplicationName = "SecondTrayAppDotNET";
    private const string ThirdApplicationName = "ThirdTrayAppDotNET";
    private const int PackageVersion = 1;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrayAppDotNETInstaller.Tests",
        Guid.NewGuid().ToString("N"));

    public InstallEngineExtractionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp leftovers are harmless for the test outcome
        }
    }

    [Fact]
    public void ExtractArchive_SkipsTheRootInstallScriptButKeepsNestedOnes()
    {
        string zipPath = CreateZip("with-script.zip",
            ("install.bat", "batch installer"),
            ("VolumeTrayAppDotNET.exe", "app"),
            ("tools/install.bat", "nested"));
        string destination = Path.Combine(_root, "script-out");
        List<InstallProgressLine> reports = [];

        int files;
        using (FileStream stream = File.OpenRead(zipPath))
        {
            files = InstallEngine.ExtractArchive(
                stream,
                destination,
                "VolumeTrayAppDotNET",
                new ProgressSlice(0, 50),
                new ListProgress(reports),
                CancellationToken.None);
        }

        Assert.Equal(2, files);
        Assert.False(File.Exists(Path.Combine(destination, "install.bat")));
        Assert.True(File.Exists(Path.Combine(destination, "VolumeTrayAppDotNET.exe")));
        // A nested script is part of the app's own layout and must survive
        Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "tools", "install.bat")));
        Assert.DoesNotContain(reports, report => report.Message.Contains(
            "install.bat",
            StringComparison.OrdinalIgnoreCase) && !report.Message.Contains(
            "tools/install.bat",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractArchive_WritesEntriesAndReportsProgressInsideSlice()
    {
        string zipPath = CreateZip("valid.zip",
            ("readme.txt", "hello"),
            ("sub/", null),
            ("sub/inner.txt", "inner"));
        string destination = Path.Combine(_root, "out");
        List<InstallProgressLine> reports = [];
        ProgressSlice slice = new(0, 50);

        int files;
        using (FileStream stream = File.OpenRead(zipPath))
        {
            files = InstallEngine.ExtractArchive(stream, destination, "Fake", slice, new ListProgress(reports), CancellationToken.None);
        }

        Assert.Equal(2, files);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "readme.txt")));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(destination, "sub", "inner.txt")));
        // One report per entry plus the completion report
        Assert.Equal(4, reports.Count);
        Assert.All(reports, report => Assert.False(report.IsFailure));
        Assert.All(reports, report => Assert.InRange(report.Percent, slice.StartPercent, slice.EndPercent));
        Assert.Equal(slice.EndPercent, reports[reports.Count - 1].Percent);
        for (int index = 1; index < reports.Count; index++)
            Assert.True(reports[index].Percent >= reports[index - 1].Percent, "progress must not go backwards");
        Assert.Contains("readme.txt", reports[0].Message);
    }

    [Fact]
    public void ExtractArchive_RejectsPathTraversalEntries()
    {
        string zipPath = CreateZip("evil.zip",
            ("ok.txt", "fine"),
            ("../evil.txt", "escaped"));
        string destination = Path.Combine(_root, "victim");
        List<InstallProgressLine> reports = [];

        using FileStream stream = File.OpenRead(zipPath);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            InstallEngine.ExtractArchive(stream, destination, "Fake", ProgressSlice.Full, new ListProgress(reports), CancellationToken.None));

        Assert.Contains("evil.txt", exception.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "ok.txt")));
        Assert.Single(reports);
    }

    [Fact]
    public void ExtractArchive_RejectsAbsoluteEntries()
    {
        string zipPath = CreateZip("absolute.zip", (@"C:\Windows\Temp\absolute.txt", "escaped"));
        string destination = Path.Combine(_root, "victim");

        using FileStream stream = File.OpenRead(zipPath);
        Assert.Throws<InvalidDataException>(() =>
            InstallEngine.ExtractArchive(stream, destination, "Fake", ProgressSlice.Full, new ListProgress([]), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_PortableExtractsIntoTargetDirectoryAndReportsCompletion()
    {
        string zipPath = CreateZip("FakeTrayAppDotNET_1.zip",
            ("FakeTrayAppDotNET.exe", "not really an exe"),
            ("LICENSE.txt", "license"));
        EmbeddedPayload payload = new("FakeTrayAppDotNET", 1, "FakeTrayAppDotNET_1.zip");
        string target = Path.Combine(_root, "portable");
        InstallPlan plan = new(InstallMode.Portable, target, [payload], CreateDesktopShortcut: false, CreateStartMenuShortcut: false);
        List<InstallProgressLine> reports = [];
        List<InstallApplicationStatus> statuses = [];

        InstallOutcome outcome = await InstallEngine.RunAsync(
            plan, _ => File.OpenRead(zipPath), new ListProgress(reports), new StatusListProgress(statuses), CancellationToken.None);

        Assert.True(outcome.Success, outcome.ErrorMessage);
        Assert.Null(outcome.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(target, "FakeTrayAppDotNET.exe")));
        Assert.True(File.Exists(Path.Combine(target, "LICENSE.txt")));
        Assert.Equal([Path.Combine(target, "FakeTrayAppDotNET.exe")], outcome.InstalledExecutables);
        Assert.True(reports[reports.Count - 1].IsComplete);
        Assert.Equal(
            [
                new InstallApplicationStatus("FakeTrayAppDotNET", InstallApplicationState.Installing),
                new InstallApplicationStatus("FakeTrayAppDotNET", InstallApplicationState.Installed)
            ],
            statuses);
    }

    [Fact]
    public async Task RunAsync_LocalFailsWhenPackageLacksExecutable()
    {
        string zipPath = CreateZip("FakeTrayAppDotNET_1.zip", ("readme.txt", "nothing to run"));
        EmbeddedPayload payload = new("FakeTrayAppDotNET", 1, "FakeTrayAppDotNET_1.zip");
        InstallPlan plan = new(InstallMode.Local, Path.Combine(_root, "local"), [payload], CreateDesktopShortcut: false, CreateStartMenuShortcut: true);
        List<InstallProgressLine> reports = [];
        List<InstallApplicationStatus> statuses = [];

        InstallOutcome outcome = await InstallEngine.RunAsync(
            plan, _ => File.OpenRead(zipPath), new ListProgress(reports), new StatusListProgress(statuses), CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.ErrorMessage);
        Assert.Contains("FakeTrayAppDotNET.exe", outcome.ErrorMessage);
        Assert.True(reports[reports.Count - 1].IsFailure);
        Assert.Empty(outcome.InstalledExecutables);
        Assert.Equal(
            [
                new InstallApplicationStatus("FakeTrayAppDotNET", InstallApplicationState.Installing),
                new InstallApplicationStatus("FakeTrayAppDotNET", InstallApplicationState.Failed)
            ],
            statuses);
    }

    [Fact]
    public async Task RunAsync_PinsAFailureToTheApplicationInFlightAndGoesNoFurther()
    {
        string[] applicationNames = [FirstApplicationName, SecondApplicationName, ThirdApplicationName];
        // The middle package escapes its destination, which the engine surfaces as an exception
        Dictionary<string, string> zipPaths = new(StringComparer.Ordinal)
        {
            [FirstApplicationName] = CreatePackage(FirstApplicationName),
            [SecondApplicationName] = CreatePackage(SecondApplicationName, (EntryName: "../escaped.txt", Content: "escaped")),
            [ThirdApplicationName] = CreatePackage(ThirdApplicationName)
        };
        string target = Path.Combine(_root, "suite");
        InstallPlan plan = CreateSuitePlan(target, applicationNames);
        List<InstallProgressLine> reports = [];
        List<InstallApplicationStatus> statuses = [];

        InstallOutcome outcome = await InstallEngine.RunAsync(
            plan,
            payload => File.OpenRead(zipPaths[payload.ApplicationName]),
            new ListProgress(reports),
            new StatusListProgress(statuses),
            CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.True(reports[reports.Count - 1].IsFailure);
        Assert.Equal([Path.Combine(target, ExecutableName(FirstApplicationName))], outcome.InstalledExecutables);
        Assert.False(File.Exists(Path.Combine(target, ExecutableName(ThirdApplicationName))));
        Assert.Equal(
            [
                new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installing),
                new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installed),
                new InstallApplicationStatus(SecondApplicationName, InstallApplicationState.Installing),
                new InstallApplicationStatus(SecondApplicationName, InstallApplicationState.Failed)
            ],
            statuses);
    }

    [Fact]
    public async Task RunAsync_CancelledBetweenApplicationsBlamesNoneOfThem()
    {
        string[] applicationNames = [FirstApplicationName, SecondApplicationName];
        Dictionary<string, string> zipPaths = new(StringComparer.Ordinal)
        {
            [FirstApplicationName] = CreatePackage(FirstApplicationName),
            [SecondApplicationName] = CreatePackage(SecondApplicationName)
        };
        InstallPlan plan = CreateSuitePlan(Path.Combine(_root, "cancelled"), applicationNames);
        List<InstallProgressLine> reports = [];
        List<InstallApplicationStatus> statuses = [];
        using CancellationTokenSource cancellation = new();
        // Cancelling as the first application finishes stops the run between the two applications
        StatusListProgress statusProgress = new(statuses, status =>
        {
            if (status.State == InstallApplicationState.Installed) cancellation.Cancel();
        });

        InstallOutcome outcome = await InstallEngine.RunAsync(
            plan,
            payload => File.OpenRead(zipPaths[payload.ApplicationName]),
            new ListProgress(reports),
            statusProgress,
            cancellation.Token);

        Assert.False(outcome.Success);
        Assert.True(reports[reports.Count - 1].IsFailure);
        Assert.Equal(
            [
                new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installing),
                new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installed)
            ],
            statuses);
    }

    [Fact]
    public async Task RunAsync_EmptyPlanFails()
    {
        InstallPlan plan = new(InstallMode.Portable, _root, [], CreateDesktopShortcut: false, CreateStartMenuShortcut: false);
        List<InstallProgressLine> reports = [];
        List<InstallApplicationStatus> statuses = [];

        InstallOutcome outcome = await InstallEngine.RunAsync(
            plan,
            _ => throw new InvalidOperationException("unreachable"),
            new ListProgress(reports),
            new StatusListProgress(statuses),
            CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Single(reports);
        Assert.True(reports[0].IsFailure);
        // No application was reached, so none is blamed
        Assert.Empty(statuses);
    }

    private string CreateZip(string fileName, params (string EntryName, string? Content)[] entries)
    {
        string zipPath = Path.Combine(_root, fileName);
        using FileStream stream = new(zipPath, FileMode.Create, FileAccess.ReadWrite);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach ((string entryName, string? content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            if (content == null) continue;

            using Stream entryStream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            entryStream.Write(bytes, 0, bytes.Length);
        }

        return zipPath;
    }

    /// <summary>A release-style package holding the application's executable plus any extra entries.</summary>
    private string CreatePackage(string applicationName, params (string EntryName, string? Content)[] extraEntries)
    {
        List<(string EntryName, string? Content)> entries = [(ExecutableName(applicationName), applicationName)];
        entries.AddRange(extraEntries);
        return CreateZip(PackageName(applicationName), [.. entries]);
    }

    private static InstallPlan CreateSuitePlan(string target, IEnumerable<string> applicationNames)
    {
        List<EmbeddedPayload> payloads = [];
        foreach (string applicationName in applicationNames)
            payloads.Add(new EmbeddedPayload(applicationName, PackageVersion, PackageName(applicationName)));

        return new InstallPlan(
            InstallMode.Portable,
            target,
            payloads,
            CreateDesktopShortcut: false,
            CreateStartMenuShortcut: false);
    }

    private static string ExecutableName(string applicationName) => applicationName + ".exe";

    private static string PackageName(string applicationName) => $"{applicationName}_{PackageVersion}.zip";

    private sealed class ListProgress(List<InstallProgressLine> lines) : IProgress<InstallProgressLine>
    {
        public void Report(InstallProgressLine value) => lines.Add(value);
    }

    private sealed class StatusListProgress(
        List<InstallApplicationStatus> statuses,
        Action<InstallApplicationStatus>? statusReported = null) : IProgress<InstallApplicationStatus>
    {
        public void Report(InstallApplicationStatus value)
        {
            statuses.Add(value);
            statusReported?.Invoke(value);
        }
    }
}

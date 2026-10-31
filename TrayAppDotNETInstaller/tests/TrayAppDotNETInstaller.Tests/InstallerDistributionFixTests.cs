using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>Shortcut presets, System staging and the relaunch list of a failed elevated run.</summary>
public sealed class InstallerDistributionFixTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"TrayAppDotNETInstaller-fix-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DesktopPreset_IsCheckedOnlyWhenEveryAppAlreadyHasItsShortcut()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase) { Path.Combine(desktop, "A.lnk") };

        Assert.True(ShortcutPresets.Desktop(InstallMode.Local, ["A"], existing.Contains));
        Assert.False(ShortcutPresets.Desktop(InstallMode.Local, ["A", "B"], existing.Contains));
        Assert.False(ShortcutPresets.Desktop(InstallMode.Portable, ["A"], existing.Contains));
    }

    [Fact]
    public void StartMenuPreset_IsUncheckedOnlyForAnInstalledAppWhoseEntryWasRemoved()
    {
        DetectedInstallation installed = new("A", InstallMode.Local, @"C:\apps");
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        HashSet<string> withEntry = new(StringComparer.OrdinalIgnoreCase) { Path.Combine(programs, "A (Local).lnk") };

        Assert.True(ShortcutPresets.StartMenu(InstallMode.Local, ["A"], [], _ => false));
        Assert.True(ShortcutPresets.StartMenu(InstallMode.Local, ["A"], [installed], withEntry.Contains));
        Assert.False(ShortcutPresets.StartMenu(InstallMode.Local, ["A"], [installed], _ => false));
        Assert.True(ShortcutPresets.StartMenu(InstallMode.System, ["A"], [installed], _ => false));
    }

    [Fact]
    public void SystemInstallsStageInsideTheirProtectedTargetFolder()
    {
        string target = Path.Combine(_root, "Program Files", "TrayAppDotNET");
        InstallPlan plan = new(InstallMode.System, target, [], CreateDesktopShortcut: null, CreateStartMenuShortcut: null);

        string staging = InstallEngine.CreateTemporaryRoot(plan);

        Assert.Equal(target, Path.GetDirectoryName(staging));
        Assert.True(Directory.Exists(staging));
    }

    [Fact]
    public void RelayedStatusesCollectTheAppsThatFinishedForTheRelaunch()
    {
        HashSet<string> installed = new(StringComparer.OrdinalIgnoreCase);
        RecordingProgress<TrayAppDotNETInstallProgress> progress = new();
        RecordingProgress<InstallApplicationStatus> statuses = new();

        ElevatedInstallWorker.RelayWorkerLine("[APP:Installed] A", progress, statuses, installed);
        ElevatedInstallWorker.RelayWorkerLine("[APP:Failed] B", progress, statuses, installed);
        ElevatedInstallWorker.RelayWorkerLine(ElevatedInstallWorker.LogLinePrefix + "a worker log line", progress, statuses, installed);

        Assert.Equal(["A"], installed);
        Assert.Equal(2, statuses.Reports.Count);
        Assert.Empty(progress.Reports);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value) => Reports.Add(value);
    }
}

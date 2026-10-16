using System.Runtime.ExceptionServices;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class InstallationServiceTests
{
    private const string SharedFileName = "shared.dll";
    private const string SharedDirectoryName = ".notices";

    [Fact]
    public void InstallOptionsDefaultToStartMenuOnly()
    {
        TrayAppDotNETInstallOptions installOptions = new();

        Assert.False(installOptions.CreateDesktopShortcut);
        Assert.True(installOptions.CreateStartMenuShortcut);
    }

    [Fact]
    public void DefaultPayloadsInstallLegalMaterials()
    {
        TrayAppDotNETInstallPayload[] payloads =
        [
            TrayAppDotNETInstallPayload.ManagedApp("TestTrayAppDotNET"),
            TrayAppDotNETInstallPayload.NativeAOTApp("TestTrayAppDotNET")
        ];

        foreach (TrayAppDotNETInstallPayload payload in payloads)
        {
            string[] requiredFileNames = payload.RequiredFiles.Select(file => file.Name).ToArray();
            string[] requiredDirectoryNames = payload.RequiredDirectories.Select(directory => directory.Name).ToArray();

            Assert.Contains("LICENSE.txt", requiredFileNames);
            Assert.Contains("NOTICE", requiredFileNames);
            Assert.Contains(".notices", requiredDirectoryNames);
        }
    }

    [Fact]
    public void LayoutPlacesLocalInstallsInTheAppsFolderBesideTheSettingsDirectories()
    {
        string localAppDataRoot = Path.Combine(Path.GetTempPath(), path2: "TrayAppDotNET");

        TrayAppDotNETInstallLayout layout = TrayAppDotNETInstallLayout.Create(
            applicationName: "TestTrayAppDotNET",
            sharedRootFolderName: "TrayAppDotNET",
            localAppDataRoot);

        Assert.Equal(Path.Combine(localAppDataRoot, path2: "apps"), layout.LocalAppDataInstallDirectory);
        Assert.Equal(
            Path.Combine(localAppDataRoot, path2: "apps", path3: "TestTrayAppDotNET.exe"),
            layout.LocalAppDataInstallExecutable);
        // Other user profiles are probed through this relative path, so it must carry the same folder
        Assert.Equal(
            Path.Combine("AppData", "Local", "TrayAppDotNET", "apps", "TestTrayAppDotNET.exe"),
            layout.LocalAppDataExecutableProfileRelativePath);
        // The system scope keeps its flat directory; only LocalAppData is shared with settings
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), path2: "TrayAppDotNET"),
            layout.ProgramFilesInstallDirectory);
    }

    [Fact]
    public void ElevatedInstallArgumentsCarryExplicitShortcutOptions()
    {
        TrayAppDotNETInstallOptions installOptions = new(
            CreateDesktopShortcut: true,
            CreateStartMenuShortcut: false);

        string arguments = TrayAppDotNETInstallationService.BuildElevatedInstallArguments(
            sourceExecutable: @"C:\staging folder\TestTrayAppDotNET.exe",
            buildNumber: 42,
            installOptions);

        Assert.Equal(
            "--install-system --source \"C:\\staging folder\\TestTrayAppDotNET.exe\" --build 42 "
            + "--desktop-shortcut true --start-menu-shortcut false",
            arguments);
    }

    [Fact]
    public void ElevatedInstallArgumentsAppendProgressPipeOnlyWhenRequested()
    {
        string withoutPipe = TrayAppDotNETInstallationService.BuildElevatedInstallArguments(
            sourceExecutable: @"C:\staging\TestTrayAppDotNET.exe",
            buildNumber: 7,
            installOptions: null);
        string withPipe = TrayAppDotNETInstallationService.BuildElevatedInstallArguments(
            sourceExecutable: @"C:\staging\TestTrayAppDotNET.exe",
            buildNumber: 7,
            installOptions: null,
            progressPipeName: "TrayAppDotNET.Progress.Test.1.abc");

        Assert.Equal("--install-system --source \"C:\\staging\\TestTrayAppDotNET.exe\" --build 7", withoutPipe);
        Assert.Equal(withoutPipe + " --progress-pipe TrayAppDotNET.Progress.Test.1.abc", withPipe);
    }

    [Fact]
    public void ExistingInstallCallPreservesDesktopShortcutBehavior()
    {
        using InstallationFixture fixture = new();
        File.WriteAllText(fixture.LocalDesktopShortcutPath, contents: "existing shortcut");

        TrayAppDotNETInstallResult result = fixture.Service.ApplyInstallOptions(
            InstallScope.LocalAppData,
            allUsers: false,
            installOptions: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(fixture.LocalDesktopShortcutPath));
        Assert.True(fixture.LastStartMenuSync.HasValue);
        Assert.Null(fixture.LastStartMenuSync.Value.Scope);
        Assert.False(fixture.LastStartMenuSync.Value.AllUsers);
    }

    [Fact]
    public void ExplicitOptionsRemoveUnselectedShellEntries()
    {
        using InstallationFixture fixture = new();
        File.WriteAllText(fixture.LocalDesktopShortcutPath, contents: "existing shortcut");
        TrayAppDotNETInstallOptions installOptions = new(
            CreateDesktopShortcut: false,
            CreateStartMenuShortcut: false);

        TrayAppDotNETInstallResult result = fixture.Service.ApplyInstallOptions(
            InstallScope.LocalAppData,
            allUsers: false,
            installOptions);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.Exists(fixture.LocalDesktopShortcutPath));
        Assert.True(fixture.LastStartMenuSync.HasValue);
        Assert.Equal(InstallScope.LocalAppData, fixture.LastStartMenuSync.Value.Scope);
        Assert.False(fixture.LastStartMenuSync.Value.AllUsers);
    }

    [Fact]
    public void DesktopShortcutReportsMissingInstalledExecutable()
    {
        using InstallationFixture fixture = new();

        TrayAppDotNETInstallResult result = fixture.Service.DesktopShortcut.SetEnabled(
            InstallScope.LocalAppData,
            enabled: true);

        Assert.False(result.Success);
        Assert.Contains(expectedSubstring: "installed executable is missing", result.ErrorMessage ?? string.Empty);
        Assert.False(File.Exists(fixture.LocalDesktopShortcutPath));
    }

    [Fact]
    public void DesktopShortcutCreatesAndRemovesLocalShortcut()
    {
        using InstallationFixture fixture = new();
        File.WriteAllText(fixture.Layout.LocalAppDataInstallExecutable, contents: "test executable");

        RunOnStaThread(() =>
        {
            TrayAppDotNETInstallResult createResult = fixture.Service.DesktopShortcut.SetEnabled(
                InstallScope.LocalAppData,
                enabled: true);

            Assert.True(createResult.Success, createResult.ErrorMessage);
            Assert.True(File.Exists(fixture.LocalDesktopShortcutPath));
            string? target = ShellLink.TryRead(fixture.LocalDesktopShortcutPath);
            Assert.Equal(
                Path.GetFullPath(fixture.Layout.LocalAppDataInstallExecutable),
                Path.GetFullPath(target ?? string.Empty),
                ignoreCase: true);

            TrayAppDotNETInstallResult removeResult = fixture.Service.DesktopShortcut.SetEnabled(
                InstallScope.LocalAppData,
                enabled: false);

            Assert.True(removeResult.Success, removeResult.ErrorMessage);
            Assert.False(File.Exists(fixture.LocalDesktopShortcutPath));
        });
    }

    [Fact]
    public void PrepareUninstallRemovesSharedFilesWhenTheAppIsTheLastOne()
    {
        using InstallationFixture fixture = new(SharedPayload());
        WriteInstalledLayout(fixture);

        TrayAppDotNETInstallResult result = PrepareLocalUninstall(fixture);

        Assert.True(result.Success, result.ErrorMessage);
        string installDirectory = fixture.Layout.LocalAppDataInstallDirectory;
        Assert.False(File.Exists(Path.Combine(installDirectory, SharedFileName)));
        Assert.False(Directory.Exists(Path.Combine(installDirectory, SharedDirectoryName)));
        // The exe waits under a name outside "*.exe" for the batch stage to delete it
        Assert.False(File.Exists(fixture.Layout.LocalAppDataInstallExecutable));
        Assert.True(File.Exists(TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(fixture.Layout.LocalAppDataInstallExecutable)));
    }

    [Fact]
    public void PrepareUninstallKeepsSharedFilesWhileAnotherAppIsInstalled()
    {
        using InstallationFixture fixture = new(SharedPayload());
        WriteInstalledLayout(fixture);
        string installDirectory = fixture.Layout.LocalAppDataInstallDirectory;
        File.WriteAllText(Path.Combine(installDirectory, "OtherTrayAppDotNET.exe"), contents: "other app");

        TrayAppDotNETInstallResult result = PrepareLocalUninstall(fixture);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(installDirectory, SharedFileName)));
        Assert.True(Directory.Exists(Path.Combine(installDirectory, SharedDirectoryName)));
        Assert.True(File.Exists(TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(fixture.Layout.LocalAppDataInstallExecutable)));
    }

    [Fact]
    public void ConcurrentUninstallsLeaveTheLastOneToRemoveTheSharedFiles()
    {
        using InstallationFixture first = new(SharedPayload());
        WriteInstalledLayout(first);
        string installDirectory = first.Layout.LocalAppDataInstallDirectory;
        // A second app in the same folder whose uninstall has already renamed its exe away
        string otherExecutable = Path.Combine(installDirectory, "OtherTrayAppDotNET.exe");
        File.WriteAllText(TrayAppDotNETInstallLayout.PendingRemovalExecutablePath(otherExecutable), contents: "other app");

        TrayAppDotNETInstallResult result = PrepareLocalUninstall(first);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(installDirectory, SharedFileName)));
    }

    private static TrayAppDotNETInstallPayload SharedPayload() =>
        new(
            [new TrayAppDotNETInstallFile(SharedFileName, RemoveOnlyWhenInstallRootHasNoExe: true)],
            [],
            [new TrayAppDotNETInstallDirectory(SharedDirectoryName, RemoveOnlyWhenInstallRootHasNoExe: true)],
            []);

    private static void WriteInstalledLayout(InstallationFixture fixture)
    {
        string installDirectory = fixture.Layout.LocalAppDataInstallDirectory;
        File.WriteAllText(fixture.Layout.LocalAppDataInstallExecutable, contents: "test executable");
        File.WriteAllText(Path.Combine(installDirectory, SharedFileName), contents: "shared library");
        Directory.CreateDirectory(Path.Combine(installDirectory, SharedDirectoryName));
        File.WriteAllText(Path.Combine(installDirectory, SharedDirectoryName, "notice.txt"), contents: "notice");
    }

    private static TrayAppDotNETInstallResult PrepareLocalUninstall(InstallationFixture fixture)
    {
        TrayAppDotNETInstallResult? result = null;
        // The desktop shortcut step goes through shell COM
        RunOnStaThread(() => result = fixture.Service.PrepareUninstall(InstallScope.LocalAppData));
        return result!;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private sealed class InstallationFixture : IDisposable
    {
        public InstallationFixture(TrayAppDotNETInstallPayload? payload = null)
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                path2: "TrayAppDotNET-install-service-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);

            string localDirectory = Path.Combine(RootDirectory, path2: "local");
            string systemDirectory = Path.Combine(RootDirectory, path2: "system");
            Directory.CreateDirectory(localDirectory);
            Directory.CreateDirectory(systemDirectory);

            Layout = new TrayAppDotNETInstallLayout(
                ApplicationName: "TestTrayAppDotNET",
                SharedRootFolderName: "TrayAppDotNET",
                localDirectory,
                systemDirectory,
                InstalledExecutableFileName: "TestTrayAppDotNET.exe");
            LocalDesktopShortcutPath = Path.Combine(RootDirectory, path2: "local-desktop.lnk");
            string systemDesktopShortcutPath = Path.Combine(RootDirectory, path2: "system-desktop.lnk");

            TrayAppDotNETInstallIdentity identity = new(
                Layout.ApplicationName,
                Publisher: "Test Publisher",
                HelpLink: null,
                Path.Combine(RootDirectory, path2: "settings"),
                Path.Combine(RootDirectory, path2: "startup.lnk"),
                LegacyRunKeyRegistryPath: @"Software\TrayAppDotNETTests",
                Log: null);
            TrayAppDotNETDesktopShortcutOptions desktopOptions = new(
                Layout.ApplicationName,
                Layout)
            {
                LocalShortcutPath = LocalDesktopShortcutPath, SystemShortcutPath = systemDesktopShortcutPath
            };
            TrayAppDotNETInstallationOptions serviceOptions = new(
                identity,
                Layout,
                payload ?? new TrayAppDotNETInstallPayload([], [], [], []),
                CurrentBuildNumber: 1,
                (scope, allUsers) => LastStartMenuSync = (scope, allUsers),
                DesktopShortcutOptions: desktopOptions);
            Service = new TrayAppDotNETInstallationService(serviceOptions);
        }

        public string RootDirectory { get; }

        public TrayAppDotNETInstallLayout Layout { get; }

        public string LocalDesktopShortcutPath { get; }

        public TrayAppDotNETInstallationService Service { get; }

        public (InstallScope? Scope, bool AllUsers)? LastStartMenuSync { get; private set; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}

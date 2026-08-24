using System.Runtime.ExceptionServices;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class InstallationServiceTests
{
    [Fact]
    public void InstallOptionsDefaultToStartMenuOnly()
    {
        TrayAppDotNETInstallOptions installOptions = new();

        Assert.False(installOptions.CreateDesktopShortcut);
        Assert.True(installOptions.CreateStartMenuShortcut);
    }

    [Fact]
    public void ElevatedInstallArgumentsCarryExplicitShortcutOptions()
    {
        TrayAppDotNETInstallOptions installOptions = new(
            CreateDesktopShortcut: true,
            CreateStartMenuShortcut: false);

        string arguments = TrayAppDotNETInstallationService.BuildElevatedInstallArguments(
            @"C:\staging folder\TestTrayAppDotNET.exe",
            buildNumber: 42,
            installOptions);

        Assert.Equal(
            "--install-system --source \"C:\\staging folder\\TestTrayAppDotNET.exe\" --build 42 "
            + "--desktop-shortcut true --start-menu-shortcut false",
            arguments);
    }

    [Fact]
    public void ExistingInstallCallPreservesDesktopShortcutBehavior()
    {
        using InstallationFixture fixture = new();
        File.WriteAllText(fixture.LocalDesktopShortcutPath, "existing shortcut");

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
        File.WriteAllText(fixture.LocalDesktopShortcutPath, "existing shortcut");
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
        Assert.Contains("installed executable is missing", result.ErrorMessage ?? string.Empty);
        Assert.False(File.Exists(fixture.LocalDesktopShortcutPath));
    }

    [Fact]
    public void DesktopShortcutCreatesAndRemovesLocalShortcut()
    {
        using InstallationFixture fixture = new();
        File.WriteAllText(fixture.Layout.LocalAppDataInstallExecutable, "test executable");

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
        private readonly string _rootDirectory;

        public InstallationFixture()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "TrayAppDotNET-install-service-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);

            string localDirectory = Path.Combine(_rootDirectory, "local");
            string systemDirectory = Path.Combine(_rootDirectory, "system");
            Directory.CreateDirectory(localDirectory);
            Directory.CreateDirectory(systemDirectory);

            Layout = new TrayAppDotNETInstallLayout(
                "TestTrayAppDotNET",
                "TrayAppDotNET",
                localDirectory,
                systemDirectory,
                "TestTrayAppDotNET.exe");
            LocalDesktopShortcutPath = Path.Combine(_rootDirectory, "local-desktop.lnk");
            string systemDesktopShortcutPath = Path.Combine(_rootDirectory, "system-desktop.lnk");

            TrayAppDotNETInstallIdentity identity = new(
                Layout.ApplicationName,
                "Test Publisher",
                null,
                Path.Combine(_rootDirectory, "settings"),
                Path.Combine(_rootDirectory, "startup.lnk"),
                @"Software\TrayAppDotNETTests",
                null);
            TrayAppDotNETDesktopShortcutOptions desktopOptions = new(
                Layout.ApplicationName,
                Layout)
            {
                LocalShortcutPath = LocalDesktopShortcutPath,
                SystemShortcutPath = systemDesktopShortcutPath
            };
            TrayAppDotNETInstallationOptions serviceOptions = new(
                identity,
                Layout,
                new TrayAppDotNETInstallPayload([], [], [], []),
                CurrentBuildNumber: 1,
                SyncStartMenu: (scope, allUsers) => LastStartMenuSync = (scope, allUsers),
                DesktopShortcutOptions: desktopOptions);
            Service = new TrayAppDotNETInstallationService(serviceOptions);
        }

        public TrayAppDotNETInstallLayout Layout { get; }

        public string LocalDesktopShortcutPath { get; }

        public TrayAppDotNETInstallationService Service { get; }

        public (InstallScope? Scope, bool AllUsers)? LastStartMenuSync { get; private set; }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory)) Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallEngineArgumentsTests
{
    private const string TaskManagerApplicationName = "TaskManagerTrayAppDotNET";
    private const string VolumeApplicationName = "VolumeTrayAppDotNET";

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void BuildHeadlessInstallArguments_PassesRunAsAdministratorToASystemTaskManagerInstall(bool choice, string value)
    {
        IReadOnlyList<string> arguments = InstallEngine.BuildHeadlessInstallArguments(
            CreatePlan(InstallMode.System, runAsAdministrator: choice), TaskManagerApplicationName);

        Assert.Equal(
            [
                "--install-headless", "system",
                "--desktop-shortcut", "false",
                "--start-menu-shortcut", "true",
                "--run-as-administrator", value
            ],
            arguments);
    }

    [Fact]
    public void BuildHeadlessInstallArguments_LeavesRunAsAdministratorOutForOtherApps()
    {
        IReadOnlyList<string> arguments = InstallEngine.BuildHeadlessInstallArguments(
            CreatePlan(InstallMode.System, runAsAdministrator: true), VolumeApplicationName);

        Assert.Equal(
            ["--install-headless", "system", "--desktop-shortcut", "false", "--start-menu-shortcut", "true"],
            arguments);
    }

    [Theory]
    [InlineData(InstallMode.Local)]
    [InlineData(InstallMode.Portable)]
    public void BuildHeadlessInstallArguments_LeavesRunAsAdministratorOutOutsideSystemInstalls(InstallMode mode)
    {
        IReadOnlyList<string> arguments = InstallEngine.BuildHeadlessInstallArguments(
            CreatePlan(mode, runAsAdministrator: true), TaskManagerApplicationName);

        Assert.Equal(
            ["--install-headless", "local", "--desktop-shortcut", "false", "--start-menu-shortcut", "true"],
            arguments);
    }

    [Fact]
    public void BuildHeadlessInstallArguments_LeavesUnsetChoicesOut()
    {
        InstallPlan plan = new(
            InstallMode.System,
            TargetDirectory: @"C:\Program Files\TrayAppDotNET",
            Payloads: [],
            CreateDesktopShortcut: null,
            CreateStartMenuShortcut: null,
            RunAsAdministrator: null);

        IReadOnlyList<string> arguments = InstallEngine.BuildHeadlessInstallArguments(plan, TaskManagerApplicationName);

        Assert.Equal(["--install-headless", "system"], arguments);
    }

    [Fact]
    public void RunAsAdministratorApplicationName_IsTaskManager() =>
        Assert.Equal(TaskManagerApplicationName, InstallEngine.RunAsAdministratorApplicationName);

    private static InstallPlan CreatePlan(InstallMode mode, bool? runAsAdministrator) => new(
        mode,
        TargetDirectory: @"C:\TrayAppDotNET",
        Payloads: [],
        CreateDesktopShortcut: false,
        CreateStartMenuShortcut: true,
        RunAsAdministrator: runAsAdministrator);
}

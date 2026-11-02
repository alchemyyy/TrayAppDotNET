using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerUninstallGuardTests
{
    private const string PerUserExecutable =
        @"C:\Users\someone\AppData\Local\TrayAppDotNET\apps\TaskManagerTrayAppDotNET.exe";

    [Fact]
    public void UnelevatedUninstallOfTheRedirectTargetIsStopped()
    {
        Assert.True(TaskManagerUninstallGuard.BlocksUninstall(PerUserExecutable, PerUserExecutable, helperElevated: false));
        Assert.True(TaskManagerUninstallGuard.BlocksUninstall(
            "\"" + PerUserExecutable.ToUpperInvariant() + "\"",
            PerUserExecutable,
            helperElevated: false));
    }

    [Fact]
    public void ElevatedHelperRemovesTheRedirectItself() =>
        Assert.False(TaskManagerUninstallGuard.BlocksUninstall(PerUserExecutable, PerUserExecutable, helperElevated: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Program Files\TrayAppDotNET\TaskManagerTrayAppDotNET.exe")]
    [InlineData(@"C:\Tools\SystemInformer\SystemInformer.exe")]
    public void RedirectToAnythingElseNeverStopsTheUninstall(string? registeredExecutable) =>
        Assert.False(TaskManagerUninstallGuard.BlocksUninstall(registeredExecutable, PerUserExecutable, helperElevated: false));

    [Theory]
    [InlineData(InstallScope.ProgramFiles, false, true)]
    [InlineData(InstallScope.ProgramFiles, true, true)]
    [InlineData(InstallScope.LocalAppData, false, false)]
    [InlineData(InstallScope.LocalAppData, true, true)]
    public void HelperElevationFollowsScopeAndStartingProcess(InstallScope scope, bool startedElevated, bool expected) =>
        Assert.Equal(expected, TaskManagerUninstallGuard.HelperIsElevated(scope, startedElevated));

    [Fact]
    public void UninstallHookChecksBeforeStartingAndCanStopPreparation()
    {
        TrayAppDotNETUninstallHook? hook = AppServices.Install.UninstallHook;

        Assert.NotNull(hook);
        Assert.NotNull(hook.CheckUninstall);
        Assert.NotNull(hook.BeforePrepare);
    }
}

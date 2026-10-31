using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// The app uninstall hook can stop an uninstall before anything is removed, and its reason reaches the uninstaller
/// window or the headless output: from the starting process through <see cref="TrayAppDotNETUninstallHook.CheckUninstall"/>
/// and from the helper through <see cref="TrayAppDotNETUninstallHook.BeforePrepare"/>.
/// </summary>
public sealed class UninstallHookTests
{
    private const string Reason = "The redirect still points at this copy.";

    [Fact]
    public async Task CheckUninstallRefusesBeforeTheHelperStarts()
    {
        List<InstallScope> checkedScopes = [];
        TrayAppDotNETAppInstallServices install = CreateServices(new TrayAppDotNETUninstallHook
        {
            CheckUninstall = scope =>
            {
                checkedScopes.Add(scope);
                return Reason;
            }
        });
        List<TrayAppDotNETInstallProgress> reports = [];
        bool exited = false;

        // The check comes first, so the (absent) installed copy is never looked at or started
        TrayAppDotNETUninstallRun run = install.Installation.RunUninstall(
            InstallScope.LocalAppData,
            deleteSettings: false,
            () => exited = true,
            new CollectingProgress(reports));

        Assert.Equal([InstallScope.LocalAppData], checkedScopes);
        Assert.Null(run.Process);
        Assert.False(run.UserCancelled);
        Assert.Equal(Reason, run.ErrorMessage);
        // Already finished when RunUninstall returns: a refusal never waits on anything
        Assert.True(run.Completion.IsCompletedSuccessfully);
        Assert.False(await run.Completion);
        Assert.False(exited);
        TrayAppDotNETInstallProgress report = Assert.Single(reports);
        Assert.True(report.IsFailure);
        Assert.Equal(Reason, report.Message);
    }

    [Fact]
    public void CheckUninstallThatThrowsDoesNotBlock()
    {
        TrayAppDotNETAppInstallServices install = CreateServices(new TrayAppDotNETUninstallHook
        {
            CheckUninstall = static _ => throw new InvalidOperationException("registry unavailable")
        });
        List<TrayAppDotNETInstallProgress> reports = [];

        TrayAppDotNETUninstallRun run = install.Installation.RunUninstall(
            InstallScope.LocalAppData,
            deleteSettings: false,
            static () => { },
            new CollectingProgress(reports));

        // The uninstall goes on to its next gate, the missing installed copy, instead of the check's failure
        Assert.Null(run.Process);
        Assert.NotNull(run.ErrorMessage);
        Assert.DoesNotContain("registry unavailable", run.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BeforePrepareReasonStopsPreparationBeforeAnythingIsRemoved()
    {
        bool afterPrepareRan = false;
        bool? deleteSettingsSeen = null;
        TrayAppDotNETAppInstallServices install = CreateServices(new TrayAppDotNETUninstallHook
        {
            BeforePrepare = (_, deleteSettings) =>
            {
                deleteSettingsSeen = deleteSettings;
                return Reason;
            },
            AfterPrepare = _ => afterPrepareRan = true
        });
        List<TrayAppDotNETInstallProgress> reports = [];

        TrayAppDotNETInstallResult result = install.PrepareUninstall(
            InstallScope.LocalAppData,
            deleteSettings: true,
            new CollectingProgress(reports));

        Assert.False(result.Success);
        Assert.Equal(Reason, result.ErrorMessage);
        Assert.True(deleteSettingsSeen);
        // A stopped uninstall stopped no instance and removed nothing, so the after-step has nothing to clean up
        Assert.False(afterPrepareRan);
        TrayAppDotNETInstallProgress report = Assert.Single(reports);
        Assert.True(report.IsFailure);
        Assert.Equal(Reason, report.Message);
    }

    private static TrayAppDotNETAppInstallServices CreateServices(TrayAppDotNETUninstallHook hook) =>
        new(new TrayAppDotNETAppInstallServicesOptions
        {
            ApplicationName = "UninstallHookTestTrayAppDotNET",
            AppGuid = "0F4F1E4A-7D5B-4C61-9D52-5B7C2E9A1C01",
            BuildNumber = 1,
            PostToUIThread = static action => action(),
            UninstallHook = hook
        });

    private sealed class CollectingProgress(List<TrayAppDotNETInstallProgress> reports)
        : IProgress<TrayAppDotNETInstallProgress>
    {
        public void Report(TrayAppDotNETInstallProgress value) => reports.Add(value);
    }
}

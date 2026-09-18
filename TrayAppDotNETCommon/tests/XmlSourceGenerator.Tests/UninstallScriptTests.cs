using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UninstallScriptTests
{
    [Fact]
    public void ScriptUsesCSharpPreparationAndBatchFileRemovalWithoutPowerShell()
    {
        string root = Path.Combine(Path.GetTempPath(), path2: "TrayAppDotNET", path3: "50%off");
        TrayAppDotNETInstallIdentity identity = new(
            ApplicationName: "TestTrayAppDotNET",
            Publisher: "Test Publisher",
            HelpLink: null,
            Path.Combine(root, path2: "settings"),
            Path.Combine(root, path2: "startup.lnk"),
            LegacyRunKeyRegistryPath: @"Software\TrayAppDotNETTests");
        TrayAppDotNETInstallPayload payload = new(
            [
                new TrayAppDotNETInstallFile("app.data"),
                new TrayAppDotNETInstallFile(Name: "shared.dll", RemoveOnlyWhenInstallRootHasNoExe: true)
            ],
            [],
            [new TrayAppDotNETInstallDirectory(Name: "app-assets", RemoveOnlyWhenInstallRootHasNoExe: false)],
            [new TrayAppDotNETInstallDirectory("shared-assets")]);

        string script = UninstallScript.BuildScript(
            Path.Combine(root, path2: "portable", path3: "TestTrayAppDotNET.exe"),
            Path.Combine(root, path2: "installed"),
            InstallScope.LocalAppData,
            deleteSettings: true,
            identity,
            installedExecutableFileName: "TestTrayAppDotNET.exe",
            payload);

        Assert.StartsWith(expectedStartString: "@echo off", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "--uninstall-prepare --scope user", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "app.data", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "shared.dll", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "app-assets", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "shared-assets", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "50%%off", script, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "powershell", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(expectedSubstring: "--progress-pipe", script, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "--uninstall-prepare --scope user --delete-settings true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptForwardsExplicitSettingsChoiceAndProgressPipeToHelper()
    {
        string root = Path.Combine(Path.GetTempPath(), path2: "TrayAppDotNET", path3: "progress");
        TrayAppDotNETInstallIdentity identity = new(
            ApplicationName: "TestTrayAppDotNET",
            Publisher: "Test Publisher",
            HelpLink: null,
            Path.Combine(root, path2: "settings"),
            Path.Combine(root, path2: "startup.lnk"),
            LegacyRunKeyRegistryPath: @"Software\TrayAppDotNETTests");
        TrayAppDotNETInstallPayload payload = new([], [], [], []);

        string script = UninstallScript.BuildScript(
            Path.Combine(root, path2: "TestTrayAppDotNET.exe"),
            Path.Combine(root, path2: "installed"),
            InstallScope.ProgramFiles,
            deleteSettings: true,
            identity,
            installedExecutableFileName: "TestTrayAppDotNET.exe",
            payload,
            progressPipeName: "TrayAppDotNET.Progress.Test.1.abc");

        Assert.Contains(
            expectedSubstring: "--uninstall-prepare --scope system --delete-settings true --progress-pipe \"TrayAppDotNET.Progress.Test.1.abc\"",
            script,
            StringComparison.Ordinal);
    }
}

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
        string root = Path.Combine(Path.GetTempPath(), "TrayAppDotNET", "50%off");
        TrayAppDotNETInstallIdentity identity = new(
            "TestTrayAppDotNET",
            "Test Publisher",
            null,
            Path.Combine(root, "settings"),
            Path.Combine(root, "startup.lnk"),
            @"Software\TrayAppDotNETTests");
        TrayAppDotNETInstallPayload payload = new(
            [
                new TrayAppDotNETInstallFile("app.data"),
                new TrayAppDotNETInstallFile("shared.dll", RemoveOnlyWhenInstallRootHasNoExe: true)
            ],
            [],
            [new TrayAppDotNETInstallDirectory("app-assets", RemoveOnlyWhenInstallRootHasNoExe: false)],
            [new TrayAppDotNETInstallDirectory("shared-assets")]);

        string script = UninstallScript.BuildScript(
            Path.Combine(root, "portable", "TestTrayAppDotNET.exe"),
            Path.Combine(root, "installed"),
            InstallScope.LocalAppData,
            deleteSettings: true,
            identity,
            "TestTrayAppDotNET.exe",
            payload);

        Assert.StartsWith("@echo off", script, StringComparison.Ordinal);
        Assert.Contains("--uninstall-prepare --scope user", script, StringComparison.Ordinal);
        Assert.Contains("app.data", script, StringComparison.Ordinal);
        Assert.Contains("shared.dll", script, StringComparison.Ordinal);
        Assert.Contains("app-assets", script, StringComparison.Ordinal);
        Assert.Contains("shared-assets", script, StringComparison.Ordinal);
        Assert.Contains("50%%off", script, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell", script, StringComparison.OrdinalIgnoreCase);
    }
}

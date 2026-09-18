using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallDefaultsTests
{
    private static readonly EmbeddedPayloadCatalog SingleCatalog = new(
    [
        new EmbeddedPayload("VolumeTrayAppDotNET", 3, "VolumeTrayAppDotNET_3.zip")
    ]);

    private static readonly EmbeddedPayloadCatalog BundleCatalog = new(
    [
        new EmbeddedPayload("VolumeTrayAppDotNET", 3, "VolumeTrayAppDotNET_3.zip"),
        new EmbeddedPayload("BatteryTrayAppDotNET", 5, "BatteryTrayAppDotNET_5.zip")
    ]);

    [Fact]
    public void DefaultDirectory_Local_IsUnderTheLocalAppDataAppsFolder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayAppDotNET",
            "apps");

        Assert.Equal(expected, InstallDefaults.DefaultDirectory(InstallMode.Local, SingleCatalog));
        Assert.Equal(expected, InstallDefaults.DefaultDirectory(InstallMode.Local, BundleCatalog));
    }

    [Fact]
    public void DefaultDirectory_System_IsUnderProgramFiles()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "TrayAppDotNET");

        Assert.Equal(expected, InstallDefaults.DefaultDirectory(InstallMode.System, SingleCatalog));
        Assert.Equal(expected, InstallDefaults.DefaultDirectory(InstallMode.System, BundleCatalog));
    }

    [Fact]
    public void DefaultDirectory_Portable_UsesAppNameForSingleAndSharedRootForBundle()
    {
        string currentDirectory = Environment.CurrentDirectory;

        Assert.Equal(
            Path.Combine(currentDirectory, "VolumeTrayAppDotNET"),
            InstallDefaults.DefaultDirectory(InstallMode.Portable, SingleCatalog));
        Assert.Equal(
            Path.Combine(currentDirectory, "TrayAppDotNET"),
            InstallDefaults.DefaultDirectory(InstallMode.Portable, BundleCatalog));
    }

    [Theory]
    [InlineData(InstallMode.Local)]
    [InlineData(InstallMode.System)]
    [InlineData(InstallMode.Portable)]
    public void InstalledExecutablePath_IsFlatUnderTargetDirectory(InstallMode mode)
    {
        string path = InstallDefaults.InstalledExecutablePath(mode, @"C:\Target", "VolumeTrayAppDotNET");

        Assert.Equal(@"C:\Target\VolumeTrayAppDotNET.exe", path);
    }
}

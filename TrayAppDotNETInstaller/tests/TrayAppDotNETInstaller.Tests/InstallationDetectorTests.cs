using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Detection reads nothing but the executable in the folder each mode installs to, so these stand in for the disk
/// with a set of paths. They pin which copies are found, their order, and the mode the window starts on.
/// </summary>
public sealed class InstallationDetectorTests
{
    private const string BatteryApplicationName = "BatteryTrayAppDotNET";
    private const string VolumeApplicationName = "VolumeTrayAppDotNET";

    private static readonly EmbeddedPayloadCatalog BundleCatalog = new(
    [
        new EmbeddedPayload(VolumeApplicationName, 3, "VolumeTrayAppDotNET_3.tadn"),
        new EmbeddedPayload(BatteryApplicationName, 3, "BatteryTrayAppDotNET_3.tadn")
    ]);

    [Fact]
    public void Detect_FindsNothingWhenNoExecutableExists()
    {
        IReadOnlyList<DetectedInstallation> installations = InstallationDetector.Detect(BundleCatalog, static _ => false);

        Assert.Empty(installations);
        Assert.Null(InstallationDetector.PreferredMode(installations));
        Assert.Equal(0, InstallationDetector.CountApplications(installations));
    }

    [Fact]
    public void Detect_ReportsEachCopyInTheFolderItsModeInstallsTo()
    {
        HashSet<string> existingFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ExecutablePath(InstallMode.System, VolumeApplicationName),
            ExecutablePath(InstallMode.Local, BatteryApplicationName)
        };

        IReadOnlyList<DetectedInstallation> installations = InstallationDetector.Detect(BundleCatalog, existingFiles.Contains);

        // The catalog sorts by name, so Battery comes first
        Assert.Equal(2, installations.Count);
        Assert.Equal(
            new DetectedInstallation(BatteryApplicationName, InstallMode.Local, InstallDirectory(InstallMode.Local)),
            installations[0]);
        Assert.Equal(
            new DetectedInstallation(VolumeApplicationName, InstallMode.System, InstallDirectory(InstallMode.System)),
            installations[1]);
        Assert.Equal(2, InstallationDetector.CountApplications(installations));
    }

    [Fact]
    public void Detect_ListsSystemBeforeLocalForAnApplicationInstalledTwice()
    {
        HashSet<string> existingFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ExecutablePath(InstallMode.Local, VolumeApplicationName),
            ExecutablePath(InstallMode.System, VolumeApplicationName)
        };
        InstallMode[] expectedModes = [InstallMode.System, InstallMode.Local];

        IReadOnlyList<DetectedInstallation> installations = InstallationDetector.Detect(BundleCatalog, existingFiles.Contains);

        Assert.Equal(expectedModes, InstallationDetector.ModesOf(installations, VolumeApplicationName));
        Assert.Empty(InstallationDetector.ModesOf(installations, BatteryApplicationName));
        Assert.Equal(1, InstallationDetector.CountApplications(installations));
    }

    [Fact]
    public void Detect_DoesNotLookInThePortableFolder()
    {
        HashSet<string> existingFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ExecutablePath(InstallMode.Portable, VolumeApplicationName)
        };

        Assert.Empty(InstallationDetector.Detect(BundleCatalog, existingFiles.Contains));
    }

    [Fact]
    public void PreferredMode_TakesTheModeHoldingMostCopies()
    {
        DetectedInstallation[] installations =
        [
            new(BatteryApplicationName, InstallMode.Local, InstallDirectory(InstallMode.Local)),
            new(VolumeApplicationName, InstallMode.Local, InstallDirectory(InstallMode.Local)),
            new(VolumeApplicationName, InstallMode.System, InstallDirectory(InstallMode.System))
        ];

        Assert.Equal(InstallMode.Local, InstallationDetector.PreferredMode(installations));
        Assert.Equal(2, InstallationDetector.CountInMode(installations, InstallMode.Local));
        Assert.Equal(1, InstallationDetector.CountInMode(installations, InstallMode.System));
    }

    [Fact]
    public void PreferredMode_PrefersSystemOnATie()
    {
        DetectedInstallation[] installations =
        [
            new(BatteryApplicationName, InstallMode.Local, InstallDirectory(InstallMode.Local)),
            new(VolumeApplicationName, InstallMode.System, InstallDirectory(InstallMode.System))
        ];

        Assert.Equal(InstallMode.System, InstallationDetector.PreferredMode(installations));
    }

    private static string InstallDirectory(InstallMode mode) => InstallDefaults.DefaultDirectory(mode, BundleCatalog);

    private static string ExecutablePath(InstallMode mode, string applicationName) =>
        InstallDefaults.InstalledExecutablePath(mode, InstallDirectory(mode), applicationName);
}

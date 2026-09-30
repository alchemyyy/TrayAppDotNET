namespace TrayAppDotNETInstaller.Services;

/// <summary>An installed copy of one application, in the folder a Local or System install puts it.</summary>
public sealed record DetectedInstallation(string ApplicationName, InstallMode Mode, string Directory);

/// <summary>
/// Finds the copies of this installer's applications that Local and System installs already put on the machine.
/// Both modes install into fixed folders, so the executable in one of them is the whole signal. The uninstall
/// registry entry is deliberately not read: an app that updates itself never rewrites its build number there,
/// and a copy without an entry is still installed. Portable copies leave no record and are not looked for.
/// </summary>
public static class InstallationDetector
{
    // Probe order, which is also the tie-break: a System copy wins, as it does for the startup shortcut target
    private static InstallMode[] InstalledModes { get; } = [InstallMode.System, InstallMode.Local];

    public static IReadOnlyList<DetectedInstallation> Detect(EmbeddedPayloadCatalog catalog) =>
        Detect(catalog, File.Exists);

    /// <summary>Lists every installed copy, System before Local for each application. <paramref name="fileExists"/> lets tests stand in for the disk.</summary>
    internal static IReadOnlyList<DetectedInstallation> Detect(EmbeddedPayloadCatalog catalog, Func<string, bool> fileExists)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));
        FrameworkCompatibility.ThrowIfNull(fileExists, nameof(fileExists));

        List<DetectedInstallation> installations = [];
        foreach (EmbeddedPayload payload in catalog.Payloads)
        {
            foreach (InstallMode mode in InstalledModes)
            {
                string directory = InstallDefaults.DefaultDirectory(mode, catalog);
                string executablePath = InstallDefaults.InstalledExecutablePath(mode, directory, payload.ApplicationName);
                if (!fileExists(executablePath)) continue;

                installations.Add(new DetectedInstallation(payload.ApplicationName, mode, directory));
                InstallerLog.Write($"InstallationDetector: {payload.ApplicationName} is installed ({mode}) in {directory}");
            }
        }

        return installations;
    }

    /// <summary>
    /// The mode that holds the most installed copies, so the installer can start on it. A tie goes to System.
    /// Returns null when nothing is installed.
    /// </summary>
    public static InstallMode? PreferredMode(IReadOnlyList<DetectedInstallation> installations)
    {
        FrameworkCompatibility.ThrowIfNull(installations, nameof(installations));

        InstallMode? preferredMode = null;
        int preferredCount = 0;
        foreach (InstallMode mode in InstalledModes)
        {
            int count = CountInMode(installations, mode);
            if (count <= preferredCount) continue;

            preferredMode = mode;
            preferredCount = count;
        }

        return preferredMode;
    }

    /// <summary>How many of the installed copies live in <paramref name="mode"/>.</summary>
    public static int CountInMode(IReadOnlyList<DetectedInstallation> installations, InstallMode mode)
    {
        FrameworkCompatibility.ThrowIfNull(installations, nameof(installations));

        int count = 0;
        foreach (DetectedInstallation installation in installations)
        {
            if (installation.Mode == mode) count++;
        }

        return count;
    }

    /// <summary>The modes <paramref name="applicationName"/> is installed in, System first.</summary>
    public static IReadOnlyList<InstallMode> ModesOf(IReadOnlyList<DetectedInstallation> installations, string applicationName)
    {
        FrameworkCompatibility.ThrowIfNull(installations, nameof(installations));

        List<InstallMode> modes = [];
        foreach (InstallMode mode in InstalledModes)
        {
            foreach (DetectedInstallation installation in installations)
            {
                if (installation.Mode != mode) continue;
                if (!string.Equals(installation.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase)) continue;

                modes.Add(mode);
                break;
            }
        }

        return modes;
    }

    /// <summary>How many different applications have at least one installed copy.</summary>
    public static int CountApplications(IReadOnlyList<DetectedInstallation> installations)
    {
        FrameworkCompatibility.ThrowIfNull(installations, nameof(installations));

        HashSet<string> applicationNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (DetectedInstallation installation in installations) applicationNames.Add(installation.ApplicationName);

        return applicationNames.Count;
    }
}

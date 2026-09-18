namespace TrayAppDotNETInstaller.Services;

/// <summary>Directory conventions shared with the apps' own installers (TrayAppDotNETInstallLayout).</summary>
public static class InstallDefaults
{
    public const string SharedRootFolderName = "TrayAppDotNET";
    public const string ExecutableExtension = ".exe";

    // Keep in sync with TrayAppDotNETInstallLayout.InstalledApplicationsFolderName. Local installs sit in
    // their own folder because the shared root also holds each app's settings directory.
    public const string InstalledApplicationsFolderName = "apps";

    public static string DefaultDirectory(InstallMode mode, EmbeddedPayloadCatalog catalog)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        switch (mode)
        {
            case InstallMode.Local:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    SharedRootFolderName,
                    InstalledApplicationsFolderName);
            case InstallMode.System:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    SharedRootFolderName);
            case InstallMode.Portable:
                string folderName = catalog.IsBundle || catalog.Payloads.Count == 0
                    ? SharedRootFolderName
                    : catalog.Payloads[0].ApplicationName;
                return Path.Combine(Environment.CurrentDirectory, folderName);
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, message: "Unsupported install mode.");
        }
    }

    /// <summary>
    /// Every mode places &lt;App&gt;.exe directly in the target directory: the app installers use a flat shared
    /// root, and portable extraction unpacks the zip root into the chosen folder.
    /// </summary>
    public static string InstalledExecutablePath(InstallMode mode, string targetDirectory, string applicationName)
    {
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(targetDirectory, nameof(targetDirectory));
        FrameworkCompatibility.ThrowIfNullOrWhiteSpace(applicationName, nameof(applicationName));

        switch (mode)
        {
            case InstallMode.Local:
            case InstallMode.System:
            case InstallMode.Portable:
                return Path.Combine(targetDirectory, applicationName + ExecutableExtension);
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, message: "Unsupported install mode.");
        }
    }
}

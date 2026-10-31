namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// Directory conventions shared with the apps' own installers; the folder names come from the linked
/// TrayAppDotNETInstallContract, the same source TrayAppDotNETInstallLayout uses.
/// </summary>
public static class InstallDefaults
{
    public const string ExecutableExtension = ".exe";

    public static string DefaultDirectory(InstallMode mode, EmbeddedPayloadCatalog catalog)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        switch (mode)
        {
            case InstallMode.Local:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    TrayAppDotNETInstallContract.SharedRootFolderName,
                    TrayAppDotNETInstallContract.InstalledApplicationsFolderName);
            case InstallMode.System:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    TrayAppDotNETInstallContract.SharedRootFolderName);
            case InstallMode.Portable:
                string folderName = catalog.IsBundle || catalog.Payloads.Count == 0
                    ? TrayAppDotNETInstallContract.SharedRootFolderName
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

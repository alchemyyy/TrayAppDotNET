namespace TrayAppDotNETInstaller;

/// <summary>
/// The application icons compiled into the factory, one per tray app plus the suite icon. A stamped
/// installer selects its window icon from these by application name, so only the shell icon of the
/// stamped file needs Win32 resource surgery.
/// </summary>
internal static class InstallerIcons
{
    // Keep in sync with TrayAppDotNETInstallerIconResourcePrefix in the project file
    public const string ResourcePrefix = "TrayAppDotNETInstaller.Icons.";
    public const string SuiteIconName = "TrayAppDotNET";
    private const string IconExtension = ".ico";

    /// <summary>Returns the manifest resource name for an application or suite icon name.</summary>
    public static string ResourceName(string iconName) => ResourcePrefix + iconName + IconExtension;

    /// <summary>
    /// Every tray application the factory carries an icon for, sorted, without the suite icon. Read from the
    /// compiled resources rather than a list, so it follows whatever the project embeds.
    /// </summary>
    public static IReadOnlyList<string> ApplicationIconNames()
    {
        List<string> names = [];
        foreach (string resourceName in typeof(InstallerIcons).Assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            if (!resourceName.EndsWith(IconExtension, StringComparison.OrdinalIgnoreCase)) continue;

            string iconName = resourceName[ResourcePrefix.Length..^IconExtension.Length];
            if (iconName.Length == 0 || string.Equals(iconName, SuiteIconName, StringComparison.Ordinal)) continue;

            names.Add(iconName);
        }

        names.Sort(static (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        return names;
    }

    /// <summary>
    /// A single-application installer shows that application's icon. A bundle, or an installer with no
    /// payload at all, shows the suite icon.
    /// </summary>
    public static string ResourceNameForApplications(IReadOnlyList<string> applicationNames)
    {
        FrameworkCompatibility.ThrowIfNull(applicationNames, nameof(applicationNames));

        return applicationNames.Count == 1
            ? ResourceName(applicationNames[0])
            : ResourceName(SuiteIconName);
    }

    /// <summary>Opens an embedded icon by resource name, or null when the factory carries no such icon.</summary>
    public static Stream? Open(string resourceName) =>
        typeof(InstallerIcons).Assembly.GetManifestResourceStream(resourceName);

    /// <summary>Opens the named application icon, falling back to the suite icon for unknown names.</summary>
    public static Stream? OpenOrSuite(string iconName) =>
        Open(ResourceName(iconName)) ?? Open(ResourceName(SuiteIconName));
}

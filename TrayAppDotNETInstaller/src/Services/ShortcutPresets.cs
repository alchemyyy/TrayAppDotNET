namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// The shortcut choices that match what is on the machine now, so the window can start from them and pass a choice
/// on only when the user changed it. An unchanged choice reaches the apps as no flag, which leaves each app's
/// shortcuts as they are.
/// </summary>
public static class ShortcutPresets
{
    private const string ShortcutExtension = ".lnk";
    // Start Menu entry names the apps write; keep in sync with TrayAppDotNETStartMenuShortcutOptions
    private const string LocalSuffix = " (Local)";
    private const string SystemSuffix = " (System)";

    /// <summary>Checked when every application already has a desktop shortcut for the mode.</summary>
    public static bool Desktop(InstallMode mode, IReadOnlyList<string> applicationNames) =>
        Desktop(mode, applicationNames, File.Exists);

    /// <summary>
    /// Checked unless an application is already installed in the mode and the user removed its Start Menu entry.
    /// </summary>
    public static bool StartMenu(
        InstallMode mode,
        IReadOnlyList<string> applicationNames,
        IReadOnlyList<DetectedInstallation> installations) =>
        StartMenu(mode, applicationNames, installations, File.Exists);

    internal static bool Desktop(InstallMode mode, IReadOnlyList<string> applicationNames, Func<string, bool> fileExists)
    {
        if (mode == InstallMode.Portable || applicationNames.Count == 0) return false;

        string desktop = Environment.GetFolderPath(mode == InstallMode.System
            ? Environment.SpecialFolder.CommonDesktopDirectory
            : Environment.SpecialFolder.DesktopDirectory);
        return applicationNames.All(name => fileExists(Path.Combine(desktop, name + ShortcutExtension)));
    }

    internal static bool StartMenu(
        InstallMode mode,
        IReadOnlyList<string> applicationNames,
        IReadOnlyList<DetectedInstallation> installations,
        Func<string, bool> fileExists)
    {
        if (mode == InstallMode.Portable) return false;

        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        foreach (string name in applicationNames)
        {
            bool installed = installations.Any(installation =>
                installation.Mode == mode
                && string.Equals(installation.ApplicationName, name, StringComparison.OrdinalIgnoreCase));
            if (!installed) continue;

            bool hasEntry = fileExists(Path.Combine(programs, name + ShortcutExtension))
                            || fileExists(Path.Combine(programs, name + LocalSuffix + ShortcutExtension))
                            || fileExists(Path.Combine(programs, name + SystemSuffix + ShortcutExtension));
            if (!hasEntry) return false;
        }

        return true;
    }
}

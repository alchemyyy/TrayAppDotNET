namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Shell integration chosen for a local or system installation. Null leaves the choice to the install: the
/// desktop shortcut stays as it is, and the Start Menu entries stay in step with the installation unless the user
/// removed them from a copy that was already installed. The command-line names live in
/// <see cref="Contracts.TrayAppDotNETInstallContract"/>.
/// </summary>
public sealed record TrayAppDotNETInstallOptions(
    bool? CreateDesktopShortcut = null,
    bool? CreateStartMenuShortcut = null);

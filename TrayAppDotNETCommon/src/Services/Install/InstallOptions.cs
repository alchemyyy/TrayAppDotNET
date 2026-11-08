namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Shell integration chosen for a local or system installation. Null leaves the choice to the install: the
/// desktop shortcut stays as it is, and the Start Menu entries stay in step with the installation unless the user
/// removed them from a copy that was already installed. <see cref="RunAsAdministrator"/> turns an app's Always run
/// as administrator on or off for the installing account; only a system install of an app with a
/// <see cref="TrayAppDotNETRunAsAdministratorOption"/> applies it, and null leaves it as it is. The command-line
/// names live in <see cref="Contracts.TrayAppDotNETInstallContract"/>.
/// </summary>
public sealed record TrayAppDotNETInstallOptions(
    bool? CreateDesktopShortcut = null,
    bool? CreateStartMenuShortcut = null,
    bool? RunAsAdministrator = null);

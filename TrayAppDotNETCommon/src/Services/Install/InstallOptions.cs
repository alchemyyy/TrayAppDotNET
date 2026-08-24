namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Optional shell integration selected for a local or system installation.
/// </summary>
public sealed record TrayAppDotNETInstallOptions(
    bool CreateDesktopShortcut = false,
    bool CreateStartMenuShortcut = true)
{
    public const string SystemInstallArgument = "--install-system";
    public const string SourceExecutableArgument = "--source";
    public const string BuildNumberArgument = "--build";
    public const string SyncStartMenuArgument = "--sync-start-menu";
    public const string PrepareUninstallArgument = "--uninstall-prepare";
    public const string DesktopShortcutArgument = "--desktop-shortcut";
    public const string StartMenuShortcutArgument = "--start-menu-shortcut";
}

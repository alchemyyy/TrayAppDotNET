namespace TrayAppDotNETCommon.Contracts;

/// <summary>
/// Install contracts shared by the apps and the .NET Framework installer factory, which links this file as source.
/// Keep it free of APIs that .NET Framework 4.8 lacks. <c>.github/scripts/publish.py</c> reads
/// <see cref="SharedNativeDllNames"/> from this file, so keep that initializer a plain list of string literals.
/// </summary>
public static class TrayAppDotNETInstallContract
{
    /// <summary>Folder under %ProgramFiles% and %LOCALAPPDATA% that every app installs into.</summary>
    public const string SharedRootFolderName = "TrayAppDotNET";

    /// <summary>
    /// Subfolder of the LocalAppData root that holds Local installs, beside each app's settings folder, its log and
    /// the shared versions manifest.
    /// </summary>
    public const string InstalledApplicationsFolderName = "apps";

    /// <summary>Headless install of one scope, used by the installer factory: <c>--install-headless local|system</c>.</summary>
    public const string HeadlessInstallArgument = "--install-headless";

    /// <summary>Scope value of a per-user install.</summary>
    public const string LocalScopeArgument = "local";

    /// <summary>Scope value of an all-users install.</summary>
    public const string SystemScopeArgument = "system";

    /// <summary>The elevated half of a System install started by an app.</summary>
    public const string SystemInstallArgument = "--install-system";
    public const string SourceExecutableArgument = "--source";
    public const string BuildNumberArgument = "--build";
    public const string SyncStartMenuArgument = "--sync-start-menu";
    public const string PrepareUninstallArgument = "--uninstall-prepare";

    /// <summary>Followed by true or false; omitted, the existing desktop shortcut stays as it is.</summary>
    public const string DesktopShortcutArgument = "--desktop-shortcut";

    /// <summary>Followed by true or false; omitted, the Start Menu entries follow the installation.</summary>
    public const string StartMenuShortcutArgument = "--start-menu-shortcut";

    public const string DeleteSettingsArgument = "--delete-settings";
    public const string ProgressPipeArgument = "--progress-pipe";

    /// <summary>
    /// Starts an app in the notification area without showing a window, whatever its startup settings say. Installers
    /// launch apps this way once they finish.
    /// </summary>
    public const string HiddenArgument = "--hidden";

    /// <summary>Prefix of the line a successful headless install prints, followed by the installed exe path.</summary>
    public const string InstalledToPrefix = "Installed to ";

    /// <summary>
    /// Native DLLs every NativeAOT app loads from the shared install folder. Replacing one requires stopping every
    /// app that runs from that folder, and they are copied before any exe so a failure never leaves a new exe beside
    /// old native code.
    /// </summary>
    public static readonly string[] SharedNativeDllNames =
    {
        "av_libglesv2.dll",
        "libHarfBuzzSharp.dll",
        "libSkiaSharp.dll",
    };

    /// <summary>Native DLLs some builds ship beside <see cref="SharedNativeDllNames"/>; handled the same way when present.</summary>
    public static readonly string[] OptionalSharedNativeDllNames =
    {
        "libMonoPosixHelper.dll",
        "MonoPosixHelper.dll",
    };
}

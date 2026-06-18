using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon;

public sealed record TrayAppDotNETInstallIdentity(
    string ApplicationName,
    string Publisher,
    string? HelpLink,
    string InstalledExecutableFileName,
    string SettingsDirectory,
    string StartupShortcutPath,
    string LegacyRunKeyRegistryPath,
    Action<string>? Log = null,
    IReadOnlyList<TrayAppDotNETInstallDirectory>? InstalledDirectories = null)
{
    public string UninstallRegistrySubKeyPath =>
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ApplicationName;

    public string[] InstalledAppFileNames =>
    [
        InstalledExecutableFileName,
        ApplicationName + ".dll",
        ApplicationName + ".pdb",
        ApplicationName + ".runtimeconfig.json",
        ApplicationName + ".deps.json",
    ];

    public IReadOnlyList<TrayAppDotNETInstallDirectory> InstalledPayloadDirectories =>
        InstalledDirectories ?? [new TrayAppDotNETInstallDirectory("runtime")];

    public void WriteLog(string message) => Log?.Invoke(message);
}

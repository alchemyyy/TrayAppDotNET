namespace TrayAppDotNETCommon;

public sealed record TrayAppDotNETInstallIdentity(
    string ApplicationName,
    string Publisher,
    string? HelpLink,
    string SettingsDirectory,
    string StartupShortcutPath,
    string LegacyRunKeyRegistryPath,
    Action<string>? Log = null)
{
    public string UninstallRegistrySubKeyPath =>
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ApplicationName;

    public void WriteLog(string message) => Log?.Invoke(message);
}

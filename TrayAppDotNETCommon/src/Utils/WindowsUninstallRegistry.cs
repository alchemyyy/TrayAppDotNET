using Microsoft.Win32;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Reads/writes the Windows Add-or-Remove-Programs registry entry under HKCU (per-user) or HKLM (machine-wide),
/// so the app shows up in Settings &gt; Apps and can be uninstalled from there.
/// <see cref="InstallScope.WindowsStore"/> has no registry surface here; MSIX manages its own uninstallation.
/// </summary>
public static class WindowsUninstallRegistry
{
    public sealed record Entry(int? DisplayVersion, string? InstallLocation);

    public static Entry? Read(InstallScope scope, TrayAppDotNETInstallIdentity identity)
    {
        try
        {
            using RegistryKey? key = OpenRoot(scope).OpenSubKey(identity.UninstallRegistrySubKeyPath, writable: false);
            if (key == null) return null;

            int? version = null;
            if (key.GetValue("DisplayVersion") is string v && int.TryParse(v, out int parsed)) version = parsed;

            string? installLocation = key.GetValue("InstallLocation") as string;
            return new Entry(version, installLocation);
        }
        catch (Exception ex)
        {
            identity.WriteLog($"WindowsUninstallRegistry.Read({scope}): {ex.Message}");
            return null;
        }
    }

    public static bool Write(
        InstallScope scope,
        string installDir,
        int buildNumber,
        TrayAppDotNETInstallIdentity identity,
        string installedExecutableFileName)
    {
        try
        {
            string installExecutable = Path.Combine(installDir, installedExecutableFileName);
            using RegistryKey key = OpenRoot(scope).CreateSubKey(identity.UninstallRegistrySubKeyPath, writable: true);

            key.SetValue("DisplayName", identity.ApplicationName, RegistryValueKind.String);
            key.SetValue("DisplayVersion", buildNumber.ToString(), RegistryValueKind.String);
            key.SetValue("Publisher", identity.Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
            key.SetValue("DisplayIcon", installExecutable, RegistryValueKind.String);
            if (!string.IsNullOrEmpty(identity.HelpLink))
            {
                key.SetValue("HelpLink", identity.HelpLink, RegistryValueKind.String);
                key.SetValue("URLInfoAbout", identity.HelpLink, RegistryValueKind.String);
            }

            key.SetValue("UninstallString",
                $"\"{installExecutable}\" --uninstall \"{installDir}\" --scope {InstallScopeExtensions.ToArg(scope)}",
                RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            // Best-effort EstimatedSize (in KB) so Add/Remove Programs shows a size.
            try
            {
                if (File.Exists(installExecutable))
                {
                    long bytes = new FileInfo(installExecutable).Length;
                    key.SetValue("EstimatedSize", (int)(bytes / 1024L), RegistryValueKind.DWord);
                }
            }
            catch
            {
                /* size is decorative; never block install on it */
            }

            return true;
        }
        catch (Exception ex)
        {
            identity.WriteLog($"WindowsUninstallRegistry.Write({scope}): {ex.Message}");
            return false;
        }
    }

    public static bool Remove(InstallScope scope, TrayAppDotNETInstallIdentity identity)
    {
        try
        {
            using RegistryKey root = OpenRoot(scope);
            using RegistryKey? key = root.OpenSubKey(identity.UninstallRegistrySubKeyPath);
            if (key == null) return true;
            root.DeleteSubKeyTree(identity.UninstallRegistrySubKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            identity.WriteLog($"WindowsUninstallRegistry.Remove({scope}): {ex.Message}");
            return false;
        }
    }

    private static RegistryKey OpenRoot(InstallScope scope) => scope switch
    {
        InstallScope.LocalAppData => Registry.CurrentUser,
        InstallScope.ProgramFiles => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(
            nameof(scope),
            $"WindowsUninstallRegistry does not apply to {scope}."),
    };
}

using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Identifies the local and system desktop shortcut locations for one application.
/// </summary>
public sealed record TrayAppDotNETDesktopShortcutOptions(
    string ApplicationName,
    TrayAppDotNETInstallLayout Layout,
    Action<string>? Log = null)
{
    public string LocalShortcutPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ApplicationName + ".lnk");

    public string SystemShortcutPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        ApplicationName + ".lnk");
}

/// <summary>
/// Creates or removes the desktop shortcut appropriate for an installation scope.
/// </summary>
public sealed class TrayAppDotNETDesktopShortcut(TrayAppDotNETDesktopShortcutOptions options)
{
    /// <summary>
    /// Creates or removes the shortcut for the requested local or system install scope.
    /// </summary>
    public TrayAppDotNETInstallResult SetEnabled(InstallScope scope, bool enabled)
    {
        (string shortcutPath, string targetExecutable) = scope switch
        {
            InstallScope.LocalAppData =>
                (options.LocalShortcutPath, options.Layout.LocalAppDataInstallExecutable),
            InstallScope.ProgramFiles =>
                (options.SystemShortcutPath, options.Layout.ProgramFilesInstallExecutable),
            _ => (string.Empty, string.Empty)
        };

        if (string.IsNullOrWhiteSpace(shortcutPath))
            return new TrayAppDotNETInstallResult(Success: false, $"Desktop shortcuts are not supported for {scope}");

        try
        {
            if (!enabled)
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                if (File.Exists(shortcutPath))
                {
                    return new TrayAppDotNETInstallResult(Success: false,
                        $"Desktop shortcut was not removed: {shortcutPath}");
                }

                options.Log?.Invoke($"TrayAppDotNETDesktopShortcut.SetEnabled: removed {shortcutPath}");
                return new TrayAppDotNETInstallResult(true);
            }

            if (!File.Exists(targetExecutable))
            {
                return new TrayAppDotNETInstallResult(
                    Success: false,
                    $"Cannot create desktop shortcut because the installed executable is missing: {targetExecutable}");
            }

            string? shortcutDirectory = Path.GetDirectoryName(shortcutPath);
            if (string.IsNullOrWhiteSpace(shortcutDirectory))
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    $"Cannot determine desktop directory for {shortcutPath}");
            }

            Directory.CreateDirectory(shortcutDirectory);
            string temporaryPath = Path.Combine(
                shortcutDirectory,
                $".{options.ApplicationName}-{Guid.NewGuid():N}.lnk");

            try
            {
                Interop.ShellLink.Create(temporaryPath, targetExecutable, options.ApplicationName);
                File.Move(temporaryPath, shortcutPath, overwrite: true);
            }
            finally
            {
                TryDeleteTemporaryShortcut(temporaryPath);
            }

            if (!File.Exists(shortcutPath))
            {
                return new TrayAppDotNETInstallResult(Success: false,
                    $"Desktop shortcut was not created: {shortcutPath}");
            }

            options.Log?.Invoke(
                $"TrayAppDotNETDesktopShortcut.SetEnabled: created {shortcutPath} -> {targetExecutable}");
            return new TrayAppDotNETInstallResult(true);
        }
        catch (Exception exception)
        {
            options.Log?.Invoke(
                $"TrayAppDotNETDesktopShortcut.SetEnabled({scope}, {enabled}): {exception}");
            return new TrayAppDotNETInstallResult(Success: false, exception.Message);
        }
    }

    private void TryDeleteTemporaryShortcut(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (Exception exception)
        {
            options.Log?.Invoke(
                $"TrayAppDotNETDesktopShortcut.TryDeleteTemporaryShortcut({temporaryPath}): {exception.Message}");
        }
    }
}

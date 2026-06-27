using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Generates a self-deleting .bat in <c>%TEMP%</c> that removes the installed app payload,
/// the Add/Remove Programs registry entry, optionally the settings folder,
/// and the shell:startup shortcut, then spawns it detached.
/// </summary>
public static class UninstallScript
{
    /// <summary>
    /// Writes and spawns the uninstallation .bat.
    /// Returns the spawned <see cref="Process"/> (with <c>EnableRaisingEvents=true</c>)
    /// so the caller can hook <c>Exited</c> for live UI refresh,
    /// or <c>null</c> if the spawn failed. UAC decline is reported via <paramref name="userCancelled"/>.
    /// </summary>
    public static Process? Run(
        string installDir,
        InstallScope scope,
        bool deleteSettings,
        TrayAppDotNETInstallIdentity identity,
        string installedExecutableFileName,
        TrayAppDotNETInstallPayload payload,
        out bool userCancelled)
    {
        userCancelled = false;
        try
        {
            string batPath = Path.Combine(
                Path.GetTempPath(),
                $"{identity.ApplicationName}-uninstall-{Guid.NewGuid():N}.bat");

            string content = BuildScript(installDir, scope, deleteSettings, identity, installedExecutableFileName, payload);
            File.WriteAllText(batPath, content, Encoding.ASCII);

            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            // System-wide uninstall touches Program Files and HKLM. UAC fires here at the click.
            if (scope == InstallScope.ProgramFiles) psi.Verb = "runas";

            Process? p = Process.Start(psi);
            p?.EnableRaisingEvents = true;
            return p;
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 0x800704C7 || ex.NativeErrorCode == 1223)
        {
            userCancelled = true;
            identity.WriteLog("UninstallScript.Run: user cancelled UAC prompt");
            return null;
        }
        catch (Exception ex)
        {
            identity.WriteLog($"UninstallScript.Run: {ex}");
            return null;
        }
    }

    private static string BuildScript(
        string installDir,
        InstallScope scope,
        bool deleteSettings,
        TrayAppDotNETInstallIdentity identity,
        string installedExecutableFileName,
        TrayAppDotNETInstallPayload payload)
    {
        string installExecutable = Path.Combine(installDir, installedExecutableFileName);
        string regKeyFullPath = (scope == InstallScope.ProgramFiles ? "HKLM\\" : "HKCU\\")
                                + identity.UninstallRegistrySubKeyPath;
        string startupLnk = identity.StartupShortcutPath;
        string settingsDir = identity.SettingsDirectory;

        // PowerShell single-quoted literal: any embedded ' must be doubled.
        string installExecutableForPs = installExecutable.Replace("'", "''");
        string installDirForPs = installDir.Replace("'", "''");
        string startupLnkForPs = startupLnk.Replace("'", "''");
        // Trailing separator pins the StartsWith comparison to whole-segment matches:
        // "C:\Foo\Bar" must not match "C:\Foo\BarBaz\app.exe".
        string installDirPrefixForPs = (installDir.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Replace("'", "''");

        StringBuilder sb = new();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine("set ERR=0");
        sb.AppendLine();
        if (scope == InstallScope.ProgramFiles)
        {
            sb.AppendLine("rem Reconcile Start Menu shortcuts across every user profile (and the Default");
            sb.AppendLine("rem template) from an already-elevated context. The install exe is still on disk;");
            sb.AppendLine("rem we ride its admin-action handler, which does the all-profiles walk in C# and");
            sb.AppendLine("rem exits before the kill/wipe steps run. start /wait blocks the bat until exit.");
            sb.AppendLine(
                $"start \"\" /wait \"{EscBat(installExecutable)}\" --admin-action sync-startmenu --remove-scope system");
            sb.AppendLine();
        }

        sb.AppendLine("rem Kill processes whose executable path equals the install exe (and only those -");
        sb.AppendLine("rem a portable copy of the app running from elsewhere is untouched).");
        sb.AppendLine("rem Loops with a brief sleep so the watcher/monitored restart race resolves.");
        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                      + "\"$p = '" + installExecutableForPs + "'; "
                      + "for ($i=0; $i -lt 20; $i++) { "
                      + "$procs = Get-Process -Name " + identity.ApplicationName + " -ErrorAction SilentlyContinue "
                      + "| Where-Object { try { $_.Path -ieq $p } catch { $false } }; "
                      + "if (-not $procs) { break }; "
                      + "$procs | Stop-Process -Force -ErrorAction SilentlyContinue; "
                      + "Start-Sleep -Milliseconds 500 }\" >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Shared install root: remove only this app's files and leave sibling apps alone.");
        foreach (TrayAppDotNETInstallFile file in payload.InstalledFiles(installedExecutableFileName))
        {
            string target = Path.Combine(installDir, file.Name);
            if (file.RemoveOnlyWhenInstallRootHasNoExe)
            {
                string targetForPs = PsSingleQuoted(target);
                sb.AppendLine($"rem The {file.Name} file may be shared by sibling TrayAppDotNET apps. Remove it only");
                sb.AppendLine("rem after this app is gone and no other apphost exe remains in the install root.");
                sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                              + "\"$dir = '" + installDirForPs + "'; "
                              + "$target = '" + targetForPs + "'; "
                              + "if (Test-Path -LiteralPath $target) { "
                              + "$remaining = @(Get-ChildItem -LiteralPath $dir -Filter '*.exe' -File -ErrorAction SilentlyContinue); "
                              + "if ($remaining.Count -eq 0) { "
                              + "Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue; "
                              + "if (Test-Path -LiteralPath $target) { exit 1 } "
                              + "} }\" >nul 2>&1");
                sb.AppendLine("if errorlevel 1 set ERR=1");
                continue;
            }

            sb.AppendLine($"del /f /q \"{EscBat(target)}\" >nul 2>&1");
        }

        sb.AppendLine($"if exist \"{EscBat(installExecutable)}\" set ERR=1");
        if (payload.CopySourceDirectoryRootFiles)
        {
            sb.AppendLine("rem Shared root files: remove them only after no apphost exe remains.");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                          + "\"$dir = '" + installDirForPs + "'; "
                          + "if (Test-Path -LiteralPath $dir) { "
                          + "$remaining = @(Get-ChildItem -LiteralPath $dir -Filter '*.exe' -File -ErrorAction SilentlyContinue); "
                          + "if ($remaining.Count -eq 0) { "
                          + "Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue "
                          + "| Remove-Item -Force -ErrorAction SilentlyContinue; "
                          + "if (@(Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue).Count -ne 0) { exit 1 } "
                          + "} }\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 set ERR=1");
        }

        foreach (TrayAppDotNETInstallDirectory directory in payload.InstalledDirectories)
        {
            string targetForPs = PsSingleQuoted(Path.Combine(installDir, directory.Name));
            if (directory.RemoveOnlyWhenInstallRootHasNoExe)
            {
                sb.AppendLine(
                    $"rem The {directory.Name} folder may be shared by sibling TrayAppDotNET apps. Remove it only");
                sb.AppendLine("rem after this app is gone and no other apphost exe remains in the install root.");
                sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                              + "\"$dir = '" + installDirForPs + "'; "
                              + "$target = '" + targetForPs + "'; "
                              + "if (Test-Path -LiteralPath $target) { "
                              + "$remaining = @(Get-ChildItem -LiteralPath $dir -Filter '*.exe' -File -ErrorAction SilentlyContinue); "
                              + "if ($remaining.Count -eq 0) { "
                              + "Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue; "
                              + "if (Test-Path -LiteralPath $target) { exit 1 } "
                              + "} }\" >nul 2>&1");
            }
            else
            {
                sb.AppendLine($"rem Remove app-specific installed directory: {directory.Name}");
                sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                              + "\"$target = '" + targetForPs + "'; "
                              + "if (Test-Path -LiteralPath $target) { "
                              + "Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue; "
                              + "if (Test-Path -LiteralPath $target) { exit 1 } "
                              + "}\" >nul 2>&1");
            }

            sb.AppendLine("if errorlevel 1 set ERR=1");
        }

        sb.AppendLine("rem Remove the shared install dir only if it is empty after this app's files are gone.");
        sb.AppendLine($"rmdir \"{EscBat(installDir)}\" >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Registry: missing key returns errorlevel 1 (orphan-cleaned state) - not a real failure.");
        sb.AppendLine("rem Only flag if the key still exists after the delete (i.e. permission denied).");
        sb.AppendLine($"reg delete \"{regKeyFullPath}\" /f >nul 2>&1");
        sb.AppendLine($"reg query \"{regKeyFullPath}\" >nul 2>&1");
        sb.AppendLine("if not errorlevel 1 set ERR=1");
        sb.AppendLine();
        if (deleteSettings)
        {
            sb.AppendLine("rem User wants settings gone. Settings remain app-specific under the shared AppData root.");
            sb.AppendLine($"rmdir /s /q \"{EscBat(settingsDir)}\" >nul 2>&1");
            sb.AppendLine($"if exist \"{EscBat(settingsDir)}\" set ERR=1");
            sb.AppendLine("rem Settings may have been the last child keeping a shared root from being empty.");
            sb.AppendLine($"rmdir \"{EscBat(installDir)}\" >nul 2>&1");
            sb.AppendLine();
        }

        sb.AppendLine("rem Surgical shortcut delete: only remove the shell:startup .lnk if its target");
        sb.AppendLine("rem still points inside the install dir we're wiping. The C# pre-uninstall pass");
        sb.AppendLine("rem already retargeted it to a peer install / running exe when one was available;");
        sb.AppendLine("rem this catches the residual case (uninstalling the live install copy with no peer).");
        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                      + "\"$lnk = '" + startupLnkForPs + "'; "
                      + "$dir = '" + installDirPrefixForPs + "'; "
                      + "if (Test-Path -LiteralPath $lnk) { try { "
                      + "$ws = New-Object -ComObject WScript.Shell; "
                      + "$sc = $ws.CreateShortcut($lnk); "
                      + "$t = $sc.TargetPath; "
                      + "if ($t -and $t.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)) "
                      + "{ Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue } "
                      + "} catch { } }\" >nul 2>&1");
        sb.AppendLine("rem Legacy HKCU\\...\\Run entry from the pre-shortcut autostart era; idempotent removal.");
        sb.AppendLine($"reg delete \"HKCU\\{identity.LegacyRunKeyRegistryPath}\""
                      + $" /v \"{identity.ApplicationName}\" /f >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Self-delete and propagate ERR. (goto) discards the rest of the script,");
        sb.AppendLine("rem but the parsed compound chain on this line still runs to completion.");
        sb.AppendLine("(goto) 2>nul & del /f /q \"%~f0\" & exit /b %ERR%");
        return sb.ToString();
    }

    // cmd.exe always expands % at parse time. A literal % in an embedded path must be doubled
    // so a folder name like "50%off" doesn't trigger a (missing) variable expansion.
    private static string EscBat(string path) => path.Replace("%", "%%");

    private static string PsSingleQuoted(string value) => value.Replace("'", "''");
}

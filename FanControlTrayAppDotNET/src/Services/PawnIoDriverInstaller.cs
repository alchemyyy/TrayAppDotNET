using System.ComponentModel;
using System.Diagnostics;
using LibreHardwareMonitor.PawnIo;

namespace FanControlTrayAppDotNET.Services;

internal static class PawnIoDriverInstaller
{
    private static readonly Version RequiredVersion = new(2, 0, 0, 0);

    public static bool EnsureInstalled()
    {
        try
        {
            if (!NeedsInstall())
                return true;

            string setupPath = ResolveSetupPath();
            if (!File.Exists(setupPath))
            {
                TADNLog.Log($"PawnIO setup file not found: {setupPath}");
                return false;
            }

            TADNLog.Log($"Installing PawnIO from {setupPath}");
            ProcessStartInfo startInfo = new(setupPath, "-install") { UseShellExecute = true, };

            if (!TrayAppDotNETInstallationService.IsElevated(TADNLog.Log))
                startInfo.Verb = "runas";

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                TADNLog.Log("PawnIO installer did not start.");
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                TADNLog.Log($"PawnIO installer exited with code {process.ExitCode}.");
                return false;
            }

            TADNLog.Log("PawnIO installer completed.");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TADNLog.Log("PawnIO installer was cancelled by the user.");
            return false;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"PawnIO installer failed: {ex}");
            return false;
        }
    }

    private static bool NeedsInstall()
    {
        if (!PawnIo.IsInstalled)
        {
            TADNLog.Log("PawnIO is not installed; motherboard fan access requires it.");
            return true;
        }

        if (PawnIo.Version < RequiredVersion)
        {
            TADNLog.Log($"PawnIO {PawnIo.Version} is older than required {RequiredVersion}; updating.");
            return true;
        }

        TADNLog.Log($"PawnIO {PawnIo.Version} is installed.");
        return false;
    }

    private static string ResolveSetupPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        foreach (string candidate in EnumerateCandidatePaths(baseDirectory, processDirectory))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(baseDirectory, AppServices.PawnIoSetupFileName);
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string baseDirectory, string? processDirectory)
    {
        yield return Path.Combine(baseDirectory, AppServices.PawnIoSetupFileName);
        yield return Path.Combine(baseDirectory, "Resources", AppServices.PawnIoSetupFileName);

        if (string.IsNullOrWhiteSpace(processDirectory))
            yield break;

        yield return Path.Combine(processDirectory, AppServices.PawnIoSetupFileName);
        yield return Path.Combine(processDirectory, "Resources", AppServices.PawnIoSetupFileName);
    }
}

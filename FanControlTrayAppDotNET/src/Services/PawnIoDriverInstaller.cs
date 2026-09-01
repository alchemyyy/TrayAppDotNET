using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using LibreHardwareMonitor.PawnIo;

namespace FanControlTrayAppDotNET.Services;

internal static class PawnIoDriverInstaller
{
    private const int InstallerDownloadTimeoutSeconds = 30;
    private const long InstallerLength = 3_225_016;
    private const string InstallerFileName = "PawnIO_setup.exe";
    private const string InstallerSHA256 = "A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0";
    private const string InstallerVersion = "2.1.0";
    private static readonly byte[] ExpectedInstallerSHA256 = Convert.FromHexString(InstallerSHA256);
    private static readonly HttpClient InstallerHTTPClient = new()
    {
        Timeout = TimeSpan.FromSeconds(InstallerDownloadTimeoutSeconds)
    };
    private static readonly Uri InstallerURI = new(
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.1.0/PawnIO_setup.exe");
    private static readonly Version RequiredVersion = new(major: 2, minor: 0, build: 0, revision: 0);

    /// <summary>Installs the required PawnIO version from its verified publisher asset when needed.</summary>
    public static bool EnsureInstalled()
    {
        try
        {
            if (!NeedsInstall())
                return true;

            string setupPath = ResolveOrDownloadSetupPath();

            TADNLog.Log($"Installing PawnIO from {setupPath}");
            ProcessStartInfo startInfo = new(setupPath, arguments: "-install") { UseShellExecute = true };

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

    /// <summary>Reports whether PawnIO is missing or older than the minimum supported version.</summary>
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

    /// <summary>Uses a valid legacy sidecar or downloads the pinned installer into the user cache.</summary>
    private static string ResolveOrDownloadSetupPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        foreach (string candidate in EnumerateLegacyCandidatePaths(baseDirectory, processDirectory))
        {
            if (!File.Exists(candidate))
                continue;

            if (HasExpectedInstaller(candidate))
                return candidate;

            TADNLog.Log($"Ignoring PawnIO installer with an unexpected size or SHA-256: {candidate}");
        }

        return DownloadInstaller();
    }

    /// <summary>Returns paths used by releases that bundled the installer before it was removed.</summary>
    private static IEnumerable<string> EnumerateLegacyCandidatePaths(string baseDirectory, string? processDirectory)
    {
        yield return Path.Combine(baseDirectory, InstallerFileName);
        yield return Path.Combine(baseDirectory, path2: "Resources", InstallerFileName);

        if (string.IsNullOrWhiteSpace(processDirectory))
            yield break;

        yield return Path.Combine(processDirectory, InstallerFileName);
        yield return Path.Combine(processDirectory, path2: "Resources", InstallerFileName);
    }

    /// <summary>Downloads the official installer and atomically promotes it after digest validation.</summary>
    private static string DownloadInstaller()
    {
        string installerPath = GetInstallerCachePath();
        if (HasExpectedInstaller(installerPath))
            return installerPath;

        string installerDirectory = Path.GetDirectoryName(installerPath)
                                    ?? throw new InvalidOperationException("PawnIO installer cache path has no directory.");
        Directory.CreateDirectory(installerDirectory);
        string temporaryPath = installerPath + "." + Guid.NewGuid().ToString("N") + ".download";

        TADNLog.Log($"Downloading PawnIO {InstallerVersion} from {InstallerURI}");
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, InstallerURI);
            using HttpResponseMessage response = InstallerHTTPClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value != InstallerLength)
                throw new InvalidDataException(
                    $"PawnIO installer download length was {contentLength.Value}; expected {InstallerLength}.");

            using (Stream responseStream = response.Content.ReadAsStream())
            using (FileStream outputStream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                responseStream.CopyTo(outputStream);
            }

            if (!HasExpectedInstaller(temporaryPath))
                throw new InvalidDataException("PawnIO installer download failed SHA-256 validation.");

            File.Move(temporaryPath, installerPath, overwrite: true);
            return installerPath;
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>Builds the per-user cache path for the pinned PawnIO installer.</summary>
    private static string GetInstallerCachePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheRoot = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;
        return Path.Combine(
            cacheRoot,
            "TrayAppDotNET",
            "Downloads",
            "PawnIO",
            InstallerVersion,
            InstallerFileName);
    }

    /// <summary>Checks the expected file length and publisher-provided SHA-256 digest.</summary>
    internal static bool HasExpectedInstaller(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        FileInfo file = new(filePath);
        if (file.Length != InstallerLength)
            return false;

        using FileStream input = file.OpenRead();
        byte[] actualSHA256 = SHA256.HashData(input);
        return CryptographicOperations.FixedTimeEquals(actualSHA256, ExpectedInstallerSHA256);
    }

    /// <summary>Removes an incomplete download without hiding the original download failure.</summary>
    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        if (!File.Exists(temporaryPath))
            return;

        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not remove incomplete PawnIO download {temporaryPath}: {ex.Message}");
        }
    }
}

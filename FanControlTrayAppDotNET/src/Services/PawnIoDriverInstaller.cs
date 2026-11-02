using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using LibreHardwareMonitor.PawnIo;
using TrayAppDotNETCommon.Services.Ipc;

namespace FanControlTrayAppDotNET.Services;

internal enum PawnIoInstallOutcome
{
    AlreadyInstalled,
    Installed,

    /// <summary>The user declined the administrator prompt; startup does not ask again until settings do.</summary>
    Declined,

    /// <summary>A previous decline is remembered, so a startup check did not prompt.</summary>
    SkippedAfterDecline,
    Failed
}

/// <summary>
/// Installs the PawnIO driver LibreHardwareMonitor needs for motherboard fan access. Everything runs off the UI
/// thread with cancellation and bounded waits. The setup file is hashed through a handle that stays open, shared for
/// reading only, until the setup process has started, so nothing can replace it between the check and the launch.
/// </summary>
internal static class PawnIoDriverInstaller
{
    private const long InstallerLength = 3_225_016;
    private const string InstallerFileName = "PawnIO_setup.exe";
    private const string InstallerSHA256 = "A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0";
    private const string InstallerVersion = "2.1.0";
    private const string DeclinedMarkerFileName = "pawnio-install-declined";
    private static readonly byte[] ExpectedInstallerSHA256 = Convert.FromHexString(InstallerSHA256);

    // The per-request limit stays off; the download has its own deadline covering the body as well
    private static readonly HttpClient InstallerHTTPClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly Uri InstallerURI = new(
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.1.0/PawnIO_setup.exe");
    private static readonly Version RequiredVersion = new(major: 2, minor: 0, build: 0, revision: 0);

    /// <summary>Gets whether PawnIO is missing or older than the minimum supported version.</summary>
    public static bool IsInstallNeeded()
    {
        if (!PawnIo.IsInstalled)
        {
            TADNLog.Log("PawnIO is not installed; motherboard fan access requires it.");
            return true;
        }

        if (PawnIo.Version < RequiredVersion)
        {
            TADNLog.Log($"PawnIO {PawnIo.Version} is older than required {RequiredVersion}.");
            return true;
        }

        TADNLog.Log($"PawnIO {PawnIo.Version} is installed.");
        return false;
    }

    /// <summary>Gets whether the user declined the administrator prompt at an earlier start.</summary>
    public static bool InstallWasDeclined => File.Exists(DeclinedMarkerPath);

    /// <summary>
    /// Installs PawnIO when needed. A startup check (<paramref name="userRequested"/> false) does not prompt again
    /// after the user declined once; the settings offer passes true.
    /// </summary>
    public static async Task<PawnIoInstallOutcome> EnsureInstalledAsync(
        bool userRequested,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsInstallNeeded())
            {
                ForgetDecline();
                return PawnIoInstallOutcome.AlreadyInstalled;
            }

            if (!userRequested && InstallWasDeclined)
            {
                TADNLog.Log("PawnIO install skipped: the administrator prompt was declined before; Settings offers it");
                return PawnIoInstallOutcome.SkippedAfterDecline;
            }

            using FileStream setup = await OpenVerifiedSetupAsync(cancellationToken).ConfigureAwait(false);
            TADNLog.Log($"Installing PawnIO from {setup.Name}");
            ProcessStartInfo startInfo = new(setup.Name, arguments: "-install") { UseShellExecute = true };
            if (!TrayAppDotNETInstallationService.IsElevated(TADNLog.Log))
                startInfo.Verb = "runas";

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                TADNLog.Log("PawnIO installer did not start.");
                return PawnIoInstallOutcome.Failed;
            }

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeConstants.PawnIoInstallerTimeoutMs);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TADNLog.Log($"PawnIO installer did not finish within {TimeConstants.PawnIoInstallerTimeoutMs} ms.");
                return PawnIoInstallOutcome.Failed;
            }

            if (process.ExitCode != 0)
            {
                TADNLog.Log($"PawnIO installer exited with code {process.ExitCode}.");
                return PawnIoInstallOutcome.Failed;
            }

            ForgetDecline();
            TADNLog.Log("PawnIO installer completed.");
            return PawnIoInstallOutcome.Installed;
        }
        catch (Win32Exception ex) when (TrayAppDotNETElevatedLaunch.IsUserCancelled(ex))
        {
            TADNLog.Log("PawnIO installer was cancelled at the administrator prompt.");
            RememberDecline();
            return PawnIoInstallOutcome.Declined;
        }
        catch (OperationCanceledException)
        {
            TADNLog.Log("PawnIO install cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"PawnIO installer failed: {ex}");
            return PawnIoInstallOutcome.Failed;
        }
    }

    /// <summary>
    /// Deletes the download cache. Fan's uninstall runs this; the driver itself stays installed, since other
    /// software may use it.
    /// </summary>
    public static void DeleteDownloadCache()
    {
        string cacheDirectory = DownloadCacheDirectory;
        if (!Directory.Exists(cacheDirectory)) return;

        try
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not remove the PawnIO download cache {cacheDirectory}: {ex.Message}");
        }
    }

    /// <summary>The cache folder under the shared %LOCALAPPDATA%\TrayAppDotNET root.</summary>
    internal static string DownloadCacheDirectory =>
        Path.Combine(TrayAppDotNETAppInstallServices.LocalAppDataRoot, "Downloads", "PawnIO");

    private static string DeclinedMarkerPath => Path.Combine(Program.AppLocalAppDataDirectory, DeclinedMarkerFileName);

    private static void RememberDecline()
    {
        try
        {
            Directory.CreateDirectory(Program.AppLocalAppDataDirectory);
            File.WriteAllText(DeclinedMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not remember the declined PawnIO install: {ex.Message}");
        }
    }

    private static void ForgetDecline()
    {
        try
        {
            File.Delete(DeclinedMarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not clear the declined PawnIO install marker: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the setup file opened for reading and shared for reading only, after verifying its length and
    /// SHA-256 through that same handle: a valid legacy sidecar, the cache, or a fresh download.
    /// </summary>
    private static async Task<FileStream> OpenVerifiedSetupAsync(CancellationToken cancellationToken)
    {
        string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        foreach (string candidate in EnumerateLegacyCandidatePaths(AppContext.BaseDirectory, processDirectory))
        {
            if (TryOpenVerified(candidate) is { } legacySetup) return legacySetup;
        }

        string installerPath = Path.Combine(DownloadCacheDirectory, InstallerVersion, InstallerFileName);
        if (TryOpenVerified(installerPath) is { } cachedSetup) return cachedSetup;

        await DownloadAsync(installerPath, cancellationToken).ConfigureAwait(false);
        return TryOpenVerified(installerPath)
               ?? throw new InvalidDataException("PawnIO installer download failed SHA-256 validation.");
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

    /// <summary>Downloads the official installer under a deadline that covers the whole body, then promotes it.</summary>
    private static async Task DownloadAsync(string installerPath, CancellationToken cancellationToken)
    {
        string installerDirectory = Path.GetDirectoryName(installerPath)
                                    ?? throw new InvalidOperationException("PawnIO installer cache path has no directory.");
        Directory.CreateDirectory(installerDirectory);
        string temporaryPath = installerPath + "." + Guid.NewGuid().ToString("N") + ".download";

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeConstants.PawnIoDownloadTimeoutMs);
        TADNLog.Log($"Downloading PawnIO {InstallerVersion} from {InstallerURI}");
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, InstallerURI);
            using HttpResponseMessage response = await InstallerHTTPClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value != InstallerLength)
            {
                throw new InvalidDataException(
                    $"PawnIO installer download length was {contentLength.Value}; expected {InstallerLength}.");
            }

            await using (Stream responseStream =
                         await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (FileStream outputStream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await responseStream.CopyToAsync(outputStream, deadline.Token).ConfigureAwait(false);
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"PawnIO installer download did not finish within {TimeConstants.PawnIoDownloadTimeoutMs} ms.");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>
    /// Opens <paramref name="filePath"/> for reading, shared for reading only, and returns the stream when its length
    /// and SHA-256 match the pinned installer; otherwise null. The caller keeps the stream open until the setup
    /// process has started.
    /// </summary>
    internal static FileStream? TryOpenVerified(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        FileStream? input = null;
        try
        {
            input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length == InstallerLength &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), ExpectedInstallerSHA256))
            {
                FileStream verified = input;
                input = null;
                return verified;
            }

            TADNLog.Log($"Ignoring PawnIO installer with an unexpected size or SHA-256: {filePath}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TADNLog.Log($"Could not open PawnIO installer {filePath}: {ex.Message}");
            return null;
        }
        finally
        {
            input?.Dispose();
        }
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

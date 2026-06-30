using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services;

public sealed record UpdateInfo(
    int Version,
    string TagName,
    string ReleaseName,
    string Changelog,
    string AssetUrl,
    string AssetName,
    string AssetSha256,
    long AssetSize);

[XmlRoot("versions")]
internal sealed class VersionsManifest
{
    [XmlElement("release")]
    public VersionsRelease Release { get; set; } = new();

    [XmlElement("artifacts")]
    public VersionsArtifacts Artifacts { get; set; } = new();
}

internal sealed class VersionsRelease
{
    [XmlAttribute("tag")]
    public string Tag { get; set; } = string.Empty;

    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class VersionsArtifacts
{
    [XmlElement("artifact")]
    public List<VersionsArtifact> Artifacts { get; set; } = [];
}

internal sealed class VersionsArtifact
{
    [XmlAttribute("profile")]
    public string Profile { get; set; } = string.Empty;

    [XmlAttribute("kind")]
    public string Kind { get; set; } = string.Empty;

    [XmlAttribute("appId")]
    public string AppId { get; set; } = string.Empty;

    [XmlAttribute("version")]
    public string Version { get; set; } = string.Empty;

    [XmlAttribute("fileName")]
    public string FileName { get; set; } = string.Empty;

    [XmlAttribute("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [XmlAttribute("size")]
    public string Size { get; set; } = string.Empty;
}

public enum UpdateCheckResult
{
    Success,
    Failed,
    Cancelled,
}

public sealed class UpdateCheckOptions
{
    public required Uri VersionsManifestUrl { get; init; }
    public required string RepositoryOwner { get; init; }
    public required string RepositoryName { get; init; }
    public required string ApplicationName { get; init; }
    public required int CurrentBuild { get; init; }
    public required string UserAgent { get; init; }
    public required Func<string> StagingDirectory { get; init; }
    public required Func<bool> IsEnabled { get; init; }
    public required Func<TimeSpan> PollInterval { get; init; }
    public required Func<Action, Task> InvokeOnUIThread { get; init; }

    public string? StagingFilePrefix { get; init; }

    public Func<string?> CurrentExecutablePath { get; init; } =
        () => Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;

    public TimeSpan StartupDelay { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckStartupDelayMs);
    public TimeSpan MinPollInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckIntervalMinMs);
    public TimeSpan MaxPollInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckIntervalMaxMs);
    public TimeSpan NetworkTimeout { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateNetworkTimeoutMs);
    public TimeSpan FailureRetryInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckFailureRetryMs);
    public int AssetDownloadMaxAttempts { get; init; } = TimeConstants.UpdateAssetDownloadMaxAttempts;
    public TimeSpan AssetDownloadInitialBackoff { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateAssetDownloadInitialBackoffMs);
}

public sealed class UpdateCheckService : IDisposable
{
    private const int HttpUnauthorized = 401;
    private const int HttpForbidden = 403;
    private const int HttpNotFound = 404;
    private const int PollFlagIdle = 0;
    private const int PollFlagBusy = 1;

    private readonly UpdateCheckOptions _options;
    private readonly HttpClient _http;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TaskCompletionSource? _manualKick;
    private int _pollInFlight;
    private TaskCompletionSource? _pollDone;
    private UpdateInfo? _available;
    private DateTime? _lastCheckTimeUtc;
    private bool _isChecking;
    private UpdateCheckResult? _lastResult;
    private bool _disposed;

    public UpdateCheckService(UpdateCheckOptions options)
    {
        _options = ValidateOptions(options);

        _http = new HttpClient { Timeout = _options.NetworkTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public event Action? StateChanged;

    public UpdateInfo? AvailableUpdate => _available;

    public DateTime? LastCheckTimeUtc => _lastCheckTimeUtc;

    public bool IsChecking => _isChecking;

    public UpdateCheckResult? LastResult => _lastResult;

    public void Start()
    {
        if (_disposed) return;

        Stop();
        CancellationTokenSource cts = new();
        _loopCts = cts;
        _loopTask = Task.Run(() => RunLoopAsync(cts.Token), cts.Token);
    }

    public void Stop()
    {
        CancellationTokenSource? cts = Interlocked.Exchange(ref _loopCts, null);
        Task? loopTask = Interlocked.Exchange(ref _loopTask, null);
        if (cts == null) return;

        try { cts.Cancel(); }
        catch { }

        if (loopTask != null)
            loopTask.ContinueWith(_ => Safe.Dispose(cts), TaskScheduler.Default);
        else
            Safe.Dispose(cts);
    }

    public async Task<UpdateInfo?> CheckNowAsync()
    {
        if (_disposed) return _available;

        if (Interlocked.CompareExchange(ref _pollInFlight, PollFlagBusy, PollFlagIdle) != PollFlagIdle)
        {
            TaskCompletionSource? running = Volatile.Read(ref _pollDone);
            Volatile.Read(ref _manualKick)?.TrySetResult();
            if (running != null) await running.Task.ConfigureAwait(false);
            return _available;
        }

        Volatile.Read(ref _manualKick)?.TrySetResult();

        TaskCompletionSource pollDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pollDone, pollDone);
        try
        {
            await PollOnceAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _pollDone, null);
            pollDone.TrySetResult();
            Interlocked.Exchange(ref _pollInFlight, PollFlagIdle);
        }

        return _available;
    }

    public async Task<bool> DownloadAndStageAsync(UpdateInfo info, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(info);

        await SetCheckingAsync(true).ConfigureAwait(false);
        string? zipPath = null;
        string? extractDirectory = null;
        string? scriptPath = null;
        bool launched = false;
        try
        {
            string stagingDirectory = _options.StagingDirectory();
            if (string.IsNullOrWhiteSpace(stagingDirectory))
                throw new InvalidOperationException("Update staging directory cannot be empty.");

            Directory.CreateDirectory(stagingDirectory);
            string prefix = SafeFileNamePart(_options.StagingFilePrefix)
                            ?? SafeFileNamePart(_options.ApplicationName)
                            ?? "trayapp";
            string updateId = $"{prefix}_update_{info.Version}_{Guid.NewGuid():N}";

            zipPath = Path.Combine(stagingDirectory, updateId + ".zip");
            extractDirectory = Path.Combine(stagingDirectory, updateId);
            scriptPath = Path.Combine(stagingDirectory, updateId + ".bat");

            bool downloaded = await DownloadAssetWithRetryAsync(info.AssetUrl, zipPath, token)
                .ConfigureAwait(false);
            if (!downloaded) return false;

            if (info.AssetSize > 0)
            {
                FileInfo onDisk = new(zipPath);
                if (onDisk.Length != info.AssetSize)
                {
                    TADNLog.Log(
                        $"UpdateCheckService.DownloadAndStageAsync: size mismatch "
                        + $"(got {onDisk.Length}, expected {info.AssetSize})");
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(info.AssetSha256))
            {
                string actualSha = await Sha256FileAsync(zipPath, token).ConfigureAwait(false);
                if (!string.Equals(actualSha, info.AssetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TADNLog.Log(
                        $"UpdateCheckService.DownloadAndStageAsync: sha256 mismatch "
                        + $"(got {actualSha}, expected {info.AssetSha256})");
                    return false;
                }
            }

            ExtractZip(zipPath, extractDirectory);

            string currentExe = _options.CurrentExecutablePath()
                                ?? throw new InvalidOperationException("Could not resolve current executable path.");
            string? targetDirectory = Path.GetDirectoryName(currentExe);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException($"Could not resolve current executable directory: {currentExe}");

            string expectedExe = Path.Combine(extractDirectory, Path.GetFileName(currentExe));
            if (!File.Exists(expectedExe))
                throw new InvalidOperationException($"Update package did not contain {Path.GetFileName(currentExe)}.");

            string scriptContents = BuildUpdateScript(
                Environment.ProcessId,
                extractDirectory,
                zipPath,
                targetDirectory,
                currentExe);
            await File.WriteAllTextAsync(scriptPath, scriptContents, Encoding.ASCII, token)
                .ConfigureAwait(false);

            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = stagingDirectory,
            };
            using Process? cmd = Process.Start(psi);
            launched = cmd != null;
            return launched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TADNLog.Log($"UpdateCheckService.DownloadAndStageAsync: {ex.Message}");
            return false;
        }
        finally
        {
            if (!launched)
            {
                TryDeleteFile(zipPath);
                TryDeleteDirectory(extractDirectory);
                TryDeleteFile(scriptPath);
            }

            await SetCheckingAsync(false).ConfigureAwait(false);
        }
    }

    public static int ParseVersionFromTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return 0;

        ReadOnlySpan<char> span = tag.AsSpan().Trim();
        if (span.Length == 0) return 0;

        if (span[0] == 'v' || span[0] == 'V') span = span[1..];
        if (span.StartsWith("TrayAppDotNET_", StringComparison.OrdinalIgnoreCase))
            span = span["TrayAppDotNET_".Length..];

        int dashIndex = span.IndexOf('-');
        if (dashIndex >= 0) span = span[..dashIndex];
        int plusIndex = span.IndexOf('+');
        if (plusIndex >= 0) span = span[..plusIndex];

        int end = 0;
        while (end < span.Length && char.IsDigit(span[end])) end++;
        if (end == 0) return 0;
        return int.TryParse(span[..end], NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            ? version
            : 0;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try { await Task.Delay(_options.StartupDelay, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!token.IsCancellationRequested)
        {
            if (_options.IsEnabled())
            {
                if (Interlocked.CompareExchange(ref _pollInFlight, PollFlagBusy, PollFlagIdle) == PollFlagIdle)
                {
                    TaskCompletionSource pollDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref _pollDone, pollDone);
                    try { await PollOnceAsync(token).ConfigureAwait(false); }
                    finally
                    {
                        Volatile.Write(ref _pollDone, null);
                        pollDone.TrySetResult();
                        Interlocked.Exchange(ref _pollInFlight, PollFlagIdle);
                    }
                }
            }

            TimeSpan interval = NextPollInterval();
            TaskCompletionSource kick = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _manualKick, kick);
            try
            {
                Task kickTask = kick.Task;
                Task delayTask = Task.Delay(interval, token);
                await Task.WhenAny(kickTask, delayTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            finally
            {
                Volatile.Write(ref _manualKick, null);
            }
        }
    }

    private TimeSpan NextPollInterval()
    {
        TimeSpan normal = NormalizedInterval(_options.PollInterval());
        if (_lastResult != UpdateCheckResult.Failed) return normal;

        TimeSpan retry = NormalizedInterval(_options.FailureRetryInterval);
        return retry < normal ? retry : normal;
    }

    private TimeSpan NormalizedInterval(TimeSpan requested)
    {
        if (requested < _options.MinPollInterval) return _options.MinPollInterval;
        if (requested > _options.MaxPollInterval) return _options.MaxPollInterval;
        return requested;
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        await SetCheckingAsync(true).ConfigureAwait(false);
        UpdateCheckResult result = UpdateCheckResult.Failed;
        try
        {
            UpdateInfo? info = await FetchLatestAsync(token).ConfigureAwait(false);
            UpdateInfo? newer = info != null && info.Version > _options.CurrentBuild ? info : null;
            await InvokeIfRunningAsync(() =>
            {
                _available = newer;
                _lastCheckTimeUtc = DateTime.UtcNow;
                _lastResult = UpdateCheckResult.Success;
            }).ConfigureAwait(false);
            result = UpdateCheckResult.Success;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result = UpdateCheckResult.Cancelled;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"UpdateCheckService.PollOnceAsync: {ex.Message}");
            await InvokeIfRunningAsync(() =>
            {
                _lastCheckTimeUtc = DateTime.UtcNow;
                _lastResult = UpdateCheckResult.Failed;
            }).ConfigureAwait(false);
        }
        finally
        {
            if (result == UpdateCheckResult.Cancelled)
            {
                await InvokeIfRunningAsync(() => _lastResult = UpdateCheckResult.Cancelled)
                    .ConfigureAwait(false);
            }

            await SetCheckingAsync(false).ConfigureAwait(false);
        }
    }

    private async Task SetCheckingAsync(bool value)
    {
        await InvokeIfRunningAsync(() =>
        {
            if (_isChecking == value) return;
            _isChecking = value;
            StateChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    private async Task InvokeIfRunningAsync(Action action)
    {
        try { await _options.InvokeOnUIThread(action).ConfigureAwait(false); }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
    }

    private async Task<UpdateInfo?> FetchLatestAsync(CancellationToken token)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, _options.VersionsManifestUrl);
        using HttpResponseMessage resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseContentRead, token)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Manifest request to {_options.VersionsManifestUrl} failed with HTTP "
                + $"{(int)resp.StatusCode} ({resp.ReasonPhrase}).",
                null,
                resp.StatusCode);

        await using Stream stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        VersionsManifest manifest = TrayXmlSerializer.Read<VersionsManifest>(stream);

        string tag = manifest.Release.Tag;
        string releaseName = string.IsNullOrWhiteSpace(manifest.Release.Name) ? tag : manifest.Release.Name;

        VersionsArtifact? appArtifact = manifest.Artifacts.Artifacts.FirstOrDefault(IsReleaseAppArtifact);
        if (appArtifact == null) return null;

        int version = ParsePositiveInt(appArtifact.Version);
        if (version <= 0) return null;

        string expectedAssetName = GitHubReleaseUrls.ReleaseAssetName(_options.ApplicationName, version);
        string manifestAssetName = string.IsNullOrWhiteSpace(appArtifact.FileName)
            ? expectedAssetName
            : appArtifact.FileName;
        if (!string.Equals(manifestAssetName, expectedAssetName, StringComparison.OrdinalIgnoreCase))
        {
            TADNLog.Log(
                $"UpdateCheckService.FetchLatestAsync: manifest asset {manifestAssetName} did not match "
                + $"expected asset {expectedAssetName}.");
            return null;
        }

        string displayName = $"{_options.ApplicationName} {version}";
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName;

        Uri assetUrl = GitHubReleaseUrls.LatestAppReleaseAssetUrl(
            _options.RepositoryOwner,
            _options.RepositoryName,
            _options.ApplicationName,
            version);
        string sha256 = appArtifact.Sha256;
        long size = ParsePositiveLong(appArtifact.Size);

        return new UpdateInfo(
            version,
            tag,
            displayName,
            "",
            assetUrl.ToString(),
            expectedAssetName,
            sha256,
            size);
    }

    private bool IsReleaseAppArtifact(VersionsArtifact artifact) =>
        string.Equals(artifact.AppId, _options.ApplicationName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(artifact.Profile, GitHubReleaseUrls.ReleaseProfile, StringComparison.OrdinalIgnoreCase)
        && string.Equals(artifact.Kind, "app", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> DownloadAssetWithRetryAsync(
        string assetUrl,
        string destination,
        CancellationToken token)
    {
        TimeSpan backoff = _options.AssetDownloadInitialBackoff;
        for (int attempt = 1; attempt <= _options.AssetDownloadMaxAttempts; attempt++)
        {
            try
            {
                using HttpResponseMessage resp = await _http
                    .GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
                if (IsTerminalHttpStatus(resp.StatusCode))
                {
                    TADNLog.Log(
                        $"UpdateCheckService.DownloadAssetWithRetryAsync: terminal HTTP {(int)resp.StatusCode}");
                    TryDeleteFile(destination);
                    return false;
                }

                resp.EnsureSuccessStatusCode();
                await using FileStream fs = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                TryDeleteFile(destination);
                throw;
            }
            catch (HttpRequestException ex) when (IsTerminalHttpRequestException(ex))
            {
                TADNLog.Log($"UpdateCheckService.DownloadAssetWithRetryAsync: terminal HTTP error: {ex.Message}");
                TryDeleteFile(destination);
                return false;
            }
            catch (Exception ex) when (attempt < _options.AssetDownloadMaxAttempts)
            {
                TADNLog.Log(
                    $"UpdateCheckService.DownloadAssetWithRetryAsync: attempt "
                    + $"{attempt}/{_options.AssetDownloadMaxAttempts} failed: {ex.Message}");
                TryDeleteFile(destination);
                try { await Task.Delay(backoff, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }

                backoff += backoff;
            }
            catch (Exception ex)
            {
                TADNLog.Log($"UpdateCheckService.DownloadAssetWithRetryAsync: final attempt failed: {ex.Message}");
                TryDeleteFile(destination);
                return false;
            }
        }

        return false;
    }

    private static bool IsTerminalHttpStatus(HttpStatusCode status)
    {
        int code = (int)status;
        return code == HttpUnauthorized || code == HttpForbidden || code == HttpNotFound;
    }

    private static bool IsTerminalHttpRequestException(HttpRequestException ex) =>
        ex.StatusCode is { } statusCode && IsTerminalHttpStatus(statusCode);

    private static string BuildUpdateScript(
        int pid,
        string sourceDirectory,
        string downloadedZip,
        string targetDirectory,
        string currentExe)
    {
        StringBuilder sb = new();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine($"set TARGETPID={pid}");
        sb.AppendLine($"set \"SOURCE={sourceDirectory}\"");
        sb.AppendLine($"set \"ZIP={downloadedZip}\"");
        sb.AppendLine($"set \"TARGET={targetDirectory}\"");
        sb.AppendLine($"set \"EXE={currentExe}\"");
        sb.AppendLine(":waitloop");
        sb.AppendLine("tasklist /FI \"PID eq %TARGETPID%\" 2>NUL | find \"%TARGETPID%\" >NUL");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >NUL");
        sb.AppendLine("  goto waitloop");
        sb.AppendLine(")");
        sb.AppendLine("timeout /t 1 /nobreak >NUL");
        sb.AppendLine("robocopy \"%SOURCE%\" \"%TARGET%\" /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS >NUL");
        sb.AppendLine("set COPYRC=%ERRORLEVEL%");
        sb.AppendLine("if %COPYRC% GEQ 8 goto cleanup");
        sb.AppendLine("start \"\" \"%EXE%\"");
        sb.AppendLine(":cleanup");
        sb.AppendLine("rmdir /S /Q \"%SOURCE%\" 2>NUL");
        sb.AppendLine("del \"%ZIP%\" 2>NUL");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
        return sb.ToString();
    }

    private static void ExtractZip(string zipPath, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
            Directory.Delete(destinationDirectory, recursive: true);
        Directory.CreateDirectory(destinationDirectory);

        string root = Path.GetFullPath(destinationDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Update package entry escapes staging directory: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken token)
    {
        await using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(fs, token).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { TADNLog.Log($"UpdateCheckService.TryDeleteFile: {ex.Message}"); }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) { TADNLog.Log($"UpdateCheckService.TryDeleteDirectory: {ex.Message}"); }
    }

    private static string? SafeFileNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(value.Length);
        foreach (char c in value) sb.Append(invalid.Contains(c) ? '_' : c);
        string cleaned = sb.ToString().Trim('.', ' ');
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static int ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : 0;

    private static long ParsePositiveLong(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && parsed > 0
            ? parsed
            : 0;

    private static UpdateCheckOptions ValidateOptions(UpdateCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.VersionsManifestUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserAgent);
        ArgumentNullException.ThrowIfNull(options.StagingDirectory);
        ArgumentNullException.ThrowIfNull(options.IsEnabled);
        ArgumentNullException.ThrowIfNull(options.PollInterval);
        ArgumentNullException.ThrowIfNull(options.InvokeOnUIThread);

        if (options.CurrentBuild < 0)
            throw new ArgumentOutOfRangeException(nameof(options.CurrentBuild));
        if (options.AssetDownloadMaxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.AssetDownloadMaxAttempts));
        if (options.NetworkTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.NetworkTimeout));
        if (options.FailureRetryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.FailureRetryInterval));
        if (options.StartupDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.StartupDelay));
        if (options.MinPollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MinPollInterval));
        if (options.MaxPollInterval < options.MinPollInterval)
            throw new ArgumentOutOfRangeException(nameof(options.MaxPollInterval));
        if (options.AssetDownloadInitialBackoff <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.AssetDownloadInitialBackoff));

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _http.Dispose();
    }
}

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    Cancelled
}

public sealed class UpdateCheckOptions
{
    public required Uri VersionsManifestUrl { get; init; }

    /// <summary>Path shared by sibling apps for the latest aggregate release manifest.</summary>
    public required string VersionsManifestCachePath { get; init; }
    public required string RepositoryOwner { get; init; }
    public required string RepositoryName { get; init; }
    public required string ApplicationName { get; init; }
    public required int CurrentBuild { get; init; }
    public required string UserAgent { get; init; }
    public required Func<string> StagingDirectory { get; init; }
    public required Func<bool> IsEnabled { get; init; }
    public required Func<TimeSpan> PollInterval { get; init; }
    public required Func<int> GetSkippedUpdateVersion { get; init; }
    public required Action<int> PersistSkippedUpdateVersion { get; init; }
    public required Func<Action, Task> InvokeOnUIThread { get; init; }

    public string? StagingFilePrefix { get; init; }

    public Func<string?> CurrentExecutablePath { get; init; } = ResolveCurrentExecutablePath;

    public TimeSpan StartupDelay { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckStartupDelayMs);
    public TimeSpan MinPollInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckIntervalMinMs);
    public TimeSpan MaxPollInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckIntervalMaxMs);
    public TimeSpan NetworkTimeout { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateNetworkTimeoutMs);
    public TimeSpan ManualCheckTimeout { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateManualCheckTimeoutMs);
    public TimeSpan FailureRetryInterval { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateCheckFailureRetryMs);
    public int AssetDownloadMaxAttempts { get; init; } = TimeConstants.UpdateAssetDownloadMaxAttempts;
    public TimeSpan AssetDownloadInitialBackoff { get; init; } =
        TimeSpan.FromMilliseconds(TimeConstants.UpdateAssetDownloadInitialBackoffMs);

    internal Func<DateTime> GetCurrentUTCTime { get; init; } = static () => DateTime.UtcNow;

    private static string? ResolveCurrentExecutablePath()
    {
        using Process currentProcess = Process.GetCurrentProcess();
        return currentProcess.MainModule?.FileName ?? Environment.ProcessPath;
    }
}

public sealed class UpdateCheckService : IDisposable
{
    private const int HttpUnauthorized = 401;
    private const int HttpForbidden = 403;
    private const int HttpNotFound = 404;
    private const int PollStateIdle = 0;
    private const int PollStateScheduled = 1;
    private const int PollStateManual = 2;
    private const int UpdateScriptTargetWaitSeconds = 60;
    private const int UpdateScriptTerminateAttempts = 20;
    private const int UpdateScriptStepDelaySeconds = 1;
    private const int UpdateScriptTerminateDelaySeconds = 1;
    private const int GitHubFallbackReleasesPerPage = 10;
    private const int GitHubFallbackReleaseMaxPages = 10;
    private const string GitHubApiVersion = "2026-03-10";
    private const string Sha256DigestPrefix = "sha256:";

    private static readonly string[] SharedNativeDLLFileNames =
    [
        "av_libglesv2.dll",
        "libHarfBuzzSharp.dll",
        "libSkiaSharp.dll",
        "libMonoPosixHelper.dll",
        "MonoPosixHelper.dll"
    ];

    private sealed record UpdateCopyPlan(IReadOnlyList<string> RelativeFilePaths, bool StopSiblingApps);

    private readonly UpdateCheckOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _previousReleaseSemaphore = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TaskCompletionSource? _manualKick;
    private int _pollState;
    private TaskCompletionSource? _pollDone;
    private UpdateInfo? _available;
    private DateTime? _lastCheckTimeUtc;
    private bool _isChecking;
    private UpdateCheckResult? _lastResult;
    private UpdateInfo? _latestRelease;
    private UpdateInfo? _previousRelease;
    private bool _isPreviousReleaseResolved;
    private long _cachedManifestWriteTimeUTCTicks;
    private bool _disposed;

    public UpdateCheckService(UpdateCheckOptions options)
        : this(options, new HttpClientHandler())
    {
    }

    internal UpdateCheckService(UpdateCheckOptions options, HttpMessageHandler httpMessageHandler)
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(httpMessageHandler);

        _http = new HttpClient(httpMessageHandler, disposeHandler: true) { Timeout = _options.NetworkTimeout };
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

    public int CurrentBuild => _options.CurrentBuild;

    public int SkippedUpdateVersion
    {
        get
        {
            int skippedUpdateVersion = _options.GetSkippedUpdateVersion();
            return skippedUpdateVersion > _options.CurrentBuild ? skippedUpdateVersion : 0;
        }
    }

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

        while (true)
        {
            int activePollState = Interlocked.CompareExchange(
                ref _pollState,
                PollStateManual,
                PollStateIdle);
            if (activePollState == PollStateIdle) break;

            TaskCompletionSource? running = Volatile.Read(ref _pollDone);
            Volatile.Read(ref _manualKick)?.TrySetResult();
            if (running == null)
            {
                await Task.Yield();
                continue;
            }

            if (!await WaitForRunningPollAsync(running.Task).ConfigureAwait(false))
            {
                await MarkCheckFailedAsync(
                    "UpdateCheckService.CheckNowAsync: timed out waiting for in-flight update check.")
                    .ConfigureAwait(false);
                return _available;
            }

            // A concurrent manual request already performed the required uncached check
            if (activePollState == PollStateManual) return _available;
        }

        Volatile.Read(ref _manualKick)?.TrySetResult();

        TaskCompletionSource pollDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pollDone, pollDone);
        try
        {
            using CancellationTokenSource timeoutCts = new(_options.ManualCheckTimeout);
            await PollOnceAsync(timeoutCts.Token, bypassLatestManifestCache: true).ConfigureAwait(false);
            if (timeoutCts.IsCancellationRequested)
            {
                await MarkCheckFailedAsync(
                    "UpdateCheckService.CheckNowAsync: manual update check timed out.")
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _pollDone, null);
            pollDone.TrySetResult();
            Interlocked.Exchange(ref _pollState, PollStateIdle);
        }

        return _available;
    }

    /// <summary>Persists an ignored release and removes it from the available update state.</summary>
    public async Task SkipReleaseAsync(UpdateInfo info)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(info);
        if (info.Version <= _options.CurrentBuild)
            throw new ArgumentOutOfRangeException(nameof(info), "Only newer releases can be skipped.");

        await InvokeIfRunningAsync(() =>
        {
            _options.PersistSkippedUpdateVersion(info.Version);
            if (_available?.Version == info.Version)
                _available = null;
            StateChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    /// <summary>Persists whether the running build should be offered after a backdate.</summary>
    public async Task SetCurrentVersionSkippedAsync(bool isSkipped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await InvokeIfRunningAsync(() =>
        {
            int persistedVersion = _options.GetSkippedUpdateVersion();
            if (isSkipped)
                _options.PersistSkippedUpdateVersion(_options.CurrentBuild);
            else if (persistedVersion == _options.CurrentBuild)
                _options.PersistSkippedUpdateVersion(0);
            StateChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    /// <summary>Returns the newest published app package older than the running build.</summary>
    public async Task<UpdateInfo?> GetPreviousReleaseAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _previousReleaseSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isPreviousReleaseResolved) return _previousRelease;

            using CancellationTokenSource timeoutCts = new(_options.ManualCheckTimeout);
            UpdateInfo? previousRelease = await FetchPreviousReleaseAsync(timeoutCts.Token).ConfigureAwait(false);
            _previousRelease = previousRelease;
            _isPreviousReleaseResolved = true;
            return previousRelease;
        }
        finally
        {
            _previousReleaseSemaphore.Release();
        }
    }

    public async Task<bool> DownloadAndStageAsync(UpdateInfo info, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(info);

        await SetCheckingAsync(true).ConfigureAwait(false);
        string? zipPath = null;
        string? extractDirectory = null;
        string? scriptPath = null;
        string? scriptLogPath = null;
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
            scriptLogPath = Path.Combine(stagingDirectory, updateId + ".log");

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

            UpdateCopyPlan copyPlan = await BuildUpdateCopyPlanAsync(
                    extractDirectory,
                    targetDirectory,
                    token)
                .ConfigureAwait(false);
            string scriptContents = BuildUpdateScript(
                Environment.ProcessId,
                extractDirectory,
                zipPath,
                targetDirectory,
                currentExe,
                scriptLogPath,
                copyPlan);
            await File.WriteAllTextAsync(scriptPath, scriptContents, Encoding.ASCII, token)
                .ConfigureAwait(false);

            ProcessStartInfo psi = new()
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = $"/d /c call \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = stagingDirectory
            };
            using Process? cmd = Process.Start(psi);
            launched = cmd != null;
            if (launched)
            {
                TADNLog.Log(
                    $"UpdateCheckService.DownloadAndStageAsync: launched update script {scriptPath}; "
                    + $"installer log {scriptLogPath}");
                TADNLog.Flush();
            }

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

    private async Task<bool> WaitForRunningPollAsync(Task runningTask)
    {
        Task timeoutTask = Task.Delay(_options.ManualCheckTimeout);
        Task completed = await Task.WhenAny(runningTask, timeoutTask).ConfigureAwait(false);
        if (completed != runningTask) return false;

        await runningTask.ConfigureAwait(false);
        return true;
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
                if (Interlocked.CompareExchange(
                        ref _pollState,
                        PollStateScheduled,
                        PollStateIdle) == PollStateIdle)
                {
                    TaskCompletionSource pollDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref _pollDone, pollDone);
                    try
                    {
                        await PollOnceAsync(token, bypassLatestManifestCache: false).ConfigureAwait(false);
                    }
                    finally
                    {
                        Volatile.Write(ref _pollDone, null);
                        pollDone.TrySetResult();
                        Interlocked.Exchange(ref _pollState, PollStateIdle);
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

    internal TimeSpan NextPollInterval()
    {
        TimeSpan normal = NormalizedInterval(_options.PollInterval());
        long cacheWriteTimeUTCTicks = Interlocked.Exchange(ref _cachedManifestWriteTimeUTCTicks, 0);
        if (_lastResult != UpdateCheckResult.Failed)
        {
            if (cacheWriteTimeUTCTicks <= 0) return normal;

            DateTime cacheWriteTimeUTC = new(cacheWriteTimeUTCTicks, DateTimeKind.Utc);
            TimeSpan cacheAge = _options.GetCurrentUTCTime() - cacheWriteTimeUTC;
            if (cacheAge <= TimeSpan.Zero) return normal;

            // Keep the writer's original cadence instead of extending the cache lifetime per reader
            return cacheAge >= normal ? TimeSpan.Zero : normal - cacheAge;
        }

        TimeSpan retry = NormalizedInterval(_options.FailureRetryInterval);
        return retry < normal ? retry : normal;
    }

    private TimeSpan NormalizedInterval(TimeSpan requested)
    {
        if (requested < _options.MinPollInterval) return _options.MinPollInterval;
        return requested > _options.MaxPollInterval ? _options.MaxPollInterval : requested;
    }

    internal async Task PollOnceAsync(CancellationToken token, bool bypassLatestManifestCache)
    {
        Interlocked.Exchange(ref _cachedManifestWriteTimeUTCTicks, 0);
        await SetCheckingAsync(true).ConfigureAwait(false);
        UpdateCheckResult result = UpdateCheckResult.Failed;
        try
        {
            UpdateInfo? info = await FetchLatestAsync(token, bypassLatestManifestCache).ConfigureAwait(false);
            UpdateInfo? newer = info != null && info.Version > _options.CurrentBuild ? info : null;
            await InvokeIfRunningAsync(() =>
            {
                int skippedUpdateVersion = _options.GetSkippedUpdateVersion();
                _latestRelease = info;
                _available = newer?.Version == skippedUpdateVersion ? null : newer;
                _lastCheckTimeUtc = _options.GetCurrentUTCTime();
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
                _lastCheckTimeUtc = _options.GetCurrentUTCTime();
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

    private async Task MarkCheckFailedAsync(string message)
    {
        TADNLog.Log(message);
        await InvokeIfRunningAsync(() =>
        {
            _lastCheckTimeUtc = _options.GetCurrentUTCTime();
            _lastResult = UpdateCheckResult.Failed;
            StateChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    private async Task InvokeIfRunningAsync(Action action)
    {
        try { await _options.InvokeOnUIThread(action).ConfigureAwait(false); }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
    }

    private async Task<UpdateInfo?> FetchLatestAsync(
        CancellationToken token,
        bool bypassLatestManifestCache = false)
    {
        if (!bypassLatestManifestCache)
        {
            TimeSpan pollInterval = NormalizedInterval(_options.PollInterval());
            UpdateInfo? cachedRelease = await TryReadCachedLatestManifestAsync(pollInterval, token)
                .ConfigureAwait(false);
            if (cachedRelease != null) return cachedRelease;
        }

        try
        {
            UpdateInfo? manifestRelease = await FetchReleaseManifestAsync(
                    _options.VersionsManifestUrl,
                    pinnedTagName: null,
                    returnNullWhenMissing: false,
                    cacheLatestManifest: true,
                    token)
                .ConfigureAwait(false);
            if (manifestRelease != null) return manifestRelease;

            TADNLog.Log(
                $"UpdateCheckService.FetchLatestAsync: latest aggregate manifest did not contain "
                + $"a release artifact for {_options.ApplicationName}; using release-list fallback.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"UpdateCheckService.FetchLatestAsync: latest aggregate manifest lookup failed: "
                + $"{exception.Message}; using release-list fallback.");
        }

        return await FetchLatestReleaseFallbackAsync(token).ConfigureAwait(false);
    }

    private async Task<UpdateInfo?> TryReadCachedLatestManifestAsync(
        TimeSpan pollInterval,
        CancellationToken token)
    {
        string cachePath = _options.VersionsManifestCachePath;
        try
        {
            if (!File.Exists(cachePath)) return null;

            DateTime cacheWriteTimeUTC = File.GetLastWriteTimeUtc(cachePath);
            TimeSpan cacheAge = _options.GetCurrentUTCTime() - cacheWriteTimeUTC;
            if (cacheAge < TimeSpan.Zero || cacheAge >= pollInterval) return null;

            byte[] manifestBytes = await File.ReadAllBytesAsync(cachePath, token).ConfigureAwait(false);
            VersionsManifest manifest = ReadVersionsManifest(manifestBytes);
            UpdateInfo? cachedRelease = CreateUpdateInfoFromManifest(manifest, pinnedTagName: null);
            if (cachedRelease == null)
            {
                TADNLog.Log(
                    $"UpdateCheckService.TryReadCachedLatestManifestAsync: cached aggregate manifest did not "
                    + $"contain a release artifact for {_options.ApplicationName}; refreshing it.");
                return null;
            }

            Interlocked.Exchange(ref _cachedManifestWriteTimeUTCTicks, cacheWriteTimeUTC.Ticks);
            return cachedRelease;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"UpdateCheckService.TryReadCachedLatestManifestAsync: could not use {cachePath}: "
                + exception.Message);
            return null;
        }
    }

    private async Task<UpdateInfo?> FetchPreviousReleaseAsync(CancellationToken token)
    {
        if (_options.CurrentBuild <= 1) return null;

        int expectedPreviousVersion = _options.CurrentBuild - 1;
        UpdateInfo? latestRelease = _latestRelease;
        if (latestRelease == null)
        {
            try
            {
                latestRelease = await FetchLatestAsync(token).ConfigureAwait(false);
                _latestRelease = latestRelease;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                TADNLog.Log(
                    $"UpdateCheckService.FetchPreviousReleaseAsync: latest release lookup failed: "
                    + $"{exception.Message}; using release-list fallback.");
            }
        }

        if (latestRelease?.Version == expectedPreviousVersion)
            return latestRelease;

        int releaseIndexDistance = latestRelease?.Version - expectedPreviousVersion ?? 0;
        if (latestRelease != null
            && releaseIndexDistance > 0
            && TryGetPriorIndexedTag(latestRelease.TagName, releaseIndexDistance, out string previousTagName))
        {
            UpdateInfo? directPreviousRelease = await TryFetchTaggedReleaseAsync(previousTagName, token)
                .ConfigureAwait(false);
            if (directPreviousRelease?.Version == expectedPreviousVersion)
                return directPreviousRelease;

            string directVersion = directPreviousRelease?.Version.ToString(CultureInfo.InvariantCulture) ?? "none";
            TADNLog.Log(
                $"UpdateCheckService.FetchPreviousReleaseAsync: direct tag {previousTagName} returned app version "
                + $"{directVersion}, expected {expectedPreviousVersion}; using release-list fallback.");
        }

        return await FetchPreviousReleaseFallbackAsync(expectedPreviousVersion, token).ConfigureAwait(false);
    }

    private async Task<UpdateInfo?> TryFetchTaggedReleaseAsync(string tagName, CancellationToken token)
    {
        try
        {
            Uri manifestUrl = GitHubReleaseUrls.ReleaseAssetUrl(
                _options.RepositoryOwner,
                _options.RepositoryName,
                tagName,
                GitHubReleaseUrls.VersionsManifestFileName);
            return await FetchReleaseManifestAsync(
                    manifestUrl,
                    tagName,
                    returnNullWhenMissing: true,
                    cacheLatestManifest: false,
                    token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"UpdateCheckService.TryFetchTaggedReleaseAsync: {tagName} lookup failed: {exception.Message}");
            return null;
        }
    }

    private async Task<UpdateInfo?> FetchReleaseManifestAsync(
        Uri manifestUrl,
        string? pinnedTagName,
        bool returnNullWhenMissing,
        bool cacheLatestManifest,
        CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, manifestUrl);
        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
            .ConfigureAwait(false);
        if (returnNullWhenMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Manifest request to {manifestUrl} failed with HTTP "
                + $"{(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        byte[] manifestBytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        VersionsManifest manifest = ReadVersionsManifest(manifestBytes);
        if (cacheLatestManifest)
            await TryWriteLatestManifestCacheAsync(manifestBytes, token).ConfigureAwait(false);

        return CreateUpdateInfoFromManifest(manifest, pinnedTagName);
    }

    private static VersionsManifest ReadVersionsManifest(byte[] manifestBytes)
    {
        using MemoryStream stream = new(manifestBytes, writable: false);
        return TrayXmlSerializer.Read<VersionsManifest>(stream);
    }

    private UpdateInfo? CreateUpdateInfoFromManifest(VersionsManifest manifest, string? pinnedTagName)
    {
        string tagName = string.IsNullOrWhiteSpace(pinnedTagName) ? manifest.Release.Tag : pinnedTagName;
        if (string.IsNullOrWhiteSpace(tagName)) return null;
        if (!string.IsNullOrWhiteSpace(pinnedTagName)
            && !string.IsNullOrWhiteSpace(manifest.Release.Tag)
            && !string.Equals(pinnedTagName, manifest.Release.Tag, StringComparison.OrdinalIgnoreCase))
        {
            TADNLog.Log(
                $"UpdateCheckService.FetchReleaseManifestAsync: manifest tag {manifest.Release.Tag} did not match "
                + $"requested tag {pinnedTagName}.");
            return null;
        }

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
                $"UpdateCheckService.FetchReleaseManifestAsync: manifest asset {manifestAssetName} did not match "
                + $"expected asset {expectedAssetName}.");
            return null;
        }

        Uri assetUrl = GitHubReleaseUrls.ReleaseAssetUrl(
            _options.RepositoryOwner,
            _options.RepositoryName,
            tagName,
            expectedAssetName);
        string releaseName = $"{_options.ApplicationName} {version}";
        return new UpdateInfo(
            version,
            tagName,
            releaseName,
            "",
            assetUrl.ToString(),
            expectedAssetName,
            appArtifact.Sha256,
            ParsePositiveLong(appArtifact.Size));
    }

    private async Task TryWriteLatestManifestCacheAsync(byte[] manifestBytes, CancellationToken token)
    {
        string cachePath = _options.VersionsManifestCachePath;
        string? cacheDirectory = Path.GetDirectoryName(cachePath);
        string? temporaryPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
                throw new InvalidOperationException($"Cache path has no parent directory: {cachePath}");

            Directory.CreateDirectory(cacheDirectory);
            temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, manifestBytes, token).ConfigureAwait(false);

            // Publish only a complete XML file because sibling apps can read it concurrently
            File.Move(temporaryPath, cachePath, overwrite: true);
            temporaryPath = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"UpdateCheckService.TryWriteLatestManifestCacheAsync: could not write {cachePath}: "
                + exception.Message);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private async Task<UpdateInfo?> FetchLatestReleaseFallbackAsync(CancellationToken token)
    {
        for (int page = 1; page <= GitHubFallbackReleaseMaxPages; page++)
        {
            (UpdateInfo? latestRelease, int releaseCount) = await FetchAppReleasePageAsync(
                    page,
                    maximumVersionExclusive: null,
                    preferredVersion: null,
                    token)
                .ConfigureAwait(false);
            if (latestRelease != null) return latestRelease;
            if (releaseCount < GitHubFallbackReleasesPerPage) return null;
        }

        return null;
    }

    private async Task<UpdateInfo?> FetchPreviousReleaseFallbackAsync(
        int expectedPreviousVersion,
        CancellationToken token)
    {
        for (int page = 1; page <= GitHubFallbackReleaseMaxPages; page++)
        {
            (UpdateInfo? previousRelease, int releaseCount) = await FetchAppReleasePageAsync(
                    page,
                    maximumVersionExclusive: _options.CurrentBuild,
                    preferredVersion: expectedPreviousVersion,
                    token)
                .ConfigureAwait(false);
            if (previousRelease != null) return previousRelease;
            if (releaseCount < GitHubFallbackReleasesPerPage) return null;
        }

        return null;
    }

    private async Task<(UpdateInfo? Release, int ReleaseCount)> FetchAppReleasePageAsync(
        int page,
        int? maximumVersionExclusive,
        int? preferredVersion,
        CancellationToken token)
    {
        Uri requestUrl = GitHubReleaseUrls.ReleasesApiUrl(
            _options.RepositoryOwner,
            _options.RepositoryName,
            page,
            GitHubFallbackReleasesPerPage);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub releases request to {requestUrl} failed with HTTP "
                + $"{(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token)
            .ConfigureAwait(false);
        JsonElement releases = document.RootElement;
        if (releases.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub releases response was not a JSON array.");

        UpdateInfo? bestRelease = null;
        foreach (JsonElement release in releases.EnumerateArray())
        {
            if (GetJSONBoolean(release, "draft") || GetJSONBoolean(release, "prerelease")) continue;

            string tagName = GetJSONString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName)) continue;
            if (!release.TryGetProperty("assets", out JsonElement assets)
                || assets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string assetName = GetJSONString(asset, "name");
                int version = ParseAppReleaseAssetVersion(assetName);
                if (version <= 0) continue;
                if (maximumVersionExclusive.HasValue && version >= maximumVersionExclusive.Value) continue;
                if (bestRelease != null && version <= bestRelease.Version) continue;

                Uri assetUrl = GitHubReleaseUrls.ReleaseAssetUrl(
                    _options.RepositoryOwner,
                    _options.RepositoryName,
                    tagName,
                    assetName);
                bestRelease = new UpdateInfo(
                    version,
                    tagName,
                    $"{_options.ApplicationName} {version}",
                    GetJSONString(release, "body"),
                    assetUrl.ToString(),
                    assetName,
                    ParseSHA256Digest(GetJSONString(asset, "digest")),
                    GetJSONPositiveInt64(asset, "size"));
                if (preferredVersion.HasValue && version == preferredVersion.Value)
                    return (bestRelease, releases.GetArrayLength());
            }
        }

        return (bestRelease, releases.GetArrayLength());
    }

    private int ParseAppReleaseAssetVersion(string assetName)
    {
        string prefix = _options.ApplicationName + "_";
        const string suffix = ".zip";
        if (!assetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return 0;

        int versionLength = assetName.Length - prefix.Length - suffix.Length;
        if (versionLength <= 0) return 0;
        return int.TryParse(
            assetName.AsSpan(prefix.Length, versionLength),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int version)
            ? version
            : 0;
    }

    private static bool TryGetPriorIndexedTag(string tagName, int releaseIndexDistance, out string previousTagName)
    {
        previousTagName = string.Empty;
        if (releaseIndexDistance <= 0) return false;

        ReadOnlySpan<char> tag = tagName.AsSpan().Trim();
        int versionStart = tag.Length;
        while (versionStart > 0 && char.IsDigit(tag[versionStart - 1])) versionStart--;
        if (versionStart == tag.Length) return false;
        if (!int.TryParse(
                tag[versionStart..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int releaseIndex)
            || releaseIndex <= releaseIndexDistance)
            return false;

        previousTagName = string.Concat(
            tag[..versionStart],
            (releaseIndex - releaseIndexDistance).ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static string GetJSONString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetJSONBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    private static long GetJSONPositiveInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.TryGetInt64(out long value)
        && value > 0
            ? value
            : 0;

    private static string ParseSHA256Digest(string digest)
    {
        if (!digest.StartsWith(Sha256DigestPrefix, StringComparison.OrdinalIgnoreCase)) return string.Empty;

        string sha256 = digest[Sha256DigestPrefix.Length..];
        if (sha256.Length != 64) return string.Empty;
        foreach (char character in sha256)
        {
            if (!Uri.IsHexDigit(character)) return string.Empty;
        }

        return sha256;
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
        return code is HttpUnauthorized or HttpForbidden or HttpNotFound;
    }

    private static bool IsTerminalHttpRequestException(HttpRequestException ex) =>
        ex.StatusCode is { } statusCode && IsTerminalHttpStatus(statusCode);

    /// <summary>Builds the exact file list the updater should copy.</summary>
    private static async Task<UpdateCopyPlan> BuildUpdateCopyPlanAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken token)
    {
        string normalizedSourceDirectory = Path.GetFullPath(sourceDirectory);
        List<string> relativeFilePaths = [];
        bool stopSiblingApps = false;

        string[] sourceFiles = [.. Directory.EnumerateFiles(
            normalizedSourceDirectory,
            "*",
            SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)];
        foreach (string sourceFile in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(normalizedSourceDirectory, sourceFile);
            if (!IsSharedNativeDLLRootFile(relativePath))
            {
                relativeFilePaths.Add(relativePath);
                continue;
            }

            SharedNativeDLLCopyDecision decision = await SharedNativeDLLCopyDecisionAsync(
                    sourceFile,
                    Path.Combine(targetDirectory, relativePath),
                    Path.GetFileName(relativePath),
                    token)
                .ConfigureAwait(false);
            if (decision == SharedNativeDLLCopyDecision.Skip) continue;

            relativeFilePaths.Add(relativePath);
            if (decision == SharedNativeDLLCopyDecision.CopyAndStopSiblings)
                stopSiblingApps = true;
        }

        return new UpdateCopyPlan(relativeFilePaths, stopSiblingApps);
    }

    /// <summary>Determines how a shared native sidecar should be handled by the update copy plan.</summary>
    private static async Task<SharedNativeDLLCopyDecision> SharedNativeDLLCopyDecisionAsync(
        string sourceFile,
        string targetFile,
        string fileName,
        CancellationToken token)
    {
        if (!File.Exists(targetFile))
        {
            TADNLog.Log(
                $"UpdateCheckService.SharedNativeDLLCopyDecisionAsync: {fileName} is missing; "
                + "sibling apps must stop.");
            return SharedNativeDLLCopyDecision.CopyAndStopSiblings;
        }

        try
        {
            FileInfo sourceInfo = new(sourceFile);
            FileInfo targetInfo = new(targetFile);
            if (sourceInfo.Length != targetInfo.Length)
            {
                TADNLog.Log(
                    $"UpdateCheckService.SharedNativeDLLCopyDecisionAsync: {fileName} size differs; "
                    + "sibling apps must stop.");
                return SharedNativeDLLCopyDecision.CopyAndStopSiblings;
            }

            string sourceHash = await Sha256FileAsync(sourceFile, token).ConfigureAwait(false);
            string targetHash = await Sha256FileAsync(targetFile, token).ConfigureAwait(false);
            if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
            {
                TADNLog.Log(
                    $"UpdateCheckService.SharedNativeDLLCopyDecisionAsync: {fileName} hash differs; "
                    + "sibling apps must stop.");
                return SharedNativeDLLCopyDecision.CopyAndStopSiblings;
            }

            TADNLog.Log($"UpdateCheckService.SharedNativeDLLCopyDecisionAsync: {fileName} is unchanged; skipping.");
            return SharedNativeDLLCopyDecision.Skip;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"UpdateCheckService.SharedNativeDLLCopyDecisionAsync: {fileName} comparison failed: "
                + $"{ex.Message}; sibling apps must stop.");
            return SharedNativeDLLCopyDecision.CopyAndStopSiblings;
        }
    }

    private enum SharedNativeDLLCopyDecision
    {
        Skip,
        CopyAndStopSiblings
    }

    /// <summary>Returns true for root-level native sidecars shared by every Native AOT tray app.</summary>
    private static bool IsSharedNativeDLLRootFile(string relativePath) =>
        string.IsNullOrEmpty(Path.GetDirectoryName(relativePath))
        && SharedNativeDLLFileNames.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the detached batch updater that replaces the installed payload and restarts apps.</summary>
    private static string BuildUpdateScript(
        int pid,
        string sourceDirectory,
        string downloadedZip,
        string targetDirectory,
        string currentExe,
        string logPath,
        UpdateCopyPlan copyPlan)
    {
        string targetPID = pid.ToString(CultureInfo.InvariantCulture);
        string sourceBAT = EscapeBATValue(sourceDirectory);
        string zipBAT = EscapeBATValue(downloadedZip);
        string targetBAT = EscapeBATValue(targetDirectory);
        string exeBAT = EscapeBATValue(currentExe);
        string logBAT = EscapeBATValue(logPath);
        string stopSiblingAppsBAT = copyPlan.StopSiblingApps ? "1" : "0";
        string plannedFileCount = copyPlan.RelativeFilePaths.Count.ToString(CultureInfo.InvariantCulture);
        string copyCommands = BuildBATCopyCommands(copyPlan.RelativeFilePaths);
        string targetWaitSeconds = UpdateScriptTargetWaitSeconds.ToString(CultureInfo.InvariantCulture);
        string terminateAttempts = UpdateScriptTerminateAttempts.ToString(CultureInfo.InvariantCulture);
        string stepDelayCommand = BATDelayCommand(UpdateScriptStepDelaySeconds);
        string terminateDelayCommand = BATDelayCommand(UpdateScriptTerminateDelaySeconds);

        return $"""
        @echo off
        setlocal EnableExtensions
        set TARGETPID={targetPID}
        set "SOURCE={sourceBAT}"
        set "ZIP={zipBAT}"
        set "TARGET={targetBAT}"
        set "EXE={exeBAT}"
        set "LOG={logBAT}"
        set STOP_SIBLING_APPS={stopSiblingAppsBAT}
        set "RESTARTLIST=%~dpn0.restart"
        set "RUNNINGFLAG=%~dpn0.running"
        set COPYRC=1
        set PROCESSRC=1
        if exist "%RESTARTLIST%" del /f /q "%RESTARTLIST%" >NUL 2>&1
        if exist "%RUNNINGFLAG%" del /f /q "%RUNNINGFLAG%" >NUL 2>&1
        > "%RESTARTLIST%" echo %EXE%
        >> "%LOG%" echo [%DATE% %TIME%] Update installer started.
        call :wait_for_target_exit
        if "%STOP_SIBLING_APPS%"=="1" (
          call :record_running_apps
          call :terminate_running_apps
        ) else (
          >> "%LOG%" echo [%DATE% %TIME%] Shared native DLLs are unchanged; sibling apps can keep running.
        )
        call :copy_update_payload
        set COPYRC=%ERRORLEVEL%
        if %COPYRC% GEQ 8 (
          set PROCESSRC=%COPYRC%
          >> "%LOG%" echo [%DATE% %TIME%] Copy failed; restarting recorded apps from the existing install root.
        ) else (
          set PROCESSRC=0
          >> "%LOG%" echo [%DATE% %TIME%] Copy succeeded; restarting recorded apps.
        )
        call :restart_recorded_apps
        goto cleanup

        :wait_for_target_exit
        >> "%LOG%" echo [%DATE% %TIME%] Waiting for PID %TARGETPID%.
        set WAITCOUNT=0
        :waitloop
        tasklist /FI "PID eq %TARGETPID%" /FO CSV /NH 2>NUL | find "%TARGETPID%" >NUL
        if errorlevel 1 goto afterwait
        set /a WAITCOUNT+=1 >NUL
        if %WAITCOUNT% GEQ {targetWaitSeconds} goto force_target_exit
        {stepDelayCommand}
        goto waitloop
        :force_target_exit
        >> "%LOG%" echo [%DATE% %TIME%] PID %TARGETPID% still running after wait limit.
        >> "%LOG%" echo [%DATE% %TIME%] Terminating PID %TARGETPID%.
        taskkill /PID %TARGETPID% /T /F >> "%LOG%" 2>&1
        {stepDelayCommand}
        :afterwait
        exit /b 0

        :record_running_apps
        >> "%LOG%" echo [%DATE% %TIME%] Recording running sibling TrayAppDotNET apps from "%TARGET%".
        for %%F in ("%TARGET%\*TrayAppDotNET.exe") do (
          if exist "%%~fF" (
            if /I not "%%~fF"=="%EXE%" (
              tasklist /FI "IMAGENAME eq %%~nxF" /FO CSV /NH 2>NUL | find /I "%%~nxF" >NUL
              if not errorlevel 1 (
                >> "%LOG%" echo [%DATE% %TIME%] Recording "%%~fF" for restart.
                >> "%RESTARTLIST%" echo %%~fF
              )
            )
          )
        )
        exit /b 0

        :terminate_running_apps
        >> "%LOG%" echo [%DATE% %TIME%] Terminating known TrayAppDotNET app names from "%TARGET%".
        for /L %%I in (1,1,{terminateAttempts}) do (
          if exist "%RUNNINGFLAG%" del /f /q "%RUNNINGFLAG%" >NUL 2>&1
          call :terminate_running_once
          if not exist "%RUNNINGFLAG%" exit /b 0
          {terminateDelayCommand}
        )
        if exist "%RUNNINGFLAG%" (
          >> "%LOG%" echo [%DATE% %TIME%] WARNING: one or more TrayAppDotNET app names remained after termination attempts.
          del /f /q "%RUNNINGFLAG%" >NUL 2>&1
        )
        exit /b 0

        :terminate_running_once
        for %%F in ("%TARGET%\*TrayAppDotNET.exe") do (
          if exist "%%~fF" (
            tasklist /FI "IMAGENAME eq %%~nxF" /FO CSV /NH 2>NUL | find /I "%%~nxF" >NUL
            if not errorlevel 1 (
              > "%RUNNINGFLAG%" echo 1
              >> "%LOG%" echo [%DATE% %TIME%] Terminating "%%~nxF".
              taskkill /IM "%%~nxF" /T /F >> "%LOG%" 2>&1
            )
          )
        )
        exit /b 0

        :copy_update_payload
        >> "%LOG%" echo [%DATE% %TIME%] Copying {plannedFileCount} planned file(s) from "%SOURCE%" to "%TARGET%".
        {stepDelayCommand}
        set COPYFAILED=0
        {copyCommands}
        if "%COPYFAILED%"=="1" (
          >> "%LOG%" echo [%DATE% %TIME%] One or more file copies failed.
          exit /b 8
        )
        >> "%LOG%" echo [%DATE% %TIME%] Planned file copy completed.
        exit /b 0

        :copy_file
        set "REL=%~1"
        set "SRCFILE=%SOURCE%\%REL%"
        set "DSTFILE=%TARGET%\%REL%"
        for %%D in ("%DSTFILE%") do if not exist "%%~dpD" mkdir "%%~dpD" >> "%LOG%" 2>&1
        >> "%LOG%" echo [%DATE% %TIME%] Copying "%REL%".
        copy /Y "%SRCFILE%" "%DSTFILE%" >> "%LOG%" 2>&1
        set COPYRC=%ERRORLEVEL%
        if %COPYRC% GEQ 1 (
          >> "%LOG%" echo [%DATE% %TIME%] Failed to copy "%REL%" with exit code %COPYRC%.
          set COPYFAILED=1
        )
        exit /b %COPYRC%

        :restart_recorded_apps
        if not exist "%RESTARTLIST%" exit /b 0
        for /F "usebackq delims=" %%P in ("%RESTARTLIST%") do call :start_recorded_app "%%~fP"
        exit /b 0

        :start_recorded_app
        if not exist "%~1" (
          >> "%LOG%" echo [%DATE% %TIME%] Skipping missing executable "%~1".
          exit /b 0
        )
        set "APPDIR=%~dp1"
        >> "%LOG%" echo [%DATE% %TIME%] Starting "%~1".
        start "" /D "%APPDIR%" "%~1"
        >> "%LOG%" echo [%DATE% %TIME%] Start exit code %ERRORLEVEL%.
        exit /b 0

        :cleanup
        >> "%LOG%" echo [%DATE% %TIME%] Cleaning staging files.
        if exist "%RUNNINGFLAG%" del /f /q "%RUNNINGFLAG%" >NUL 2>&1
        if exist "%RESTARTLIST%" del /f /q "%RESTARTLIST%" >NUL 2>&1
        rmdir /S /Q "%SOURCE%" 2>NUL
        del "%ZIP%" 2>NUL
        (goto) 2>nul & del "%~f0" & exit /b %PROCESSRC%
        """;
    }

    /// <summary>Escapes a value embedded in a batch file variable assignment.</summary>
    private static string EscapeBATValue(string value) =>
        value.Replace("%", "%%", StringComparison.Ordinal);

    /// <summary>Builds one explicit batch copy call for each planned update file.</summary>
    private static string BuildBATCopyCommands(IReadOnlyList<string> relativeFilePaths)
    {
        if (relativeFilePaths.Count == 0)
            return "rem No files were selected for update copy.";

        StringBuilder sb = new();
        foreach (string relativePath in relativeFilePaths)
        {
            sb.Append("call :copy_file \"");
            sb.Append(EscapeBATValue(relativePath));
            sb.Append('"');
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Builds a batch-compatible delay command that does not read console input.</summary>
    private static string BATDelayCommand(int seconds)
    {
        int pingCount = Math.Max(2, seconds + 1);
        return $"ping -n {pingCount.ToString(CultureInfo.InvariantCulture)} 127.0.0.1 >NUL";
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
        ArgumentException.ThrowIfNullOrWhiteSpace(options.VersionsManifestCachePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserAgent);
        ArgumentNullException.ThrowIfNull(options.StagingDirectory);
        ArgumentNullException.ThrowIfNull(options.IsEnabled);
        ArgumentNullException.ThrowIfNull(options.PollInterval);
        ArgumentNullException.ThrowIfNull(options.GetSkippedUpdateVersion);
        ArgumentNullException.ThrowIfNull(options.PersistSkippedUpdateVersion);
        ArgumentNullException.ThrowIfNull(options.InvokeOnUIThread);
        ArgumentNullException.ThrowIfNull(options.GetCurrentUTCTime);

        if (options.CurrentBuild < 0)
            throw new ArgumentOutOfRangeException(nameof(options.CurrentBuild));
        if (options.AssetDownloadMaxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.AssetDownloadMaxAttempts));
        if (options.NetworkTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.NetworkTimeout));
        if (options.ManualCheckTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.ManualCheckTimeout));
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

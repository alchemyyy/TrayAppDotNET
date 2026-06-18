using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services;

public sealed record UpdateInfo(
    int Version,
    string TagName,
    string ReleaseName,
    string Changelog,
    string AssetUrl,
    long AssetSize);

public enum UpdateCheckResult
{
    Success,
    Failed,
    Cancelled,
}

public sealed class UpdateCheckOptions
{
    public required Uri ReleasesApiUrl { get; init; }
    public required string AssetName { get; init; }
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
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
        string? stagedExe = null;
        string? scriptPath = null;
        bool launched = false;
        try
        {
            string stagingDirectory = _options.StagingDirectory();
            if (string.IsNullOrWhiteSpace(stagingDirectory))
                throw new InvalidOperationException("Update staging directory cannot be empty.");

            Directory.CreateDirectory(stagingDirectory);
            string prefix = SafeFileNamePart(_options.StagingFilePrefix)
                            ?? SafeFileNamePart(Path.GetFileNameWithoutExtension(_options.AssetName))
                            ?? "trayapp";

            stagedExe = Path.Combine(
                stagingDirectory,
                $"{prefix}_update_{info.Version}_{Guid.NewGuid():N}.exe");
            scriptPath = Path.Combine(
                stagingDirectory,
                $"{prefix}_update_{info.Version}_{Guid.NewGuid():N}.bat");

            bool downloaded = await DownloadAssetWithRetryAsync(info.AssetUrl, stagedExe, token)
                .ConfigureAwait(false);
            if (!downloaded) return false;

            FileInfo onDisk = new(stagedExe);
            if (info.AssetSize > 0 && onDisk.Length != info.AssetSize)
            {
                TADNLog.Log(
                    $"UpdateCheckService.DownloadAndStageAsync: size mismatch "
                    + $"(got {onDisk.Length}, expected {info.AssetSize})");
                return false;
            }

            string currentExe = _options.CurrentExecutablePath()
                                ?? throw new InvalidOperationException("Could not resolve current executable path.");

            string scriptContents = BuildUpdateScript(Environment.ProcessId, stagedExe, currentExe);
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
                TryDeleteFile(stagedExe);
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

        int dashIndex = span.IndexOf('-');
        if (dashIndex >= 0) span = span[..dashIndex];
        int plusIndex = span.IndexOf('+');
        if (plusIndex >= 0) span = span[..plusIndex];

        int end = 0;
        while (end < span.Length && char.IsDigit(span[end])) end++;
        if (end == 0) return 0;
        return int.TryParse(span[..end], out int version) ? version : 0;
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

            TimeSpan interval = NormalizedInterval(_options.PollInterval());
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
        using HttpRequestMessage req = new(HttpMethod.Get, _options.ReleasesApiUrl);
        using HttpResponseMessage resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseContentRead, token)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        Stream stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: token)
            .ConfigureAwait(false);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("prerelease", out JsonElement preRel) && preRel.GetBoolean()) return null;
        if (root.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean()) return null;

        string tag = root.TryGetProperty("tag_name", out JsonElement tagEl) ? tagEl.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "" : "";
        string body = root.TryGetProperty("body", out JsonElement bodyEl) ? bodyEl.GetString() ?? "" : "";

        int version = ParseVersionFromTag(tag);
        if (version <= 0) return null;

        string? assetUrl = null;
        long assetSize = 0;
        if (root.TryGetProperty("assets", out JsonElement assets))
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out JsonElement an)
                    ? an.GetString() ?? ""
                    : "";
                if (!string.Equals(assetName, _options.AssetName, StringComparison.OrdinalIgnoreCase)) continue;

                assetUrl = asset.TryGetProperty("browser_download_url", out JsonElement url)
                    ? url.GetString()
                    : null;
                assetSize = asset.TryGetProperty("size", out JsonElement size)
                            && size.ValueKind == JsonValueKind.Number
                    ? size.GetInt64()
                    : 0;
                break;
            }
        }

        if (string.IsNullOrEmpty(assetUrl)) return null;

        string displayName = string.IsNullOrWhiteSpace(name) ? tag : name;
        return new UpdateInfo(version, tag, displayName, body, assetUrl, assetSize);
    }

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

    private static string BuildUpdateScript(int pid, string stagedExe, string currentExe)
    {
        StringBuilder sb = new();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine($"set TARGETPID={pid}");
        sb.AppendLine(":waitloop");
        sb.AppendLine("tasklist /FI \"PID eq %TARGETPID%\" 2>NUL | find \"%TARGETPID%\" >NUL");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >NUL");
        sb.AppendLine("  goto waitloop");
        sb.AppendLine(")");
        sb.AppendLine("timeout /t 1 /nobreak >NUL");
        sb.AppendLine($"move /Y \"{stagedExe}\" \"{currentExe}\" >NUL");
        sb.AppendLine("if errorlevel 1 goto cleanup");
        sb.AppendLine($"start \"\" \"{currentExe}\"");
        sb.AppendLine(":cleanup");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
        return sb.ToString();
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

    private static string? SafeFileNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(value.Length);
        foreach (char c in value) sb.Append(invalid.Contains(c) ? '_' : c);
        string cleaned = sb.ToString().Trim('.', ' ');
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static UpdateCheckOptions ValidateOptions(UpdateCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ReleasesApiUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AssetName);
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

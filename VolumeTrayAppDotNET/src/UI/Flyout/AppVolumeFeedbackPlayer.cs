using System.Collections.Concurrent;
using Avalonia.Threading;
using VolumeTrayAppDotNET.Audio;

namespace VolumeTrayAppDotNET.UI.Flyout;

internal sealed class AppVolumeFeedbackPlayer : IDisposable
{
    private const string AppFeedbackWavName = "Windows Background.wav";
    private const string DeviceDingThrottleKey = "device";
    private const string AppDingThrottleKey = "app";

    private readonly AsyncThrottler<string> _feedbackThrottler = new(0, StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _dingActiveUntilTicks = new(StringComparer.Ordinal);
    private readonly Dispatcher _uiDispatcher;
    private readonly AppSettings? _settings;
    private WAVTemplate? _wavTemplate;
    private System.Media.SoundPlayer? _currentAppSound;
    private bool _disposed;

    public AppVolumeFeedbackPlayer(Dispatcher uiDispatcher, AppSettings? settings)
    {
        _uiDispatcher = uiDispatcher;
        _settings = settings;
        EnsureAppFeedbackData();
    }

    public void PlayForDevice(AudioDevice device, bool immediate = false)
    {
        if (_settings?.PlayDeviceVolumeChangeSound != true) return;
        if (device.IsCaptureDevice) return;

        EnsureAppFeedbackData();
        WAVTemplate? wav = _wavTemplate;
        if (wav == null) return;

        string throttleKey = DeviceDingThrottleKey + ":" + device.Id;
        int dingWindowMs = wav.DurationMs + TimeConstants.VolumeFeedbackDingMeterBypassGraceMs;
        _ = _feedbackThrottler.RunAsync(throttleKey, async ctx =>
        {
            if (!immediate)
            {
                if (!await DwellWithReplacementBailAsync(
                        ctx,
                        TimeConstants.VolumeFeedbackDingDelayMs,
                        () => ShouldSuppressDeviceDing(device)).ConfigureAwait(false))
                    return;
            }
            else if (ctx.HasReplacement) return;

            if (ShouldSuppressDeviceDing(device)) return;
            if (!device.IsActive || string.IsNullOrEmpty(device.Id)) return;

            _dingActiveUntilTicks[device.Id] = Environment.TickCount64 + dingWindowMs;
            try { EndpointSoundPlayback.PlayAsync(device.Id, wav); }
            catch
            {
                /* feedback is best-effort */
            }
        });
    }

    public void PlayForApp(AudioAppGroup group, bool immediate = false)
    {
        if (_settings?.PlayAppVolumeChangeSound != true) return;

        EnsureAppFeedbackData();
        if (_wavTemplate == null) return;

        float scalarVolume = group.Volume;
        _ = _feedbackThrottler.RunAsync(AppDingThrottleKey, async ctx =>
        {
            if (!immediate)
            {
                if (!await DwellWithReplacementBailAsync(
                        ctx,
                        TimeConstants.VolumeFeedbackDingDelayMs,
                        () => ShouldSuppressAppDing(group)).ConfigureAwait(false))
                    return;
            }
            else if (ctx.HasReplacement) return;

            if (ShouldSuppressAppDing(group)) return;
            try { await _uiDispatcher.InvokeAsync(() => PlayAppFeedbackNow(scalarVolume)); }
            catch
            {
                /* dispatcher torn down */
            }
        });
    }

    private bool ShouldSuppressDeviceDing(AudioDevice device)
    {
        AppSettings? settings = _settings;
        if (settings == null || !settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying) return false;
        if (!device.IsCurrentEndpointPeakAbove(settings.DingSuppressionPeakThresholdPercent * 0.01f)) return false;

        return !_dingActiveUntilTicks.TryGetValue(device.Id, out long until)
               || Environment.TickCount64 >= until;
    }

    private bool ShouldSuppressAppDing(AudioAppGroup group)
    {
        AppSettings? settings = _settings;
        if (settings == null || !settings.SuppressDeviceVolumeChangeSoundWhenAudioPlaying) return false;
        return group.PeakValues.Max > settings.DingSuppressionPeakThresholdPercent * 0.01f;
    }

    private void PlayAppFeedbackNow(float scalarVolume)
    {
        WAVTemplate? template = _wavTemplate;
        if (template == null) return;

        try
        {
            byte[] scaled = template.CloneScaled(scalarVolume);
            MemoryStream stream = new(scaled, writable: false);
            System.Media.SoundPlayer player = new(stream);
            player.Play();

            _currentAppSound?.Dispose();
            _currentAppSound = player;
        }
        catch
        {
            /* feedback is best-effort */
        }
    }

    private static async Task<bool> DwellWithReplacementBailAsync(
        ThrottlerContext ctx,
        int totalMs,
        Func<bool>? shouldCancel = null)
    {
        int waited = 0;
        while (waited < totalMs)
        {
            if (ctx.HasReplacement) return false;
            if (shouldCancel?.Invoke() == true) return false;
            int slice = Math.Min(TimeConstants.VolumeFeedbackDingDwellPollSliceMs, totalMs - waited);
            try { await Task.Delay(slice, ctx.CancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }

            waited += slice;
        }

        return !ctx.HasReplacement && shouldCancel?.Invoke() != true;
    }

    private void EnsureAppFeedbackData()
    {
        if (_wavTemplate != null) return;

        string wavPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Media",
            AppFeedbackWavName);
        _wavTemplate = WAVTemplate.FromFile(wavPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _feedbackThrottler.Dispose(); }
        catch
        {
            /* shutdown best-effort */
        }

        if (_currentAppSound != null)
        {
            try
            {
                _currentAppSound.Stop();
                _currentAppSound.Dispose();
            }
            catch
            {
                /* shutdown best-effort */
            }

            _currentAppSound = null;
        }

        _wavTemplate = null;
    }
}

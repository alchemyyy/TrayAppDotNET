using Avalonia.Threading;
using VolumeTrayAppDotNET.Audio;

namespace VolumeTrayAppDotNET.UI.Flyout;

internal sealed class AppVolumeFeedbackPlayer : IDisposable
{
    private const string AppFeedbackWavName = "Windows Background.wav";
    private const string DeviceDingThrottleKey = "device";
    private const string AppDingThrottleKey = "app";

    private readonly AsyncThrottler<string> _feedbackThrottler = new(0, StringComparer.Ordinal);
    private readonly Lock _soundGate = new();
    private readonly AppSettings? _settings;
    private readonly Task<WAVTemplate?> _wavTemplateTask;
    private System.Media.SoundPlayer? _currentAppSound;
    private bool _disposed;

    public AppVolumeFeedbackPlayer(Dispatcher uiDispatcher, AppSettings? settings)
    {
        _ = uiDispatcher;
        _settings = settings;
        _wavTemplateTask = Task.Run(LoadAppFeedbackData);
    }

    public void PlayForDevice(AudioDevice device, bool immediate = false)
    {
        if (_disposed) return;
        if (_settings?.PlayDeviceVolumeChangeSound != true) return;
        if (device.IsCaptureDevice) return;

        string throttleKey = DeviceDingThrottleKey + ":" + device.Id;
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

            WAVTemplate? wav = await _wavTemplateTask.ConfigureAwait(false);
            if (_disposed || ctx.CancellationToken.IsCancellationRequested) return;
            if (wav == null) return;

            int dingWindowMs = wav.DurationMs + TimeConstants.VolumeFeedbackDingMeterBypassGraceMs;
            device.BeginDingSuppressionPeakBypass(dingWindowMs);
            try { EndpointSoundPlayback.PlayAsync(device.Id, wav); }
            catch
            {
                /* feedback is best-effort */
            }
        });
    }

    public void PlayForApp(AudioAppGroup group, bool immediate = false)
    {
        if (_disposed) return;
        if (_settings?.PlayAppVolumeChangeSound != true) return;

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
            WAVTemplate? wav = await _wavTemplateTask.ConfigureAwait(false);
            if (_disposed || ctx.CancellationToken.IsCancellationRequested) return;
            if (wav == null) return;

            try { PlayAppFeedbackNow(scalarVolume, wav); }
            catch
            {
                /* feedback is best-effort */
            }
        });
    }

    private bool ShouldSuppressDeviceDing(AudioDevice device)
    {
        AppSettings? settings = _settings;
        if (settings is not { SuppressDeviceVolumeChangeSoundWhenAudioPlaying: true }) return false;

        bool isPeakAvailable = device.TryReadDingSuppressionPeak(out float recentPeak);
        return DingSuppressionPeak.ShouldSuppressFeedback(
            recentPeak,
            settings.DingSuppressionPeakThresholdPercent,
            device.Volume,
            isPeakAvailable);
    }

    private bool ShouldSuppressAppDing(AudioAppGroup group)
    {
        AppSettings? settings = _settings;
        if (settings is not { SuppressDeviceVolumeChangeSoundWhenAudioPlaying: true }) return false;

        return DingSuppressionPeak.ShouldSuppressFeedback(
            group.ReadDingSuppressionPeak(),
            settings.DingSuppressionPeakThresholdPercent,
            group.Volume,
            isPeakAvailable: true);
    }

    private void PlayAppFeedbackNow(float scalarVolume, WAVTemplate template)
    {
        try
        {
            byte[] scaled = template.CloneScaled(scalarVolume);
            MemoryStream stream = new(scaled, writable: false);
            System.Media.SoundPlayer player = new(stream);
            player.Play();

            lock (_soundGate)
            {
                _currentAppSound?.Dispose();
                _currentAppSound = player;
            }
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

    private static WAVTemplate? LoadAppFeedbackData()
    {
        string wavPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Media",
            AppFeedbackWavName);
        return WAVTemplate.FromFile(wavPath);
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

        lock (_soundGate)
        {
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
        }
    }
}

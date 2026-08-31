using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Throttled COM-write driver shared by <see cref="AudioDevice.Volume.set"/> and
/// <see cref="AudioSession.Volume.set"/>. The two sites differ only in the write delegate
/// (SetMasterVolumeLevelScalar vs SetMasterVolume); everything else - the shared AsyncThrottler,
/// per-key latest-pending-wins semantics, the EventContext echo-suppression GUID, and the
/// swallow-COM-exception guard - is identical. Compose one per host in the ctor.
/// </summary>
internal sealed class VolumeThrottle(AsyncThrottler<string> throttler, string key)
{
    /// <summary>
    /// Queue a clamped float write. <paramref name="writer"/> runs on a threadpool worker and
    /// performs the actual COM call (with the shared event-context GUID already set).
    /// <paramref name="writeFailed"/> can retire a session whose endpoint was invalidated between
    /// the user's drag and the deferred write. Other callers can omit it for best-effort writes.
    /// </summary>
    public void Write(float value, Action<float, Guid> writer, Action<Exception>? writeFailed = null)
    {
        float captured = value;
        _ = throttler.RunAsync(key, _ =>
        {
            try
            {
                Guid ctx = AudioEventContext.Value;
                writer(captured, ctx);
            }
            catch (Exception exception)
            {
                // Endpoint may have been torn down between the user's drag and the deferred write
                writeFailed?.Invoke(exception);
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Drop any queued write. Called from the host's Dispose so the throttler driver doesn't try
    /// to invoke our writer on a soon-to-be-released RCW.
    /// </summary>
    public void Drop()
    {
        try { throttler.Drop(key); }
        catch { }
    }
}

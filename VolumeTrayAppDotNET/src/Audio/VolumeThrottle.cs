using VolumeTrayAppDotNET.Audio.Interop;

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
    /// performs the actual COM call (with the shared event-context GUID already set). Exceptions
    /// inside the writer are swallowed - typically the endpoint or session was torn down between
    /// the user's drag and the deferred write, and the next OnNotify / OnState event will
    /// reconcile the cached value.
    /// </summary>
    public void Write(float value, Action<float, Guid> writer)
    {
        float captured = value;
        _ = throttler.RunAsync(key, _ =>
        {
            try
            {
                Guid ctx = AudioEventContext.Value;
                writer(captured, ctx);
            }
            catch
            {
                // Endpoint may have been torn down between the user's drag and the deferred write.
                // The next OnNotify / OnState event will reconcile the cached value.
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

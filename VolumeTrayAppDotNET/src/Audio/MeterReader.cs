using VolumeTrayAppDotNET.Interop;
using IAudioMeterInformation = VolumeTrayAppDotNET.Interop.IAudioMeterInformation;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Thin wrapper around <see cref="IAudioMeterInformation"/> that returns min(L, R) and max(L, R)
/// over the first two channels. Falls back to GetPeakValue for mono streams (one channel = both
/// values equal); returns zero on failure so callers can leave previous lerp targets in place
/// without special-casing exceptions.
/// In unified mode the two channels collapse into a single weighted value (returned as both
/// min and max) so the base bar and stereo overlay coincide and the meter renders as one solid bar.
/// </summary>
internal static class MeterReader
{
    private const int Ok = 0;

    // Endpoint channel-count cap for the stackalloc peak buffer. Audio endpoints rarely exceed
    // 8 channels; 32 gives slack for exotic surround layouts without putting > 128B on the stack.
    private const int MaxStackChannels = 32;

    /// <summary>
    /// Reads the per-channel peak values and returns min/max over the first two channels.
    /// Uses a stackalloc'd buffer sized to the metering channel count and passes it via fixed
    /// pointer through the IntPtr-typed COM signature - no managed array and no heap alloc per
    /// tick. When <paramref name="unified"/> is true, collapses the per-channel result through
    /// <see cref="ApplyUnifiedWeighting"/> so both outputs carry the same weighted value.
    /// </summary>
    internal static unsafe void ReadStereoPeaks(
        IAudioMeterInformation meter, bool unified, int biasMultiplier, out float min, out float max)
    {
        min = 0f;
        max = 0f;

        try
        {
            meter.GetMeteringChannelCount(out uint chanCount);
            if (chanCount == 0) return;

            if (chanCount == 1)
            {
                // Mono: GetPeakValue avoids the buffer entirely, and the matching min/max keeps
                // the stereo overlay coincident with the base bar. Unified mode is a no-op here
                // since both values are already equal.
                meter.GetPeakValue(out float p);
                min = p;
                max = p;
                return;
            }

            // Exotic > MaxStackChannels endpoints fall back to GetPeakValue (single max-over-all)
            // so we never put a 100ch+ buffer on the stack. The peak meter only visualizes the
            // first two channels anyway, so the mono fallback is a graceful degradation.
            if (chanCount > MaxStackChannels)
            {
                meter.GetPeakValue(out float fp);
                min = fp;
                max = fp;
                return;
            }

            // COM contract requires u32ChannelCount to match GetMeteringChannelCount. Stack buffer
            // sized to chanCount; we only read the first two slots out.
            Span<float> peaks = stackalloc float[(int)chanCount];
            int hr;
            fixed (float* pPeaks = peaks)
                hr = meter.GetChannelsPeakValues(chanCount, (IntPtr)pPeaks);
            if (hr != Ok) return;

            // Read just the first two channels - the rest are surround / LFE which the meter
            // doesn't visualize.
            float a = peaks[0];
            float b = peaks[1];
            float lo = a < b ? a : b;
            float hi = a > b ? a : b;

            if (unified)
            {
                float combined = ApplyUnifiedWeighting(lo, hi, biasMultiplier);
                min = combined;
                max = combined;
            }
            else
            {
                min = lo;
                max = hi;
            }
        }
        catch
        {
            // Endpoint or session can fail mid-disconnect; leave 0/0 - the calling lerp will
            // continue from its previous targets until the next successful read.
        }
    }

    /// <summary>
    /// Combines the quieter and louder channel into one weighted value:
    /// <c>(low * M + high) / (M + 1)</c>. M=0 falls back to max(L, R); M=1 averages the channels;
    /// larger M biases the result toward min(L, R), dampening moment-to-moment stereo flutter
    /// without fully collapsing to the quieter channel. Multiplier is clamped to non-negative.
    /// </summary>
    private static float ApplyUnifiedWeighting(float low, float high, int biasMultiplier)
    {
        int m = biasMultiplier < 0 ? 0 : biasMultiplier;
        return (low * m + high) / (m + 1);
    }
}

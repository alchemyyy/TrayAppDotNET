namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Accepts or rejects one candidate ramp. <see cref="GammaRampMath.ApplyWithBackoff{TWriter}"/> drives it with
/// strengths (0-100); the implementation builds the matching ramp and hands it to the display.
/// </summary>
internal interface IGammaRampWriter
{
    bool TryWrite(int strength);
}

/// <summary>Outcome of one <see cref="GammaRampMath.ApplyWithBackoff{TWriter}"/> run.</summary>
/// <param name="AppliedStrength">Strength now on the display, or -1 when even the identity ramp was refused.</param>
/// <param name="Attempts">Ramp writes issued, including refused ones.</param>
internal readonly record struct GammaRampWriteResult(int AppliedStrength, int Attempts)
{
    public bool Succeeded => AppliedStrength >= 0;
}

/// <summary>
/// Pure math for the gamma-ramp Night Light backend: strength to white point, white point to a 3x256 GDI ramp,
/// and the bounded back-off used when Windows refuses a ramp.
/// Strength uses the same kelvin mapping as Windows Night Light (<see cref="NightLightKelvin"/>),
/// so 0 is 6500 K and produces the exact identity ramp.
/// </summary>
internal static class GammaRampMath
{
    public const int EntriesPerChannel = 256;
    public const int RampLength = EntriesPerChannel * 3;

    /// <summary>
    /// Binary-search probes after the first refused write. ceil(log2(100)) covers the whole 0-100 strength range,
    /// so the search always converges before the bound.
    /// </summary>
    public const int MaxBackoffProbes = 7;

    // 255 * 257 = 65535: the identity ramp maps each 8-bit input to the full 16-bit output range.
    private const int IdentityStep = 257;

    /// <summary>
    /// Per-channel multipliers (0-1) for <paramref name="kelvin"/>, normalized so
    /// <see cref="NightLightKelvin.MaxKelvin"/> (6500 K) is exactly 1, 1, 1. Lower temperatures only ever reduce
    /// green and blue; red stays at 1 across the Night Light range.
    /// </summary>
    public static (double Red, double Green, double Blue) KelvinToChannelScale(int kelvin)
    {
        if (kelvin >= NightLightKelvin.MaxKelvin) return (1.0, 1.0, 1.0);

        (double red, double green, double blue) = KelvinToRgb(kelvin);
        (double whiteRed, double whiteGreen, double whiteBlue) = KelvinToRgb(NightLightKelvin.MaxKelvin);
        return (
            Math.Clamp(red / whiteRed, min: 0.0, max: 1.0),
            Math.Clamp(green / whiteGreen, min: 0.0, max: 1.0),
            Math.Clamp(blue / whiteBlue, min: 0.0, max: 1.0));
    }

    /// <summary>
    /// Blackbody white point in 0-255 per channel, using Tanner Helland's curve fit of the CIE 1964 blackbody
    /// data (valid 1000-40000 K). Input is clamped to that range.
    /// </summary>
    public static (double Red, double Green, double Blue) KelvinToRgb(int kelvin)
    {
        double t = Math.Clamp(kelvin, min: 1000, max: 40000) / 100.0;

        double red = t <= 66
            ? 255.0
            : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double green = t <= 66
            ? 99.4708025861 * Math.Log(t) - 161.1195681661
            : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double blue = t >= 66
            ? 255.0
            : t <= 19
                ? 0.0
                : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;

        return (
            Math.Clamp(red, min: 0.0, max: 255.0),
            Math.Clamp(green, min: 0.0, max: 255.0),
            Math.Clamp(blue, min: 0.0, max: 255.0));
    }

    /// <summary>
    /// Fills <paramref name="ramp"/> (red, green, blue; 256 entries each) for <paramref name="strength"/> (clamped
    /// to 0-100). Strength 0 is the identity ramp.
    /// </summary>
    public static void BuildRamp(int strength, Span<ushort> ramp)
    {
        if (ramp.Length != RampLength)
            throw new ArgumentException($"Gamma ramp must hold {RampLength} entries.", nameof(ramp));

        (double red, double green, double blue) = KelvinToChannelScale(NightLightKelvin.PercentToKelvin(strength));
        FillChannel(ramp[..EntriesPerChannel], red);
        FillChannel(ramp.Slice(EntriesPerChannel, EntriesPerChannel), green);
        FillChannel(ramp.Slice(EntriesPerChannel * 2, EntriesPerChannel), blue);
    }

    /// <summary>Largest absolute per-entry difference between two ramps of equal length.</summary>
    public static int MaxDeviation(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right)
    {
        int max = 0;
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
            max = Math.Max(max, Math.Abs(left[i] - right[i]));
        return max;
    }

    /// <summary>
    /// Writes <paramref name="requestedStrength"/>, capped at <paramref name="maxAcceptedStrength"/> (the strongest
    /// strength this display accepted earlier; 100 when unknown). When the writer refuses, binary-searches the
    /// strongest strength below it that the writer accepts, with at most <see cref="MaxBackoffProbes"/> probes,
    /// and leaves that ramp applied. When nothing above 0 is accepted, writes the identity ramp so a stale stronger
    /// tint can't stay on screen.
    /// </summary>
    public static GammaRampWriteResult ApplyWithBackoff<TWriter>(
        int requestedStrength,
        int maxAcceptedStrength,
        ref TWriter writer)
        where TWriter : IGammaRampWriter
    {
        int target = Math.Min(
            Math.Clamp(requestedStrength, min: 0, max: 100),
            Math.Clamp(maxAcceptedStrength, min: 0, max: 100));

        int attempts = 1;
        if (writer.TryWrite(target)) return new GammaRampWriteResult(target, attempts);
        if (target == 0) return new GammaRampWriteResult(-1, attempts);

        int accepted = 0;
        int refused = target;
        bool appliedAccepted = false;
        for (int probe = 0; probe < MaxBackoffProbes && refused - accepted > 1; probe++)
        {
            int candidate = accepted + (refused - accepted) / 2;
            attempts++;
            if (writer.TryWrite(candidate))
            {
                accepted = candidate;
                appliedAccepted = true;
            }
            else
                refused = candidate;
        }

        // A refused write leaves the previous ramp in place, so the last accepted probe is what's on screen.
        if (appliedAccepted) return new GammaRampWriteResult(accepted, attempts);

        attempts++;
        return writer.TryWrite(0)
            ? new GammaRampWriteResult(0, attempts)
            : new GammaRampWriteResult(-1, attempts);
    }

    private static void FillChannel(Span<ushort> channel, double scale)
    {
        for (int i = 0; i < channel.Length; i++)
            channel[i] = (ushort)Math.Round(i * IdentityStep * scale);
    }
}

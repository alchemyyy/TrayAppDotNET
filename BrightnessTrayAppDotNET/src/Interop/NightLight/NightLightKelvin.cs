namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Kelvin range of Windows night-light and the strength-to-kelvin mapping used everywhere we touch the slider.
/// 0% strength = <see cref="MaxKelvin"/> (no warmth, daylight); 100% strength = <see cref="MinKelvin"/> (max warmth).
/// The range matches what <c>BlueLightSingleton::ClampTargetColorTemperature</c> accepts - values outside it are
/// clamped server-side, but we clamp first for clarity.
/// </summary>
internal static class NightLightKelvin
{
    public const int MinKelvin = 1200;
    public const int MaxKelvin = 6500;

    /// <summary>Maps strength 0-100 to kelvin. <paramref name="percent"/> is clamped.</summary>
    public static int PercentToKelvin(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        return MaxKelvin - clamped * (MaxKelvin - MinKelvin) / 100;
    }

    /// <summary>
    /// Maps kelvin back to strength 0-100. Result is clamped, so out-of-range kelvin (e.g. from a malformed blob)
    /// lands at the nearest endpoint instead of producing a negative percent.
    /// </summary>
    public static int KelvinToPercent(int kelvin)
    {
        int percent = 100 - ((kelvin - MinKelvin) * 100) / (MaxKelvin - MinKelvin);
        return Math.Clamp(percent, 0, 100);
    }
}

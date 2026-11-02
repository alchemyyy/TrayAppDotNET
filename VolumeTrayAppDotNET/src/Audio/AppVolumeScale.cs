namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Maps app slider positions (0 to 100) to Windows session volume levels (0 to 1) and back, for every path that
/// shows or sets an app volume. Windows applies a session level (ISimpleAudioVolume) to the samples as a plain
/// amplitude multiplier, so a slider wired straight to it spends most of its travel in the loudest few decibels.
/// The logarithmic scale spreads loudness evenly instead: every 10% of travel is 6 dB, and below the point where a
/// line from the origin touches that curve, the line itself fades to true silence at 0. Device sliders never go
/// through this mapping because IAudioEndpointVolume's scalar level is already audio-tapered by Windows.
/// Off, both directions are the plain percent conversion the sliders always used.
/// </summary>
internal static class AppVolumeScale
{
    internal const double PercentMax = 100.0;

    // Decibels across the whole travel of the constant-decibel curve. 60 dB makes every 10% of travel one 6 dB
    // step, i.e. roughly half or double the signal level.
    internal const double DynamicRangeDecibels = 60.0;

    // Decimal places kept on a logarithmic position read back from a level. Float noise in the level would
    // otherwise leave a thumb at e.g. 3.0000001, and three 1% wheel steps down would end a hair above silence.
    // 1e-5 percent is far below the 0.001 a FlyoutSlider needs to move.
    private const int PositionPercentDecimals = 5;

    // Natural-log change in level per unit of travel: level = e^(GrowthRate * (position - 1))
    private static readonly double GrowthRate = DynamicRangeDecibels * Math.Log(10.0) / 20.0;

    // The line from the origin is tangent to the curve where position = 1 / GrowthRate, so the two pieces meet
    // with the same slope and the whole mapping stays smooth and strictly increasing
    internal static readonly double KneePosition = 1.0 / GrowthRate;
    private static readonly double KneeLevel = Math.Exp(1.0 - GrowthRate);
    private static readonly double TangentSlope = KneeLevel / KneePosition;

    /// <summary>Windows session level for an app slider position in percent.</summary>
    internal static float LevelFromSliderPercent(double percent, bool isLogarithmic)
    {
        double position = Math.Clamp(percent / PercentMax, min: 0.0, max: 1.0);
        return (float)(isLogarithmic ? LevelFromPosition(position) : position);
    }

    /// <summary>App slider position in percent for a Windows session level.</summary>
    internal static double SliderPercentFromLevel(float level, bool isLogarithmic)
    {
        double clampedLevel = Math.Clamp(level, min: 0.0, max: 1.0);
        return isLogarithmic
            ? Math.Round(PositionFromLevel(clampedLevel) * PercentMax, PositionPercentDecimals)
            : clampedLevel * PercentMax;
    }

    /// <summary>
    /// Peak fraction to hand an app slider. The slider draws its meter as this fraction of the thumb's extent,
    /// i.e. it treats the session peak as the signal before the session level is applied. On the logarithmic scale
    /// the meter instead ends where the thumb would sit for the level that reaches the device (peak times level),
    /// so a full-scale signal still reaches the thumb and quieter audio reads on the same curve as the thumb.
    /// </summary>
    internal static float MeterPeakFraction(float peak, float level, bool isLogarithmic)
    {
        if (!isLogarithmic) return peak;

        double clampedPeak = float.IsFinite(peak) ? Math.Clamp(peak, min: 0f, max: 1f) : 0.0;
        double clampedLevel = float.IsFinite(level) ? Math.Clamp(level, min: 0f, max: 1f) : 0.0;
        double thumbPosition = PositionFromLevel(clampedLevel);
        if (clampedPeak <= 0 || thumbPosition <= 0) return 0f;

        double meterPosition = PositionFromLevel(clampedPeak * clampedLevel);
        return (float)Math.Clamp(meterPosition / thumbPosition, min: 0.0, max: 1.0);
    }

    private static double LevelFromPosition(double position) =>
        position <= KneePosition
            ? position * TangentSlope
            : Math.Exp(GrowthRate * (position - 1.0));

    private static double PositionFromLevel(double level) =>
        level <= KneeLevel
            ? level / TangentSlope
            : 1.0 + Math.Log(level) / GrowthRate;
}

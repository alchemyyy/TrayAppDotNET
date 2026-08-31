namespace VolumeTrayAppDotNET.Audio;

internal readonly record struct MeterPeakValues(float Min, float Max)
{
    public static readonly MeterPeakValues Zero = new(Min: 0f, Max: 0f);
}

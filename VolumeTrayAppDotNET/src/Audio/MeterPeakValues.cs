namespace VolumeTrayAppDotNET.Audio;

internal readonly record struct MeterPeakValues(float Min, float Max)
{
    public static readonly MeterPeakValues Zero = new(0f, 0f);
}

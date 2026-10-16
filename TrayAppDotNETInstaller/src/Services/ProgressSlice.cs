namespace TrayAppDotNETInstaller.Services;

/// <summary>A contiguous percent range of the overall progress bar, used to rescale per-app progress.</summary>
public readonly record struct ProgressSlice(int StartPercent, int EndPercent)
{
    private const int FullPercent = InstallProgressLine.CompletePercent;

    public static ProgressSlice Full => new(StartPercent: 0, FullPercent);

    /// <summary>Maps a 0-100 percent inside this slice onto the overall scale.</summary>
    public int Map(int innerPercent)
    {
        int clamped = FrameworkCompatibility.Clamp(innerPercent, min: 0, FullPercent);
        return StartPercent + (EndPercent - StartPercent) * clamped / FullPercent;
    }

    /// <summary>The sub-slice between two inner percents of this slice.</summary>
    public ProgressSlice Portion(int fromInnerPercent, int toInnerPercent) =>
        new(Map(fromInnerPercent), Map(toInnerPercent));
}

namespace TrayAppDotNETInstaller.Services;

/// <summary>A contiguous percent range of the overall progress bar, used to rescale per-app progress.</summary>
public readonly record struct ProgressSlice(int StartPercent, int EndPercent)
{
    private const int FullPercent = InstallProgressLine.CompletePercent;

    public static ProgressSlice Full => new(StartPercent: 0, FullPercent);

    /// <summary>Equal slice for item <paramref name="index"/> of <paramref name="count"/>; the last one ends at 100.</summary>
    public static ProgressSlice ForIndex(int index, int count)
    {
        if (count <= 0) return Full;

        // FrameworkCompatibility.Clamp stands in for Math.Clamp, which the .NET Framework does not define
        int clampedIndex = FrameworkCompatibility.Clamp(index, min: 0, count - 1);
        return new ProgressSlice(clampedIndex * FullPercent / count, (clampedIndex + 1) * FullPercent / count);
    }

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

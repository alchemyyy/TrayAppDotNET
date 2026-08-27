using System.Globalization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Calculates and formats elapsed process lifetime from Windows file-time ticks.</summary>
internal static class ProcessLifetime
{
    public const long UnavailableTicks = -1;

    private const long SecondsPerMinute = 60;
    private const long SecondsPerHour = 60 * SecondsPerMinute;
    private const long SecondsPerDay = 24 * SecondsPerHour;

    public static long CalculateTicks(long creationTimeTicks, long sampleTimeTicks)
    {
        if (creationTimeTicks <= 0 || sampleTimeTicks < creationTimeTicks)
            return UnavailableTicks;
        return sampleTimeTicks - creationTimeTicks;
    }

    public static string Format(long lifetimeTicks)
    {
        if (lifetimeTicks < 0) throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));

        long totalSeconds = lifetimeTicks / TimeSpan.TicksPerSecond;
        long days = totalSeconds / SecondsPerDay;
        long hours = totalSeconds / SecondsPerHour % 24;
        long minutes = totalSeconds / SecondsPerMinute % 60;
        long seconds = totalSeconds % 60;
        return days > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{days}d {hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}");
    }
}

using System.Globalization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Formats resource values shared by App History and Users rows.</summary>
internal static class TaskManagerUsageFormatter
{
    private const double BytesPerMebibyte = 1_048_576;
    private const double BytesPerMegabit = 1_000_000.0 / 8;
    private const string UnavailableText = "Unavailable";

    public static string FormatCPUTime(long ticks) => ProcessLifetime.Format(Math.Max(0, ticks));

    public static string FormatAppHistoryNetwork(
        double bytes,
        CultureInfo? culture = null)
    {
        if (!double.IsFinite(bytes) || bytes < 0) return UnavailableText;

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        return string.Concat(
            (bytes / BytesPerMebibyte).ToString("0.#", effectiveCulture),
            " MB");
    }

    public static string FormatCPUPercent(
        double percent,
        CultureInfo? culture = null)
    {
        if (!double.IsFinite(percent) || percent < 0) return UnavailableText;

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        return string.Concat(percent.ToString("0.#", effectiveCulture), "%");
    }

    public static string FormatMemory(
        long bytes,
        CultureInfo? culture = null)
    {
        if (bytes < 0) return UnavailableText;

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        return string.Concat(
            (bytes / BytesPerMebibyte).ToString("N1", effectiveCulture),
            " MB");
    }

    public static string FormatDiskRate(
        bool isAvailable,
        double bytesPerSecond,
        CultureInfo? culture = null)
    {
        if (!isAvailable || !double.IsFinite(bytesPerSecond) || bytesPerSecond < 0)
            return UnavailableText;

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        return string.Concat(
            (bytesPerSecond / BytesPerMebibyte).ToString("0.#", effectiveCulture),
            " MB/s");
    }

    public static string FormatNetworkRate(
        bool isAvailable,
        double bytesPerSecond,
        CultureInfo? culture = null)
    {
        if (!isAvailable || !double.IsFinite(bytesPerSecond) || bytesPerSecond < 0)
            return UnavailableText;

        CultureInfo effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        return string.Concat(
            (bytesPerSecond / BytesPerMegabit).ToString("0.#", effectiveCulture),
            " Mbps");
    }

    public static string FormatSessionState(UserSessionState state) => state switch
    {
        UserSessionState.Active => "Active",
        UserSessionState.Connected => "Connected",
        UserSessionState.Disconnected => "Disconnected",
        UserSessionState.Idle => "Idle",
        _ => string.Empty
    };
}

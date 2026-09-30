using System.Globalization;
using BatteryTrayAppDotNET.Models;

namespace BatteryTrayAppDotNET.Services;

/// <summary>
/// Keeps learned battery usage across restarts, so Predicted life is available on external power before the
/// battery has been used again.
/// </summary>
public static class BatteryLearnedUsageStore
{
    public static string DefaultPath => Path.Combine(Program.AppLocalAppDataDirectory, "learned-battery-usage.txt");

    public static BatteryLearnedUsage? Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryLearnedUsageStore.Load: {ex.Message}");
            return null;
        }
    }

    public static void Save(string path, BatteryLearnedUsage usage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Format(usage));
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryLearnedUsageStore.Save: {ex.Message}");
        }
    }

    public static string Format(BatteryLearnedUsage usage) =>
        string.Create(CultureInfo.InvariantCulture, $"{usage.Seconds:R} {usage.Watts:R}");

    public static BatteryLearnedUsage? Parse(string text)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double watts))
            return null;

        BatteryLearnedUsage usage = new(seconds, watts);
        return usage.IsUsable ? usage : null;
    }
}

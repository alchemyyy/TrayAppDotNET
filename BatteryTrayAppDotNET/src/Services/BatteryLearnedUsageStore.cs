using System.Globalization;
using BatteryTrayAppDotNET.Models;
using TrayAppDotNETCommon.Serialization;

namespace BatteryTrayAppDotNET.Services;

/// <summary>
/// Keeps learned battery usage across restarts, so Predicted life is available on external power before the
/// battery has been used again. Writes are atomic; an unparsable file is moved aside to a <c>.corrupt-*</c> backup.
/// </summary>
public static class BatteryLearnedUsageStore
{
    public static string DefaultPath => Path.Combine(Program.AppLocalAppDataDirectory, "learned-battery-usage.txt");

    public static BatteryLearnedUsage? Load(string path)
    {
        DataFileLoadResult result = DataFileLoader.TryLoad(path, ParseStream, Log, out BatteryLearnedUsage usage);
        if (result == DataFileLoadResult.Corrupt) DataFileLoader.MoveCorruptAside(path, Log);
        return result == DataFileLoadResult.Loaded ? usage : null;
    }

    public static void Save(string path, BatteryLearnedUsage usage)
    {
        try
        {
            AtomicFile.WriteAllText(path, Format(usage));
        }
        catch (Exception ex)
        {
            Log($"save failed: {ex.Message}");
        }
    }

    private static void Log(string message) => TADNLog.Log($"BatteryLearnedUsageStore: {message}");

    private static BatteryLearnedUsage ParseStream(Stream stream)
    {
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd()) ?? throw new InvalidDataException("Expected two positive numbers.");
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

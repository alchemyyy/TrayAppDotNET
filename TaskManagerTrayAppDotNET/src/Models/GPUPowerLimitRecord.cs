using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>
/// A GPU's default power limit, read once through its vendor library, kept with the vBIOS it was read under. Windows
/// reports GPU power as a percentage of this limit, so watts are that percentage times the limit.
/// </summary>
public sealed class GPUPowerLimitRecord
{
    [XmlAttribute]
    [AllowNull]
    public string DeviceInstanceID
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    [AllowNull]
    public string VBIOSVersion
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    public uint DefaultLimitMilliwatts { get; set; }

    [XmlAttribute]
    public bool ShowWatts { get; set; }
}

/// <summary>Normalizes and queries the persisted GPU power limits.</summary>
internal static class GPUPowerLimitCollection
{
    private const double MilliwattsPerWatt = 1_000;

    public static List<GPUPowerLimitRecord> Normalize(IEnumerable<GPUPowerLimitRecord?>? records)
    {
        List<GPUPowerLimitRecord> normalized = [];
        if (records == null) return normalized;

        foreach (GPUPowerLimitRecord? record in records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.DeviceInstanceID)) continue;

            // A later record for the same GPU replaces an earlier one
            normalized.RemoveAll(existing => IsSameDevice(existing.DeviceInstanceID, record.DeviceInstanceID));
            normalized.Add(new GPUPowerLimitRecord
            {
                DeviceInstanceID = record.DeviceInstanceID.Trim(),
                VBIOSVersion = record.VBIOSVersion.Trim(),
                DefaultLimitMilliwatts = record.DefaultLimitMilliwatts,
                ShowWatts = record.ShowWatts && record.DefaultLimitMilliwatts > 0
            });
        }

        return normalized;
    }

    public static GPUPowerLimitRecord? Find(IReadOnlyList<GPUPowerLimitRecord> records, string deviceInstanceID)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceID)) return null;

        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            if (IsSameDevice(records[recordIndex].DeviceInstanceID, deviceInstanceID))
                return records[recordIndex];
        }

        return null;
    }

    /// <summary>
    /// Returns whether a saved limit still describes the GPU: it must have been read under the vBIOS the GPU reports
    /// now, since flashing a vBIOS is what changes a board's default power limit.
    /// </summary>
    public static bool IsCurrent(GPUPowerLimitRecord record, string currentVBIOSVersion) =>
        record.DefaultLimitMilliwatts > 0
        && string.Equals(record.VBIOSVersion, currentVBIOSVersion.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the default limit in watts when the user chose watts and the record still applies.</summary>
    public static bool TryGetDisplayLimitWatts(
        IReadOnlyList<GPUPowerLimitRecord> records,
        string deviceInstanceID,
        string currentVBIOSVersion,
        out double limitWatts)
    {
        limitWatts = 0;
        GPUPowerLimitRecord? record = Find(records, deviceInstanceID);
        if (record is not { ShowWatts: true } || !IsCurrent(record, currentVBIOSVersion)) return false;

        limitWatts = record.DefaultLimitMilliwatts / MilliwattsPerWatt;
        return true;
    }

    private static bool IsSameDevice(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

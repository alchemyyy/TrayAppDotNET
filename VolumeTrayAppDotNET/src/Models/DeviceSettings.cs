namespace VolumeTrayAppDotNET.Models;

/// <summary>
/// One row of persisted per-device UI state. The Id is the Windows audio endpoint
/// id from MMDevice -- the same string <see cref="Audio.AudioDevice.Id"/> exposes.
/// Properties default to the same values a brand-new VolumeFlyoutCell would render, so a missing
/// entry behaves identically to one with all defaults.
/// </summary>
public class DeviceSettingsEntry
{
    public string Id { get; set; } = "";
    public bool IsAppDrawerExpanded { get; set; } = true;
}

/// <summary>
/// Persisted per-device UI state collection. Backs devices.xml in the LocalAppData app folder,
/// the same directory as settings.xml. Only state that is specific to a single endpoint belongs
/// here; everything global lives in <see cref="AppSettings"/>.
///
/// Reads are non-destructive (<see cref="Find"/> returns null for unknown ids); writes go through
/// <see cref="GetOrCreate"/> so a device only gets a row on its first persisted edit, not on every
/// flyout open.
/// </summary>
public partial class DeviceSettings
{
    public List<DeviceSettingsEntry> Devices { get; set; } = [];

    /// <summary>Returns the entry for the given endpoint id, or null when no row exists.</summary>
    public DeviceSettingsEntry? Find(string id)
    {
        for (int i = 0; i < Devices.Count; i++)
            if (string.Equals(Devices[i].Id, id, StringComparison.Ordinal))
                return Devices[i];
        return null;
    }

    /// <summary>Returns the existing entry or appends a fresh default row tagged with the given id.</summary>
    public DeviceSettingsEntry GetOrCreate(string id)
    {
        DeviceSettingsEntry? existing = Find(id);
        if (existing != null) return existing;
        DeviceSettingsEntry entry = new() { Id = id };
        Devices.Add(entry);
        return entry;
    }
}

using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace VolumeTrayAppDotNET.Models;

/// <summary>
/// One row of persisted per-device UI state. The Id is the Windows audio endpoint
/// id from MMDevice -- the same string <see cref="Audio.AudioDevice.Id"/> exposes.
/// Properties default to the same values a brand-new VolumeFlyoutCell would render, so a missing
/// entry behaves identically to one with all defaults.
/// </summary>
public class DeviceSettingsEntry
{
    [XmlAttribute]
    public string Id { get; set; } = "";

    [XmlAttribute]
    public bool IsAppDrawerExpanded { get; set; } = true;

    /// <summary>
    /// VTADN-only display-name override. Bluetooth endpoints use this instead of attempting to
    /// write the often read-only Windows endpoint property store. Empty means use the name
    /// reported by Windows.
    /// </summary>
    [XmlAttribute]
    public string CustomFriendlyName { get; set; } = "";
}

/// <summary>
/// Persisted per-device UI state collection. Backs devices.xml in the LocalAppData app folder,
/// the same directory as settings.xml. Only state that is specific to a single endpoint belongs
/// here; everything global lives in <see cref="AppSettings"/>.
///
/// Reads are non-destructive (<see cref="Find"/> returns null for unknown ids); writes go through
/// <see cref="GetOrCreate"/> so a device only gets a row on its first persisted edit, not on every
/// flyout open. Loading and saving go through <see cref="TrayXmlFileStore{T}"/>: a corrupt file is kept as a
/// <c>.corrupt-*</c> backup and a locked one is never overwritten.
/// </summary>
[XmlRoot("Devices")]
public class DeviceSettings : ITrayXmlStoredDocument
{
    private ITrayXmlFileStore? _store;

    [XmlElement("Device")]
    public List<DeviceSettingsEntry> Devices { get; set; } = [];

    /// <summary>Returns the entry for the given endpoint id, or null when no row exists.</summary>
    public DeviceSettingsEntry? Find(string id)
    {
        for (int i = 0; i < Devices.Count; i++)
        {
            if (string.Equals(Devices[i].Id, id, StringComparison.Ordinal))
                return Devices[i];
        }

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

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "devices.xml");
    }

    /// <summary>Writes the file now (in the background); a detached instance never writes.</summary>
    public void Save() => _store?.SaveNow();

    /// <summary>Waits for queued writes; call at exit.</summary>
    public void Flush() => _store?.Flush();

    public static DeviceSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static DeviceSettings LoadOrDefault(string path) =>
        new TrayXmlFileStore<DeviceSettings>(
            path,
            static () => new DeviceSettings(),
            static message => TADNLog.Log($"DeviceSettings: {message}")).Load();

    void ITrayXmlStoredDocument.AttachStore(ITrayXmlFileStore store) => _store = store;
}

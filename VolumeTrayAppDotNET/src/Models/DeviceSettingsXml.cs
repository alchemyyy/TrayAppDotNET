using System.Xml;
using System.Xml.Linq;

namespace VolumeTrayAppDotNET.Models;

public partial class DeviceSettings
{
    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "devices.xml");
    }

    public void Save() => Save(GetDefaultPath());

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        try
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (directory.Length > 0) Directory.CreateDirectory(directory);

            XmlWriterSettings writerSettings = new()
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace,
            };

            using (FileStream stream = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (XmlWriter writer = XmlWriter.Create(stream, writerSettings))
            {
                ToXml().Save(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DeviceSettings.Save: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    public static DeviceSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static DeviceSettings LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                XDocument document = XDocument.Load(path);
                XElement? root = document.Root;
                if (root?.Name == "Devices") return FromXml(root);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DeviceSettings.LoadOrDefault: {ex.Message}");
        }

        return new DeviceSettings();
    }

    private XDocument ToXml()
    {
        XElement root = new("Devices");
        foreach (DeviceSettingsEntry device in Devices)
        {
            root.Add(new XElement("Device",
                new XAttribute(nameof(DeviceSettingsEntry.Id), device.Id ?? string.Empty),
                new XAttribute(nameof(DeviceSettingsEntry.IsAppDrawerExpanded),
                    XmlConvert.ToString(device.IsAppDrawerExpanded))));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static DeviceSettings FromXml(XElement root)
    {
        DeviceSettings settings = new();
        foreach (XElement element in root.Elements("Device"))
        {
            string id = (string?)element.Attribute(nameof(DeviceSettingsEntry.Id)) ?? string.Empty;
            bool expanded = !bool.TryParse(
                (string?)element.Attribute(nameof(DeviceSettingsEntry.IsAppDrawerExpanded)),
                out bool value) || value;

            settings.Devices.Add(new DeviceSettingsEntry { Id = id, IsAppDrawerExpanded = expanded, });
        }

        return settings;
    }
}

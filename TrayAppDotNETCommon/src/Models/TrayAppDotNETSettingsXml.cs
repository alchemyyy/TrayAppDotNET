using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.Models;

public static class TrayAppDotNETSettingsXml
{
    public static void SaveDocument(Stream stream, XDocument document)
    {
        XmlWriterSettings writerSettings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
        };

        using XmlWriter writer = XmlWriter.Create(stream, writerSettings);
        document.Save(writer);
        writer.Flush();
        if (stream is FileStream fileStream) fileStream.Flush(flushToDisk: true);
    }

    public static XElement LoadRoot(
        Stream stream,
        string rootName,
        string missingRootMessage = "Missing settings root.",
        string unexpectedRootMessage = "Unexpected settings root.")
    {
        XDocument document = XDocument.Load(stream);
        return LoadRoot(document, rootName, missingRootMessage, unexpectedRootMessage);
    }

    public static XElement LoadRoot(
        XDocument document,
        string rootName,
        string missingRootMessage = "Missing settings root.",
        string unexpectedRootMessage = "Unexpected settings root.")
    {
        XElement root = document.Root ?? throw new InvalidDataException(missingRootMessage);
        if (root.Name != rootName) throw new InvalidDataException(unexpectedRootMessage);
        return root;
    }

    public static XElement Bool(string name, bool value) => new(name, XmlConvert.ToString(value));

    public static XElement Int(string name, int value) => new(name, XmlConvert.ToString(value));

    public static XElement Long(string name, long value) => new(name, XmlConvert.ToString(value));

    public static XElement Double(string name, double value) => new(name, XmlConvert.ToString(value));

    public static XElement Text(string name, string? value) =>
        value == null ? new XElement(name) : new XElement(name, value);

    public static XElement Enum<T>(string name, T value)
        where T : struct, Enum =>
        new(name, value.ToString());

    public static XAttribute Attribute(string name, string? value) => new(name, value ?? string.Empty);

    public static XAttribute Attribute(string name, bool value) => new(name, XmlConvert.ToString(value));

    public static XAttribute Attribute(string name, int value) => new(name, XmlConvert.ToString(value));

    public static XAttribute Attribute(string name, long value) => new(name, XmlConvert.ToString(value));

    public static XAttribute Attribute(string name, double value) => new(name, XmlConvert.ToString(value));

    public static XAttribute Attribute(string name, DateTime value) =>
        new(name, value.ToString("O", CultureInfo.InvariantCulture));

    public static XAttribute Attribute<T>(string name, T value)
        where T : struct, Enum =>
        new(name, value.ToString());

    public static XElement StringListElement(string name, string itemName, IEnumerable<string> items) =>
        new(name, items.Select(item => new XElement(itemName, item)));

    public static List<string> ReadStringList(XElement? element, string itemName) =>
        element?.Elements(itemName).Select(e => e.Value).Where(s => s.Length > 0).ToList() ?? [];

    public static XElement NullableThemeColorElement(string name, NullableThemeColor color)
    {
        XElement element = new(name);
        if (!string.IsNullOrEmpty(color.LightHex))
            element.Add(new XElement(nameof(NullableThemeColor.LightHex), color.LightHex));
        if (!string.IsNullOrEmpty(color.DarkHex))
            element.Add(new XElement(nameof(NullableThemeColor.DarkHex), color.DarkHex));
        return element;
    }

    public static XElement SliderThumbElement(SliderThumbGlyphOption? option)
    {
        option ??= SliderThumbGlyphOption.CreateDefaults().FirstOrDefault();
        XElement element = new("SliderThumb");
        if (option == null) return element;

        element.Add(
            new XAttribute(nameof(SliderThumbGlyphOption.Name), option.Name),
            new XAttribute(nameof(SliderThumbGlyphOption.Glyph), option.Glyph),
            new XAttribute(nameof(SliderThumbGlyphOption.FontFamily), option.FontFamily),
            new XAttribute(nameof(SliderThumbGlyphOption.FontSize), XmlConvert.ToString(option.FontSize)),
            new XAttribute(nameof(SliderThumbGlyphOption.Width), XmlConvert.ToString(option.Width)),
            new XAttribute(nameof(SliderThumbGlyphOption.Height), XmlConvert.ToString(option.Height)),
            new XAttribute(nameof(SliderThumbGlyphOption.XScale), XmlConvert.ToString(option.XScale)),
            new XAttribute(nameof(SliderThumbGlyphOption.Shape), option.Shape.ToString()));
        return element;
    }

    public static XElement HotkeysElement(IEnumerable<HotkeyBinding> hotkeys) =>
        HotkeysElement<HotkeyAction>(hotkeys);

    public static XElement HotkeysElement<TAction>(IEnumerable<IHotkeyBinding<TAction>> hotkeys)
        where TAction : struct, Enum
    {
        XElement element = new("Hotkeys");
        foreach (IHotkeyBinding<TAction> hotkey in hotkeys)
        {
            element.Add(new XElement("Binding",
                new XAttribute(nameof(HotkeyBinding.Action), hotkey.Action.ToString()),
                new XAttribute(nameof(HotkeyBinding.Parameter), hotkey.Parameter),
                new XAttribute(nameof(HotkeyBinding.Modifiers), XmlConvert.ToString(hotkey.Modifiers)),
                new XAttribute(nameof(HotkeyBinding.VirtualKey), XmlConvert.ToString(hotkey.VirtualKey)),
                new XAttribute(nameof(HotkeyBinding.Enabled), XmlConvert.ToString(hotkey.Enabled)),
                new XAttribute(nameof(HotkeyBinding.BindingID), XmlConvert.ToString(hotkey.BindingID)),
                new XAttribute(nameof(HotkeyBinding.RemovedByUser), XmlConvert.ToString(hotkey.RemovedByUser))));
        }

        return element;
    }

    public static bool ReadBool(XElement root, string name, bool fallback) =>
        bool.TryParse(root.Element(name)?.Value, out bool value) ? value : fallback;

    public static int ReadInt(XElement root, string name, int fallback) =>
        int.TryParse(root.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    public static double ReadDouble(XElement root, string name, double fallback) =>
        double.TryParse(root.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    public static string ReadString(XElement root, string name, string fallback)
    {
        XElement? element = root.Element(name);
        return element == null ? fallback : element.Value;
    }

    public static string? ReadNullableString(XElement root, string name, string? fallback)
    {
        XElement? element = root.Element(name);
        return element == null ? fallback : element.Value;
    }

    public static T ReadEnum<T>(XElement root, string name, T fallback)
        where T : struct, Enum =>
        System.Enum.TryParse(root.Element(name)?.Value, ignoreCase: false, out T value) ? value : fallback;

    public static NullableThemeColor ReadNullableThemeColor(XElement root, string name) =>
        ReadNullableThemeColor(root, name, new NullableThemeColor());

    public static NullableThemeColor ReadNullableThemeColor(
        XElement root,
        string name,
        NullableThemeColor fallback)
    {
        XElement? element = root.Element(name);
        if (element == null) return fallback;

        return new NullableThemeColor
        {
            LightHex = ReadThemeColorPart(element, nameof(NullableThemeColor.LightHex)),
            DarkHex = ReadThemeColorPart(element, nameof(NullableThemeColor.DarkHex)),
        };
    }

    public static string? ReadThemeColorPart(XElement element, string name)
    {
        string? value = (string?)element.Attribute(name) ?? element.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static SliderThumbGlyphOption? ReadSliderThumb(
        XElement? element,
        string defaultName = "Circle",
        string defaultGlyph = GlyphCatalog.SLIDER_THUMB_CIRCLE,
        string defaultFontFamily = GlyphCatalog.SEGOE_FLUENT_ICONS)
    {
        if (element == null) return null;

        return new SliderThumbGlyphOption
        {
            Name = ReadAttribute(element, nameof(SliderThumbGlyphOption.Name), defaultName),
            Glyph = ReadAttribute(element, nameof(SliderThumbGlyphOption.Glyph), defaultGlyph),
            FontFamily = ReadAttribute(element, nameof(SliderThumbGlyphOption.FontFamily), defaultFontFamily),
            FontSize = ReadDoubleAttribute(element, nameof(SliderThumbGlyphOption.FontSize), 18),
            Width = ReadDoubleAttribute(element, nameof(SliderThumbGlyphOption.Width), 18),
            Height = ReadDoubleAttribute(element, nameof(SliderThumbGlyphOption.Height), 18),
            XScale = ReadDoubleAttribute(element, nameof(SliderThumbGlyphOption.XScale), 1.0),
            Shape = ReadEnumAttribute(element, nameof(SliderThumbGlyphOption.Shape), SliderThumbShape.Glyph),
        };
    }

    public static List<HotkeyBinding> ReadHotkeys(XElement? element,
        HotkeyAction fallbackAction = HotkeyAction.OpenSettings) =>
        ReadHotkeys<HotkeyAction, HotkeyBinding>(
            element,
            fallbackAction,
            static (action, parameter, modifiers, virtualKey, enabled, bindingID, removedByUser) => new HotkeyBinding
            {
                Action = action,
                Parameter = parameter,
                Modifiers = modifiers,
                VirtualKey = virtualKey,
                Enabled = enabled,
                BindingID = bindingID,
                RemovedByUser = removedByUser,
            });

    public static List<THotkey> ReadHotkeys<TAction, THotkey>(
        XElement? element,
        TAction fallbackAction,
        Func<TAction, string, uint, uint, bool, int, bool, THotkey> create)
        where TAction : struct, Enum
    {
        List<THotkey> hotkeys = [];
        if (element == null) return hotkeys;

        foreach (XElement binding in element.Elements("Binding"))
        {
            hotkeys.Add(create(
                ReadEnumAttribute(binding, nameof(HotkeyBinding.Action), fallbackAction),
                ReadAttribute(binding, nameof(HotkeyBinding.Parameter), string.Empty),
                ReadUIntAttribute(binding, nameof(HotkeyBinding.Modifiers), 0),
                ReadUIntAttribute(binding, nameof(HotkeyBinding.VirtualKey), 0),
                ReadBoolAttribute(binding, nameof(HotkeyBinding.Enabled), true),
                ReadIntAttribute(binding, nameof(HotkeyBinding.BindingID), 0),
                ReadBoolAttribute(binding, nameof(HotkeyBinding.RemovedByUser), false)));
        }

        return hotkeys;
    }

    public static string ReadAttribute(XElement element, string name, string fallback) =>
        (string?)element.Attribute(name) ?? fallback;

    public static string? ReadNullableAttribute(XElement element, string name)
    {
        string? value = (string?)element.Attribute(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool ReadBoolAttribute(XElement element, string name, bool fallback) =>
        bool.TryParse((string?)element.Attribute(name), out bool value) ? value : fallback;

    public static int ReadIntAttribute(XElement element, string name, int fallback) =>
        int.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    public static int? TryReadIntAttribute(XElement element, string name) =>
        int.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int value)
            ? value
            : null;

    public static long ReadLongAttribute(XElement element, string name, long fallback) =>
        long.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long value)
            ? value
            : fallback;

    public static uint ReadUIntAttribute(XElement element, string name, uint fallback) =>
        uint.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out uint value)
            ? value
            : fallback;

    public static double ReadDoubleAttribute(XElement element, string name, double fallback) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture,
            out double value)
            ? value
            : fallback;

    public static DateTime ReadDateTimeAttribute(XElement element, string name, DateTime fallback) =>
        DateTime.TryParse(
            (string?)element.Attribute(name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTime value)
            ? value
            : fallback;

    public static T ReadEnumAttribute<T>(XElement element, string name, T fallback)
        where T : struct, Enum =>
        System.Enum.TryParse((string?)element.Attribute(name), ignoreCase: false, out T value) ? value : fallback;
}

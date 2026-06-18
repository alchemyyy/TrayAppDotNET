using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BrightnessTrayAppDotNET.Services;

internal static class ProfileXml
{
    public static ProfileCollection Load(Stream stream)
    {
        XDocument document = XDocument.Load(stream);
        XElement root = document.Root ?? throw new InvalidDataException("Missing BrightnessProfiles root.");
        if (root.Name != "BrightnessProfiles") throw new InvalidDataException("Unexpected profiles root.");

        ProfileCollection collection = new()
        {
            LastSelectedIndex = ReadIntAttribute(root, nameof(ProfileCollection.LastSelectedIndex), 0),
        };

        foreach (XElement profileElement in root.Elements("Profile"))
            collection.Profiles.Add(ReadProfile(profileElement));

        return collection;
    }

    public static void Save(ProfileCollection collection, Stream stream)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("BrightnessProfiles",
                new XAttribute(nameof(ProfileCollection.LastSelectedIndex),
                    XmlConvert.ToString(collection.LastSelectedIndex)),
                collection.Profiles.Select(ProfileElement)));

        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
        };

        using XmlWriter writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
        writer.Flush();
        if (stream is FileStream fileStream) fileStream.Flush(flushToDisk: true);
    }

    private static XElement ProfileElement(BrightnessProfile profile) =>
        new("Profile",
            new XAttribute(nameof(BrightnessProfile.Index), XmlConvert.ToString(profile.Index)),
            AttributeOrNull(nameof(BrightnessProfile.Name), profile.Name),
            AttributeOrNull(nameof(BrightnessProfile.CustomGlyph), profile.CustomGlyph),
            new XAttribute(nameof(BrightnessProfile.MasterSliderMode), profile.MasterSliderMode.ToString()),
            new XAttribute(nameof(BrightnessProfile.NightLight), XmlConvert.ToString(profile.NightLight)),
            MonitorStatesElement(profile.MonitorStates),
            EnvironmentalCurveElement(profile.EnvironmentalCurve));

    private static XElement MonitorStatesElement(IEnumerable<MonitorState> states) =>
        new("MonitorStates", states.Select(MonitorStateElement));

    private static XElement MonitorStateElement(MonitorState state)
    {
#pragma warning disable CS0618
        string legacyID = state.ID;
#pragma warning restore CS0618

        return new XElement("Monitor",
            AttributeOrNull("ID", string.IsNullOrEmpty(legacyID) ? null : legacyID),
            new XAttribute(nameof(MonitorState.EDIDKey), state.EDIDKey),
            new XAttribute(nameof(MonitorState.Brightness), XmlConvert.ToString(state.Brightness)),
            new XAttribute(nameof(MonitorState.IsPoweredOn), XmlConvert.ToString(state.IsPoweredOn)),
            new XAttribute(nameof(MonitorState.IsSliderEnabled), XmlConvert.ToString(state.IsSliderEnabled)));
    }

    private static XElement EnvironmentalCurveElement(EnvironmentalCurve curve) =>
        new("EnvironmentalCurve",
            new XAttribute(nameof(EnvironmentalCurve.BrightnessOffsetMin),
                XmlConvert.ToString(curve.BrightnessOffsetMin)),
            new XAttribute(nameof(EnvironmentalCurve.BrightnessOffsetMax),
                XmlConvert.ToString(curve.BrightnessOffsetMax)),
            new XAttribute(nameof(EnvironmentalCurve.NightLightOffsetMin),
                XmlConvert.ToString(curve.NightLightOffsetMin)),
            new XAttribute(nameof(EnvironmentalCurve.NightLightOffsetMax),
                XmlConvert.ToString(curve.NightLightOffsetMax)),
            new XAttribute(nameof(EnvironmentalCurve.FollowTheSun), XmlConvert.ToString(curve.FollowTheSun)),
            new XAttribute(nameof(EnvironmentalCurve.LastSunShiftDate), curve.LastSunShiftDate),
            new XAttribute(nameof(EnvironmentalCurve.LastSunShiftLatitude),
                XmlConvert.ToString(curve.LastSunShiftLatitude)),
            new XAttribute(nameof(EnvironmentalCurve.LastSunShiftLongitude),
                XmlConvert.ToString(curve.LastSunShiftLongitude)),
            new XAttribute(nameof(EnvironmentalCurve.LastSunShiftUseDaylightSavings),
                XmlConvert.ToString(curve.LastSunShiftUseDaylightSavings)),
            new XAttribute(nameof(EnvironmentalCurve.UseDaylightSavings),
                XmlConvert.ToString(curve.UseDaylightSavings)),
            new XAttribute(nameof(EnvironmentalCurve.DisabledPeriodEnabled),
                XmlConvert.ToString(curve.DisabledPeriodEnabled)),
            new XAttribute(nameof(EnvironmentalCurve.DisabledPeriodStart),
                XmlConvert.ToString(curve.DisabledPeriodStart)),
            new XAttribute(nameof(EnvironmentalCurve.DisabledPeriodEnd), XmlConvert.ToString(curve.DisabledPeriodEnd)),
            new XAttribute(nameof(EnvironmentalCurve.DisabledPeriodFollowTheSun),
                XmlConvert.ToString(curve.DisabledPeriodFollowTheSun)),
            new XAttribute(nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftDate),
                curve.LastDisabledPeriodSunShiftDate),
            new XAttribute(nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftLatitude),
                XmlConvert.ToString(curve.LastDisabledPeriodSunShiftLatitude)),
            new XAttribute(nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftLongitude),
                XmlConvert.ToString(curve.LastDisabledPeriodSunShiftLongitude)),
            new XAttribute(nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftUseDaylightSavings),
                XmlConvert.ToString(curve.LastDisabledPeriodSunShiftUseDaylightSavings)),
            PointsElement(nameof(EnvironmentalCurve.Brightness), curve.Brightness),
            PointsElement(nameof(EnvironmentalCurve.NightLight), curve.NightLight),
            PointsElement(nameof(EnvironmentalCurve.BrightnessOffset), curve.BrightnessOffset),
            PointsElement(nameof(EnvironmentalCurve.NightLightOffset), curve.NightLightOffset));

    private static XElement PointsElement(string name, IEnumerable<EnvironmentalCurvePoint> points) =>
        new(name, points.Select(point => new XElement("P",
            new XAttribute(nameof(EnvironmentalCurvePoint.Time), XmlConvert.ToString(point.Time)),
            new XAttribute(nameof(EnvironmentalCurvePoint.Value), XmlConvert.ToString(point.Value)))));

    private static BrightnessProfile ReadProfile(XElement element)
    {
        BrightnessProfile profile = new()
        {
            Index = ReadIntAttribute(element, nameof(BrightnessProfile.Index), 0),
            Name = ReadNullableAttribute(element, nameof(BrightnessProfile.Name)),
            CustomGlyph = ReadNullableAttribute(element, nameof(BrightnessProfile.CustomGlyph)),
            MasterSliderMode =
                ReadEnumAttribute(element, nameof(BrightnessProfile.MasterSliderMode), MasterSliderMode.Average),
            NightLight = ReadIntAttribute(element, nameof(BrightnessProfile.NightLight), 0),
            MonitorStates = ReadMonitorStates(element.Element("MonitorStates")),
            EnvironmentalCurve = ReadEnvironmentalCurve(element.Element("EnvironmentalCurve")),
        };
        profile.EnvironmentalCurve.EnsureNormalized();
        return profile;
    }

    private static List<MonitorState> ReadMonitorStates(XElement? element)
    {
        List<MonitorState> states = [];
        if (element == null) return states;

        foreach (XElement item in element.Elements("Monitor"))
        {
            MonitorState state = new()
            {
                EDIDKey = ReadAttribute(item, nameof(MonitorState.EDIDKey), string.Empty),
                Brightness = ReadIntAttribute(item, nameof(MonitorState.Brightness), 0),
                IsPoweredOn = ReadBoolAttribute(item, nameof(MonitorState.IsPoweredOn), true),
                IsSliderEnabled = ReadBoolAttribute(item, nameof(MonitorState.IsSliderEnabled), true),
            };
#pragma warning disable CS0618
            state.ID = ReadAttribute(item, nameof(MonitorState.ID), string.Empty);
#pragma warning restore CS0618
            states.Add(state);
        }

        return states;
    }

    private static EnvironmentalCurve ReadEnvironmentalCurve(XElement? element)
    {
        EnvironmentalCurve curve = new();
        if (element == null)
        {
            curve.EnsureNormalized();
            return curve;
        }

        curve.BrightnessOffsetMin = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.BrightnessOffsetMin),
            curve.BrightnessOffsetMin);
        curve.BrightnessOffsetMax = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.BrightnessOffsetMax),
            curve.BrightnessOffsetMax);
        curve.NightLightOffsetMin = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.NightLightOffsetMin),
            curve.NightLightOffsetMin);
        curve.NightLightOffsetMax = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.NightLightOffsetMax),
            curve.NightLightOffsetMax);
        curve.FollowTheSun = ReadBoolAttribute(element, nameof(EnvironmentalCurve.FollowTheSun), curve.FollowTheSun);
        curve.LastSunShiftDate =
            ReadAttribute(element, nameof(EnvironmentalCurve.LastSunShiftDate), curve.LastSunShiftDate);
        curve.LastSunShiftLatitude = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.LastSunShiftLatitude),
            curve.LastSunShiftLatitude);
        curve.LastSunShiftLongitude = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.LastSunShiftLongitude),
            curve.LastSunShiftLongitude);
        curve.LastSunShiftUseDaylightSavings = ReadBoolAttribute(element,
            nameof(EnvironmentalCurve.LastSunShiftUseDaylightSavings), curve.LastSunShiftUseDaylightSavings);
        curve.UseDaylightSavings = ReadBoolAttribute(element, nameof(EnvironmentalCurve.UseDaylightSavings),
            curve.UseDaylightSavings);
        curve.DisabledPeriodEnabled = ReadBoolAttribute(element, nameof(EnvironmentalCurve.DisabledPeriodEnabled),
            curve.DisabledPeriodEnabled);
        curve.DisabledPeriodStart = ReadDoubleAttribute(element, nameof(EnvironmentalCurve.DisabledPeriodStart),
            curve.DisabledPeriodStart);
        curve.DisabledPeriodEnd =
            ReadDoubleAttribute(element, nameof(EnvironmentalCurve.DisabledPeriodEnd), curve.DisabledPeriodEnd);
        curve.DisabledPeriodFollowTheSun = ReadBoolAttribute(element,
            nameof(EnvironmentalCurve.DisabledPeriodFollowTheSun), curve.DisabledPeriodFollowTheSun);
        curve.LastDisabledPeriodSunShiftDate = ReadAttribute(element,
            nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftDate), curve.LastDisabledPeriodSunShiftDate);
        curve.LastDisabledPeriodSunShiftLatitude = ReadDoubleAttribute(element,
            nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftLatitude), curve.LastDisabledPeriodSunShiftLatitude);
        curve.LastDisabledPeriodSunShiftLongitude = ReadDoubleAttribute(element,
            nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftLongitude), curve.LastDisabledPeriodSunShiftLongitude);
        curve.LastDisabledPeriodSunShiftUseDaylightSavings = ReadBoolAttribute(element,
            nameof(EnvironmentalCurve.LastDisabledPeriodSunShiftUseDaylightSavings),
            curve.LastDisabledPeriodSunShiftUseDaylightSavings);
        curve.Brightness = ReadPoints(element.Element(nameof(EnvironmentalCurve.Brightness)));
        curve.NightLight = ReadPoints(element.Element(nameof(EnvironmentalCurve.NightLight)));
        curve.BrightnessOffset = ReadPoints(element.Element(nameof(EnvironmentalCurve.BrightnessOffset)));
        curve.NightLightOffset = ReadPoints(element.Element(nameof(EnvironmentalCurve.NightLightOffset)));
        curve.EnsureNormalized();
        return curve;
    }

    private static List<EnvironmentalCurvePoint> ReadPoints(XElement? element)
    {
        List<EnvironmentalCurvePoint> points = [];
        if (element == null) return points;

        foreach (XElement item in element.Elements("P"))
        {
            points.Add(new EnvironmentalCurvePoint
            {
                Time = ReadDoubleAttribute(item, nameof(EnvironmentalCurvePoint.Time), 0),
                Value = ReadDoubleAttribute(item, nameof(EnvironmentalCurvePoint.Value), 0),
            });
        }

        return points;
    }

    private static XAttribute? AttributeOrNull(string name, string? value) =>
        string.IsNullOrEmpty(value) ? null : new XAttribute(name, value);

    private static string ReadAttribute(XElement element, string name, string fallback) =>
        (string?)element.Attribute(name) ?? fallback;

    private static string? ReadNullableAttribute(XElement element, string name) =>
        (string?)element.Attribute(name);

    private static bool ReadBoolAttribute(XElement element, string name, bool fallback) =>
        bool.TryParse((string?)element.Attribute(name), out bool value) ? value : fallback;

    private static int ReadIntAttribute(XElement element, string name, int fallback) =>
        int.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    private static double ReadDoubleAttribute(XElement element, string name, double fallback) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture,
            out double value)
            ? value
            : fallback;

    private static T ReadEnumAttribute<T>(XElement element, string name, T fallback)
        where T : struct, Enum =>
        Enum.TryParse((string?)element.Attribute(name), ignoreCase: false, out T value) ? value : fallback;
}

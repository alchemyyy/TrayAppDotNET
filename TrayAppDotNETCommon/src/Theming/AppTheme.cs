using System.Xml;
using System.Xml.Linq;
using Avalonia.Media;
using Microsoft.Win32;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Theming;

/// <summary>
/// XML-serializable light/dark color pair.
/// </summary>
public class ThemeColor
{
    public string LightHex { get; set; } = "#000000";
    public string DarkHex { get; set; } = "#000000";

    public Color Light => ParseHexStrict(LightHex);
    public Color Dark => ParseHexStrict(DarkHex);

    public Color For(bool isLightTheme) => isLightTheme ? Light : Dark;

    public ThemeColor() { }

    public ThemeColor(string lightHex, string darkHex)
    {
        LightHex = Normalize(lightHex);
        DarkHex = Normalize(darkHex);
    }

    public ThemeColor(string hex) : this(hex, hex) { }

    public ThemeColor(Color light, Color dark)
    {
        LightHex = ToHex(light);
        DarkHex = ToHex(dark);
    }

    private static string Normalize(string hex)
    {
        string normalized = hex.StartsWith('#') ? hex : "#" + hex;
        _ = ParseHexStrict(normalized);
        return normalized;
    }

    private static Color ParseHexStrict(string hex)
    {
        string hexString = hex.StartsWith('#') ? hex[1..] : hex;
        try
        {
            return hexString.Length switch
            {
                6 => Color.FromRgb(
                    Convert.ToByte(hexString[..2], 16),
                    Convert.ToByte(hexString[2..4], 16),
                    Convert.ToByte(hexString[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(hexString[..2], 16),
                    Convert.ToByte(hexString[2..4], 16),
                    Convert.ToByte(hexString[4..6], 16),
                    Convert.ToByte(hexString[6..8], 16)),
                _ => throw new FormatException($"Invalid color literal '{hex}'."),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FormatException($"Invalid color literal '{hex}'.", ex);
        }
    }

    private static string ToHex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>
/// Shared theme defaults, XML persistence, and system light/dark detection.
/// </summary>
public class AppTheme : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public static AppTheme Default { get; } = new();

    public string Name { get; set; } = "Default";
    public int Version { get; set; } = 1;

    public ThemeColor Background { get; set; } = new("F3F3F3", "202020");
    public ThemeColor Foreground { get; set; } = new("000000", "FFFFFF");
    public ThemeColor Border { get; set; } = new("E0E0E0", "454545");
    public ThemeColor Separator { get; set; } = new("E5E5E5", "3A3A3A");
    public ThemeColor Hover { get; set; } = new("E9E9E9", "333333");
    public ThemeColor Pressed { get; set; } = new("DFDFDF", "2A2A2A");
    public ThemeColor ControlBackground { get; set; } = new("FFFFFF", "3C3C3C");
    public ThemeColor ControlBorder { get; set; } = new("808080", "444444");
    public ThemeColor DisabledForeground { get; set; } = new("808080");
    public ThemeColor Accent { get; set; } = new("0078D4");
    public ThemeColor Acrylic { get; set; } = new("D0F3F3F3", "D0202020");
    public ThemeColor SecondaryForeground { get; set; } = new("222222", "DDDDDD");
    public ThemeColor FooterBackground { get; set; } = new("E8E8E8", "1A1A1A");
    public ThemeColor SliderTrack { get; set; } = new("C0C0C0", "3A3A3A");
    public ThemeColor SliderProgress { get; set; } = new("606060", "6A6A6A");
    public ThemeColor SliderThumb { get; set; } = new("404040", "F0F0F0");
    public ThemeColor ButtonHover { get; set; } = new("D5D5D5", "3A3A3A");
    public ThemeColor ButtonPressed { get; set; } = new("CACACA", "4A4A4A");
    public ThemeColor IconForeground { get; set; } = new("222222", "DDDDDD");
    public ThemeColor CardBackground { get; set; } = new("FBFBFB", "2B2B2B");
    public ThemeColor TextBoxFocused { get; set; } = new("F5F5F5", "363636");
    public ThemeColor ToggleSwitchOnTrack { get; set; } = new("5B5B5B");
    public ThemeColor ToggleSwitchOnThumb { get; set; } = new("FFFFFF");
    public ThemeColor CloseButtonHover { get; set; } = new("C42B1C");
    public ThemeColor CloseButtonPressed { get; set; } = new("A42B1C");
    public ThemeColor CloseButtonGlyphActive { get; set; } = new("FFFFFF");
    public ThemeColor FlyoutOverlayBackdrop { get; set; } = new("A0000000");
    public ThemeColor MenuShadow { get; set; } = new("80000000");

    public string GlyphSettings { get; set; } = GlyphCatalog.SETTINGS;
    public string GlyphPower { get; set; } = GlyphCatalog.POWER;
    public string GlyphInfo { get; set; } = GlyphCatalog.INFO;
    public string GlyphExit { get; set; } = GlyphCatalog.EXIT;

    public bool IsLightTheme { get; private set; }

    public event Action<bool>? ThemeChanged;

    public AppTheme()
    {
        IsLightTheme = DetectSystemLightTheme();
        _lastKnownIsLightTheme = IsLightTheme;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static TTheme LoadOrDefault<TTheme>(string filePath)
        where TTheme : AppTheme, new()
    {
        try
        {
            if (File.Exists(filePath)) return Load<TTheme>(filePath);
        }
        catch
        {
            // fall through and return default on any load error
        }

        return new TTheme();
    }

    public static TTheme Load<TTheme>(string filePath)
        where TTheme : AppTheme, new()
    {
        XDocument document = XDocument.Load(filePath);
        XElement root = document.Root ?? throw new InvalidDataException("Missing Theme root.");
        if (root.Name != "Theme") throw new InvalidDataException("Unexpected theme root.");

        TTheme theme = new();
        theme.ReadCoreXml(root);
        theme.ReadAdditionalXml(root);
        return theme;
    }

    public void Save(string filePath)
    {
        XmlWriterSettings writerSettings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
        };

        using FileStream stream = new(filePath, FileMode.Create);
        using XmlWriter writer = XmlWriter.Create(stream, writerSettings);
        ToXml().Save(writer);
    }

    protected virtual void ReadAdditionalXml(XElement root) { }

    protected virtual IEnumerable<XElement> AdditionalXmlElements()
    {
        yield break;
    }

    private XDocument ToXml()
    {
        XElement root = new("Theme",
            new XAttribute(nameof(Name), Name),
            new XAttribute(nameof(Version), Version),
            ThemeColorElement(nameof(Background), Background),
            ThemeColorElement(nameof(Foreground), Foreground),
            ThemeColorElement(nameof(Border), Border),
            ThemeColorElement(nameof(Separator), Separator),
            ThemeColorElement(nameof(Hover), Hover),
            ThemeColorElement(nameof(Pressed), Pressed),
            ThemeColorElement(nameof(ControlBackground), ControlBackground),
            ThemeColorElement(nameof(ControlBorder), ControlBorder),
            ThemeColorElement(nameof(DisabledForeground), DisabledForeground),
            ThemeColorElement(nameof(Accent), Accent),
            ThemeColorElement(nameof(Acrylic), Acrylic),
            ThemeColorElement(nameof(SecondaryForeground), SecondaryForeground),
            ThemeColorElement(nameof(FooterBackground), FooterBackground),
            ThemeColorElement(nameof(SliderTrack), SliderTrack),
            ThemeColorElement(nameof(SliderProgress), SliderProgress),
            ThemeColorElement(nameof(SliderThumb), SliderThumb),
            ThemeColorElement(nameof(ButtonHover), ButtonHover),
            ThemeColorElement(nameof(ButtonPressed), ButtonPressed),
            ThemeColorElement(nameof(IconForeground), IconForeground),
            ThemeColorElement(nameof(CardBackground), CardBackground),
            ThemeColorElement(nameof(TextBoxFocused), TextBoxFocused),
            ThemeColorElement(nameof(ToggleSwitchOnTrack), ToggleSwitchOnTrack),
            ThemeColorElement(nameof(ToggleSwitchOnThumb), ToggleSwitchOnThumb),
            ThemeColorElement(nameof(CloseButtonHover), CloseButtonHover),
            ThemeColorElement(nameof(CloseButtonPressed), CloseButtonPressed),
            ThemeColorElement(nameof(CloseButtonGlyphActive), CloseButtonGlyphActive),
            ThemeColorElement(nameof(FlyoutOverlayBackdrop), FlyoutOverlayBackdrop),
            ThemeColorElement(nameof(MenuShadow), MenuShadow),
            new XElement(nameof(GlyphSettings), GlyphSettings),
            new XElement(nameof(GlyphPower), GlyphPower),
            new XElement(nameof(GlyphInfo), GlyphInfo),
            new XElement(nameof(GlyphExit), GlyphExit),
            AdditionalXmlElements());

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private void ReadCoreXml(XElement root)
    {
        Name = (string?)root.Attribute(nameof(Name)) ?? "Default";
        Version = ReadIntAttribute(root, nameof(Version), 1);

        Background = ReadThemeColor(root, nameof(Background), Background);
        Foreground = ReadThemeColor(root, nameof(Foreground), Foreground);
        Border = ReadThemeColor(root, nameof(Border), Border);
        Separator = ReadThemeColor(root, nameof(Separator), Separator);
        Hover = ReadThemeColor(root, nameof(Hover), Hover);
        Pressed = ReadThemeColor(root, nameof(Pressed), Pressed);
        ControlBackground = ReadThemeColor(root, nameof(ControlBackground), ControlBackground);
        ControlBorder = ReadThemeColor(root, nameof(ControlBorder), ControlBorder);
        DisabledForeground = ReadThemeColor(root, nameof(DisabledForeground), DisabledForeground);
        Accent = ReadThemeColor(root, nameof(Accent), Accent);
        Acrylic = ReadThemeColor(root, nameof(Acrylic), Acrylic);
        SecondaryForeground = ReadThemeColor(root, nameof(SecondaryForeground), SecondaryForeground);
        FooterBackground = ReadThemeColor(root, nameof(FooterBackground), FooterBackground);
        SliderTrack = ReadThemeColor(root, nameof(SliderTrack), SliderTrack);
        SliderProgress = ReadThemeColor(root, nameof(SliderProgress), SliderProgress);
        SliderThumb = ReadThemeColor(root, nameof(SliderThumb), SliderThumb);
        ButtonHover = ReadThemeColor(root, nameof(ButtonHover), ButtonHover);
        ButtonPressed = ReadThemeColor(root, nameof(ButtonPressed), ButtonPressed);
        IconForeground = ReadThemeColor(root, nameof(IconForeground), IconForeground);
        CardBackground = ReadThemeColor(root, nameof(CardBackground), CardBackground);
        TextBoxFocused = ReadThemeColor(root, nameof(TextBoxFocused), TextBoxFocused);
        ToggleSwitchOnTrack = ReadThemeColor(root, nameof(ToggleSwitchOnTrack), ToggleSwitchOnTrack);
        ToggleSwitchOnThumb = ReadThemeColor(root, nameof(ToggleSwitchOnThumb), ToggleSwitchOnThumb);
        CloseButtonHover = ReadThemeColor(root, nameof(CloseButtonHover), CloseButtonHover);
        CloseButtonPressed = ReadThemeColor(root, nameof(CloseButtonPressed), CloseButtonPressed);
        CloseButtonGlyphActive = ReadThemeColor(root, nameof(CloseButtonGlyphActive), CloseButtonGlyphActive);
        FlyoutOverlayBackdrop = ReadThemeColor(root, nameof(FlyoutOverlayBackdrop), FlyoutOverlayBackdrop);
        MenuShadow = ReadThemeColor(root, nameof(MenuShadow), MenuShadow);

        GlyphSettings = ReadString(root, nameof(GlyphSettings), GlyphCatalog.SETTINGS);
        GlyphPower = ReadString(root, nameof(GlyphPower), GlyphCatalog.POWER);
        GlyphInfo = ReadString(root, nameof(GlyphInfo), GlyphCatalog.INFO);
        GlyphExit = ReadString(root, nameof(GlyphExit), GlyphCatalog.EXIT);
    }

    protected static XElement ThemeColorElement(string name, ThemeColor color) =>
        new(name,
            new XAttribute(nameof(ThemeColor.LightHex), color.LightHex),
            new XAttribute(nameof(ThemeColor.DarkHex), color.DarkHex));

    protected static ThemeColor ReadThemeColor(XElement root, string name, ThemeColor fallback)
    {
        XElement? element = root.Element(name);
        if (element == null) return fallback;

        string light = (string?)element.Attribute(nameof(ThemeColor.LightHex)) ?? fallback.LightHex;
        string dark = (string?)element.Attribute(nameof(ThemeColor.DarkHex)) ?? fallback.DarkHex;
        try { return new ThemeColor(light, dark); }
        catch { return fallback; }
    }

    protected static string ReadString(XElement root, string name, string fallback)
    {
        string? value = root.Element(name)?.Value;
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static int ReadIntAttribute(XElement element, string name, int fallback) =>
        int.TryParse(element.Attribute(name)?.Value, out int value) ? value : fallback;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        bool newIsLightTheme = DetectSystemLightTheme();
        if (newIsLightTheme == _lastKnownIsLightTheme) return;

        _lastKnownIsLightTheme = newIsLightTheme;
        IsLightTheme = newIsLightTheme;
        ThemeChanged?.Invoke(newIsLightTheme);
    }

    private static bool DetectSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("SystemUsesLightTheme");
            return value is 1;
        }
        catch
        {
            return false;
        }
    }

    public static bool DetectAppsLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is 1;
        }
        catch
        {
            return false;
        }
    }

    public Color ResolveForeground(AppSettingsCommon? settings, bool isLightTheme)
    {
        if (settings?.TextColor.Resolve(isLightTheme) is { } color) return color;
        return Foreground.For(isLightTheme);
    }

    public Color ResolveBackground(AppSettingsCommon? settings, bool isLightTheme)
    {
        if (settings?.BackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return Background.For(isLightTheme);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        GC.SuppressFinalize(this);
    }
}

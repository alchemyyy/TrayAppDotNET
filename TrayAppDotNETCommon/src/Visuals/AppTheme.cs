using System.Xml.Serialization;
using Avalonia.Media;
using Microsoft.Win32;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// XML-serializable light/dark color pair.
/// </summary>
public class ThemeColor
{
    private string _lightHex = "#000000";
    private string _darkHex = "#000000";

    [XmlAttribute]
    public string LightHex
    {
        get => _lightHex;
        set => _lightHex = Normalize(value);
    }

    [XmlAttribute]
    public string DarkHex
    {
        get => _darkHex;
        set => _darkHex = Normalize(value);
    }

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

    public static readonly Color ColorPickerDefaultColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    public static readonly Color ColorPickerBlack = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    public static readonly Color ColorPickerWhite = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly Color ColorPickerTransparentBlack = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
    public static readonly Color ColorPickerTransparentWhite = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
    public static readonly Color ColorPickerHueRed = Color.FromArgb(0xFF, 0xFF, 0x00, 0x00);
    public static readonly Color ColorPickerHueMagenta = Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF);
    public static readonly Color ColorPickerHueBlue = Color.FromArgb(0xFF, 0x00, 0x00, 0xFF);
    public static readonly Color ColorPickerHueCyan = Color.FromArgb(0xFF, 0x00, 0xFF, 0xFF);
    public static readonly Color ColorPickerHueLime = Color.FromArgb(0xFF, 0x00, 0xFF, 0x00);
    public static readonly Color ColorPickerHueYellow = Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00);
    public const byte TextSelectionHighlightAlpha = 0x66;

    public static Color ResolveTextSelectionHighlight(Color accent) =>
        Color.FromArgb(TextSelectionHighlightAlpha, accent.R, accent.G, accent.B);

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public static AppTheme Default { get; } = new();

    [XmlAttribute]
    public string Name { get; set; } = "Default";

    [XmlAttribute]
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
    public ThemeColor FlyoutShadow { get; set; } = new("99000000");
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
        => TrayXmlSerializer.LoadFileOrDefault(filePath, static () => new TTheme());

    public static TTheme Load<TTheme>(string filePath)
        where TTheme : AppTheme, new()
        => TrayXmlSerializer.ReadFile<TTheme>(filePath);

    public void Save(string filePath) => TrayXmlSerializer.WriteFile(filePath, this);

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

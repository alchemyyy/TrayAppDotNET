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
    [XmlAttribute]
    public string LightHex
    {
        get;
        set => field = Normalize(value);
    } = "#000000";

    [XmlAttribute]
    public string DarkHex
    {
        get;
        set => field = Normalize(value);
    } = "#000000";

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
                _ => throw new FormatException($"Invalid color literal '{hex}'.")
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

internal static class AppThemeColorCatalog
{
#if DEBUG
    private static readonly AppThemeHotReloadStore<AppThemeResources> Resources =
        AppThemeHotReloadStore<AppThemeResources>.Create(
            "Common",
            static () => new AppThemeResources());
#else
    private static readonly Lazy<AppThemeResources> Resources = new(static () => new AppThemeResources());
#endif

    /// <summary>Gets a common theme color from the active resource dictionary.</summary>
    public static ThemeColor Color(string name)
    {
#if DEBUG
        return Resources.Current.Color(name);
#else
        return Resources.Value.Color(name);
#endif
    }

    /// <summary>Gets a single common color from the active resource dictionary.</summary>
    public static Color SingleColor(string name)
    {
#if DEBUG
        return Resources.Current.SingleColor(name);
#else
        return Resources.Value.SingleColor(name);
#endif
    }
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
    public static byte TextSelectionHighlightAlpha =>
        AppThemeColorCatalog.SingleColor(nameof(TextSelectionHighlightAlpha)).A;

    public static Color ResolveTextSelectionHighlight(Color accent) =>
        Color.FromArgb(TextSelectionHighlightAlpha, accent.R, accent.G, accent.B);

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public static AppTheme Default { get; } = new();

    [XmlAttribute]
    public string Name { get; set; } = "Default";

    [XmlAttribute]
    public int Version { get; set; } = 1;

    public ThemeColor Background { get; set; } = AppThemeColorCatalog.Color(nameof(Background));
    public ThemeColor Foreground { get; set; } = AppThemeColorCatalog.Color(nameof(Foreground));
    public ThemeColor Border { get; set; } = AppThemeColorCatalog.Color(nameof(Border));
    public ThemeColor Separator { get; set; } = AppThemeColorCatalog.Color(nameof(Separator));
    public ThemeColor Hover { get; set; } = AppThemeColorCatalog.Color(nameof(Hover));
    public ThemeColor HoverDeep { get; set; } = AppThemeColorCatalog.Color(nameof(HoverDeep));
    public ThemeColor Pressed { get; set; } = AppThemeColorCatalog.Color(nameof(Pressed));
    public ThemeColor PressedDeep { get; set; } = AppThemeColorCatalog.Color(nameof(PressedDeep));
    public ThemeColor ControlBackground { get; set; } = AppThemeColorCatalog.Color(nameof(ControlBackground));
    public ThemeColor ControlBackgroundDeep { get; set; } =
        AppThemeColorCatalog.Color(nameof(ControlBackgroundDeep));
    public ThemeColor ControlBorder { get; set; } = AppThemeColorCatalog.Color(nameof(ControlBorder));
    public ThemeColor DisabledForeground { get; set; } = AppThemeColorCatalog.Color(nameof(DisabledForeground));
    public ThemeColor Accent { get; set; } = AppThemeColorCatalog.Color(nameof(Accent));
    public ThemeColor Acrylic { get; set; } = AppThemeColorCatalog.Color(nameof(Acrylic));
    public ThemeColor SecondaryForeground { get; set; } =
        AppThemeColorCatalog.Color(nameof(SecondaryForeground));
    public ThemeColor FooterBackground { get; set; } = AppThemeColorCatalog.Color(nameof(FooterBackground));
    public ThemeColor SliderTrack { get; set; } = AppThemeColorCatalog.Color(nameof(SliderTrack));
    public ThemeColor SliderProgress { get; set; } = AppThemeColorCatalog.Color(nameof(SliderProgress));
    public ThemeColor SliderThumb { get; set; } = AppThemeColorCatalog.Color(nameof(SliderThumb));
    public ThemeColor ButtonHover { get; set; } = AppThemeColorCatalog.Color(nameof(ButtonHover));
    public ThemeColor ButtonPressed { get; set; } = AppThemeColorCatalog.Color(nameof(ButtonPressed));
    public ThemeColor IconForeground { get; set; } = AppThemeColorCatalog.Color(nameof(IconForeground));
    public ThemeColor CardBackground { get; set; } = AppThemeColorCatalog.Color(nameof(CardBackground));
    public ThemeColor TextBoxFocused { get; set; } = AppThemeColorCatalog.Color(nameof(TextBoxFocused));
    public ThemeColor SearchListItemSelected { get; set; } =
        AppThemeColorCatalog.Color(nameof(SearchListItemSelected));
    public ThemeColor SearchListItemHover { get; set; } =
        AppThemeColorCatalog.Color(nameof(SearchListItemHover));
    public ThemeColor ToggleSwitchOnTrack { get; set; } =
        AppThemeColorCatalog.Color(nameof(ToggleSwitchOnTrack));
    public ThemeColor ToggleSwitchOnThumb { get; set; } =
        AppThemeColorCatalog.Color(nameof(ToggleSwitchOnThumb));
    public ThemeColor CloseButtonHover { get; set; } = AppThemeColorCatalog.Color(nameof(CloseButtonHover));
    public ThemeColor CloseButtonPressed { get; set; } = AppThemeColorCatalog.Color(nameof(CloseButtonPressed));
    public ThemeColor CloseButtonGlyphActive { get; set; } =
        AppThemeColorCatalog.Color(nameof(CloseButtonGlyphActive));
    public ThemeColor FlyoutOverlayBackdrop { get; set; } =
        AppThemeColorCatalog.Color(nameof(FlyoutOverlayBackdrop));
    public ThemeColor FlyoutShadow { get; set; } = AppThemeColorCatalog.Color(nameof(FlyoutShadow));
    public ThemeColor MenuShadow { get; set; } = AppThemeColorCatalog.Color(nameof(MenuShadow));

    public string GlyphSettings { get; set; } = GlyphCatalog.SETTINGS.Text;
    public string GlyphPower { get; set; } = GlyphCatalog.POWER.Text;
    public string GlyphInfo { get; set; } = GlyphCatalog.INFO.Text;
    public string GlyphExit { get; set; } = GlyphCatalog.EXIT.Text;

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

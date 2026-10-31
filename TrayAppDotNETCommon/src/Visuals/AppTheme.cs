using System.Xml.Serialization;
using Avalonia.Media;
using Microsoft.Win32;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using TrayAppDotNETCommon.Utils;

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

    public Color Light => ColorMath.ParseHexStrict(LightHex);
    public Color Dark => ColorMath.ParseHexStrict(DarkHex);

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
        _ = ColorMath.ParseHexStrict(normalized);
        return normalized;
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

    public static readonly Color ColorPickerDefaultColor = Color.FromArgb(a: 0xFF, r: 0x00, g: 0x00, b: 0x00);
    public static readonly Color ColorPickerBlack = Color.FromArgb(a: 0xFF, r: 0x00, g: 0x00, b: 0x00);
    public static readonly Color ColorPickerWhite = Color.FromArgb(a: 0xFF, r: 0xFF, g: 0xFF, b: 0xFF);
    public static readonly Color ColorPickerTransparentBlack = Color.FromArgb(a: 0x00, r: 0x00, g: 0x00, b: 0x00);
    public static readonly Color ColorPickerTransparentWhite = Color.FromArgb(a: 0x00, r: 0xFF, g: 0xFF, b: 0xFF);
    public static readonly Color ColorPickerHueRed = Color.FromArgb(a: 0xFF, r: 0xFF, g: 0x00, b: 0x00);
    public static readonly Color ColorPickerHueMagenta = Color.FromArgb(a: 0xFF, r: 0xFF, g: 0x00, b: 0xFF);
    public static readonly Color ColorPickerHueBlue = Color.FromArgb(a: 0xFF, r: 0x00, g: 0x00, b: 0xFF);
    public static readonly Color ColorPickerHueCyan = Color.FromArgb(a: 0xFF, r: 0x00, g: 0xFF, b: 0xFF);
    public static readonly Color ColorPickerHueLime = Color.FromArgb(a: 0xFF, r: 0x00, g: 0xFF, b: 0x00);
    public static readonly Color ColorPickerHueYellow = Color.FromArgb(a: 0xFF, r: 0xFF, g: 0xFF, b: 0x00);

    public static byte TextSelectionHighlightAlpha =>
        ThemeDefaults.TextSelectionHighlightAlpha.A;

    public static Color ResolveTextSelectionHighlight(Color accent) =>
        Color.FromArgb(TextSelectionHighlightAlpha, accent.R, accent.G, accent.B);

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.AppThemeAxamlProperties ThemeDefaults => AppThemeResources.Current.AxamlAppTheme;

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public static AppTheme Default { get; } = new();

    [XmlAttribute]
    public string Name { get; set; } = "Default";

    [XmlAttribute]
    public int Version { get; set; } = 1;

    public ThemeColor Background { get; set; } = ThemeDefaults.Background;
    public ThemeColor Foreground { get; set; } = ThemeDefaults.Foreground;
    public ThemeColor Border { get; set; } = ThemeDefaults.Border;
    public ThemeColor Separator { get; set; } = ThemeDefaults.Separator;
    public ThemeColor Hover { get; set; } = ThemeDefaults.Hover;
    public ThemeColor HoverDeep { get; set; } = ThemeDefaults.HoverDeep;
    public ThemeColor Pressed { get; set; } = ThemeDefaults.Pressed;
    public ThemeColor PressedDeep { get; set; } = ThemeDefaults.PressedDeep;
    public ThemeColor ControlBackground { get; set; } = ThemeDefaults.ControlBackground;

    public ThemeColor ControlBackgroundDeep { get; set; } =
        ThemeDefaults.ControlBackgroundDeep;

    public ThemeColor ControlBorder { get; set; } = ThemeDefaults.ControlBorder;
    public ThemeColor DisabledForeground { get; set; } = ThemeDefaults.DisabledForeground;
    public ThemeColor Accent { get; set; } = ThemeDefaults.Accent;
    public ThemeColor Acrylic { get; set; } = ThemeDefaults.Acrylic;

    public ThemeColor SecondaryForeground { get; set; } =
        ThemeDefaults.SecondaryForeground;

    public ThemeColor FooterBackground { get; set; } = ThemeDefaults.FooterBackground;
    public ThemeColor SliderTrack { get; set; } = ThemeDefaults.SliderTrack;
    public ThemeColor SliderProgress { get; set; } = ThemeDefaults.SliderProgress;
    public ThemeColor SliderThumb { get; set; } = ThemeDefaults.SliderThumb;
    public ThemeColor ButtonHover { get; set; } = ThemeDefaults.ButtonHover;
    public ThemeColor ButtonPressed { get; set; } = ThemeDefaults.ButtonPressed;
    public ThemeColor IconForeground { get; set; } = ThemeDefaults.IconForeground;
    public ThemeColor CardBackground { get; set; } = ThemeDefaults.CardBackground;
    public ThemeColor TextBoxFocused { get; set; } = ThemeDefaults.TextBoxFocused;

    public ThemeColor SearchListItemSelected { get; set; } =
        ThemeDefaults.SearchListItemSelected;

    public ThemeColor SearchListItemHover { get; set; } =
        ThemeDefaults.SearchListItemHover;

    public ThemeColor ToggleSwitchOnTrack { get; set; } =
        ThemeDefaults.ToggleSwitchOnTrack;

    public ThemeColor ToggleSwitchOnThumb { get; set; } =
        ThemeDefaults.ToggleSwitchOnThumb;

    public ThemeColor CloseButtonHover { get; set; } = ThemeDefaults.CloseButtonHover;
    public ThemeColor CloseButtonPressed { get; set; } = ThemeDefaults.CloseButtonPressed;

    public ThemeColor CloseButtonGlyphActive { get; set; } =
        ThemeDefaults.CloseButtonGlyphActive;

    public ThemeColor FlyoutOverlayBackdrop { get; set; } =
        ThemeDefaults.FlyoutOverlayBackdrop;

    public ThemeColor FlyoutShadow { get; set; } = ThemeDefaults.FlyoutShadow;
    public ThemeColor MenuShadow { get; set; } = ThemeDefaults.MenuShadow;

    public string GlyphSettings { get; set; } = GlyphCatalog.SETTINGS.Text;
    public string GlyphPower { get; set; } = GlyphCatalog.POWER.Text;

    public bool IsLightTheme { get; private set; }

    public event Action<bool>? ThemeChanged;

    public AppTheme()
    {
        IsLightTheme = DetectSystemLightTheme();
        _lastKnownIsLightTheme = IsLightTheme;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Loads a user-authored <c>theme.xml</c>, or the defaults when it is missing or unreadable. Failures are logged
    /// through <paramref name="log"/>; the file is never moved or rewritten because the app does not own its content.
    /// </summary>
    public static TTheme LoadOrDefault<TTheme>(string filePath, Action<string> log)
        where TTheme : AppTheme, new()
    {
        DataFileLoadResult result = TrayXmlSerializer.TryLoadFile(filePath, out TTheme? theme, log);
        return result == DataFileLoadResult.Loaded ? theme! : new TTheme();
    }

    public static TTheme Load<TTheme>(string filePath)
        where TTheme : AppTheme, new()
        => TrayXmlSerializer.ReadFile<TTheme>(filePath);

    /// <summary>
    /// The one light/dark rule for every app and window. <see cref="TrayAppDotNETThemeMode.Light"/> and
    /// <see cref="TrayAppDotNETThemeMode.Dark"/> force the mode. <see cref="TrayAppDotNETThemeMode.System"/> follows
    /// the Windows app theme through <paramref name="theme"/>; when no theme is loaded yet the fallback is
    /// <see cref="Default"/>, which tracks the same Windows setting, so a missing theme never forces dark or light.
    /// </summary>
    public static bool ResolveIsLight(TrayAppDotNETThemeMode mode, AppTheme? theme) => mode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => (theme ?? Default).IsLightTheme
    };

    /// <inheritdoc cref="ResolveIsLight(TrayAppDotNETThemeMode, AppTheme?)"/>
    public static bool ResolveIsLight(AppSettingsCommon? settings, AppTheme? theme) =>
        ResolveIsLight(settings?.ThemeMode ?? TrayAppDotNETThemeMode.System, theme);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        ApplySystemLightTheme(DetectSystemLightTheme());
    }

    /// <summary>
    /// Records the Windows mode (the shell's light or dark setting, <c>SystemUsesLightTheme</c>, which the taskbar
    /// and tray follow) and raises <see cref="ThemeChanged"/> when it changed.
    /// </summary>
    internal void ApplySystemLightTheme(bool isLightTheme)
    {
        if (isLightTheme == _lastKnownIsLightTheme) return;

        _lastKnownIsLightTheme = isLightTheme;
        IsLightTheme = isLightTheme;
        ThemeChanged?.Invoke(isLightTheme);
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

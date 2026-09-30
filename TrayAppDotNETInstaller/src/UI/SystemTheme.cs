using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TrayAppDotNETInstaller.UI;

/// <summary>
/// Matches the installer to the operating system appearance: the light or dark palette declared in
/// Theme.xaml, the real Windows accent colour, a dark title bar and rounded window corners.
/// </summary>
internal static class SystemTheme
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";
    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string AccentPaletteValueName = "AccentPalette";
    private const string DWMKeyPath = @"Software\Microsoft\Windows\DWM";
    private const string AccentColorValueName = "AccentColor";

    // AccentPalette holds eight RGBA entries ordered lightest to darkest: Light3, Light2, Light1, the base
    // accent, Dark1, Dark2, Dark3 and one reserved entry. Windows fills its accent controls with Light2 on a
    // dark background and with Dark1 on a light one, never with the base accent, which has too little
    // contrast against the page in either theme. The stored alpha byte is zero and must be ignored.
    private const int AccentPaletteEntrySize = 4;
    private const int AccentPaletteEntryCount = 8;
    private const int AccentPaletteLight2Index = 1;
    private const int AccentPaletteDark1Index = 4;
    private const int RedOffset = 0;
    private const int GreenOffset = 1;
    private const int BlueOffset = 2;

    // The DWM AccentColor fallback is a DWORD laid out as 0xAABBGGRR
    private const int ByteMask = 0xFF;
    private const int GreenShift = 8;
    private const int BlueShift = 16;

    // Resource keys. The palettes are declared twice in Theme.xaml under these two prefixes, and the styles
    // bind to the unprefixed brush keys this class writes.
    private const string ThemeKeyPrefix = "InstallerTheme.";
    private const string LightPaletteKeyPrefix = "InstallerTheme.Light.";
    private const string DarkPaletteKeyPrefix = "InstallerTheme.Dark.";
    private const string ColorKeySuffix = "Color";
    private const string BrushKeySuffix = "Brush";
    private const string AccentFallbackColorKey = "InstallerTheme.AccentFallbackColor";
    private const string AccentHoverOpacityKey = "InstallerTheme.AccentHoverOpacity";
    private const string AccentPressedOpacityKey = "InstallerTheme.AccentPressedOpacity";
    private const string AccentName = "Accent";
    private const string AccentHoverName = "AccentHover";
    private const string AccentPressedName = "AccentPressed";

    // DWM window attributes
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int HResultOK = 0;

    /// <summary>The colour names declared under both palette prefixes in Theme.xaml.</summary>
    private static readonly string[] PaletteColorNames =
    [
        "WindowBackground",
        "PrimaryText",
        "SecondaryText",
        "DisabledText",
        "ControlBackground",
        "ControlHoverBackground",
        "ControlPressedBackground",
        "ControlDisabledBackground",
        "ControlBorder",
        "ControlStrongBorder",
        "ControlDisabledBorder",
        "ProgressTrack",
        "TextOnAccent",
        "CautionBackground",
        "CautionBorder",
        "InformationBackground",
        "InformationBorder",
        "SuccessText",
        "CriticalText"
    ];

    private static ResourceDictionary? _appliedPalette;

    /// <summary>
    /// Resolves the operating system appearance into brushes and merges them into the application
    /// resources, replacing any palette merged by an earlier call.
    /// </summary>
    public static void ApplyPalette(ResourceDictionary applicationResources)
    {
        FrameworkCompatibility.ThrowIfNull(applicationResources, nameof(applicationResources));

        bool darkTheme = IsDarkTheme();
        string palettePrefix = darkTheme ? DarkPaletteKeyPrefix : LightPaletteKeyPrefix;
        ResourceDictionary palette = new();
        foreach (string colorName in PaletteColorNames)
        {
            Color color = RequireColor(applicationResources, palettePrefix + colorName + ColorKeySuffix);
            palette[ThemeKeyPrefix + colorName + BrushKeySuffix] = CreateBrush(color);
        }

        Color accentColor = ResolveAccentColor(darkTheme, RequireColor(applicationResources, AccentFallbackColorKey));
        double hoverOpacity = RequireDouble(applicationResources, AccentHoverOpacityKey);
        double pressedOpacity = RequireDouble(applicationResources, AccentPressedOpacityKey);
        palette[ThemeKeyPrefix + AccentName + BrushKeySuffix] = CreateBrush(accentColor);
        palette[ThemeKeyPrefix + AccentHoverName + BrushKeySuffix] =
            CreateBrush(WithOpacity(accentColor, hoverOpacity));
        palette[ThemeKeyPrefix + AccentPressedName + BrushKeySuffix] =
            CreateBrush(WithOpacity(accentColor, pressedOpacity));

        if (_appliedPalette != null) applicationResources.MergedDictionaries.Remove(_appliedPalette);
        applicationResources.MergedDictionaries.Add(palette);
        _appliedPalette = palette;
        InstallerLog.Write(
            $"SystemTheme: applied the {(darkTheme ? "dark" : "light")} palette with accent {accentColor}");
    }

    /// <summary>True when Windows renders application surfaces dark. A missing value means the light default.</summary>
    public static bool IsDarkTheme()
    {
        try
        {
            using RegistryKey? personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (personalizeKey?.GetValue(AppsUseLightThemeValueName) is int appsUseLightTheme)
                return appsUseLightTheme == 0;

            return false;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            InstallerLog.Write("SystemTheme.IsDarkTheme", exception);
            return false;
        }
    }

    /// <summary>
    /// The colour Windows paints its accent-filled controls with for the given appearance, taken from the
    /// shade palette Windows computed for the user's accent.
    /// </summary>
    public static Color ResolveAccentColor(bool darkTheme, Color fallbackColor)
    {
        Color? paletteAccent = ReadAccentPaletteShade(darkTheme);
        if (paletteAccent != null) return paletteAccent.Value;

        Color? baseAccent = ReadBaseAccentColor();
        if (baseAccent != null) return baseAccent.Value;

        InstallerLog.Write("SystemTheme: Windows exposed no accent colour, so the built-in one is used");
        return fallbackColor;
    }

    /// <summary>
    /// Applies the dark frame and the rounded corners once the window has a handle. The window draws its own
    /// title bar, so the dark attribute now only tints the frame edge. Both attributes are refinements, so a
    /// Windows build that rejects them costs nothing but the effect.
    /// </summary>
    public static void ApplyWindowChrome(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            InstallerLog.Write("SystemTheme.ApplyWindowChrome: the window has no handle yet");
            return;
        }

        int useDarkTitleBar = IsDarkTheme() ? 1 : 0;
        SetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkTitleBar, "the dark title bar");
        int cornerPreference = DWMWCP_ROUND;
        SetWindowAttribute(windowHandle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, "rounded corners");
    }

    private static Color? ReadAccentPaletteShade(bool darkTheme)
    {
        try
        {
            using RegistryKey? accentKey = Registry.CurrentUser.OpenSubKey(AccentKeyPath);
            if (accentKey?.GetValue(AccentPaletteValueName) is not byte[] accentPalette) return null;
            if (accentPalette.Length < AccentPaletteEntryCount * AccentPaletteEntrySize)
            {
                InstallerLog.Write($"SystemTheme: AccentPalette holds only {accentPalette.Length} bytes");
                return null;
            }

            int entryIndex = darkTheme ? AccentPaletteLight2Index : AccentPaletteDark1Index;
            int offset = entryIndex * AccentPaletteEntrySize;
            return Color.FromRgb(
                r: accentPalette[offset + RedOffset],
                g: accentPalette[offset + GreenOffset],
                b: accentPalette[offset + BlueOffset]);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            InstallerLog.Write("SystemTheme.ReadAccentPaletteShade", exception);
            return null;
        }
    }

    /// <summary>
    /// The unshaded accent, used only when the shade palette is missing. DwmGetColorizationColor is
    /// deliberately not consulted: it returns the composited glass colour with the colorization balance
    /// baked into its alpha, which is a different colour from the accent.
    /// </summary>
    private static Color? ReadBaseAccentColor()
    {
        try
        {
            using RegistryKey? dwmKey = Registry.CurrentUser.OpenSubKey(DWMKeyPath);
            if (dwmKey?.GetValue(AccentColorValueName) is not int storedAccent) return null;

            uint accent = unchecked((uint)storedAccent);
            return Color.FromRgb(
                r: (byte)(accent & ByteMask),
                g: (byte)((accent >> GreenShift) & ByteMask),
                b: (byte)((accent >> BlueShift) & ByteMask));
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            InstallerLog.Write("SystemTheme.ReadBaseAccentColor", exception);
            return null;
        }
    }

    private static void SetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, string description)
    {
        int result = DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
        if (result == HResultOK) return;

        InstallerLog.Write($"SystemTheme: Windows refused {description} with HRESULT 0x{result:X8}");
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Scales the alpha channel, which is how Windows shades an accent fill for hover and press.</summary>
    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb(a: (byte)Math.Round(color.A * opacity), r: color.R, g: color.G, b: color.B);

    private static Color RequireColor(ResourceDictionary resources, string key)
    {
        if (resources[key] is Color color) return color;

        throw new InvalidOperationException($"Theme.xaml declares no Color resource named {key}.");
    }

    private static double RequireDouble(ResourceDictionary resources, string key)
    {
        if (resources[key] is double value) return value;

        throw new InvalidOperationException($"Theme.xaml declares no Double resource named {key}.");
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}

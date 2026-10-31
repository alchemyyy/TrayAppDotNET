namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// The glyphs every app shares. Members are public, so an app catalog deriving from this one exposes them through its
/// own name without declaring them again; the app catalog adds only the app's own glyphs.
/// </summary>
public abstract class GlyphCatalog
{
    public const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    public const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    public static Glyph SETTINGS => Glyphs.Settings;
    public static Glyph POWER => Glyphs.Power;
    public static Glyph INFO => Glyphs.Info;
    public static Glyph EXIT => Glyphs.Exit;
    public static Glyph WARNING => Glyphs.Warning;

    public static Glyph CHROME_MINIMIZE => Glyphs.ChromeMinimize;
    public static Glyph CHROME_MAXIMIZE => Glyphs.ChromeMaximize;
    public static Glyph CHROME_RESTORE => Glyphs.ChromeRestore;
    public static Glyph CHROME_CLOSE => Glyphs.ChromeClose;

    public static Glyph CHEVRON_UP => Glyphs.ChevronUp;
    public static Glyph CHEVRON_DOWN => Glyphs.ChevronDown;
    public static Glyph CHEVRON_LEFT => Glyphs.ChevronLeft;
    public static Glyph CHEVRON_RIGHT => Glyphs.ChevronRight;
    public static Glyph CHEVRON_UP_SMALL => Glyphs.ChevronUpSmall;
    public static Glyph CHEVRON_DOWN_SMALL => Glyphs.ChevronDownSmall;
    public static Glyph CALENDAR => Glyphs.Calendar;

    public static Glyph UNDOCK => Glyphs.Undock;
    public static Glyph REDOCK => Glyphs.Redock;

    public static Glyph SLIDER_THUMB_CIRCLE => Glyphs.SliderThumbCircle;
    public static Glyph SLIDER_THUMB_DIAMOND => Glyphs.SliderThumbDiamond;
    public static Glyph SLIDER_THUMB_STAR => Glyphs.SliderThumbStar;
    public static Glyph SLIDER_THUMB_SQUARE => Glyphs.SliderThumbSquare;
    public static Glyph SLIDER_THUMB_HEART => Glyphs.SliderThumbHeart;

    public static Glyph SETTINGS_NAV_GENERAL => Glyphs.SettingsNavGeneral;
    public static Glyph SETTINGS_NAV_FLYOUT => Glyphs.SettingsNavFlyout;
    public static Glyph SETTINGS_NAV_TRAY_ICON => Glyphs.SettingsNavTrayIcon;
    public static Glyph SETTINGS_NAV_MONITOR_OPTIONS => Glyphs.SettingsNavMonitorOptions;
    public static Glyph SETTINGS_NAV_HOTKEYS => Glyphs.SettingsNavHotkeys;
    public static Glyph SETTINGS_NAV_THEME => Glyphs.SettingsNavTheme;
    public static Glyph SETTINGS_NAV_TRIGGERS => Glyphs.SettingsNavTriggers;
    public static Glyph SETTINGS_NAV_DEVICES => Glyphs.SettingsNavDevices;
    public static Glyph SETTINGS_NAV_DEVICE_APP_DRAWERS => Glyphs.SettingsNavDeviceAppDrawers;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}

/// <summary>Standard page glyphs for Windows 11-style settings navigation.</summary>
public static class SettingsNavigationGlyphs
{
    public static Glyph Settings => GlyphCatalog.SETTINGS;
    public static Glyph General => GlyphCatalog.SETTINGS_NAV_GENERAL;
    public static Glyph Flyout => GlyphCatalog.SETTINGS_NAV_FLYOUT;
    public static Glyph TrayIcon => GlyphCatalog.SETTINGS_NAV_TRAY_ICON;
    public static Glyph MonitorOptions => GlyphCatalog.SETTINGS_NAV_MONITOR_OPTIONS;
    public static Glyph Hotkeys => GlyphCatalog.SETTINGS_NAV_HOTKEYS;
    public static Glyph Theme => GlyphCatalog.SETTINGS_NAV_THEME;
    public static Glyph About => GlyphCatalog.INFO;
    public static Glyph Triggers => GlyphCatalog.SETTINGS_NAV_TRIGGERS;
    public static Glyph Devices => GlyphCatalog.SETTINGS_NAV_DEVICES;
    public static Glyph DeviceAppDrawers => GlyphCatalog.SETTINGS_NAV_DEVICE_APP_DRAWERS;
}

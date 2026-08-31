namespace TrayAppDotNETCommon.Visuals;

public abstract class GlyphCatalog
{
#if DEBUG
    private static readonly GlyphCatalogHotReloadStore<GlyphCatalogResources> Resources =
        GlyphCatalogHotReloadStore<GlyphCatalogResources>.Create(
            catalogName: "Common",
            static () => new GlyphCatalogResources());
#else
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());
#endif

    protected internal const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    protected internal const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;

    protected internal static Glyph SETTINGS => Glyph("Settings");
    protected internal static Glyph POWER => Glyph("Power");
    protected internal static Glyph INFO => Glyph("Info");
    protected internal static Glyph EXIT => Glyph("Exit");
    protected internal static Glyph WARNING => Glyph("Warning");

    protected internal static Glyph CHROME_MINIMIZE => Glyph("ChromeMinimize");
    protected internal static Glyph CHROME_MAXIMIZE => Glyph("ChromeMaximize");
    protected internal static Glyph CHROME_RESTORE => Glyph("ChromeRestore");
    protected internal static Glyph CHROME_CLOSE => Glyph("ChromeClose");

    protected internal static Glyph CHEVRON_UP => Glyph("ChevronUp");
    protected internal static Glyph CHEVRON_DOWN => Glyph("ChevronDown");
    protected internal static Glyph CHEVRON_LEFT => Glyph("ChevronLeft");
    protected internal static Glyph CHEVRON_RIGHT => Glyph("ChevronRight");
    protected internal static Glyph CHEVRON_DOWN_BIG => Glyph("ChevronDownBig");
    protected internal static Glyph CHEVRON_UP_BIG => Glyph("ChevronUpBig");
    protected internal static Glyph CALENDAR => Glyph("Calendar");

    protected internal static Glyph UNDOCK => Glyph("Undock");
    protected internal static Glyph REDOCK => Glyph("Redock");

    protected internal static Glyph SLIDER_THUMB_CIRCLE => Glyph("SliderThumbCircle");
    protected internal static Glyph SLIDER_THUMB_DIAMOND => Glyph("SliderThumbDiamond");
    protected internal static Glyph SLIDER_THUMB_STAR => Glyph("SliderThumbStar");
    protected internal static Glyph SLIDER_THUMB_SQUARE => Glyph("SliderThumbSquare");
    protected internal static Glyph SLIDER_THUMB_HEART => Glyph("SliderThumbHeart");

    protected internal static Glyph SETTINGS_NAV_GENERAL => Glyph("SettingsNavGeneral");
    protected internal static Glyph SETTINGS_NAV_FLYOUT => Glyph("SettingsNavFlyout");
    protected internal static Glyph SETTINGS_NAV_TRAY_ICON => Glyph("SettingsNavTrayIcon");
    protected internal static Glyph SETTINGS_NAV_MONITOR_OPTIONS => Glyph("SettingsNavMonitorOptions");
    protected internal static Glyph SETTINGS_NAV_HOTKEYS => Glyph("SettingsNavHotkeys");
    protected internal static Glyph SETTINGS_NAV_THEME => Glyph("SettingsNavTheme");
    protected internal static Glyph SETTINGS_NAV_TRIGGERS => Glyph("SettingsNavTriggers");
    protected internal static Glyph SETTINGS_NAV_DEVICES => Glyph("SettingsNavDevices");
    protected internal static Glyph SETTINGS_NAV_DEVICE_APP_DRAWERS => Glyph("SettingsNavDeviceAppDrawers");

    private static Glyph Glyph(string name)
    {
#if DEBUG
        return Resources.Current.Glyph(name);
#else
        return Resources.Value.Glyph(name);
#endif
    }
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

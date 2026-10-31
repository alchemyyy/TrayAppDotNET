using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.Visuals;

internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public static Glyph LIGHTNING_BOLT => Glyphs.LightningBolt;

    public static Glyph POWER_MODE_EFFICIENCY => Glyphs.PowerModeEfficiency;
    public static Glyph POWER_MODE_PERFORMANCE => Glyphs.PowerModePerformance;

    public static Glyph BATTERY_0 => Glyphs.Battery0;
    public static Glyph BATTERY_1 => Glyphs.Battery1;
    public static Glyph BATTERY_2 => Glyphs.Battery2;
    public static Glyph BATTERY_3 => Glyphs.Battery3;
    public static Glyph BATTERY_4 => Glyphs.Battery4;
    public static Glyph BATTERY_5 => Glyphs.Battery5;
    public static Glyph BATTERY_6 => Glyphs.Battery6;
    public static Glyph BATTERY_7 => Glyphs.Battery7;
    public static Glyph BATTERY_8 => Glyphs.Battery8;
    public static Glyph BATTERY_9 => Glyphs.Battery9;
    public static Glyph BATTERY_10 => Glyphs.Battery10;

    public static Glyph BATTERY_CHARGING_0 => Glyphs.BatteryCharging0;
    public static Glyph BATTERY_CHARGING_1 => Glyphs.BatteryCharging1;
    public static Glyph BATTERY_CHARGING_2 => Glyphs.BatteryCharging2;
    public static Glyph BATTERY_CHARGING_3 => Glyphs.BatteryCharging3;
    public static Glyph BATTERY_CHARGING_4 => Glyphs.BatteryCharging4;
    public static Glyph BATTERY_CHARGING_5 => Glyphs.BatteryCharging5;
    public static Glyph BATTERY_CHARGING_6 => Glyphs.BatteryCharging6;
    public static Glyph BATTERY_CHARGING_7 => Glyphs.BatteryCharging7;
    public static Glyph BATTERY_CHARGING_8 => Glyphs.BatteryCharging8;
    public static Glyph BATTERY_CHARGING_9 => Glyphs.BatteryCharging9;
    public static Glyph BATTERY_CHARGING_10 => Glyphs.BatteryCharging10;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}

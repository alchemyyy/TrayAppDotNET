using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.Visuals;

internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => new GlyphCatalogResources());

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new static Glyph SETTINGS => Glyph("Settings");
    public new static Glyph POWER => Glyph("Power");
    public new static Glyph EXIT => Glyph("Exit");
    public new static Glyph WARNING => Glyph("Warning");
    public new static Glyph UNDOCK => Glyph("Undock");
    public new static Glyph REDOCK => Glyph("Redock");

    public static Glyph BATTERY_0 => Glyph("Battery0");
    public static Glyph BATTERY_1 => Glyph("Battery1");
    public static Glyph BATTERY_2 => Glyph("Battery2");
    public static Glyph BATTERY_3 => Glyph("Battery3");
    public static Glyph BATTERY_4 => Glyph("Battery4");
    public static Glyph BATTERY_5 => Glyph("Battery5");
    public static Glyph BATTERY_6 => Glyph("Battery6");
    public static Glyph BATTERY_7 => Glyph("Battery7");
    public static Glyph BATTERY_8 => Glyph("Battery8");
    public static Glyph BATTERY_9 => Glyph("Battery9");
    public static Glyph BATTERY_10 => Glyph("Battery10");

    public static Glyph BATTERY_CHARGING_0 => Glyph("BatteryCharging0");
    public static Glyph BATTERY_CHARGING_1 => Glyph("BatteryCharging1");
    public static Glyph BATTERY_CHARGING_2 => Glyph("BatteryCharging2");
    public static Glyph BATTERY_CHARGING_3 => Glyph("BatteryCharging3");
    public static Glyph BATTERY_CHARGING_4 => Glyph("BatteryCharging4");
    public static Glyph BATTERY_CHARGING_5 => Glyph("BatteryCharging5");
    public static Glyph BATTERY_CHARGING_6 => Glyph("BatteryCharging6");
    public static Glyph BATTERY_CHARGING_7 => Glyph("BatteryCharging7");
    public static Glyph BATTERY_CHARGING_8 => Glyph("BatteryCharging8");
    public static Glyph BATTERY_CHARGING_9 => Glyph("BatteryCharging9");
    public static Glyph BATTERY_CHARGING_10 => Glyph("BatteryCharging10");

    private static Glyph Glyph(string name) => Resources.Value.Glyph(name);
}

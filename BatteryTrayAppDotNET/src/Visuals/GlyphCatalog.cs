using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.Visuals;

internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new static readonly Glyph SETTINGS = Glyph.Fluent(CommonGlyphCatalog.SETTINGS);
    public new static readonly Glyph POWER = Glyph.Fluent(CommonGlyphCatalog.POWER);
    public new static readonly Glyph EXIT = Glyph.Fluent(CommonGlyphCatalog.EXIT);
    public new static readonly Glyph WARNING = Glyph.Fluent(CommonGlyphCatalog.WARNING);
    public new static readonly Glyph UNDOCK = Glyph.Fluent(CommonGlyphCatalog.UNDOCK);
    public new static readonly Glyph REDOCK = Glyph.Fluent(CommonGlyphCatalog.REDOCK);

    // NOTE: Battery tray rendering prefers Fluent on Windows 11 and MDL2 on older Windows
    public static readonly Glyph BATTERY_0 = Glyph.Fluent("\uEBA0"); // Fluent, MobBattery0
    public static readonly Glyph BATTERY_1 = Glyph.Fluent("\uEBA1"); // Fluent, MobBattery1
    public static readonly Glyph BATTERY_2 = Glyph.Fluent("\uEBA2"); // Fluent, MobBattery2
    public static readonly Glyph BATTERY_3 = Glyph.Fluent("\uEBA3"); // Fluent, MobBattery3
    public static readonly Glyph BATTERY_4 = Glyph.Fluent("\uEBA4"); // Fluent, MobBattery4
    public static readonly Glyph BATTERY_5 = Glyph.Fluent("\uEBA5"); // Fluent, MobBattery5
    public static readonly Glyph BATTERY_6 = Glyph.Fluent("\uEBA6"); // Fluent, MobBattery6
    public static readonly Glyph BATTERY_7 = Glyph.Fluent("\uEBA7"); // Fluent, MobBattery7
    public static readonly Glyph BATTERY_8 = Glyph.Fluent("\uEBA8"); // Fluent, MobBattery8
    public static readonly Glyph BATTERY_9 = Glyph.Fluent("\uEBA9"); // Fluent, MobBattery9
    public static readonly Glyph BATTERY_10 = Glyph.Fluent("\uEBAA"); // Fluent, MobBattery10

    public static readonly Glyph BATTERY_CHARGING_0 = Glyph.Fluent("\uEBAB"); // Fluent, MobBatteryCharging0
    public static readonly Glyph BATTERY_CHARGING_1 = Glyph.Fluent("\uEBAC"); // Fluent, MobBatteryCharging1
    public static readonly Glyph BATTERY_CHARGING_2 = Glyph.Fluent("\uEBAD"); // Fluent, MobBatteryCharging2
    public static readonly Glyph BATTERY_CHARGING_3 = Glyph.Fluent("\uEBAE"); // Fluent, MobBatteryCharging3
    public static readonly Glyph BATTERY_CHARGING_4 = Glyph.Fluent("\uEBAF"); // Fluent, MobBatteryCharging4
    public static readonly Glyph BATTERY_CHARGING_5 = Glyph.Fluent("\uEBB0"); // Fluent, MobBatteryCharging5
    public static readonly Glyph BATTERY_CHARGING_6 = Glyph.Fluent("\uEBB1"); // Fluent, MobBatteryCharging6
    public static readonly Glyph BATTERY_CHARGING_7 = Glyph.Fluent("\uEBB2"); // Fluent, MobBatteryCharging7
    public static readonly Glyph BATTERY_CHARGING_8 = Glyph.Fluent("\uEBB3"); // Fluent, MobBatteryCharging8
    public static readonly Glyph BATTERY_CHARGING_9 = Glyph.Fluent("\uEBB4"); // Fluent, MobBatteryCharging9
    public static readonly Glyph BATTERY_CHARGING_10 = Glyph.Fluent("\uEBB5"); // Fluent, MobBatteryCharging10
}

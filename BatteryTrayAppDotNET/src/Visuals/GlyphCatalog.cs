using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace BatteryTrayAppDotNET.Visuals;

internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new const string SETTINGS = CommonGlyphCatalog.SETTINGS;
    public new const string POWER = CommonGlyphCatalog.POWER;
    public new const string EXIT = CommonGlyphCatalog.EXIT;
    public new const string WARNING = CommonGlyphCatalog.WARNING;

    // NOTE: Battery tray rendering prefers Fluent on Windows 11 and MDL2 on older Windows
    public const string BATTERY_0 = "\uEBA0"; // Fluent, MobBattery0
    public const string BATTERY_1 = "\uEBA1"; // Fluent, MobBattery1
    public const string BATTERY_2 = "\uEBA2"; // Fluent, MobBattery2
    public const string BATTERY_3 = "\uEBA3"; // Fluent, MobBattery3
    public const string BATTERY_4 = "\uEBA4"; // Fluent, MobBattery4
    public const string BATTERY_5 = "\uEBA5"; // Fluent, MobBattery5
    public const string BATTERY_6 = "\uEBA6"; // Fluent, MobBattery6
    public const string BATTERY_7 = "\uEBA7"; // Fluent, MobBattery7
    public const string BATTERY_8 = "\uEBA8"; // Fluent, MobBattery8
    public const string BATTERY_9 = "\uEBA9"; // Fluent, MobBattery9
    public const string BATTERY_10 = "\uEBAA"; // Fluent, MobBattery10

    public const string BATTERY_CHARGING_0 = "\uEBAB"; // Fluent, MobBatteryCharging0
    public const string BATTERY_CHARGING_1 = "\uEBAC"; // Fluent, MobBatteryCharging1
    public const string BATTERY_CHARGING_2 = "\uEBAD"; // Fluent, MobBatteryCharging2
    public const string BATTERY_CHARGING_3 = "\uEBAE"; // Fluent, MobBatteryCharging3
    public const string BATTERY_CHARGING_4 = "\uEBAF"; // Fluent, MobBatteryCharging4
    public const string BATTERY_CHARGING_5 = "\uEBB0"; // Fluent, MobBatteryCharging5
    public const string BATTERY_CHARGING_6 = "\uEBB1"; // Fluent, MobBatteryCharging6
    public const string BATTERY_CHARGING_7 = "\uEBB2"; // Fluent, MobBatteryCharging7
    public const string BATTERY_CHARGING_8 = "\uEBB3"; // Fluent, MobBatteryCharging8
    public const string BATTERY_CHARGING_9 = "\uEBB4"; // Fluent, MobBatteryCharging9
    public const string BATTERY_CHARGING_10 = "\uEBB5"; // Fluent, MobBatteryCharging10
}

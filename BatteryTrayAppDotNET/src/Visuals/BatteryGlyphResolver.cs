using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace BatteryTrayAppDotNET.Visuals;

/// <summary>Selects the same MobBattery charge level for the tray and flyout.</summary>
internal static class BatteryGlyphResolver
{
    public static Glyph Resolve(BatterySnapshot snapshot)
    {
        int level = Math.Clamp((int)Math.Ceiling(snapshot.ChargePercentage / 10.0), min: 0, max: 10);
        if (snapshot.IsCharging || snapshot.IsOnExternalPower)
        {
            return level switch
            {
                0 => GlyphCatalog.BATTERY_CHARGING_0,
                1 => GlyphCatalog.BATTERY_CHARGING_1,
                2 => GlyphCatalog.BATTERY_CHARGING_2,
                3 => GlyphCatalog.BATTERY_CHARGING_3,
                4 => GlyphCatalog.BATTERY_CHARGING_4,
                5 => GlyphCatalog.BATTERY_CHARGING_5,
                6 => GlyphCatalog.BATTERY_CHARGING_6,
                7 => GlyphCatalog.BATTERY_CHARGING_7,
                8 => GlyphCatalog.BATTERY_CHARGING_8,
                9 => GlyphCatalog.BATTERY_CHARGING_9,
                _ => GlyphCatalog.BATTERY_CHARGING_10
            };
        }

        return level switch
        {
            0 => GlyphCatalog.BATTERY_0,
            1 => GlyphCatalog.BATTERY_1,
            2 => GlyphCatalog.BATTERY_2,
            3 => GlyphCatalog.BATTERY_3,
            4 => GlyphCatalog.BATTERY_4,
            5 => GlyphCatalog.BATTERY_5,
            6 => GlyphCatalog.BATTERY_6,
            7 => GlyphCatalog.BATTERY_7,
            8 => GlyphCatalog.BATTERY_8,
            9 => GlyphCatalog.BATTERY_9,
            _ => GlyphCatalog.BATTERY_10
        };
    }
}

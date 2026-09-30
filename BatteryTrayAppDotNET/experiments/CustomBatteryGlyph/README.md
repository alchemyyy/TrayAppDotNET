# Preserved custom flyout battery

These are the original `BatteryChargeGlyph.cs` and `BatteryChargeGlyph.axaml`,
moved here unchanged while the flyout tries the catalog's Segoe Fluent MobBattery glyphs.
This folder is outside the project source directory and is not compiled.

All custom geometry and tuning are retained, including `BatteryGlyph.Scale = 0.8`,
`FillInsetRatio = 0.22`, and `FillTransform = (-0.6, -0.255)`.

To restore:

1. Move both control files back to `src/UI/Flyout/`.
2. The flyout summary now updates in place instead of rebuilding on every reading. In `BuildBatterySummary`, host
   the glyph where the `TextBlock` battery glyph is, and in `UpdateSummary` replace it with
   `new BatteryChargeGlyph(snapshot, p.Foreground, fill, Layout.BatteryGlyphHeight)` when the charge level or fill
   color changes, because the control takes its snapshot only in its constructor.
3. Replace `Flyout.BatteryGlyphFontSize` in `BatteryFlyoutWindow.axaml` with
   `Flyout.BatteryGlyphHeight` set to `30`.

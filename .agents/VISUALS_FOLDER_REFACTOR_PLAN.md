# Visuals Folder Refactor Plan

## Scope

Assumption: "refactor out `Visuals`" means removing both the seven physical
`src/Visuals` directories and the corresponding `*.Visuals` namespaces.

Current scope (inventory of 2026-10-30) is 55 files:

- 52 C#/AXAML files: Common 14, Battery 7, Brightness 9, Fan 6, Network 7, Volume 6, Task Manager 3
- 3 assets (Brightness map SVG, Fan font and glyph PNG)
- 7 projects
- Four mixed concerns: theming, glyphs, tray rendering, and assets

## Target Layout

| Concern | Destination | Namespace |
| --- | --- | --- |
| Common theme infrastructure | `TrayAppDotNETCommon/src/UI/Theming` | `TrayAppDotNETCommon.UI.Theming` |
| Common glyph models/catalogs | `TrayAppDotNETCommon/src/UI/Glyphs` | `TrayAppDotNETCommon.UI.Glyphs` |
| Common Skia glyph controls | `TrayAppDotNETCommon/src/UI/Controls` | `TrayAppDotNETCommon.UI.Controls` |
| App theme triplets | `<App>/src/UI/Theming` | `<App>.UI.Theming` |
| App glyph catalog triplets | `<App>/src/UI/Glyphs` | `<App>.UI.Glyphs` |
| Brightness custom glyph controls | `BrightnessTrayAppDotNET/src/UI/Glyphs` | `BrightnessTrayAppDotNET.UI.Glyphs` |
| Brightness/Network tray renderers | `<App>/src/UI/Tray` | `<App>.UI.Tray` |

Theme triplets apply to Battery, Brightness, Fan, Network, and Volume. Task
Manager only needs `UI/Glyphs`.

### Outliers

- Move `BrightnessTrayAppDotNET/src/Visuals/BrightnessTrayIcon.cs` to
  `BrightnessTrayAppDotNET/src/UI/Tray` (that folder already holds
  `BrightnessTrayMenuEntries.cs`).
- Move `NetworkTrayAppDotNET/src/Visuals/NetworkTrayIcon.cs` to
  `NetworkTrayAppDotNET/src/UI/Tray` (new folder).
- Move `BatteryTrayAppDotNET/src/Visuals/BatteryGlyphResolver.cs` with Battery's
  glyph catalog to `BatteryTrayAppDotNET/src/UI/Glyphs`.
- Palette consolidation is done (audit 03-12): every app builds its
  `SettingsPalette` through `TrayAppDotNETCommon/src/UI/AppSettingsPalette.cs`
  (`Create(theme, settings, isLight)`, the core `Create(theme, background,
  foreground, isLight)`, and `CreateForFlyout`). `VolumeSettingsPalette`,
  `BatterySettingsPalette` and the per-window `CreatePalette` copies (Brightness,
  Fan, Network, Task Manager, the Fan and Brightness curve editors) are gone.
  When this plan runs, move `AppSettingsPalette.cs` with the other Common theming
  files; there is nothing app-specific left to relocate.
- The light/dark rule is likewise single: `AppTheme.ResolveIsLight(settings or
  mode, theme)` in `TrayAppDotNETCommon/src/Visuals/AppTheme.cs` (audit 03-11);
  the per-app `ResolveEffectiveIsLightTheme` statics no longer exist.
- Already deleted instead of moved (audit 03-20): `CompositeGlyph`, `SkiaCompositeGlyphIcon`, Brightness's
  `SkiaFlyoutGlyphIcon` wrapper, `Glyph.Fluent`/`Glyph.FanFont`, and the per-app icon loaders (`AppTheme.LoadAppIcon`,
  `LoadAppNativeIcon`, Task Manager's `AppThemeStore` icon code; all apps use
  `TrayAppDotNETCommon/src/UI/Tray/AppIconLoader.cs`). `TrayIconMetrics` already lives in `UI/Tray`, and Fan
  registers its icon font through `TADNFontResolver.RegisterAppFont` instead of Common naming Fan's asset.

### Assets

```text
BrightnessTrayAppDotNET/src/Visuals/map_fla-shop.com_ccby4.0.svg
    -> BrightnessTrayAppDotNET/src/Assets/Maps/map_fla-shop.com_ccby4.0.svg

FanControlTrayAppDotNET/src/Visuals/FanFont.ttf
    -> FanControlTrayAppDotNET/src/Assets/Fonts/FanFont.ttf

FanControlTrayAppDotNET/src/Visuals/FAN_GLYPH.png
    -> FanControlTrayAppDotNET/src/Assets/Images/FAN_GLYPH.png
```

`FAN_GLYPH.png` currently has no source reference beyond packaging. Preserve it
during this refactor; deletion should be a separate cleanup.

## Implementation Sequence

### 1. Baseline

- Record the current build and test state.
- Preserve unrelated dirty changes in the working tree; check `git status` for
  modified consumers before moving files.

### 2. Move Common Glyph Infrastructure

- Move glyph data, catalogs, their resource dictionaries and hot-reload stores,
  fonts, and the applicator to `UI/Glyphs`.
- Move `SkiaFlyoutGlyphIcon` to `UI/Controls`.
- Update all C# aliases and AXAML namespace declarations solution-wide.
- Build before proceeding.

### 3. Move Common Theming

- Move the `AppTheme*` files to `UI/Theming`.
- Move each `.axaml` together with its resource class. Debug hot reload watches the
  AXAML source file by path, so verify a reload after the move (see `AXAML_HOT_RELOAD_PLAYBOOK.md`).
- Update `x:Class`, AXAML aliases, tests, and consumers.
- Build and run common tests.

### 4. Move App-Specific Glyphs

- Move each `GlyphCatalog.cs`, `GlyphCatalog.axaml`, and
  `GlyphCatalogResources.cs` as an inseparable set.
- Move the three Brightness custom glyph controls with them.
- Replace app project global `*.Visuals` usings with `*.UI.Glyphs`.
- Keep common glyph imports explicit or aliased to avoid app/common
  `GlyphCatalog` ambiguity.

### 5. Move App-Specific Themes

- Move each theme triplet as a unit.
- Replace app project global `*.Visuals` usings with `*.UI.Theming`.
- Update test aliases such as `BrightnessTrayAppDotNET.Visuals.AppTheme`.

### 6. Move Outliers and Assets

- Move the Brightness and Network tray renderers.
- (Palette helpers are already consolidated; see Outliers.)
- Update resource includes, links, and runtime URIs.
- Required URI and path consumers include:
  - `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalMapPickerCanvas.cs`
    (`MapResourceUri`)
  - `FanControlTrayAppDotNET/src/Constants.cs` (`FanFontRelativePath`)
  - `BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj`
  - `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj`
  - `NOTICE` (the map and fan artwork file paths)

### 7. Update Tests and Documentation

- Update literal source paths and class names in
  `TrayAppDotNETCommon/tests/TrayAppDotNETCommon.Tests/DebugUIProvenanceTests.cs`.
- Update `.agents/PROJECT_MAP.md`.
- Do not alter or regenerate the SVG contents.

## Verification Gates

Run after each independently compiling stage:

```powershell
dotnet build TrayAppDotNET.slnx -c Debug -p:Platform=x64
dotnet test TrayAppDotNET.slnx -c Debug -p:Platform=x64 --no-build
```

Final verification:

- Run a clean build so stale compiled AXAML cannot mask failures.
- Run Native AOT publish and runtime smoke tests for Brightness and Fan because
  their resource URIs change.
- Launch all six apps and inspect the tray icon, flyout, settings navigation,
  and theme loading.
- Verify Debug hot reload for one common and one app-specific theme and glyph
  catalog.
- Open the Brightness environmental map and verify the Fan custom font glyph.
- Confirm repository searches return no source occurrences of:
  - `.Visuals`
  - `src/Visuals`
  - `Visuals/` resource URIs or project links
  - Source directories named `Visuals`
  - Malformed `avares://.../src/...` URIs

## Compatibility and Constraints

- The namespace migration is source-breaking for external consumers of
  `TrayAppDotNETCommon`. This plan assumes the common project is consumed only
  by this workspace and updates everything atomically.
- Do not add compatibility shims unless an external consumer is identified.
- Preserve runtime behavior, XML theme schemas, glyph values, and AXAML resource
  keys.
- Do not combine this structural migration with unrelated theme, layout, or
  rendering changes.

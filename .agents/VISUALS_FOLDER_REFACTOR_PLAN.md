# Visuals Folder Refactor Plan

## Scope

Assumption: "refactor out `Visuals`" means removing both the seven physical
`src/Visuals` directories and the corresponding `*.Visuals` namespaces.

Current scope is 60 files:

- 57 C#/AXAML files
- 3 assets
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
  `BrightnessTrayAppDotNET/src/UI/Tray`.
- Move `NetworkTrayAppDotNET/src/Visuals/NetworkTrayIcon.cs` to
  `NetworkTrayAppDotNET/src/UI/Tray`.
- Do not merely relocate `BatterySettingsPalette`. It is functionally identical
  to the common `VolumeSettingsPalette`. Rename the common helper to
  `AppSettingsPalette`, redirect Battery and Volume to it, and delete
  `BatteryTrayAppDotNET/src/Visuals/BatterySettingsPalette.cs`.

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
- Preserve unrelated dirty changes. Two required consumer files are already
  modified:
  - `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETUpdatePromptPresenter.cs`
  - `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs`

### 2. Move Common Glyph Infrastructure

- Move glyph data, catalogs, readers, hot reload, fonts, and the applicator to
  `UI/Glyphs`.
- Move `SkiaFlyoutGlyphIcon` and `SkiaCompositeGlyphIcon` to `UI/Controls`.
- Update all C# aliases and AXAML namespace declarations solution-wide.
- Build before proceeding.

### 3. Move Common Theming

- Move the `AppTheme*` files to `UI/Theming`.
- Keep `AppTheme.axaml` adjacent to its caller because debug hot reload resolves
  it through `[CallerFilePath]` in `AppThemeHotReload.cs`.
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
- Consolidate the Battery and Volume palette helpers.
- Update resource includes, links, and runtime URIs.
- Required URI consumers include:
  - `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalMapPickerCanvas.cs`
  - `TrayAppDotNETCommon/src/Visuals/TADNFontResolver.cs`
  - `FanControlTrayAppDotNET/src/Constants.cs`
  - `BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj`
  - `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj`

### 7. Update Tests and Documentation

- Update literal source paths and class names in
  `TrayAppDotNETCommon/tests/XmlSourceGenerator.Tests/DebugUIProvenanceTests.cs`.
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

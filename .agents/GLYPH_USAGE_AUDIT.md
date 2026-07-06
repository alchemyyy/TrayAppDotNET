# Glyph Usage Audit

Generated from a four-agent code audit plus local `rg` verification.

## Scope and method

- Scope: `TrayAppDotNETCommon/src`, each app `src` tree, and app test trees where
  glyphs were potentially referenced.
- Excluded: `bin`, `obj`, generated localization designer output, and
  `FanControlTrayAppDotNET/vendor`.
- Glyph usage means any of:
  - `GlyphCatalog.*` references.
  - Raw private-use Unicode glyph string literals.
  - `IconFont`, `IconText`, `TrayIconGlyphLayer`, `FormattedText`, or Skia glyph
    drawing paths.
  - Theme string properties later rendered as glyphs.
  - Glyph-specific font size, weight, scale, render transform, clipping, or
    line-height overrides.

## Global findings

- Glyph data is flat at the app call sites, but rendering behavior is scattered
  across helpers, AXAML resources, and local one-off `TextBlock` mutation.
- The recurring normalization targets are:
  - A glyph text value.
  - A preferred font family or fallback family list.
  - Font size.
  - Font weight.
  - Optional X/Y translation.
  - Optional X/Y scale.
  - Optional line-height or clipping behavior.
- Direct render transforms are rare but important. The meaningful glyph-specific
  transforms are concentrated in Volume microphone/state glyphs, Fan overlay/load
  glyphs, Brightness Skia composites, and slider thumb X scaling.
- `GlyphCatalog.EXIT` and `GlyphCatalog.CHROME_CLOSE` are the same common glyph:
  `\uE8BB`.

## TrayAppDotNETCommon

### Catalog and theme defaults

Path: `TrayAppDotNETCommon/src/Visuals/GlyphCatalog.cs`

Common catalog entries:

- UI: `SETTINGS`, `POWER`, `INFO`, `EXIT`, `WARNING`.
- Caption: `CHROME_MINIMIZE`, `CHROME_MAXIMIZE`, `CHROME_RESTORE`,
  `CHROME_CLOSE`.
- Chevron/navigation: `CHEVRON_UP`, `CHEVRON_DOWN`, `CHEVRON_LEFT`,
  `CHEVRON_RIGHT`, `CHEVRON_UP_BIG`, `CHEVRON_DOWN_BIG`.
- Calendar: `CALENDAR`.
- Undock/redock: `UNDOCK`, `REDOCK`.
- Slider thumb glyphs: `SLIDER_THUMB_CIRCLE`, `SLIDER_THUMB_DIAMOND`,
  `SLIDER_THUMB_STAR`, `SLIDER_THUMB_SQUARE`, `SLIDER_THUMB_HEART`.
- Font family names: `SEGOE_FLUENT_ICONS`, `SEGOE_MDL2_ASSETS`.

Path: `TrayAppDotNETCommon/src/Visuals/AppTheme.cs`

Theme glyph properties:

- `GlyphSettings = GlyphCatalog.SETTINGS`
- `GlyphPower = GlyphCatalog.POWER`
- `GlyphInfo = GlyphCatalog.INFO`
- `GlyphExit = GlyphCatalog.EXIT`

These are plain strings. No size, weight, or transform metadata lives with the
theme value.

### Shared text and icon helpers

Path: `TrayAppDotNETCommon/src/UI/Controls/SettingsUI.cs`

- `TrayAppDotNETSettingsUI.IconFont` is:
  `Segoe Fluent Icons, Segoe MDL2 Assets`.
- `TrayAppDotNETSettingsUI.Text(...)` defaults to font size `14` and
  `FontWeight.Normal`.
- `CaptionGlyph(...)` renders with `IconFont` and
  `SettingsUI.CaptionGlyphFontSize = 10`.
- `SettingsSpinnerButton` renders `CHEVRON_UP` and `CHEVRON_DOWN` with
  `IconFont` and `SettingsUI.SpinnerGlyphFontSize = 8`.
- No glyph transforms are applied here.

Path: `TrayAppDotNETCommon/src/UI/Controls/FlyoutCards.cs`

- `TrayAppDotNETFlyoutUI.IconText(...)` creates a `TextBlock`.
- Default font family is `TrayAppDotNETSettingsUI.IconFont`.
- Callers can override font family and weight.
- Default weight is `FontWeight.Normal`.
- `ApplyGlyphTextRendering(...)` sets text rendering/hinting/baseline behavior.
- No scaling, translation, clipping, or line-height is encoded by this helper.

Path: `TrayAppDotNETCommon/src/UI/FlyoutUndockButtonController.cs`

- Renders `UNDOCK` or `REDOCK`.
- Default size comes from `FlyoutUndockButton.FontSize = 18`.
- Allows caller-provided font family and font weight.
- No transform by default.

### Shared tray icon renderer

Path: `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderer.cs`

- `TrayIconGlyphLayer` stores `BackdropGlyph` and `ForegroundGlyph`.
- `TrayIconRendererOptions` carries:
  - `IconFontFamilies`.
  - `IconFontStyle`, default normal.
  - `MeasureFontScale`, default `1.0`.
  - `DrawFontScale`, default `1.0`.
- Rendering draws optional backdrop first, then foreground.
- If a backdrop exists, foreground X placement is aligned to the backdrop X.
- No per-glyph transform exists. Per-app tray differences are currently encoded
  as layer choice, opacity, and renderer options.

### Shared slider glyphs

Paths:

- `TrayAppDotNETCommon/src/UI/Models/SliderThumbGlyphOption.cs`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.axaml`

Defaults:

| Thumb | Glyph source | Font size | Shape/size notes |
| --- | --- | ---: | --- |
| Capsule | non-glyph | n/a | `Width = 10`, `Height = 22` |
| Circle | `SLIDER_THUMB_CIRCLE` | 18 | glyph |
| Diamond | `SLIDER_THUMB_DIAMOND` | 16 | glyph |
| Star | `SLIDER_THUMB_STAR` | 18 | glyph |
| Square | `SLIDER_THUMB_SQUARE` | 16 | glyph |
| Heart | `SLIDER_THUMB_HEART` | 16 | glyph |

Behavior:

- Thumb glyphs render with `FormattedText` using the option `FontFamily` and
  `FontSize`.
- `XScale` is supported. When not `1.0`, rendering pushes a horizontal scale
  transform around the glyph.
- The slider indicator defaults to `SLIDER_THUMB_DIAMOND`.
- `IndicatorFontFamily` defaults to `SEGOE_MDL2_ASSETS`.
- `FlyoutSlider.IndicatorFontSize = 12`.
- No weight override.

### Shared windows and controls

Paths:

- `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs`
- `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETCaptionCloseButton.cs`
- `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.cs`
- `TrayAppDotNETCommon/src/UI/Controls/ColorPickerWindow.cs`
- `TrayAppDotNETCommon/src/UI/Tray/TrayMenuWindow.cs`

Usages:

- Settings window caption buttons use:
  - `CHROME_MINIMIZE`
  - `CHROME_MAXIMIZE`
  - `CHROME_RESTORE`
  - `CHROME_CLOSE`
  - Font size `10`
  - `IconFont`
  - No transform or weight override.
- `TrayAppDotNETCaptionCloseButton` uses `CHROME_CLOSE`, `IconFont`, size `10`,
  width `46`, height `32`.
- `SearchableListBox` clear button uses `CHROME_CLOSE`, `IconFont`, size `10`.
- `ColorPickerWindow` uses `TrayAppDotNETCaptionCloseButton`, so it inherits the
  same close glyph styling.
- `TrayMenuWindow` supports an optional trailing glyph:
  - Main label font size defaults to `15`.
  - Trailing glyph size defaults to `12`.
  - Trailing glyph font is `IconFont`.
  - Trailing glyph margin defaults to `24,0,0,0`.
  - No transform or weight override.

## VolumeTrayAppDotNET

### Catalog

Path: `VolumeTrayAppDotNET/src/Visuals/GlyphCatalog.cs`

App catalog entries:

- Reexports common fonts, settings, exit, warning, chevrons, undock/redock, and
  caption glyphs.
- Header/activity:
  - `COMMUNICATIONS_ACTIVITY`
  - `SOUND_SETTINGS`
- Playback volume:
  - `PLAYBACK_VOLUME_MUTE`
  - `PLAYBACK_VOLUME_SILENT`
  - `PLAYBACK_VOLUME_LOW`
  - `PLAYBACK_VOLUME_MID`
  - `PLAYBACK_VOLUME_HIGH`
- Microphone:
  - `MICROPHONE`
  - `MICROPHONE_OFF`
  - `MICROPHONE_SLEEP`
  - `MICROPHONE_LISTENING`
  - `EAR_LISTEN`
- State and badges:
  - `LOCK`
  - `UNLOCK`
  - `EQUALIZER`
  - `SIGNAL_NOT_CONNECTED`
  - `PLAYBACK_DEVICE_DEFAULT`
  - `PLAYBACK_DEVICE_ENABLED`
  - `PLAYBACK_DEVICE_DISABLED`
  - `PLAYBACK_DEVICE_DEFAULT_COMMS`
  - `APP_MUTE_OVERLAY`
  - `APP_FALLBACK`
- Bluetooth battery glyphs:
  - `BT_BATTERY_0` through `BT_BATTERY_10`
- Decorative aliases:
  - `CIRCLE`, `DIAMOND`, `STAR`, `SQUARE`, `HEART`

### Tray icon

Path: `VolumeTrayAppDotNET/src/UI/Tray/VolumeTrayIcon.cs`

- Uses `TrayIconRenderer`.
- Font families: `SEGOE_FLUENT_ICONS`, then `SEGOE_MDL2_ASSETS`.
- `IconFontStyle` is normal.
- `MeasureFontScale = 1.0`.
- `DrawFontScale = 1.0`.
- Backdrop opacity is `0.21`.
- Foreground is chosen by `GlyphCatalog.GetVolumeTier(...)`.
- Low, mid, and silent unmuted states use a high-volume backdrop plus the tier
  foreground.
- Muted and high-volume states use foreground only.
- No per-glyph transform or weight.

### Flyout AXAML glyph metrics

Path: `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.axaml`

Glyph-specific resources:

| Resource | Value |
| --- | ---: |
| `Flyout.IconGlyphLineHeightPadding` | 6 |
| `Flyout.HeaderIconFontSize` | 20 |
| `Flyout.HeaderUndockFontSize` | 20.5 |
| `Flyout.AppIconGlyphSize` | 20 |
| `Flyout.AppIconCellBadgeFontSize` | 7 |
| `Flyout.DeviceMuteGlyphFontSize` | 26 |
| `Flyout.DeviceMuteMicrophoneGlyphFontSize` | 20.5 |
| `Flyout.DeviceIconButtonFontSize` | 15 |
| `Flyout.EqualizerFontSize` | 15 |
| `Flyout.EqualizerBadgeFontSize` | 11 |
| `Flyout.DeviceStateFontSize` | 18 |
| `Flyout.DeviceStateDisabledFontSize` | 34 |
| `Flyout.MenuMarkerFontSize` | 8 |

Glyph-specific transforms:

| Resource | Transform |
| --- | --- |
| `Flyout.DeviceMuteMicrophoneTransform` | `TranslateTransform X=-1` |
| `Flyout.DeviceStateDisabledTransform` | `TranslateTransform X=-1.5 Y=-0.5` |

### Flyout header group

Path: `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`

Usages:

- `SETTINGS`
- `SOUND_SETTINGS`
- `COMMUNICATIONS_ACTIVITY`
- `UNDOCK` / `REDOCK`

Local styling:

- Header icon buttons use size `20`, line height `26`, `IconFont`,
  normal weight, no transform.
- Communications button uses the same style but drops opacity when inactive.
- Undock/redock uses size `20.5`, line height `27`, `IconFont`, normal weight,
  no transform.

### App icon cells

Path: `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`

Usages:

- `APP_FALLBACK`
- `APP_MUTE_OVERLAY`
- `LOCK`
- `CIRCLE`

Local styling:

- Real app image size is `22`.
- Fallback glyph base size is `20`, scaled by the app-drawer icon scale.
- Mute overlay uses the same base glyph size and opacity `0.5`.
- Badge glyph size is `7`, bottom-right aligned.
- No transform or weight override.

### Device mute button group

Path: `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`

Usages:

- Playback:
  - `PLAYBACK_VOLUME_MUTE`
  - `PLAYBACK_VOLUME_SILENT`
  - `PLAYBACK_VOLUME_LOW`
  - `PLAYBACK_VOLUME_MID`
  - `PLAYBACK_VOLUME_HIGH`
- Microphone:
  - `MICROPHONE`
  - `MICROPHONE_OFF`
  - `MICROPHONE_SLEEP`
  - `MICROPHONE_LISTENING`

Local styling:

| Glyph group | Font size | Weight | Transform | Line height |
| --- | ---: | --- | --- | ---: |
| Playback volume | 26 | Normal | none | 32 |
| Microphone | 20.5 | ExtraBold | `X=-1` | 27 |

This is one of the strongest candidates for glyph-owned metadata because the
microphone family is consistently smaller, heavier, and shifted relative to the
playback volume family in the same local control.

### Device action buttons and status glyphs

Path: `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`

Usages:

- Generic device action path:
  - Bluetooth battery glyphs `BT_BATTERY_0` through `BT_BATTERY_10`
  - `LOCK`
  - `UNLOCK`
  - `EAR_LISTEN`
  - `CHEVRON_UP_BIG`
  - `CHEVRON_DOWN_BIG`
- Equalizer path:
  - `EQUALIZER`
  - `SIGNAL_NOT_CONNECTED`
- Playback device state path:
  - `PLAYBACK_DEVICE_DEFAULT`
  - `PLAYBACK_DEVICE_ENABLED`
  - `PLAYBACK_DEVICE_DISABLED`
  - `PLAYBACK_DEVICE_DEFAULT_COMMS`

Local styling:

| Local group | Font size | Weight | Transform/position |
| --- | ---: | --- | --- |
| Generic device icon button | 15 | Normal | none |
| Equalizer main | 15 | Normal | none |
| Equalizer badge | 11 | ExtraBold | bottom-right margin `0,0,-2,-1` |
| Device default/enabled/comms | 18 | Normal | none |
| Device disabled | 34 | Normal | `TranslateTransform X=-1.5 Y=-0.5` |

The disabled device state glyph is the largest explicit size outlier in Volume.

### Menus and settings

Paths:

- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`
- `VolumeTrayAppDotNET/src/UI/Settings/HotkeysPage.cs`
- `VolumeTrayAppDotNET/src/UI/Settings/ThemePage.cs`

Usages:

- Menu marker uses `CIRCLE` or empty text, `IconFont`, size `8`.
- Hotkey conflict/warning status uses `WARNING`, `IconFont`, default settings
  text size `14`.
- Hotkey delete uses literal `"x"` size `20`, not an icon font glyph.
- Theme slider thumb preview renders selected thumb glyphs with their option
  font family and font size.
- Theme slider thumb preview applies `ScaleTransform(option.XScale, 1)` when
  `XScale != 1.0`.

No raw Unicode glyph literals were found outside `GlyphCatalog.cs` in Volume.

## BrightnessTrayAppDotNET

### Catalog, theme, and model defaults

Paths:

- `BrightnessTrayAppDotNET/src/Visuals/GlyphCatalog.cs`
- `BrightnessTrayAppDotNET/src/Visuals/AppTheme.cs`
- `BrightnessTrayAppDotNET/src/Models/MonitorInfo.cs`
- `BrightnessTrayAppDotNET/src/Services/BrightnessFlyoutSession.cs`

App catalog entries:

- Sun/moon/composite ingredients:
  - `ECLIPSED_SUN`
  - `HALF_SUN`
  - `FILLED_CIRCLE_SMALL`
  - `CRESCENT_SUN`
  - `CRESCENT_MOON_OLD`
  - `CRESCENT_MOON`
  - `CRESCENT_MOON_BOLD`
  - `EMPTY_CIRCLE_0`
  - `EMPTY_CIRCLE_3`
  - `FILLED_CIRCLE_0`
  - `FILLED_CIRCLE_1`
  - `FILLED_CIRCLE_2`
  - `FILLED_CIRCLE_3`
  - `FILLED_CIRCLE_4`
  - `FILLED_CIRCLE_LARGE`
  - `FILLED_SQUARE`
  - `LIGHTBULB`
- Common reexports:
  - Font names
  - `CHROME_CLOSE`
  - chevrons
  - `CALENDAR`
  - `POWER`
  - `SETTINGS`
  - `WARNING`
- Monitor/profile:
  - `MONITOR`
  - `SYNC_BADGE`
  - `DISPLAY_SETTINGS`
  - `PROFILE_SAVE`
  - `PROFILE_INDICATOR`

Theme/model glyph defaults:

- `AppTheme.GlyphMonitor = MONITOR`.
- `AppTheme.GlyphDisplaySettings = DISPLAY_SETTINGS`.
- `AppTheme.GlyphProfileSave = PROFILE_SAVE`.
- `AppTheme.GlyphProfileIndicator = PROFILE_INDICATOR`.
- `MonitorInfo.IconGlyph = MONITOR`.
- Night-light virtual display session uses `CRESCENT_SUN`.
- sync virtual display session uses `SYNC_BADGE`.
- Profile buttons are plain string labels `1` through `9`, not icon font glyphs.
- Profile custom glyphs are stored as raw text in profile data.

### Tray icon

Path: `BrightnessTrayAppDotNET/src/Visuals/BrightnessTrayIcon.cs`

- Uses Skia direct glyph drawing.
- Font families: `SEGOE_FLUENT_ICONS`, then `SEGOE_MDL2_ASSETS`.
- No weight override.
- Brightness > 99:
  - Draws `HALF_SUN`.
  - Draws mirrored `HALF_SUN` using canvas scale `-1` about the center.
- Brightness between 1 and 99:
  - Draws `HALF_SUN`.
  - Draws `FILLED_CIRCLE_SMALL` at `size + 2`.
  - Uses a clip path built from `FILLED_CIRCLE_SMALL`.
- Brightness 0:
  - Draws `ECLIPSED_SUN`.

This tray icon is not using `TrayIconRenderer`, so its mirroring and clipping
logic is another candidate for glyph-specific render metadata only if the new
glyph layer is also intended to cover Skia icons.

### Flyout AXAML glyph metrics

Path: `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.axaml`

Glyph-specific resources:

| Resource | Value |
| --- | ---: |
| `Flyout.HeaderButtonFontSize` | 20 |
| `Flyout.FooterIconButtonFontSize` | 18 |
| `Flyout.ProfileGlyphFontSize` | 16 |
| `Flyout.SaveProfileGlyphFontSize` | 16 |
| `Flyout.MasterIconFontSize` | 21 |
| `Flyout.MonitorIconFontSize` | 20 |
| `Flyout.StopwatchButtonFontSize` | 18 |
| `Flyout.CurveDisabledGlyphFontSize` | 28 |
| `Flyout.CurveDisabledGlyphSize` | 32 |

No AXAML render transforms were found in the Brightness flyout. Transform-like
behavior is in custom Skia controls and slider preview code.

### Flyout header, rows, and footer

Path: `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs`

Usages:

- Header/footer actions:
  - `POWER`
  - `DISPLAY_SETTINGS`
  - `SETTINGS`
  - `PROFILE_SAVE`
- Row icons:
  - `MONITOR`
  - `LIGHTBULB`
  - `WARNING`
  - `CRESCENT_MOON`
- Stopwatch:
  - `STOPWATCH`

Local styling:

| Local group | Font size | Font family | Weight | Transform |
| --- | ---: | --- | --- | --- |
| Header buttons | 20 | default icon font | default | none |
| Footer power | 18 | default icon font | default | none |
| Footer display/settings | 18 | Fluent | Black | none |
| Profile glyph labels | 16 | flyout UI font | Bold | none |
| Save profile | 16 | Fluent | Black | none |
| Master row icon | 21 | Fluent | Black | none |
| Monitor/warning row icon | 20 | Fluent | Black | none |
| Stopwatch | 18 | MDL2 | default | none |
| Disabled curve crescent | 28 | MDL2 | default | none |

The Brightness flyout uses `FontWeight.Black` for many icon font glyphs. That is
heavier than the default common and Volume/Fan/Battery/Network icon paths.

### Custom Skia glyph controls

Paths:

- `BrightnessTrayAppDotNET/src/Visuals/SkiaFlyoutGlyphIcon.cs`
- `BrightnessTrayAppDotNET/src/Visuals/NightLightBulbGlyphIcon.cs`
- `BrightnessTrayAppDotNET/src/Visuals/EnvironmentalCurveGlyphIcon.cs`

`SkiaFlyoutGlyphIcon`:

- Base custom control for Fluent glyph drawing.
- Uses `SEGOE_FLUENT_ICONS`.
- Supports explicit font size, Skia font weight, scale, and translation.

`NightLightBulbGlyphIcon`:

- Uses `ECLIPSED_SUN`, `FILLED_CIRCLE_SMALL`, and `LIGHTBULB`.
- Composite glyph weight is ExtraBold.
- Important scaling/translation values:
  - bulb scale `0.6`
  - ray scale `0.93`
  - ray circle clip scale `1.35`
  - ray translate Y `-0.08`
  - global translate Y `0.04`
  - default glyph scale `1.25`
  - Y squish `0.9`

`EnvironmentalCurveGlyphIcon`:

- Uses `ECLIPSED_SUN`, `FILLED_SQUARE`, `FILLED_CIRCLE_2`, and
  `CRESCENT_MOON`.
- Composite glyph weight is ExtraBold.
- Important scale values:
  - square scale `0.5`
  - circle scale `0.75`
  - mask-result scale `0.85`
  - moon scale `0.60`
  - additional path shifts are hard-coded in the control.

### Settings and maps

Paths:

- `BrightnessTrayAppDotNET/src/UI/Settings/GeneralPage.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/EnvironmentalPage.Layout.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalMapPickerWindow.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalMapPickerCanvas.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/HotkeysPage.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/ThemePage.cs`
- `BrightnessTrayAppDotNET/src/UI/Tray/BrightnessTrayMenuWindow.cs`

Usages:

- General page order buttons use `CHEVRON_UP` and `CHEVRON_DOWN`,
  `IconFont`, default settings button sizing.
- Environmental page calendar button uses `CALENDAR`, `IconFont`, size `13`.
- Environmental map picker close button uses `CHROME_CLOSE`, `IconFont`.
- Environmental map picker HUD uses `CHEVRON_UP`, `CHEVRON_DOWN`,
  `CHEVRON_LEFT`, `CHEVRON_RIGHT`, `IconFont`; zoom plus/minus are plain text.
- Environmental map picker center button uses `MAP_CENTER`.
- Environmental map picker canvas uses `MAP_PIN`, font
  `Segoe Fluent Icons, Segoe MDL2 Assets`, size `28`.
- Brightness tray menu selected profile marker uses `CHECK_MARK` as the common
  tray menu trailing glyph, so it renders size `12`, `IconFont`.
- Hotkey conflict/warning status uses `WARNING`, `IconFont`, default settings
  text size `14`.
- Hotkey delete uses literal `"x"` size `20`, not an icon font glyph.
- Theme slider thumb preview renders selected thumb glyphs with option font
  family/size and applies `ScaleTransform(option.XScale, 1)` when needed.

Direct raw glyph literals outside the catalog: none after the catalog fix that
added `STOPWATCH`, `CHECK_MARK`, `MAP_CENTER`, and `MAP_PIN`.

## FanControlTrayAppDotNET

### Catalog and probe mapping

Paths:

- `FanControlTrayAppDotNET/src/Visuals/GlyphCatalog.cs`
- `FanControlTrayAppDotNET/src/UI/ProbeValueFormatter.cs`

App catalog entries:

- Common reexports:
  - `SETTINGS`
  - `POWER`
  - `INFO`
  - `EXIT`
  - `WARNING`
  - `UNDOCK`
  - `REDOCK`
- Fan/probe:
  - `FAN` from `FanFont.ttf`, codepoint `\U000F1111`
  - `VOLTAGE`
  - `LOAD`
  - `WATTAGE`
  - `TEMPERATURE = PROBE`
  - `CLOCK`
  - `PROBE`
- Navigation/actions:
  - `ARROW_LEFT`
  - `ARROW_RIGHT`
  - `CURVE_WINDOW`
  - `ADD`
  - `CHECK`
  - `GROUP`
  - `DELETE`
  - `CLOSE = DELETE`
  - `VIEW`
  - `HIDE`
  - `DRAG_HANDLE`
  - `PIN`
  - `PINNED`
  - `COLLAPSED`
  - `EXPANDED`
  - `FLYOUT_FAN_CONTROL_MODE_MANUAL`
  - `FLYOUT_FAN_CONTROL_MODE_CURVE`
- Slider/thumb aliases:
  - `CIRCLE`, `DIAMOND`, `STAR`, `SQUARE`, `HEART`

`ProbeValueFormatter` maps probe data types to glyphs:

| Data source type | Glyph |
| --- | --- |
| Temperature | `TEMPERATURE` |
| Power | `WATTAGE` |
| Load | `LOAD` |
| Clock | `CLOCK` |
| Voltage | `VOLTAGE` |
| fallback | `PROBE` |

### Tray icon

Path: `FanControlTrayAppDotNET/src/UI/Tray/FanTrayIcon.cs`

- Renders `FAN`.
- Uses `FanFont.ttf`/fan font family, not Segoe Fluent.
- Font size equals taskbar small icon size.
- Normal Skia text rendering and antialiasing.
- No transform or weight override.

### Flyout AXAML glyph metrics

Path: `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml`

Glyph-specific resources:

| Resource | Value |
| --- | ---: |
| `Flyout.HeaderButtonFontSize` | 18 |
| `Flyout.HeaderManagerButtonFontSize` | 17 |
| `Flyout.HeaderAddGroupFontSize` | 18 |
| `Flyout.HeaderAddProbeFontSize` | 16 |
| `Flyout.HeaderAddGlyphFontSize` | 9 |
| `Flyout.UndockFontSize` | 18 |
| `Flyout.FanButtonFontSize` | 18 |
| `Flyout.ModeButtonFontSize` | 16 |
| `Flyout.GroupIconFontSize` | 18 |
| `Flyout.GroupExpandFontSize` | 13 |
| `Flyout.GroupDeleteFontSize` | 11 |
| `Flyout.ProbeButtonFontSize` | 16 |
| `Flyout.ProbeRowGlyphWidth` | 16 |
| `Flyout.ProbeRowGlyphFontSize` | 13 |

Glyph-specific transform:

| Resource | Transform |
| --- | --- |
| `Flyout.HeaderAddGlyphTransform` | `TranslateTransform X=2 Y=-2` |

### Flyout header

Path: `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs`

Usages:

- `SETTINGS`
- `CURVE_WINDOW`
- `VIEW`
- `HIDE`
- `GROUP`
- `PROBE`
- `ADD`
- `UNDOCK` / `REDOCK`

Local styling:

| Header glyph | Font size | Weight | Transform |
| --- | ---: | --- | --- |
| Settings | 18 | Normal | none |
| Curve manager | 17 | Normal | none |
| Non-functioning fans view/hide | 18 | Normal | none |
| Add group base `GROUP` | 18 | Normal | none |
| Add probe base `PROBE` | 16 | Normal | none |
| Add overlay `ADD` | 9 | Normal | `TranslateTransform X=2 Y=-2` |
| Undock/redock | 18 | Normal | none |

The add overlay is a clear glyph-specific transform candidate.

### Fan, probe, and group rows

Path: `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs`

Usages:

- Info card: `INFO`.
- Probe header/card:
  - `PROBE`
  - `DELETE`
  - `COLLAPSED`
  - `EXPANDED`
- Probe value rows:
  - `TEMPERATURE`
  - `WATTAGE`
  - `LOAD`
  - `CLOCK`
  - `VOLTAGE`
  - `PROBE`
- Fan row:
  - `FAN`
- Group row:
  - `GROUP`
  - `DELETE`
  - `COLLAPSED`
  - `EXPANDED`
  - `FLYOUT_FAN_CONTROL_MODE_MANUAL`
  - `FLYOUT_FAN_CONTROL_MODE_CURVE`

Local styling:

| Local group | Font size | Font family | Weight | Transform |
| --- | ---: | --- | --- | --- |
| Info card | 18 | default icon font | Normal | none |
| Probe header | 16 | default icon font | Normal | none |
| Probe expand/collapse | 13 | default icon font | Normal | none |
| Probe delete | 11 | default icon font | Normal | none |
| Probe row value glyph | 13 | default icon font | Normal | none except Load |
| Probe row `LOAD` | 13 | default icon font | Normal | `ScaleTransform(0.9, 1.0)` |
| Fan row `FAN` | 18 | fan font | Normal | none |
| Group icon | 18 | default icon font | Normal | none |
| Group expand/collapse | 13 | default icon font | Normal | none |
| Group delete | 11 | default icon font | Normal | none |
| Mode lock/unlock | 16 | default icon font | Normal | none |

The group/probe rows have several local size tiers in one control group:
`18`, `16`, `13`, and `11`.

### Probe selector and settings

Paths:

- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.cs`
- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.axaml`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanPropertiesWindow.cs`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanPropertiesWindow.axaml`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs`

Usages:

- Probe selector nickname arrow uses `ARROW_RIGHT`, `IconFont`, size `13`.
- Probe selector value glyphs use `ProbeValueFormatter`, `IconFont`, width `16`,
  size `12`.
- Probe selector `LOAD` uses `ScaleTransform(0.9, 1.0)`.
- Probe selector gear uses `SETTINGS`, `IconFont`, size `18`.
- Probe selector nickname delete uses `CLOSE`, `IconFont`, size `10`.
- Fan properties window caption buttons use `PIN`, `PINNED`, and `EXIT`,
  `IconFont`, size `10`.
- Fan settings hotkey warning uses `WARNING`, `IconFont`, default settings text
  size `14`.
- Fan settings drag handle uses `DRAG_HANDLE`, `IconFont`, size `16`.
- Fan settings delete uses literal `"x"` size `20`, not an icon font glyph.

No raw Unicode glyph literals were found outside `GlyphCatalog.cs` in Fan app
code, excluding vendor.

## BatteryTrayAppDotNET

### Catalog

Path: `BatteryTrayAppDotNET/src/Visuals/GlyphCatalog.cs`

App catalog entries:

- Reexports common fonts, `SETTINGS`, `POWER`, `EXIT`, and `WARNING`.
- Battery:
  - `BATTERY_0` through `BATTERY_10`
  - `BATTERY_CHARGING_0` through `BATTERY_CHARGING_10`

### Tray icon

Path: `BatteryTrayAppDotNET/src/UI/Tray/BatteryTrayIcon.cs`

- Uses `TrayIconRenderer`.
- `IconFontStyle` is normal.
- Windows 11 font order: Fluent, then MDL2.
- Older Windows font order: MDL2, then Fluent.
- State maps charge bucket to the normal or charging battery glyph series.
- Foreground only, no backdrop.
- No transform or weight override.

### Flyout title bar

Path: `BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs`

Usages:

- `SETTINGS`
- `POWER`
- `UNDOCK` / `REDOCK`

Local styling:

| Local group | Font size | Font family | Weight | Transform |
| --- | ---: | --- | --- | --- |
| Settings title bar action | 18 | IconFont | Normal | none |
| Power title bar action | 14 | IconFont | Normal | none |
| Undock/redock | 20 | IconFont | Normal | none |

The power titlebar glyph is intentionally `4` points smaller than the other
titlebar action glyph in the same group.

The undock path uses `FlyoutUndockButtonController`, then overrides glyph
`FontFamily`, `FontWeight`, and line-height behavior. It also calls default
glyph rendering reset logic. No transform is applied.

### Settings

Path: `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs`

Usages:

- Hotkey warning/status uses `WARNING`, `IconFont`, default settings text size
  `14`.
- Delete uses literal `"x"` size `20`, not an icon font glyph.
- Other `RenderTransform` usages in this file are drag/reorder layout offsets,
  not glyph tuning.

No raw Unicode glyph literals were found outside `GlyphCatalog.cs` in Battery.

## NetworkTrayAppDotNET

### Catalog and theme

Paths:

- `NetworkTrayAppDotNET/src/Visuals/GlyphCatalog.cs`
- `NetworkTrayAppDotNET/src/Visuals/AppTheme.cs`

App catalog entries:

- Reexports `WARNING`, `SEGOE_FLUENT_ICONS`, and `SEGOE_MDL2_ASSETS`.
- Network:
  - `NETWORK_ETHERNET`
  - `NETWORK_WIFI_0`
  - `NETWORK_WIFI_1`
  - `NETWORK_WIFI_2`
  - `NETWORK_WIFI_3`
  - `NETWORK_WIFI_4`
  - `NETWORK_NONE`

Theme glyph properties:

- `GlyphNetworkEthernet`
- `GlyphNetworkWifi0`
- `GlyphNetworkWifi1`
- `GlyphNetworkWifi2`
- `GlyphNetworkWifi3`
- `GlyphNetworkWifi4`
- `GlyphNetworkNone`

These are plain strings with no style metadata.

### Tray icon

Path: `NetworkTrayAppDotNET/src/Visuals/NetworkTrayIcon.cs`

- Uses `TrayIconRenderer`.
- Font families: `SEGOE_FLUENT_ICONS`, then `SEGOE_MDL2_ASSETS`.
- Backdrop opacity is `0.55`.
- Wi-Fi state composition:
  - `Wifi0Bars`, `Wifi1Bar`, `Wifi2Bars`, `Wifi3Bars`, no-internet variants,
    disconnected, and connecting use `GlyphNetworkWifi4` as a backdrop.
  - The lower-bar/no-connection state is rendered as foreground.
  - `Wifi4Bars` renders foreground only.
- Ethernet and no-network states render foreground only.
- No transform, scale, or weight override.

### Settings

Path: `NetworkTrayAppDotNET/src/UI/Settings/HotkeysPage.cs`

Usages:

- Hotkey warning/status uses `WARNING`, `IconFont`, default settings text size
  `14`.
- Delete uses literal `"x"` size `20`, not an icon font glyph.

No raw Unicode glyph literals were found outside `GlyphCatalog.cs` in Network.

## Raw Unicode glyph literals outside app catalogs

No raw Unicode glyph literals remain outside app catalogs in the audited app
source trees.

`FanControlTrayAppDotNET/src/Visuals/GlyphCatalog.cs` also contains
`\U000F1111` for the custom fan font. That is correctly cataloged.

## Transform and scale hotspots

| Area | Glyph(s) | Local tweak |
| --- | --- | --- |
| Volume mute button | microphone family | size `20.5`, `ExtraBold`, `TranslateTransform X=-1` |
| Volume mute button | playback volume family | size `26`, normal, no transform |
| Volume device state | disabled state | size `34`, `TranslateTransform X=-1.5 Y=-0.5` |
| Volume equalizer | signal badge | size `11`, `ExtraBold`, margin `0,0,-2,-1` |
| Fan header add buttons | add overlay | size `9`, `TranslateTransform X=2 Y=-2` |
| Fan probe row | `LOAD` | `ScaleTransform(0.9, 1.0)` |
| Fan probe selector | `LOAD` | `ScaleTransform(0.9, 1.0)` |
| Brightness tray icon | `HALF_SUN` | mirrored via canvas scale `-1` |
| Brightness tray icon | partial brightness | clipped with `FILLED_CIRCLE_SMALL` path and center fill size `size + 2` |
| Brightness night-light icon | composite glyphs | multiple Skia scales/translates, ExtraBold |
| Brightness environmental curve icon | composite glyphs | multiple Skia scales/shifts, ExtraBold |
| Slider thumb rendering | selected thumb option | horizontal `XScale` applied in common slider and theme previews |

Drag/reorder `TranslateTransform` usage was found in Battery, Fan, and Probe
selector windows, but those transforms move rows during drag operations and are
not glyph-specific.

## Font weight hotspots

| Area | Weight difference |
| --- | --- |
| Common/default helpers | icon glyphs usually `FontWeight.Normal` |
| Volume microphone glyphs | `FontWeight.ExtraBold` while playback mute glyphs are normal |
| Volume equalizer badge | `FontWeight.ExtraBold` while main equalizer glyph is normal |
| Brightness flyout action/row icons | many use `FontWeight.Black` |
| Brightness profile labels | `FontWeight.Bold`, but rendered in UI font rather than icon font |
| Brightness Skia composites | ExtraBold Skia font weight |
| Fan/Battery/Network | mostly normal/default icon glyph weight |

## Font size outliers by local group

| Area | Sizes |
| --- | --- |
| Common settings combo/spinner | caption glyph `10`, spinner chevrons `8` |
| Common slider thumb glyphs | `16` and `18`; indicator `12` |
| Volume header | header icons `20`, undock `20.5` |
| Volume app cell | app glyph `20`, app image `22`, badge `7` |
| Volume mute button | playback `26`, microphone `20.5` |
| Volume device state | normal states `18`, disabled state `34` |
| Volume menu marker | `8` |
| Brightness footer/rows | footer `18`, master `21`, monitor/warning `20`, custom icons `22` |
| Brightness disabled curve | crescent `28` inside a `32x32` slot |
| Brightness map pin | cataloged `MAP_PIN` size `28` |
| Fan header | settings/view `18`, manager `17`, add group `18`, add probe `16`, overlay add `9` |
| Fan rows | group/fan/info `18`, mode/probe header `16`, expand/probe row `13`, delete `11` |
| Fan probe selector | value glyph `12`, nickname arrow `13`, gear `18`, delete `10` |
| Fan properties caption | `10` |
| Battery title bar | settings `18`, power `14`, undock `20` |
| Network tray | no local TextBlock size outliers; differences are tray backdrop layering only |

## Implications for a future flat `Glyph` object

The cleanest migration target can remain flat:

- Keep app catalogs flat: `GlyphCatalog.Settings`, `GlyphCatalog.VolumeHigh`,
  etc.
- Replace string constants with immutable glyph objects that include optional
  style metadata.
- Keep raw text access available through a property such as `Text`.
- Let callers still pass local size/foreground/tooltip/handler values.
- Provide one small applicator for primitive glyph renderers:
  - `ApplyTo(TextBlock textBlock)`
  - optionally `ApplyTo(SettingsButton button.Label)`
  - optionally a separate Skia descriptor/applicator if tray/custom Skia glyphs
    are meant to join the same object model.

The data that should move into a glyph object first:

1. Font family.
2. Default font size only where the glyph has a stable intrinsic correction.
3. Font weight.
4. Translate X/Y.
5. Scale X/Y.
6. Optional line-height/clipping flags only if the call sites prove they are
   glyph-intrinsic rather than control-intrinsic.

The best initial candidates are:

- Volume microphone glyph family.
- Volume disabled playback-device glyph.
- Fan `LOAD`.
- Fan add overlay `ADD`.
- Brightness `DISPLAY_SETTINGS`, monitor/warning row icons, and custom Skia
  composites if Skia is in scope.
- Brightness map and stopwatch glyphs now cataloged as flat app glyphs.

# TrayAppDotNET Common Controls Audit

Workspace root: `C:\Users\Alchemy\Desktop\@Workbench\@TRAY_APP_DOT_NET\TrayAppDotNET`

Target common project: `TrayAppDotNETCommon`

## Scope

This audit focuses on UI structures that should be commonized across the TrayAppDotNET apps:

- Multi-part controls and window shells that are currently rebuilt per app.
- Primitive control property divergence that causes visual inconsistency.
- Settings pages and sections whose app-specific parts are small compared with their common layout.
- Common schemas that let app-specific behavior remain app-specific while making the visual and interaction model identical.

The strongest pattern is that `TrayAppDotNETCommon` already contains good primitives, but the apps still compose those primitives independently. The result is not just duplication; it creates different UX for the same action.

## Executive Summary

Highest-value remaining commonization targets:

1. **Install-card uninstall result/refresh contract**: The common install-card section still takes callback-style uninstall delegates. Volume owns the most complete post-uninstall process-exit refresh behavior; the common section should own it for all apps.
2. **Hotkey editor**: All apps duplicate the same search, modifier, key, add, and binding-card editor structure. Brightness adds target parameters, but that should be an extension point, not a separate page.
3. **Flyout shell/header/update/undock controls**: `FlyoutWindowCommon` exists, but the actual header buttons, update affordance, root chrome, window constants, and undock layout are per-app.
4. **Flyout slider rows and entity rows**: `FlyoutSlider` is common, but Volume, Brightness, and Fan independently build the multi-part row around it.
5. **Theme/tray icon/flyout settings page builders**: Common settings primitives exist, but the pages still repeat section structure, card shape, combo options, and save/rebuild wiring.
6. **Primitive resource tokens**: Card radii, button sizes, text box variants, flyout empty states, overlay sizes, and typography tokens need a common resource vocabulary.

The commonization strategy should not flatten app-specific behavior. Instead, the common layer should own layout, visual metrics, window chrome, dialog shape, and row mechanics. Apps should provide descriptors, callbacks, labels, and domain state.

## Completed Since This Audit Was Written

The completed items below are no longer active commonization targets in this audit:

- Added common caption close button:
  - `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETCaptionCloseButton.cs`
- Added common dialog chrome resources:
  - `TrayAppDotNETCommon/src/UI/Controls/DialogChrome.axaml`
- Added common uninstaller window:
  - `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETUninstallerWindow.cs`
- Replaced Battery, Brightness, Fan, Network, and Volume uninstaller window bodies with thin wrappers over the common uninstaller window.
- Added common update prompt presenter:
  - `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETUpdatePromptPresenter.cs`
- Migrated settings About update install flow to the common update prompt:
  - `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETAboutPage.cs`
- Migrated Brightness, Fan, and Volume flyout update install confirmations to the common update prompt.
- Hid the flyout update-button setting for Battery and Network, where no flyout update affordance exists.
- Moved settings-window dimensions into `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.axaml` and replaced app constructor literals with resource-backed standard/compact profiles.

Verification after implementation:

- `dotnet build TrayAppDotNET.slnx -c Debug -m:1` passed.
- `dotnet test TrayAppDotNET.slnx -c Debug --no-build -m:1` passed.
  - Brightness tests: 16 passed.
  - Fan tests: 58 passed.
  - Common XML source generator tests: 7 passed.

Open follow-up from the dialog work:

- The settings installation-section uninstall contract is still callback-oriented. A later pass should replace `Func<Action, Task> UninstallAsync` with a result-oriented common API so the common section can refresh after dialog close and after uninstall process exit. Volume still owns the most complete post-uninstall process-exit refresh behavior.

## Existing Common Layer To Extend

These should be extended instead of bypassed:

- `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs:23`
  - Common settings shell, navigation, page host, custom chrome, and confirmation overlay.
- `TrayAppDotNETCommon/src/UI/CommonBindings.cs:12`
  - Common binding helpers for string combo cards, pair bool cards, single color cards, and variant color cards.
- `TrayAppDotNETCommon/src/UI/Tray/TrayMenuWindow.cs:89`
  - Shared right-click menu window.
- `TrayAppDotNETCommon/src/UI/FlyoutWindowCommon.cs:7`
  - Flyout auto-hide and warm-window base behavior.
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs:39`
  - Shared slider primitive.
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutUndockButtonController.cs:45`
  - Shared undock button controller.
- `TrayAppDotNETCommon/src/UI/Controls/SettingsUI.axaml`
  - Common settings design tokens for buttons, text, cards, toggles, and inputs.
- `TrayAppDotNETCommon/src/UI/Controls/Cards.axaml`
  - Settings card resource values, currently partly duplicating `SettingsUI.axaml`.
- `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.cs`
  - Existing searchable list primitive.
- `TrayAppDotNETCommon/src/UI/Controls/UpdateConfirmationWindow.cs:39`
  - Existing common update confirmation window.
- `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETUpdatePromptPresenter.cs`
  - Common update-install confirmation, staging, failure prompt, and shutdown handoff.
- `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETUninstallerWindow.cs`
  - Common custom-chrome uninstall confirmation window.
- `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETCaptionCloseButton.cs`
  - Common custom-chrome close button used by dialogs.
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETGeneralSettingsSection.cs:59`
  - Common startup/install/general settings section.
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETKeepWarmSettingsSection.cs`
  - Common keep-warm settings section.
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETAboutPage.cs:52`
  - Common About/update settings block.

## P1: Install-Card Uninstall Result And Refresh Contract

### Evidence

The uninstaller window surface is common, but the settings-page install card still delegates uninstall flow through a callback:

- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETGeneralSettingsSection.cs:37`
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETGeneralSettingsSection.cs:178`

Volume still owns the most complete post-uninstall process-exit refresh path:

- `VolumeTrayAppDotNET/src/UI/Settings/GeneralPage.cs:162`

Battery, Brightness, Fan, and Network still hand control to app-specific uninstall callbacks and return before any uninstall process exit refresh can be coordinated by the common section.

### Divergence

- The common installation card refreshes install state immediately after `UninstallAsync` returns.
- Volume opens its uninstaller modally from its settings page and hooks `UninstallProcess.Exited` for a second refresh and incomplete-uninstall message.
- Other apps use their injected app-level uninstaller callbacks and do not get the same process-exit refresh behavior.
- `TrayAppDotNETUninstallerWindow` exposes `ConfirmedUninstall` and `UninstallProcess`, but the common install-card API does not model those results.

### Required Commonization

Replace callback-only uninstall delegates with a result-oriented common API.

Proposed common types:

```csharp
public sealed record TrayAppDotNETUninstallDialogResult
{
    public required bool Confirmed { get; init; }
    public Process? UninstallProcess { get; init; }
    public bool WasHandedOffToRunningCopy { get; init; }
}

public sealed class TrayAppDotNETInstallCardOptions
{
    public required InstallScope Scope { get; init; }
    public required string Title { get; init; }
    public required string ExecutablePath { get; init; }
    public required bool Elevated { get; init; }
    public required Func<TrayAppDotNETInstallResult> Install { get; init; }
    public required Func<Task<TrayAppDotNETUninstallDialogResult>> ShowUninstallerAsync { get; init; }
}
```

Common behavior should be:

- Refresh installation rows after the uninstaller dialog closes.
- If an uninstall process is returned, subscribe once to `Process.Exited` and refresh again.
- Dispose the returned process after exit handling.
- Show the incomplete-uninstall message from the common section when the process exit code is non-zero.
- Keep app-specific inputs limited to install directory, scope, palette/icon/localization, and the uninstall callback already used by `TrayAppDotNETUninstallerWindow`.

## P1: Hotkey Editor Page And Rows

### Evidence

Hotkey editor implementations:

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:659`
- `BrightnessTrayAppDotNET/src/UI/Settings/HotkeysPage.cs:16`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:413`
- `NetworkTrayAppDotNET/src/UI/Settings/NetworkSettingsWindow/Pages/HotkeysPage.cs:13`
- `VolumeTrayAppDotNET/src/UI/Settings/HotkeysPage.cs:13`

Common backend already exists:

- `TrayAppDotNETCommon/src/Models/Hotkey.cs:16`
- `TrayAppDotNETCommon/src/UI/Hotkeys/HotkeyKeys.cs`

### Divergence

Every app repeats the same editor skeleton:

- Search box.
- Modifier combo.
- Read-only key box.
- Add button.
- Existing binding cards.
- Delete button.
- Key capture logic.
- Failed registration status display.
- Default binding comparison.

Primitive values are mostly identical but still hardcoded:

- Search box width 240 in Battery/Network/Volume, 260 in Brightness.
- Modifier combo width 170.
- Key box width 60.
- Add button min width 70.
- Binding grid first column min width 240.
- Delete button width 32, height 29, padding 0, font size 20.
- Binding card margin `0,0,0,4`.

Brightness adds monitor target selection, but that is an extension point. It is not a reason to duplicate the whole editor.

### Required Commonization

Proposed common type:

```csharp
public sealed record TrayAppDotNETHotkeyActionDescriptor<TAction>
{
    public required TAction Action { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string Parameter { get; init; } = string.Empty;
    public TrayAppDotNETHotkeyTargetDescriptor? Target { get; init; }
}

public sealed record TrayAppDotNETHotkeyPageOptions<TAction, TBinding>
{
    public required IList<TBinding> Bindings { get; init; }
    public required IReadOnlyList<TrayAppDotNETHotkeyActionDescriptor<TAction>> Actions { get; init; }
    public required Func<TAction, string, int, TBinding> CreateBinding { get; init; }
    public required Func<IEnumerable<TBinding>, HotkeyApplyResult<TAction, TBinding>?> Apply { get; init; }
    public required Func<TBinding, bool> IsDefaultBinding { get; init; }
    public required Action Save { get; init; }
    public required Action Refresh { get; init; }
    public required SettingsPalette Palette { get; init; }
}
```

Common resources:

- `HotkeySearchWidth`: 240 or standardized to 260.
- `HotkeyModifierComboWidth`: 170.
- `HotkeyKeyBoxWidth`: 60.
- `HotkeyAddButtonMinWidth`: 70.
- `HotkeyDeleteButtonWidth`: 32.
- `HotkeyDeleteButtonHeight`: 29.
- `HotkeyEntryCardMargin`: `0,0,0,4`.

## P1: Theme Page Builder And Color/Thumb Editors

### Evidence

Common color primitives:

- `TrayAppDotNETCommon/src/UI/CommonBindings.cs:118`
- `TrayAppDotNETCommon/src/UI/CommonBindings.cs:174`
- `TrayAppDotNETCommon/src/UI/Controls/SettingsColorCardCoordinator.cs:10`

Palette factory with misleading name:

- `TrayAppDotNETCommon/src/UI/VolumeSettingsPalette.cs:7`

Theme pages:

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:945`
- `BrightnessTrayAppDotNET/src/UI/Settings/ThemePage.cs:8`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:430`
- `NetworkTrayAppDotNET/src/UI/Settings/NetworkSettingsWindow/Pages/ThemePage.cs:9`
- `VolumeTrayAppDotNET/src/UI/Settings/ThemePage.cs:8`

Slider thumb option duplication:

- `BrightnessTrayAppDotNET/src/UI/Settings/ThemePage.cs:78`
- `VolumeTrayAppDotNET/src/UI/Settings/ThemePage.cs:101`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:489`

### Divergence

The same categories are repeatedly rebuilt:

- Context menu font size.
- Theme mode.
- Text/background color cards.
- Rounded corners.
- Animation mode.
- Tooltip delay.
- Tray icon color.
- Flyout colors.
- Slider thumb style and color.

Specific divergence:

- Network manually creates a context header as `TitleText` with `FontWeight.SemiBold` instead of using the same subsection header shape.
- Fan contains more hardcoded labels than the other settings pages.
- Brightness and Volume both build slider thumb combo content with preview glyphs, while Fan uses weaker plain-name choices.
- `VolumeSettingsPalette` is in common but has an app-specific class name. Battery has a duplicate `BatterySettingsPalette`.

### Required Commonization

Rename or replace `VolumeSettingsPalette` with a neutral common factory:

- `TrayAppDotNETSettingsPaletteFactory`

Proposed theme page builder:

```csharp
public sealed record TrayAppDotNETThemePageOptions
{
    public required SettingsPalette Palette { get; init; }
    public required Func<bool> EffectiveLightTheme { get; init; }
    public required Action Save { get; init; }
    public required Action Rebuild { get; init; }
    public required IReadOnlyList<TrayAppDotNETThemeColorDescriptor> ColorDescriptors { get; init; }
    public IReadOnlyList<TrayAppDotNETSliderThumbOption> SliderThumbOptions { get; init; } = [];
    public IReadOnlyList<Control> ExtraCardsBeforeTrayIcon { get; init; } = [];
    public IReadOnlyList<Control> ExtraCardsAfterTrayIcon { get; init; } = [];
}
```

Common subcontrols:

- `TrayAppDotNETThemeModeCard`.
- `TrayAppDotNETColorCardList`.
- `TrayAppDotNETSliderThumbCombo`.
- `TrayAppDotNETTrayIconColorCard`.
- `TrayAppDotNETRoundedCornersCard`.
- `TrayAppDotNETAnimationModeCard`.
- `TrayAppDotNETTooltipDelayCard`.

## P1: Tray Icon Settings Section

### Evidence

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:630`
- `BrightnessTrayAppDotNET/src/UI/Settings/TrayIconPage.cs:8`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:370`
- `NetworkTrayAppDotNET/src/UI/Settings/NetworkSettingsWindow/Pages/TrayIconPage.cs:11`
- `VolumeTrayAppDotNET/src/UI/Settings/TrayIconPage.cs:7`

### Divergence

Repeated shapes:

- Context menu position card.
- Classic/modern context menu style.
- Modified click action section.
- Ctrl/Alt left click.
- Ctrl/Alt right click.
- Double-click variants.
- Optional wheel action cards.

Battery only needs a subset. Brightness needs wheel actions. Volume currently has click action options that are mostly "Nothing". These are descriptor differences, not page-structure differences.

### Required Commonization

Proposed common type:

```csharp
public sealed record TrayAppDotNETTrayIconPageOptions<TClickAction, TWheelAction>
{
    public required SettingsPalette Palette { get; init; }
    public required Action Save { get; init; }
    public required Func<object> GetContextMenuPosition { get; init; }
    public required Action<object> SetContextMenuPosition { get; init; }
    public required IReadOnlyList<TrayAppDotNETActionChoice<TClickAction>> ClickChoices { get; init; }
    public required IReadOnlyList<TrayAppDotNETTrayGestureDescriptor<TClickAction>> ClickGestures { get; init; }
    public IReadOnlyList<TrayAppDotNETActionChoice<TWheelAction>> WheelChoices { get; init; } = [];
    public IReadOnlyList<TrayAppDotNETTrayGestureDescriptor<TWheelAction>> WheelGestures { get; init; } = [];
}
```

Also consider moving context menu position into common:

- `TrayAppDotNETContextMenuPosition`

This would remove the repeated per-app conversion to `TrayMenuWindowPlacement`.

## P1: Flyout Settings Section

### Evidence

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:585`
- `BrightnessTrayAppDotNET/src/UI/Settings/FlyoutPage.cs:8`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:315`
- `VolumeTrayAppDotNET/src/UI/Settings/FlyoutPage.cs:7`

### Divergence

Repeated settings:

- Restore undocked flyout state on startup.
- Show or allow undock button.
- Clamp undocked flyout to screen.
- Place flyout header at bottom.

App-specific settings like Battery trigger visibility, Volume peak meter, Brightness monitor visibility, and Fan sensor layout should remain app-owned.

### Required Commonization

Add a common section builder:

```csharp
public sealed record TrayAppDotNETFlyoutSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required Action Save { get; init; }
    public required Func<bool> GetRestoreUndockedOnStartup { get; init; }
    public required Action<bool> SetRestoreUndockedOnStartup { get; init; }
    public required Func<bool> GetAllowUndock { get; init; }
    public required Action<bool> SetAllowUndock { get; init; }
    public Func<bool>? GetClampToScreen { get; init; }
    public Action<bool>? SetClampToScreen { get; init; }
    public Func<bool>? GetHeaderAtBottom { get; init; }
    public Action<bool>? SetHeaderAtBottom { get; init; }
}
```

## P1: Flyout Shell, Header, And Update Button

### Evidence

Flyout windows:

- `BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs:15`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:27`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:19`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:20`

Header and undock patterns:

- `BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs:356`
- `BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs:473`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:1090`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:1173`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:300`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:1852`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:518`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:1645`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:1672`

AXAML token divergence:

- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.axaml:17`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml:17`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.axaml:17`

### Divergence

Flyout shell metrics are parallel but not centralized:

- Brightness flyout width 330.
- Fan flyout width 350.
- Volume flyout width 350.
- Edge padding, drag threshold, snap tolerance, fallback work area, offscreen position, root border, root radius, and shadow values are AXAML resources per app.

Header buttons are similar but drift:

- Brightness icon button height 32, font 20.
- Fan icon buttons 37 x 32, font 18, manager icon size 17.
- Volume icon buttons 40 x 32, font 20.
- Update button margins, height, font size, and placement differ.
- Undock button has a common controller but layout values still live in each flyout.

### Required Commonization

Add a common flyout shell/header layer:

```csharp
public sealed record TrayAppDotNETFlyoutShellOptions
{
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required bool HeaderAtBottom { get; init; }
    public required IReadOnlyList<TrayAppDotNETFlyoutHeaderAction> HeaderActions { get; init; }
    public required Control Content { get; init; }
    public required Func<PixelRect> GetWorkArea { get; init; }
    public required Func<PixelPoint?> GetUndockedPosition { get; init; }
    public required Action<PixelPoint?> SetUndockedPosition { get; init; }
}
```

Common resources:

- `FlyoutEdgePadding`: 8.
- `FlyoutDragThreshold`: 4.
- `FlyoutSnapTolerancePercent`: 0.02.
- `FlyoutWorkAreaMinHeight`: 220.
- `FlyoutOffscreenPosition`: -32000.
- `FlyoutFallbackWorkArea`: `0,0,1920,1080`.
- `FlyoutBorderThickness`: 1.
- `FlyoutOuterRadius`: 8.
- `FlyoutInnerRadius`: 7.
- `FlyoutShadowOffsetY`: 4.
- `FlyoutShadowBlur`: 30.
- `FlyoutHeaderIconButtonWidth`: 40.
- `FlyoutHeaderIconButtonHeight`: 32.
- `FlyoutHeaderIconButtonRadius`: 4.
- `FlyoutHeaderIconButtonFontSize`: 18.
- `FlyoutHeaderIconButtonLargeFontSize`: 20.
- `FlyoutUpdateButtonWidth`: 80.

Only app-specific content width should remain an option.

## P1: Flyout Slider Row And Inline Value Editor

### Evidence

Volume slider rows:

- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:703`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:1034`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:1184`

Brightness slider rows:

- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:725`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:1113`

Fan slider rows:

- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:835`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:1220`

AXAML token divergence:

- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.axaml:21`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml:94`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.axaml:97`

### Divergence

The shared `FlyoutSlider` is only the inner primitive. Apps still build:

- Entity icon.
- Entity title/status.
- Value text.
- Inline value editor.
- Slider hit area.
- Drag start/end handling.
- Wheel and keyboard stepping.
- Optional action buttons.
- Disabled/opacity states.

Primitive drift:

- Brightness and Fan slider row height 24.
- Volume slider hit padding 14 instead of 10.
- Fan value font size 18.
- Volume percent editor min width 42, border 1, padding `2,0`.
- Volume device name editor font 14, border 1, padding `4,0`, min height 24.
- Fan inline editor font 14, border 0, padding `2,0`.

### Required Commonization

Create the row around `FlyoutSlider`:

```csharp
public sealed record TrayAppDotNETFlyoutSliderRowOptions
{
    public required string Title { get; init; }
    public required string ValueText { get; init; }
    public required double Minimum { get; init; }
    public required double Maximum { get; init; }
    public required double Value { get; init; }
    public required Action<double> PreviewValue { get; init; }
    public required Action<double> CommitValue { get; init; }
    public required Func<double, string> FormatValue { get; init; }
    public required Func<string, double?> ParseValue { get; init; }
    public Control? Icon { get; init; }
    public string? Subtitle { get; init; }
    public IReadOnlyList<TrayAppDotNETFlyoutRowAction> Actions { get; init; } = [];
    public bool IsEnabled { get; init; } = true;
}
```

Common resources:

- `FlyoutSliderRowHeight`: 24.
- `FlyoutSliderHitPadding`: 10.
- `FlyoutSliderValueMinWidth`: 32.
- `FlyoutSliderValueFontSize`: 18.
- `FlyoutSliderValueMargin`: `8,-4,0,0`.
- `FlyoutInlineEditorMinWidth`: 42.
- `FlyoutInlineEditorBorderThickness`: 1.
- `FlyoutInlineEditorPadding`: `2,0`.

Volume can keep `HitPadding=14` only if the larger target is intentional and documented as a style variant.

## P1: Entity Header And Metric Rows

### Evidence

- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs:948`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs:725`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:652`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs:777`
- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.cs:1120`

### Pattern

Several apps build the same conceptual high-level row:

- Entity glyph/icon.
- Name.
- Subtitle or status.
- Live value.
- Optional inline rename or numeric editor.
- Optional expansion.
- Optional action buttons.
- Optional nested child rows.

Examples:

- Volume device rows.
- Brightness monitor rows.
- Fan probe/fan rows.
- Probe selector cards.

### Required Commonization

Create a common entity row with style variants:

```csharp
public enum TrayAppDotNETEntityRowVariant
{
    Flyout,
    SettingsCard,
    CompactProbeCard
}

public sealed record TrayAppDotNETEntityHeaderRowOptions
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? ValueText { get; init; }
    public Control? Icon { get; init; }
    public TrayAppDotNETInlineEditorOptions? InlineEditor { get; init; }
    public IReadOnlyList<TrayAppDotNETEntityRowAction> Actions { get; init; } = [];
    public bool IsExpanded { get; init; }
    public bool IsEnabled { get; init; } = true;
    public TrayAppDotNETEntityRowVariant Variant { get; init; } = TrayAppDotNETEntityRowVariant.Flyout;
}
```

This should not own domain behavior. It should own layout, spacing, hover/pressed visuals, disabled opacity, editor metrics, and action button shape.

## P1: Reorderable Cards And Lists

### Evidence

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:253`
- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:314`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:774`
- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.cs:764`
- `TrayAppDotNETCommon/src/UI/Drag/FlyoutReorderDragController.cs:1`

### Divergence

Repeated behavior:

- Pointer capture for drag.
- Drag handle or full-card drag mode.
- Hover/pressed visual state.
- Interactive child suppression.
- Move up/down keyboard or buttons.
- Persist order.
- Empty-state rebuild.

Repeated metrics:

- Drag handle width 28.
- Settings card padding around 8 or `16,12`.
- Bottom margin 4 or 6.
- Drag threshold 4.
- Drag ghost opacity around 0.82.

### Required Commonization

Generalize existing drag helpers into a settings/flyout reorder list:

```csharp
public sealed record TrayAppDotNETReorderableListOptions<TItem>
{
    public required IList<TItem> Items { get; init; }
    public required Func<TItem, string> GetStableId { get; init; }
    public required Func<TItem, Control> BuildRowContent { get; init; }
    public required Action<int, int> Move { get; init; }
    public required Action Save { get; init; }
    public Control? EmptyState { get; init; }
    public bool UseDragHandle { get; init; } = true;
}
```

## P2: Settings General Page Composition

### Evidence

Common general and keep-warm sections:

- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETGeneralSettingsSection.cs:59`
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETKeepWarmSettingsSection.cs`

Repeated app composition:

- `BatteryTrayAppDotNET/src/UI/Settings/BatterySettingsWindow.cs:174`
- `BrightnessTrayAppDotNET/src/UI/Settings/GeneralPage.cs:273`
- `FanControlTrayAppDotNET/src/UI/Settings/FanSettingsWindow.cs:1035`
- `NetworkTrayAppDotNET/src/UI/Settings/NetworkSettingsWindow/Pages/GeneralPage.cs:55`
- `VolumeTrayAppDotNET/src/UI/Settings/GeneralPage.cs:117`

### Required Commonization

The common section is good, but each app repeats context assembly. Introduce a single settings common context:

```csharp
public sealed record TrayAppDotNETSettingsCommonContext
{
    public required SettingsPalette Palette { get; init; }
    public required Func<string, string?, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<bool> EffectiveLightTheme { get; init; }
    public required string ApplicationName { get; init; }
}
```

Use this context across general, theme, hotkey, flyout, tray icon, and About page builders.

## P2: Tray Menu Wrappers

### Evidence

Common menu:

- `TrayAppDotNETCommon/src/UI/Tray/TrayMenuWindow.cs:89`

App wrappers:

- `BatteryTrayAppDotNET/src/UI/Tray/BatteryTrayMenuWindow.cs:7`
- `BrightnessTrayAppDotNET/src/UI/Tray/BrightnessTrayMenuWindow.cs:11`
- `FanControlTrayAppDotNET/src/UI/Tray/FanTrayMenuWindow.cs:7`
- `NetworkTrayAppDotNET/src/UI/NetworkTrayMenuWindow.cs:6`
- `VolumeTrayAppDotNET/src/UI/Tray/VolumeTrayMenuWindow.cs:9`

### Required Commonization

The apps still repeat:

- Context menu position conversion.
- Standard Settings/Exit entries.
- Separator placement.
- Palette/options construction.
- Scroll-to-bottom selection.

Add a common builder:

```csharp
public sealed record TrayAppDotNETStandardTrayMenuOptions
{
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required int FontSize { get; init; }
    public required TrayMenuWindowPlacement Placement { get; init; }
    public required Action ShowSettings { get; init; }
    public required Action Exit { get; init; }
    public IReadOnlyList<TrayMenuEntry> AppEntries { get; init; } = [];
    public bool ScrollToBottom { get; init; }
}
```

## P2: Search/List And Dense Editor Controls

### Evidence

Existing common searchable list:

- `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.cs`
- `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.axaml`

Fan curve and probe selector duplicate or override list/search tokens:

- `FanControlTrayAppDotNET/src/UI/Curves/FanCurveEditorWindow.axaml:43`
- `FanControlTrayAppDotNET/src/UI/Curves/FanCurveEditorWindow.axaml:72`
- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.axaml:31`
- `FanControlTrayAppDotNET/src/UI/Flyout/ProbeDataSelectorWindow.axaml:75`

### Required Commonization

Keep `SearchableListBox` as the primitive. Add named density variants:

- `SearchableListBox.Settings`.
- `SearchableListBox.Compact`.
- `SearchableListBox.Dense`.

Do not extract Fan curve graph logic unless another app needs it. Extract only dense editor blocks:

- `CompactCard`.
- `NumberGridBlock`.
- `ToggleGridBlock`.
- `InlineSearchHeader`.

## Primitive Control Divergence Audit

### Window Dimensions And Chrome

| Surface | Current state | Commonization target |
| --- | --- | --- |
| Settings windows | Dimensions are AXAML-backed in `SettingsWindowCommon.axaml`: standard 960 x 670, compact 900 x 640. | Completed; future tuning should edit AXAML resources only. |
| Flyout windows | Brightness 330 wide, Fan/Volume 350 wide, repeated work-area/chrome tokens. | Common flyout shell resources with app width override. |
| Tool windows | Color picker, Fan properties, Fan curve, probe selector each own metrics. | Extract only reusable chrome/card/editor metrics, not domain layouts. |

### Buttons

| Button type | Current divergence | Target |
| --- | --- | --- |
| Settings buttons | Common radius 4, min height 32, padding `12,6`. | Keep as default. |
| Dialog action buttons | Update/uninstaller use padding `20,8`; settings overlay uses min width 96. | Add `DialogPrimaryButton` and `DialogSecondaryButton` styles. |
| Hotkey delete button | 32 x 29, padding 0, font 20 in each app. | Add `HotkeyDeleteButton` style. |
| Header icon buttons | Brightness/Fan/Volume differ in width/font. | Add `FlyoutHeaderIconButton` style. |
| Probe/action buttons | Probe selector uses 26/28/36 px variants. | Add compact action button variants if reused outside Fan. |

### TextBox, ComboBox, And NumberBox

| Control | Current divergence | Target |
| --- | --- | --- |
| Settings TextBox | Common height 32, font 14, border 0, padding `4,0`. | Keep default. |
| Hotkey search box | Width 240 in most apps, 260 in Brightness. | Tokenize and standardize. |
| Hotkey key box | Width 60 everywhere. | Tokenize. |
| Modifier ComboBox | Width 170 everywhere. | Tokenize. |
| Inline flyout value editor | Volume border 1 padding `2,0`; Fan border 0 padding `2,0`. | Add `FlyoutInlineValueEditor` style. |
| Device rename editor | Volume min height 24, border 1, padding `4,0`. | Add `FlyoutInlineNameEditor` style. |
| Probe selector boxes | Widths are domain-specific, but height/padding should be common. | Use compact input variants. |

### Toggle, CheckBox, And RadioButton

| Control | Current divergence | Target |
| --- | --- | --- |
| Settings toggle | Common width 40, height 20, radius 10. | Keep default. |
| Probe selector compact toggle | Custom 32 px track and font 10 label. | Add `SettingsToggle.CompactInline`. |

### Cards And Borders

| Card type | Current divergence | Target |
| --- | --- | --- |
| Settings card | Radius 6, padding `16,12`, margin `0,0,0,6`. Repeated in `SettingsUI.axaml` and `Cards.axaml`. | Single token source. |
| Fan flyout card | Border 1.5, radius 8, padding `8,6,8,4`. | Add `FlyoutCompactCard` style. |
| Probe cards | Radius 5, padding `6,5`. | Add `DenseCard` style. |
| Dialog border | Brightness radius 6, Fan radius 4, Settings overlay wider. | Add `DialogCard` and `FlyoutConfirmCard` styles. |

### Typography

| Token | Current divergence | Target |
| --- | --- | --- |
| Settings section title | Common 22. | Keep. |
| Settings subsection title | Common 14. Network manually recreates one header. | Force `SubsectionHeader` usage. |
| Settings card title | Common 14. | Keep. |
| Description text | Common 12, opacity 0.8. | Keep. |
| Flyout empty text | Font 13 in Brightness/Fan/Volume; opacity missing or 0.7. | Add `FlyoutEmptyText` token. |
| Dialog title | Brightness 16, Fan 14, Settings overlay 16. | Add `DialogTitleText` and `FlyoutDialogTitleText` tokens. |
| Meta/tiny text | Probe selector uses 10/12, map picker has own HUD text. | Add `MetaText` and `TinyText` tokens only where reused. |

### Color And Palette

| Area | Current divergence | Target |
| --- | --- | --- |
| Settings palette | Common factory named `VolumeSettingsPalette`, Battery duplicate. | Rename to neutral common factory and migrate all apps. |
| Fan hardcoded labels/colors | Several settings cards and editor surfaces are hardcoded. | Use localizer and palette descriptors. |
| Probe separator | `#E8E8E8` hard-coded in AXAML. | Use palette separator/border token. |
| Slider thumb colors | Brightness/Volume/Fan build options differently. | Common slider thumb option schema with preview content. |

## Proposed Resource Taxonomy

The common project should have named resource groups. Do not keep accumulating anonymous per-window AXAML values.

Suggested files:

- `TrayAppDotNETCommon/src/UI/Controls/WindowChrome.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/DialogChrome.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutChrome.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutRows.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/HotkeyEditor.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/ReorderableList.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/DenseEditors.axaml`

Suggested resource names:

```xml
<x:Double x:Key="SettingsWindow.StandardWindowWidth">960</x:Double>
<x:Double x:Key="SettingsWindow.StandardWindowHeight">670</x:Double>
<x:Double x:Key="SettingsWindow.StandardWindowMinWidth">720</x:Double>
<x:Double x:Key="SettingsWindow.StandardWindowMinHeight">520</x:Double>
<x:Double x:Key="SettingsWindow.CompactWindowWidth">900</x:Double>
<x:Double x:Key="SettingsWindow.CompactWindowHeight">640</x:Double>
<x:Double x:Key="SettingsWindow.CompactWindowMinWidth">680</x:Double>
<x:Double x:Key="SettingsWindow.CompactWindowMinHeight">500</x:Double>

<x:Double x:Key="DialogWidth">520</x:Double>
<x:Double x:Key="DialogMinWidth">420</x:Double>
<x:Double x:Key="DialogTitleBarHeight">32</x:Double>
<Thickness x:Key="DialogBodyMargin">28,8,28,20</Thickness>
<Thickness x:Key="DialogButtonPadding">20,8</Thickness>
<CornerRadius x:Key="DialogRootRadius">8</CornerRadius>

<x:Double x:Key="FlyoutHeaderIconButtonWidth">40</x:Double>
<x:Double x:Key="FlyoutHeaderIconButtonHeight">32</x:Double>
<x:Double x:Key="FlyoutHeaderIconButtonFontSize">18</x:Double>
<x:Double x:Key="FlyoutSliderRowHeight">24</x:Double>
<Thickness x:Key="FlyoutSliderHitPadding">10</Thickness>

<x:Double x:Key="HotkeySearchWidth">240</x:Double>
<x:Double x:Key="HotkeyModifierComboWidth">170</x:Double>
<x:Double x:Key="HotkeyKeyBoxWidth">60</x:Double>
<x:Double x:Key="HotkeyDeleteButtonWidth">32</x:Double>
```

## App Settings Model Divergence

Common model:

- `TrayAppDotNETCommon/src/Models/AppSettingsCommon.cs`

Battery, Network, and Volume inherit the common settings base. Brightness and Fan implement common interfaces but duplicate many base settings properties. That is a structural reason common UI builders are harder to use in Brightness and Fan.

Recommendation:

- Move Brightness and Fan toward `AppSettingsCommon` if feasible.
- If not feasible, define small adapter interfaces per builder:
  - `ITrayAppDotNETThemeSettings`.
  - `ITrayAppDotNETFlyoutSettings`.
  - `ITrayAppDotNETTrayIconSettings`.
  - `ITrayAppDotNETHotkeySettings`.

Avoid passing whole app settings objects into generic common controls unless the control genuinely needs them. Prefer narrow descriptors and callbacks.

## Implementation Roadmap

### Phase 1: Install-Card Uninstall Completion Flow

1. Move Volume's post-uninstall process-exit refresh behavior into `TrayAppDotNETGeneralSettingsSection`.
2. Replace install-card `UninstallAsync` with a result-oriented common uninstall dialog API.

This is the only remaining work from the dialog/uninstaller pass. The visual dialog and prompt commonization is complete.

### Phase 2: Common Settings Page Builders

1. Rename `VolumeSettingsPalette` to a neutral common palette factory.
2. Add `TrayAppDotNETSettingsCommonContext`.
3. Add hotkey page builder.
4. Add tray icon settings section builder.
5. Add flyout settings section builder.
6. Add theme page builder and slider thumb combo.

This phase reduces settings page duplication and primitive drift.

### Phase 3: Flyout Composition

1. Add flyout shell/header resources.
2. Add `TrayAppDotNETFlyoutHeader`.
3. Add `TrayAppDotNETFlyoutSliderRow`.
4. Add inline value/name editor styles.
5. Add entity header row variants.
6. Migrate Brightness, Volume, and Fan row composition gradually.

This phase has more integration risk because flyouts contain app-specific behavior and high-frequency UI updates.

### Phase 4: Reorderable Lists And Dense Editors

1. Generalize reorder drag controller to settings/flyout list contexts.
2. Migrate Battery trigger cards and Fan slot rows.
3. Add dense editor resource variants.
4. Reduce Fan curve/probe selector local tokens only where common styles fit.

## Non-Goals And Cautions

- Do not force Network behavioral parity without verifying intent. Network may intentionally lack some flyout affordances. Visual primitives should still be common where the same control exists.
- Do not extract Fan curve graph behavior just because it has many controls. Extract dense primitive controls and cards only if they will be reused.
- Do not create abstract factories or service layers for internal UI composition. Descriptor records plus common builder methods are enough.
- Do not commonize app-specific labels, sensor math, audio device behavior, monitor enumeration, or battery trigger logic.
- Do not make common controls depend on whole app settings types unless they are specifically `AppSettingsCommon` controls. Use narrow callbacks.

## Final Architecture Target

`TrayAppDotNETCommon` should own:

- Dialog/window chrome.
- Caption buttons.
- Update and uninstall prompt presentation.
- Settings card and primitive input resource tokens.
- Hotkey editor UI.
- Theme/tray/flyout settings page sections.
- Flyout shell/header controls.
- Flyout slider row and inline editor controls.
- Entity row layout variants.
- Reorderable card/list mechanics.
- Tray menu builder boilerplate.

Each app should own:

- Domain state.
- Domain action lists.
- Labels/localization resources.
- App icon and app name.
- Domain-specific settings pages.
- Device/sensor/monitor/audio behavior.
- App-specific extra flyout content.

The key correction is to commonize composition, not just primitives. The visual inconsistency is coming from repeated high-level controls that happen to be built out of common primitives with app-local constants.

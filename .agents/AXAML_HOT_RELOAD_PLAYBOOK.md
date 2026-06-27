# AXAML Hot-Reload Playbook

Use this with `AXAML_PLAYBOOK.md` when an AXAML edit must update UI that is already open.

## Definition Of Done

An AXAML value is hot-reloadable only when editing its source updates the already-created surface that consumes it.

These do not satisfy that definition by themselves:

- Moving a literal from C# to AXAML
- Generating a typed AXAML getter
- Replacing a resource dictionary without notifying its consumers
- Making the edit visible only after closing and reopening the app

A captured generated property wrapper remains live because each getter looks up the owner again. A primitive, brush, pen, geometry, timer interval, option object, or control property copied from that wrapper is stale until code reapplies or rebuilds it.

## Release And Native AOT Boundary

Hot reload is a Debug-only development facility. Release, Prerelease, and Native AOT builds must contain no watcher, runtime AXAML loader, reload event, subscription, callback, synchronization pass, polling, or no-op stand-in. Do not rely on the AOT trimmer to remove it.

Apply the boundary at compile time:

- Put runtime AXAML loader and HotAvalonia package references behind a Debug-only MSBuild condition.
- Keep `MSBuildProjectExtensionsPath` configuration-specific. A shared `project.assets.json` can otherwise carry Debug-only HotAvalonia runtime assets into a later Release dependency manifest or copy-local output.
- Defend against a shared `project.assets.json` retaining Debug package content after a configuration switch. `TrayAppDotNET.Parent.targets` removes `HotAvalonia.Extensions` compile items and disables its Fody injection outside Debug.
- Put reload stores, events, subscriptions, unsubscriptions, callbacks, and hot-reload-only state behind `#if DEBUG`.
- Keep the normal compiled `ResourceDictionary` construction path outside that block.
- Do not expose a Release event with empty `add` and `remove` accessors. Although functionally inert, its call sites and handler code still compile into the application.
- Do not run dictionary synchronization in Release solely to support stable hot-reload identity. Construct the ordinary per-owner compiled dictionary instead.
- Do not combine the Debug configuration with Native AOT. A Debug publish with a RID remains self-contained JIT, while `ReleaseWithDebugging` provides diagnostic Native AOT without defining `DEBUG`. The shared parent targets reject an explicit Debug AOT request.

Verify this with an actual Release Native AOT publish, not only a managed Release build. Search the published executable and dependency manifests for hot-reload type names, source AXAML paths, `HotAvalonia`, and `Avalonia.Markup.Xaml.Loader`.

## Property Linker Contract

`TrayAppDotNETCommon.AxamlPropertyLinker` reads project `AdditionalFiles` ending in `.axaml`. Every app and common project should retain:

```xml
<AdditionalFiles Include="**\*.axaml" />
```

The AXAML root needs a valid fully qualified `x:Class`, and the matching C# class must be partial. The generator silently ignores malformed XML, a missing or invalid `x:Class`, invalid keys, and unsupported types.

Resource keys must have exactly this shape:

```text
Prefix.PropertyName
```

Requirements:

- Exactly one dot
- Case-sensitive
- Both parts start with a letter or underscore
- Remaining characters are letters, digits, or underscores
- Avoid C# keywords even though the generator does not diagnose them

Supported resource elements:

| AXAML element | Generated C# type | Notes |
|---|---|---|
| `sys:Double` | `double` | Getter also converts integer and invariant numeric strings |
| `sys:Int32` | `int` | Getter rounds its numeric input |
| `sys:String` | `string` | Invariant conversion |
| `sys:String` with a property ending in `Color` | `Avalonia.Media.Color` | Parses the string as a color |
| `Color` | `Avalonia.Media.Color` | Strict typed lookup |
| `Thickness` | `Avalonia.Thickness` | Strict typed lookup |
| `CornerRadius` | `Avalonia.CornerRadius` | Strict typed lookup |
| `TranslateTransform` | `Avalonia.Media.TranslateTransform` | A clone is returned on every read |

Unsupported examples include `bool`, enums, `GridLength`, brushes, `Point`, `Rect`, `Size`, and transforms other than `TranslateTransform`. Use a supported primitive and derive or cast in C# when that keeps the resource understandable. Do not put behavior into AXAML merely to work around this table.

For a key such as `Flyout.Width`, the matching partial owner gets an `AxamlFlyout` property with a typed `Width` getter. Prefer this generated API. `HotReloadResourceReader` is a legacy manual reader and should not gain new call sites.

## Choose The Correct Reload Path

### AXAML owned by a control or window

HotAvalonia patches control-owned AXAML in Debug builds. Constructors do not run again. Use the established exact-name, parameterless callback:

```csharp
private void InitializeComponentState()
{
    _layout = AxamlFlyout;
    RebuildVisual();
}
```

HotAvalonia invokes `InitializeComponentState()` after a successful patch. Refresh the property wrapper, then reapply properties or rebuild code-created content. The Brightness and Volume main flyouts are established examples. The Fan main flyout is source-live but currently bypasses its guarded rebuild request path, so it is not yet a safe example during pointer capture.

If rebuilding during pointer capture can corrupt a gesture, cancel or defer the rebuild. Preserve runtime state such as selected item, filter text, scroll offset, and active device where practical.

### Standalone `ResourceDictionary`

HotAvalonia does not patch a standalone dictionary. In Debug, use `AXAMLResourceHotReloadStore<TResource>` to watch and runtime-compile the adjacent source file. Keep the runtime loader out of Release.

Use stable synchronized identity when any consumer retains the dictionary or a generated wrapper:

```csharp
#if DEBUG
private static readonly AXAMLResourceHotReloadStore<SampleResources> Resources =
    AXAMLResourceHotReloadStore<SampleResources>.Create(
        "sample resources",
        static () => new SampleResources(),
        NotifyResourcesReloaded,
        "SampleResources.axaml",
        synchronizeReload: SynchronizeResources);
#else
private static readonly Lazy<SampleResources> Resources =
    new(static () => new SampleResources());
#endif
```

If identity is not synchronized, every callback must reacquire `Current` before rebuilding. Never retain a wrapper whose dictionary will be replaced.

In Debug, common dictionaries use `CommonAXAMLResourceStore<TResource>` and publish `CommonAXAMLHotReload.ResourcesReloaded`. `SettingsWindowCommon` then synchronizes its per-window merged dictionaries and rebuilds the current shell. The store, event, synchronization, and callbacks do not exist outside Debug; each dictionary class declares its own ordinary `Lazy<TResource>` compiled-resource path for Release and Native AOT.

## Applying Reloaded Values

| Consumer | Required action |
|---|---|
| Assigned `Control` property | Assign it again, or rebuild the control |
| Code-built page or flyout | Rebuild and swap a complete content generation |
| Cached brush, pen, geometry, or text layout | Recreate it, dispose old disposable objects, and invalidate measure or visual |
| Painted control metrics | Recompute all derived metrics, then invalidate the affected render layers |
| `DispatcherTimer` interval | Assign the new `Interval` explicitly |
| Popup or immutable menu options | Rebuild in place, or close and let the next open recreate it |
| Window startup size | Change only when that specific AXAML value changed; unrelated edits must not overwrite a user-resized window |
| Native or background renderer | Copy an immutable style snapshot on the UI thread before queueing background work |

Rebuilding a whole surface is the preferred first implementation when it is transactional and preserves important state. Narrow apply passes are worthwhile for high-frequency surfaces or editors with unsaved state.

## Event Lifetime Rules

- Subscribe only after construction has established everything the callback reads.
- Invoke each handler independently so one failed surface does not block the others.
- Unsubscribe on `Closed`, `Dispose`, or the owner's existing lifetime registration.
- Keep callbacks on the Avalonia UI thread. `AXAMLResourceHotReloadStore` already dispatches reloads there.
- A failed runtime XAML compile must retain the last valid dictionary and must not notify consumers.
- Do not share one mutable `ResourceDictionary` as a merged dictionary across multiple owners. Synchronize each per-window dictionary from the stable current source.

## Explicit Exceptions

When live application is unsafe or disproportionate, put a concrete comment immediately above the retained code:

```csharp
// AXAML hot-reload exception: Changing this after native handle creation would invalidate the registered window class
```

Do not use a generic "too hard" comment. State the constraint and, when useful, the architecture needed to remove it.

Typical valid exceptions:

- Model defaults used before Avalonia starts and persisted as user state
- Native values fixed at handle or class registration
- Background rendering that lacks an immutable UI-thread style snapshot
- Linker-unsupported immutable objects where all useful numeric parts are already resources

## Audit Procedure

1. List every `*.axaml`, its `x:Class`, and its C# owner.
2. Confirm the file is an `AdditionalFile` and every key follows the one-dot schema.
3. Classify the owner as control-owned AXAML or standalone dictionary.
4. Find every construction-time read of each generated getter.
5. Trace the already-open surface after reload. A live getter is insufficient if its value was copied.
6. Inspect derived brushes, pens, geometries, text layouts, timers, context-menu options, and native snapshots.
7. Inspect popups and child windows that can outlive a page rebuild.
8. Include common dictionaries used by the app, not only app-local AXAML.
9. Search C# for remaining presentation literals. Keep behavior and runtime calculations in code.
10. Search for unused AXAML keys. Delete stale keys rather than claiming they reload.

Useful searches:

```powershell
rg --files -g '*.axaml'
rg -n 'x:Class=|x:Key=' -g '*.axaml'
rg -n 'AXAMLResourceHotReloadStore|CommonAXAMLResourceStore|ResourcesReloaded|InitializeComponentState' -g '*.cs'
rg -n 'new Thickness|new CornerRadius|FontSize|Opacity|const double|const int' -g '*.cs'
rg -n 'avares://[^\"]+/src/' -g '*.cs' -g '*.axaml'
```

## Verification

1. Build Debug with zero warnings.
2. Run the app from the checkout.
3. Edit one visible value in each dictionary class while its consumer is open.
4. Confirm both the successful reload log and the live visual change.
5. Confirm selected state, user-resized state, and active gestures are not unintentionally reset.
6. Introduce a temporary invalid value and confirm the previous UI remains active without a notification.
7. Restore the source value.
8. Run affected tests.
9. Build Release to prove Debug-only loader code did not leak.
10. For release-sensitive changes, publish the Native AOT app and run a smoke test.

Useful Release-boundary checks:

```powershell
dotnet msbuild .\App\src\App.csproj -nologo -getItem:PackageReference -p:Configuration=Release
dotnet publish .\App\src\App.csproj -c Release -r win-x64 --no-restore -p:SkipPublishAfterBuild=true
rg -a 'HotAvalonia|AvaloniaRuntimeXamlLoader|AXAMLResourceHotReloadStore|ResourcesReloaded' .\publish
```

The evaluated Release package list must omit both `HotAvalonia` and `Avalonia.Markup.Xaml.Loader`. Interpret binary string hits rather than accepting them blindly: ordinary application text may contain the words "hot reload," but reload store types, event handlers, watcher messages, and runtime-loader symbols must be absent.

## Task Manager Reference Implementation

`TaskManagerTrayAppDotNET` uses four app-local AXAML dictionaries:

- `TaskManagerWindow.axaml`: synchronized stable dictionary; the main window applies the open Processes page in place so search text, scroll offsets, selection, run input, and open editors survive. Performance rebuilds while preserving the selected device. App history, Startup apps, Users, and Services rebuild so every construction-time `TaskManagerTable.*` value is refreshed, then restore search, run input, selection, sort, collapsed groups, user-resized columns, and deferred two-axis scroll offsets. A changed AXAML column baseline wins over a prior drag, while unrelated reloads preserve the dragged width. Painted process controls update typography, derived drawings, and all nine AXAML-backed widths; the hot widths also replace the canvas's authoritative column settings so later resize, property, chooser, and reset interactions do not revert them. Main-window dimensions are reapplied only when their own resource changes, and Task Manager explicitly rejects common settings-window dimension ownership. `RefreshSidebarCollapseControls()` reapplies the navigation action without rebuilding the shell; its caret reads the vertical and left/right translations from the synchronized dictionary during each reapply.
- `TaskManagerContextMenuResources.axaml`: synchronized stable dictionary; open immutable menus close, autocomplete reapplies and reranks, the Processes page replaces cached table and open Columns-dialog scrollbar options, and generic table pages replace both painted-scrollbar menu snapshots without discarding page state.
- `TaskManagerReorderResources.axaml`: replacement dictionary; `TaskManagerReorderList` reacquires `Current`, resets its timer interval, cancels an active drag safely, and rebuilds rows.
- `Visuals/GlyphCatalog.axaml`: replacement dictionary through `GlyphCatalogHotReloadStore`; settings shells, compact-sidebar application geometry, navigation glyphs, reorder rows, and cached process-table caret and header text rebuild on notification. Common and glyph shell rebuilds capture and restore Processes and generic-table runtime state. Open child editor windows are the marked exception because shared-shell page cleanup owns and closes them.

`TaskManagerWindow.RegisterPage` activates sampling, timers, and static/external subscriptions only after a fully constructed page attaches to the committed visual tree. It deactivates the prior page on detach and reapplies the idle process schema before activating a replacement. Keep this ownership in `SetPageActive`; do not start background work from a table-page constructor. Generic table controls attach icon notifications from the page's visual-tree lifetime, and all resource reload events remain inside `#if DEBUG` so the Release/AOT graph contains no hot-reload handlers or state records.

Intentional Task Manager exceptions are marked in source:

- Persisted process-column model defaults in `UI/ProcessTableLayout.cs`
- Background tray-icon rendering style in `UI/Tray/TaskManagerTrayIcon.cs`
- Avalonia's built-in `DashStyle.Dash` in the performance graph controls
- Optional glyph scale/translation metadata in painted process-table sort `TextLayout` geometry
- App-specific construction-time navigation geometry owned by private Common shell controls while Processes remains open; navigation-action caret translations reapply in place
- Private generic reorder-dialog chrome while a mutable column or header-button edit remains open
- Open Processes child editors during a Common or glyph shell rebuild, because page cleanup owns and closes those windows
- In-flight Startup apps, Users, or Services actions during a generic-page rebuild, because operation ownership remains on the disposed page

## Other App Audit - 2026-08-30

This is a follow-up inventory, not a claim that every item below is implemented.

### Common impact

The common standalone dictionaries now use `CommonAXAMLResourceStore<TResource>`, and open `SettingsWindowCommon` shells rebuild after `CommonAXAMLHotReload.ResourcesReloaded`.

Surfaces outside a settings shell still need their own common-event callback when they consume these dictionaries. In particular, audit already-open app flyouts using `FlyoutSlider`, `FlyoutCards`, `FlyoutFrame`, or `FlyoutUndockButton`, plus open update-confirmation, color-picker, and installer/uninstaller dialogs.

No non-settings app surface currently subscribes to `CommonAXAMLHotReload`. Already-open Battery, Brightness, Fan, and Volume flyouts therefore retain copied common-control values until their host rebuilds. Reuse each host's existing guarded or queued rebuild path when adding that subscription.

`UpdateConfirmationWindowResources` remains the reference replacement-dictionary implementation for an open window that rebuilds its local resources. The window still copies common Settings UI and glyph values without subscribing to their reload events, so it is not a complete cross-dictionary example.

### Battery

- Glyph and theme dictionaries already reload.
- There is no app layout AXAML dictionary, so the remaining issue is extraction rather than a broken reload path.
- Highest-value future work: create flyout and settings dictionaries for `BatteryFlyoutWindow` and `BatterySettingsWindow`; use the flyout's existing queued rebuild and `RebuildShell(CurrentPageKey)` for settings.
- Keep power-scheme identifiers, timeouts, charge thresholds, runtime fill width, and drag transforms in code.

### Brightness

- `BrightnessFlyoutWindow.axaml` is live because `InitializeComponentState()` calls `RebuildVisual()`.
- `BrightnessSettingsUIResources` is still a direct instance and needs a standalone store plus settings-shell rebuild.
- Remaining flyout extraction candidates include disabled/action/thumb/row opacities and the explicit static flyout font.
- `Flyout.OffscreenPosition` is unused; delete it rather than treating it as reloadable.
- An open `BrightnessTrayMenuWindow` retains the check-mark glyph in immutable entries; close it on glyph reload.
- Larger follow-up: environmental editor and map picker need their own dictionaries and repaint/rebuild paths. The display-identifier overlay consumes app-theme colors only when constructed, so close/recreate that transient overlay on theme reload.

### Fan Control

- The main `FanFlyoutWindow` is source-live, but `InitializeComponentState()` calls `RebuildVisual()` directly and bypasses the pointer-capture, hidden-window, and reentrancy guards in `RequestFanRebuild()`. Route reloads through guarded/deferred rebuild logic before considering it safe.
- `ProbeDataSelectorWindow`, `FanPropertiesWindow`, and `FanCurveEditorWindow` currently refresh only root geometry. Their copied control properties remain stale.
- `ProbeDataSelectorWindow` can use its transactional `RebuildContent()` after reload, but it needs a real pending-rebuild flag while a gesture pointer is captured. Its current glyph callback rebuilds immediately.
- `FanPropertiesWindow` needs an apply-layout pass or content-generation rebuild, including reapplying its app-theme title-bar background.
- `FanCurveEditorWindow` must at least reassign `_editor.EditorLayout`; its other copied controls still need an apply or rebuild path.
- Those three child windows also copy common Settings UI values, and the curve editor additionally copies `SearchableListBox` values. Their local and `CommonAXAMLHotReload` callbacks must converge on the same safe apply/rebuild path.
- `FanFlyoutCellResources` is a cached standalone dictionary. Put presentation widths in the parent flyout dictionary or add a store/event; keep slider maximum as behavior in code.
- Curve render opacities, strokes, fonts, thumb/pill geometry, and the main flyout's explicit static font remain extraction candidates.
- The open add-item menu retains group/probe glyph text and survives a main content-generation rebuild; close it on glyph or relevant resource reload.
- Unused keys to delete or wire are flyout `OffscreenPosition`, `HeaderIconButtonCornerRadius`, `ModeButtonGroupedMargin`, `DragGhostOpacity`, and `DropMarkerCornerRadius`, plus probe selector `TransformRowMargin`.

### Network

- Glyph and theme dictionaries already reload.
- No app layout AXAML dictionary exists.
- The repeated hotkey-page geometry should be commonized with Battery, Brightness, Fan, and Volume, then settings shells should rebuild on reload.
- Preserve Network-specific behavior rather than forcing parity with other apps.

### Volume

- `VolumeFlyoutWindow.axaml` is live because `InitializeComponentState()` calls `Rebuild()`.
- Remaining flyout extraction candidates are muted/inactive opacities, device outline opacity, and the explicit static flyout font.
- `Flyout.OffscreenPosition` is unused; delete it rather than treating it as reloadable.
- Volume settings has no app layout dictionary. Its hotkey page should use the future common hotkey resources.

### Priority Order For Follow-Up

1. Fix the three partially live Fan windows; existing AXAML currently gives a false impression of reload coverage.
2. Make `BrightnessSettingsUIResources` and `FanFlyoutCellResources` source-live.
3. Subscribe non-settings consumers of common dictionaries to `CommonAXAMLHotReload`. Battery, Brightness, and Volume can reuse existing queued/guarded rebuild paths; Fan must use `RequestFanRebuild()` rather than calling `RebuildVisual()` directly.
4. Commonize repeated hotkey-page presentation resources.
5. Extract Battery layout and the larger Brightness/Fan custom-rendered surfaces.

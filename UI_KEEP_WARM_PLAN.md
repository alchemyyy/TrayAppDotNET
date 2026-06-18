# UI Keep-Warm Plan

## Goal

Add a real, user-configurable UI keep-warm system for TrayAppDotNET apps built on Avalonia 12 and Native AOT.

This replaces one-shot "prewarm" behavior with an explicit lifetime policy:

- Keep supported UI windows realized and hidden for fast first/opened-again display.
- Let users disable keep-warm per UI surface.
- When keep-warm is disabled, release hidden UI resources after a short idle timeout.
- Put the shared lifecycle machinery in `TrayAppDotNETCommon`.

## Supported Surfaces

The common system should support these UI surfaces:

- Flyout window, when the app has an Avalonia flyout.
- Tray context menu, when the app has an Avalonia tray context menu.

Settings windows are intentionally excluded. They are never primed and have no user-exposed keep-warm setting.

Current app capability mapping:

- Brightness: flyout, tray context menu.
- Volume: flyout, tray context menu.
- Fan Control: flyout, tray context menu.
- Network: tray context menu.

Network's "flyout" is a native Windows shell surface, not an Avalonia window, so it should not get a flyout keep-warm setting.

## User Settings

Add common settings with these defaults:

```csharp
public bool KeepFlyoutWarm { get; set; } = true;
public bool KeepTrayContextMenuWarm { get; set; } = true;
```

Expose toggles only for surfaces supported by the current app.

Suggested UI labels:

- Keep flyout warm
- Keep tray context menu warm

Suggested description pattern:

> Keeps this window created in the background so it opens faster. When off, hidden UI resources are released after a short idle delay.

## Common Timeout

Add a common constant in `TrayAppDotNETCommon`:

```csharp
public const int WarmWindowIdleEvictionDelayMs = 10_000;
```

This timeout applies when a surface's keep-warm setting is off.

## Behavior

### KeepWarm Enabled

When keep-warm is enabled for a surface:

1. Create the window after core app startup, scheduled at idle priority.
2. Realize it offscreen and non-activated.
3. Run layout/render work before hiding it.
4. Keep the hidden instance cached.
5. Use the cached instance on real open.
6. Hide it again when dismissed.
7. Do not schedule idle eviction while keep-warm remains enabled.

### KeepWarm Disabled

When keep-warm is disabled for a surface:

1. Do not create the window during startup.
2. Create it normally on first real open.
3. On dismiss/close, hide it and start a 10 second eviction timer.
4. If the user opens it again before the timer fires, cancel eviction and reuse it.
5. If the timer fires, close the hidden window, detach app handlers, clear references, and free UI/native resources.

This means disabled does not force immediate cold opens on every click. It keeps a short grace period for repeated use while still conserving memory.

## Invisible Priming

`PrimeAsync()` must not visibly flash, steal focus, or show a taskbar entry.

The common priming path should:

1. Run on `Dispatcher.UIThread`.
2. Create the window through an app-provided factory.
3. Save any properties that need restoration.
4. Set:

```csharp
window.ShowActivated = false;
window.ShowInTaskbar = false;
window.Opacity = 0;
window.Position = new PixelPoint(-32000, -32000);
```

5. Suppress auto-dismiss/deactivation behavior while priming.
6. Call `Show()`.
7. Call `UpdateLayout()`.
8. Await dispatcher work at `DispatcherPriority.Loaded`.
9. Optionally await one additional idle/layout pass if needed in testing.
10. Hide the window.
11. Restore normal properties needed for real user display.

The previous prewarm failed because it showed and hid immediately without waiting for Avalonia's native window, layout, and render queues to complete.

## Common Infrastructure

Add a reusable warm-window slot in `TrayAppDotNETCommon`, conceptually:

```csharp
public sealed class TrayAppDotNETWarmWindowSlot<TWindow>
    where TWindow : Window
{
    public TWindow? Cached { get; }

    public Task PrimeAsync(Func<TWindow> createWindow);
    public TWindow TakeOrCreate(Func<TWindow> createWindow);
    public void MarkDismissed();
    public void ScheduleIdleEviction();
    public void CancelIdleEviction();
    public void EvictNow();
    public void Invalidate();
}
```

Responsibilities:

- Own one cached instance for one UI surface.
- Ensure all UI work happens on the Avalonia UI thread.
- Prime hidden windows without user-visible artifacts.
- Cancel eviction when a window is reopened.
- Evict after `WarmWindowIdleEvictionDelayMs` when keep-warm is disabled.
- Close and detach hidden windows during shutdown.

The slot should not know how to build app-specific UI. Apps provide factories.

## Flyout Integration

Extend `FlyoutWindowCommon` or add a common interface so flyouts can participate in managed dismissal:

```csharp
public interface ITrayAppDotNETWarmWindow
{
    bool IsWarmPriming { get; set; }
    void DismissForWarmCache();
    void CloseForWarmEviction();
}
```

Flyout-specific notes:

- Dismiss should hide, not close, while managed by a warm slot.
- Deactivation during priming must not trigger normal auto-hide behavior.
- Existing realtime rebuild/theme behavior remains app-owned.
- Do not invalidate flyouts merely for theme/settings changes, since apps already rebuild or update live.

## Tray Context Menu Integration

`TrayMenuWindow` currently closes on deactivation and after item click. That needs to become policy-driven.

Required changes:

- Add a managed dismiss path.
- During normal use, clicking outside or pressing Escape should dismiss.
- Under a warm slot, dismiss means hide and notify the slot.
- During eviction, close the hidden window for real.
- During priming, suppress deactivation close/dismiss.

Menu contents are usually snapshots. The common warm slot should support explicit invalidation, but should not decide when data is stale.

Apps can call invalidation when they know a cached menu cannot update itself. Existing live rebuild/theme logic should remain app-owned.

## App Adapters

Each app should create small adapter methods instead of duplicating lifecycle logic.

Example responsibilities:

```csharp
private BrightnessFlyoutWindow CreateBrightnessFlyout();
private BrightnessTrayMenuWindow CreateBrightnessTrayMenu();
private BrightnessSettingsWindow CreateSettingsWindow();
```

The app remains responsible for:

- constructing app-specific windows
- attaching/detaching app-specific event handlers
- invoking real `ShowAt(...)` placement methods
- opening settings pages or flyout actions
- calling explicit invalidation where needed

The common slot remains responsible for:

- hidden priming
- caching
- reuse
- idle eviction
- shutdown eviction

## Startup Scheduling

Do not prime during the critical part of `OnFrameworkInitializationCompleted`.

Recommended flow:

1. Load settings and theme.
2. Start core services.
3. Create tray icon.
4. Complete initial tray refresh.
5. Schedule keep-warm priming with `DispatcherPriority.ApplicationIdle` or equivalent idle scheduling.

If a surface must be shown for real at startup, such as restoring an undocked flyout, that real show replaces priming for that surface.

## Native AOT Considerations

This design avoids reflection-based construction and dynamic XAML loading.

Use strongly typed factories from each app:

```csharp
() => new VolumeFlyoutWindow(_audioManager, _settings, OpenSettings)
```

This is compatible with Native AOT because:

- control/window types are statically referenced
- XAML is compiled by the app build
- no runtime reflection activation is needed
- no common dynamic type lookup is required

## Theming and Rebuilds

Do not make the common keep-warm layer responsible for theme or settings correctness.

The apps already explicitly rebuild or modify UI in realtime. Keep that model.

The common layer should manage only:

- whether a hidden window stays alive
- when hidden UI resources are released
- when invisible priming occurs

## Shutdown

On app shutdown:

1. Cancel pending prime operations.
2. Cancel eviction timers.
3. Close cached windows for real.
4. Detach app-specific handlers.
5. Clear app references.

Shutdown should not leave hidden windows alive or rely on timer callbacks.

## Verification Plan

Build:

- `BrightnessTrayAppDotNET`
- `VolumeTrayAppDotNET`
- `FanControlTrayAppDotNET`
- `NetworkTrayAppDotNET`

Manual checks per app:

1. With keep-warm enabled, launch app and verify no visible flash or focus steal.
2. Open each supported surface and confirm first open is fast.
3. Dismiss and reopen; confirm same cached path works.
4. Disable keep-warm, open and dismiss, reopen within 10 seconds; confirm reuse.
5. Disable keep-warm, open and dismiss, wait past 10 seconds; confirm resource eviction and clean next open.
6. Toggle settings while cached windows exist; confirm app-owned realtime rebuild/update still works.
7. Exit app; confirm no hidden UI survives shutdown.

## Risks

- Settings windows may need special handling to avoid taskbar/focus artifacts during priming.
- Tray menus currently close on deactivation, so their close/dismiss semantics must be changed carefully.
- Some flyouts perform work in `ShowAt(...)`; priming should avoid running real user-facing show behavior.
- Any app-specific `Closed` handlers that null references must distinguish warm-cache hide from real eviction close.

## Preferred Implementation Sequence

1. Add common settings and timeout.
2. Add common settings UI section.
3. Add `TrayAppDotNETWarmWindowSlot<TWindow>`.
4. Add invisible `PrimeAsync()` helper.
5. Update `TrayMenuWindow` dismissal semantics.
6. Add flyout/settings warm-window hooks.
7. Wire Brightness.
8. Wire Volume.
9. Wire Fan Control.
10. Wire Network context menu.
11. Build and smoke-test all apps.

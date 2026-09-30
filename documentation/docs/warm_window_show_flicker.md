# Warm flyout and tray menu show flicker

## Status

- **Local status:** Fixed for the Volume, Brightness, Fan, and Battery flyouts and for every tray menu
- **Affected baseline:** Avalonia 12.1.1 on Windows 11 with the default WinUI composition path
- **Affected surfaces:** Flyouts and tray menus shown again from a warm (reused) window

Keep the retained-frame show path in place unless an Avalonia upgrade changes how `Hide()`, `Show()`, and
`Opacity` interact with the native surface.

## Observed symptom

Opening a warm flyout drew the previous flyout, then a blank frame, then the fresh flyout. Each step lasted
about one frame at 144 Hz, too short to capture with a 60 fps recording. The requested behavior was that
the old flyout appear at once and the fresh controls draw over it with nothing blank in between.

Warm tray menus had the same problem. They vanished for one to three frames on every re-show, and their
old frame could flash at the top-left corner of the work area before the menu appeared at the cursor.

## Root cause

On Windows 11, Avalonia renders each window through WinUI composition. The window has no redirection
bitmap, and its content is a drawing surface that the render thread presents.

1. `Window.Hide()` stops rendering before it hides the native window
   ([`Window.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Window.cs),
   [`WindowImpl.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Windows/Avalonia.Win32/WindowImpl.cs)).
   Nothing clears the drawing surface, so it keeps the last presented frame.
2. `Window.Show()` renders nothing before `ShowWindow`. DWM therefore presents that retained frame the
   moment the window appears. This is the "cold flyout" seen first.
3. `Window.Opacity` is not a native window property. It is copied into the composition visual when a
   batch is committed
   ([`Visual.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Visual.cs),
   [`Visual.Composition.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Visual.Composition.cs)),
   and the compositor skips any subtree whose opacity is effectively zero
   ([`ServerCompositionVisual.Render.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Rendering/Composition/Server/ServerCompositionVisual/ServerCompositionVisual.Render.cs)).
4. The render job runs at `DispatcherPriority.Render` (4), ahead of `Loaded` (1)
   ([`DispatcherPriority.cs`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Threading/DispatcherPriority.cs)).

The old flyouts set `Opacity = 0` before hiding and before showing, then set it back to 1 in a `Loaded`
callback. The `Opacity = 0` set before `Hide()` was never drawn, so the retained frame was fully opaque.
On the next show the sequence was:

1. DWM shows the retained frame.
2. The first commit after `Show()` carries `Opacity = 0`, so the first rendered frame is fully transparent.
3. The `Loaded` callback sets `Opacity = 1`, and the fresh flyout appears one frame later.

Step 2 is a race: a synchronous `WM_PAINT` commit sometimes merged the two batches. The stale frame in
step 1 appeared every time. A window that has never rendered has no retained frame, because its WinUI
target is created on the first render, so only warm reuse showed the stale frame.

Tray menus used the same transparent reveal and staged the window at the work-area origin before moving
it to the cursor, which is why a stale menu frame could appear in the corner.

### Why keep-warm looked ineffective

The setting persisted and the warm window was reused, but four things hid the benefit:

- Every open rebuilt the flyout behind the transparent reveal, so a warm open looked no faster than a
  cold one.
- Volume and Brightness discarded the hidden flyout's content on any change while hidden. `Show()` then
  collapsed the window and clipped the retained frame.
- Every settings save re-primed the cached window offscreen at opacity 0, replacing its retained frame
  with a blank one.
- The Brightness flyout owns the curve session (`BrightnessFlyoutSession` owns
  `EnvironmentalCurveService`). With keep-warm off, the idle eviction closed the flyout, which stopped
  curve automation and left hotkeys and tray scrolling without a target.

## Implemented fix

### Flyouts

The retained frame is used as the cold flyout rather than hidden:

1. `FlyoutWindowCommon.ShowWithRetainedFrame` shows the window at the position resolved for its current
   size. After the first reveal it keeps `Opacity = 1`, so DWM presents the retained frame at once.
2. In the same dispatcher turn, `ShowAt` builds the fresh content generation, commits it, runs layout,
   and positions the window. The first frame rendered afterwards is the fresh flyout, and it replaces
   the retained frame directly.
3. The `Loaded` callback calls `FlyoutWindowCommon.CompleteReveal`, which only matters for a flyout that
   has never been revealed. That flyout has no retained frame, so it stays transparent until the complete
   first frame is ready.
4. `Hide()` no longer touches `Opacity`.

The fresh controls replace the old frame in a single committed frame rather than being layered over a
snapshot. On screen this is the same as drawing over it, and it needs no extra rendering.

Supporting changes:

- Volume and Brightness keep the stale generation attached while hidden and only mark a rebuild as
  pending, so the window keeps the size of its retained frame.
- Volume applies restored and bottom scroll offsets synchronously after layout. A deferred scroll would
  render one frame at offset zero first.
- Queued rebuild jobs in Volume, Brightness, and Battery exit when a synchronous rebuild, such as the one
  in `ShowAt`, has already consumed them, so an open does not swap content twice.
- The Fan reveal job no longer skips when a newer generation was committed first. That guard could leave
  a first-time Fan flyout invisible.
- `TrayAppDotNETWarmWindowSlot.PrimeAsync` primes only windows the slot creates, so saving settings no
  longer blanks a cached window's retained frame.
- Brightness keeps its flyout warm regardless of `KeepFlyoutWarm` and no longer shows that toggle, because
  the flyout hosts the curve session. It also re-anchors the flyout after every committed rebuild, since a
  queued rebuild could change the height after the reposition had already run.

Relevant repository locations:

- `TrayAppDotNETCommon/src/UI/FlyoutWindowCommon.cs`
- `TrayAppDotNETCommon/src/UI/WarmWindows/TrayAppDotNETWarmWindow.cs`
- `VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs`
- `BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs`
- `BrightnessTrayAppDotNET/src/App.cs`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs`
- `BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs`

### Tray menus

`ContextMenuWindow.ShowAt(trayIcon, cursorPoint, placement)` handles every app's tray menu. A menu that
has already been revealed is now positioned for its retained size before `Show()`, and
`TrySettleTrayMenu` constrains, lays out, and positions it in the same dispatcher turn. It never passes
through opacity 0 again. A menu that has never been revealed keeps the transparent staged reveal.

In-window menus and submenus are created for each use, have no retained frame, and are unchanged.

## Verification

Automated coverage:

- `UnrevealedShowStagesATransparentWindowOnTheTargetMonitorBeforeCreatingItsNativeSurface`
- `RevealedReshowKeepsFullOpacitySoTheRetainedFrameIsNeverPaintedTransparent`
- `WarmSlotPrimesOnlyTheWindowItCreatesSoRetainedFramesSurviveRepriming`
- `WarmTrayMenuReshowsOpaqueAtItsFinalPositionBeforeTheNextRender`

The last three fail against the previous implementation.

The flyouts were also measured frame by frame with DXGI Desktop Duplication, which delivers every frame
DWM composes. Each flyout was opened by posting the shell tray callback (`WM_USER + 1024` with
`WM_LBUTTONDOWN` and `WM_LBUTTONUP`) to the app's tray message window. Every captured frame of the
flyout rectangle was classified against the desktop before the click and the settled open flyout. No
frames were dropped in any run.

| App | Installed build: warm opens with a blank frame | Fixed build |
|---|---|---|
| Volume | 2 of 8 | 0 of 40, including a first open |
| Brightness | 15 of 16 | 0 of 24 |
| Fan | 7 of 16 | 0 of 24 |

On the installed builds, the stale frame appeared about 10 ms after the click, the blank frame at about
17 ms, and the fresh flyout at about 24 ms. With the fix, the flyout is visible from the first captured
frame, 5 to 14 ms after the click, onward. A first open shows nothing until the complete flyout appears.

For tray menus, the installed Volume menu vanished on 10 of 10 warm re-shows. The menu fix is covered
by the automated test above but was not captured at runtime.

Every Brightness flyout open currently triggers a full monitor refresh with DDC traffic. Keep automated
Brightness open and close cycles few and spaced.

## Trade-offs

- If the flyout's height changed since the last show, the retained frame has the old size for about one
  frame while the resize lands.
- Anything visible when the flyout was hidden, such as a hover or pressed state, is part of the retained
  frame for about one frame.
- Every open still builds a fresh generation. Skipping that would need per-app hooks to refresh values
  that are only written at build time.

## Rules for future UI code

1. Never set `Opacity = 0` to hide a flyout or tray menu that has already been revealed.
2. Show a flyout through `ShowWithRetainedFrame`, then publish content, run layout, and position the
   window in the same dispatcher turn.
3. Call `CompleteReveal` from the `Loaded` callback.
4. Keep a hidden flyout's content attached and mark it stale instead of discarding it.
5. Apply scroll offsets synchronously after layout, not in a deferred callback.
6. Do not prime a warm window that already exists.
7. A queued rebuild must skip when a synchronous rebuild already published the change.

## Maintenance notes

- After an Avalonia upgrade, re-check whether `Hide()` still stops rendering before hiding, whether
  `Show()` still renders nothing before `ShowWindow`, and whether `Opacity` is still applied only at commit.
- If Avalonia starts clearing the surface on hide, the retained frame disappears and a warm open behaves
  like a first open. That is not a flicker, but the instant appearance is lost.

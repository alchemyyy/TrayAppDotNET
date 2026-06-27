# UI And Tray Playbook

Use this for tray icon flicker, stale tooltip, hover scroll failures, shell notification freezes, flyout responsiveness, slider capture, and high-frequency UI updates.

## First Files

- Common shell icon:
  - `TrayAppDotNETCommon/src/UI/Tray/TrayAppDotNETShellTrayIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/NativeIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderer.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderQueue.cs`
- Shared throttling and timing:
  - `TrayAppDotNETCommon/src/Services/AsyncThrottler.cs`
  - `TrayAppDotNETCommon/src/TimeConstants.cs`
- App render inputs:
  - `VolumeTrayAppDotNET/src/UI/Tray/VolumeTrayIcon.cs`
  - `BrightnessTrayAppDotNET/src/Visuals/BrightnessTrayIcon.cs`
  - App-specific `src/App.cs`
- Flyout/slider capture:
  - `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs`
  - `VolumeTrayAppDotNET/src/UI/Flyout`
  - `FanControlTrayAppDotNET/src/UI/Flyout`
- Render-thread process-row hover:
  - `TaskManagerTrayAppDotNET/src/UI/ProcessRowHoverVisual.cs`
  - `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsCanvas.cs`
  - `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsPage.cs`
  - `TrayAppDotNETCommon/src/Interop/User32.cs`

## Rules

- Avoid arbitrary fixed-time throttles in high-frequency UI, tray, audio, hardware, or IPC paths.
- Prefer state dedupe, coalescing, backpressure, and the existing `AsyncThrottler`.
- If a small delay is truly needed, name it in `TimeConstants.cs`.
- Do not rebuild or replace active slider controls while the user is dragging them. Preserve pointer capture.
- Keep slow backend work off the UI thread where feasible:
  - Audio writes
  - Hardware polling
  - Skia icon generation
  - Shell update preparation
- Marshal only final UI/shell updates to the UI thread.
- For tray icon dedupe, compare the computed render input against applied and pending render input.
- Preserve shell icon and tooltip semantics when skipping image generation.
- Tooltip state can update locally immediately, but avoid high-frequency shell `NIM_MODIFY` calls unless the icon changes or tooltip sync is needed.
- When clicks still work but hover scroll, tooltip, or icon are frozen, suspect shell notification/message-window state rather than the whole app being dead.

## Render-Thread Cursor-Sampled Hover

Use this pattern only for transient, decorative feedback that must continue updating while the Avalonia UI thread is
briefly busy. Keep clicks, selection, keyboard handling, accessibility, header resizing, and other semantic input on
the normal Avalonia UI path.

### Why A Composition Visual Alone Is Not Enough

- `PointerMoved` is delivered through the UI thread. Reading `GetCursorPos` in that handler avoids stale queued event
  coordinates, but the handler still cannot run while the UI thread is stalled.
- Assigning `Offset`, `Size`, or `IsVisible` on a normal composition visual from the UI thread still requires those
  property changes to reach the compositor. It does not make hover state render-thread-owned.
- A dispatcher timer or UI-thread polling loop has the same scheduling limitation and adds latency or extra wakeups.

For native-style process-row hover, the cursor sample, hit test, hover transition, invalidation, and paint decision all
run in a `CompositionCustomVisualHandler`.

### Task Manager Architecture

1. `ProcessDetailsCanvas` publishes immutable `ProcessRowHoverGeometry` only when structural state changes. That state
   contains the table viewport, visible row count, header and row heights, sticky-header position, and whether row
   hover is currently enabled.
2. `ProcessDetailsPage` passes the geometry to the non-hit-testable `ProcessRowHoverVisual` overlay.
3. `ProcessRowHoverVisual` creates a `CompositionCustomVisual` when attached and sends it immutable render-state
   messages. Do not share mutable UI objects with its handler.
4. `ProcessRowHoverHandler.OnAnimationFrameUpdate` samples the current Win32 cursor, hit-tests the row, invalidates
   only the previous and current row rectangles, and registers for the next animation-frame update.
5. `OnRender` samples once more immediately before drawing to minimize the sample-to-paint gap. It draws with one
   immutable brush and does not create controls, view models, or formatted text.

The render thread never sends a per-frame message back to the UI thread. Scrolling, layout, column interaction, row
count changes, and DPI changes cause the UI thread to send a new structural snapshot instead.

### Win32 Cursor And Coordinate Mapping

The composition handler uses this sequence:

1. `GetCursorPos` returns the current cursor in physical screen pixels.
2. `WindowFromPoint` and `GetAncestor(..., GA_ROOT)` verify that the cursor is over the owning top-level HWND. This
   prevents a hover from showing through another top-level window.
3. `ScreenToClient` converts the cursor to physical client pixels for that HWND.
4. Divide by `TopLevel.RenderScaling`, then subtract the hover host's client-DIP origin from
   `TranslatePoint(default, topLevel)`:

   `localDIP = clientPhysicalPixels / renderScaling - hostOriginDIP`

5. Arithmetic hit-testing rejects points outside the viewport, inside the sticky header, beyond the visible row
   count, or while hover is suppressed by another table interaction.

Resend the coordinate map after arrange and `TopLevel.ScalingChanged`. Do not reach through
`ImmediateDrawingContext.PlatformImpl.Transform`: that implementation detail is not exposed by Avalonia's NuGet
reference API, and reflection would be fragile under Native AOT.

### Scheduling, Invalidation, And Lifetime

- Call `RegisterForNextAnimationFrameUpdate` from every active animation callback, including callbacks where the
  cursor is outside the table. Otherwise entry cannot be detected without a UI-thread event.
- Run the sampling clock only while the top level is visible and not minimized. Stop it on detach or disposal.
- Track the hovered visible-row index inside the custom visual handler. On a transition, invalidate only the old and
  new full-width row bounds.
- Keep the per-frame path allocation-free: Win32 structs, scalar arithmetic, geometry hit-testing, and an existing
  immutable brush only.
- Use `SendHandlerMessage` for immutable structural snapshots and start/stop signals. Handler-owned state stays on the
  render thread.
- Do not invalidate the normal process table layers for pointer motion. The hover overlay is independently dirty.

### Verification

1. Unit-test geometry boundaries: viewport edges, sticky-header exclusion, horizontal clipping, disabled state,
   visible-row limit, and returned row bounds.
2. Run with `GPUPreferred`, including a non-100-percent display scale, and verify row entry, row exit, scrolling,
   sticky headers, resizing, minimization, restoration, and occlusion by another top-level window.
3. In a disposable runtime instance, suspend only the Avalonia UI thread, move the cursor between process rows, and
   confirm the hover still follows. Always resume the thread in a `finally` path. This is the decisive test that the
   hover transition is not UI-thread-dependent.
4. Smoke-test a Release Native AOT build. Avoid reflection or dynamically generated interop in this path.

## Common Symptoms

### Flickering Tray Icon

Check:

1. Whether a new icon is generated when render input is unchanged.
2. Whether shell icon is temporarily nulled before the replacement is ready.
3. Whether render work can happen off-thread before a single shell swap.
4. Whether all apps use the same common queue/dedupe path.

### Tooltip Disappears During Wheel Scroll

Check:

1. Whether wheel scroll causes a shell icon update.
2. Whether tooltip sync is coupled to icon replacement.
3. Whether tooltip text changes are local-only until explicitly requested.

### Slider Drops Thumb During Drag

Check:

1. Whether flyout rows/cards are rebuilt during drag.
2. Whether backend volume/hardware events trigger `PropertyChanged` flooding.
3. Whether the active slider instance is replaced while captured.
4. Whether feedback sounds or backend writes block the UI thread.

### Native AOT Freeze

Check:

1. Running process state.
2. Message pump responsiveness.
3. Shell notification icon callbacks.
4. Audio/hardware COM callback threading.
5. UI-thread blocking operations.
6. Dump evidence if the app is already frozen and the user wants diagnosis.

## Verification

- Do not build unless you believe compile time errors or compile time test failures are likely
- For tray/freeze fixes, verify the exact behavior the user reported.
- For watcher-mode runtime changes, launch the app normally and confirm watcher plus monitored process stay alive when feasible.
- For no-watcher tests, use the existing project mechanism or user-provided command. Do not invent a new launch architecture.

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

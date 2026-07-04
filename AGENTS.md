# AGENTS.md

These companion files can be found in the `.agents` folder. Read them only when the task needs them:

- `PROJECT_MAP.md` for TrayAppDotNET topology and common entry points.
- `UI_TRAY_PLAYBOOK.md` for tray icon, flyout, tooltip, freeze, and high-frequency UI work.
- `AXAML_PLAYBOOK.md` for Avalonia resource extraction and visual tuning.
- `NATIVE_AOT_DUMP_ANALYSIS_TOOLING.md` special case toolkit for debugging native aot builds

## Acronyms and shortnames
  - `batadn` means `BatteryTrayAppDotNET`
  - `brtadn` means `BrightnessTrayAppDotNET`
  - `fctadn` means `FanControlTrayAppDotNET`
  - `ntadn` means `NetworkTrayAppDotNET`
  - `vtadn` means `VolumeTrayAppDotNET`

## Project Defaults

- TrayAppDotNET is an x64-only Windows Avalonia tray app workspace unless the user says otherwise.
- Cross-app tray, Avalonia startup, update, install, shell-notification, common controls, and shared UI behavior should live in `TrayAppDotNETCommon` when practical.
- App-specific behavior belongs in the individual app folders:
  - `BatteryTrayAppDotNET`
  - `BrightnessTrayAppDotNET`
  - `FanControlTrayAppDotNET`
  - `NetworkTrayAppDotNET`
  - `VolumeTrayAppDotNET`
- NetworkTrayAppDotNET may intentionally differ from the other apps. Verify before forcing parity.
- Installed/runtime behavior matters more than compile-only success. For packaging, update, watcher, or startup changes, run a real runtime smoke test when feasible.

## Task Routing

- Tray icon flicker, stale tooltip, hover scroll, or shell freeze: read `UI_TRAY_PLAYBOOK.md`.
- Avalonia constants, AXAML resources, settings windows, visual card layout, scrollbars, and styling: read `AXAML_PLAYBOOK.md`.
- New sessions that need codebase orientation: read `PROJECT_MAP.md` before broad `rg --files` inventory.
# AGENTS.md

These companion files can be found in the `.agents` folder. Read them only when the task needs them:

- `PROJECT_MAP.md` for TrayAppDotNET topology and common entry points.
- `UI_TRAY_PLAYBOOK.md` for tray icon, flyout, tooltip, freeze, and high-frequency UI work.
- `AXAML_PLAYBOOK.md` for Avalonia resource extraction and visual tuning.
- `AXAML_HOT_RELOAD_PLAYBOOK.md` for making AXAML edits update already-open UI and auditing reload gaps.
- `NATIVE_AOT_DUMP_ANALYSIS_TOOLING.md` special case toolkit for debugging native aot builds
- `PYTHON_TOOLING.md` for Python linting and formatting commands.

## Acronyms and shortnames
  - `batadn` means `BatteryTrayAppDotNET`
  - `btadn`, `brtadn` mean `BrightnessTrayAppDotNET`
  - `fctadn` means `FanControlTrayAppDotNET`
  - `ntadn` means `NetworkTrayAppDotNET`
  - `vtadn` means `VolumeTrayAppDotNET`
  - `tadnc`, `tadncommon` means `TrayAppDotNETCommon`
  - `commonize` means to consolidate into `TrayAppDotNETCommon`

## Project Defaults

- TrayAppDotNET is an x64-only Windows Avalonia tray app workspace.
- For PlantUML documentation changes, edit only `*.puml` source files. Do not edit or regenerate `*.svg` files.
- Cross-app tray, Avalonia startup, update, install, shell-notification, common controls, shared behavior, etc. should live in `TrayAppDotNETCommon` when practical.
- App-specific behavior belongs in the individual app folders:
  - `BatteryTrayAppDotNET`
  - `BrightnessTrayAppDotNET`
  - `FanControlTrayAppDotNET`
  - `NetworkTrayAppDotNET`
  - `TaskManagerTrayAppDotNET`
  - `VolumeTrayAppDotNET`
- NetworkTrayAppDotNET may intentionally differ from the other apps. Verify before forcing parity.
- Installed/runtime behavior matters more than compile-only success. For packaging, update, watcher, or startup changes, run a real runtime smoke test when feasible.

## Task Routing

- Tray icon flicker, stale tooltip, hover scroll, or shell freeze: read `UI_TRAY_PLAYBOOK.md`.
- Avalonia constants, AXAML resources, settings windows, visual card layout, scrollbars, and styling: read `AXAML_PLAYBOOK.md`.
- New sessions that need codebase orientation: read `PROJECT_MAP.md` before broad `rg --files` inventory.
- Python linting or formatting: read `PYTHON_TOOLING.md` before invoking Ruff.

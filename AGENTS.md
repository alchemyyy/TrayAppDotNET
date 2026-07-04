# AGENTS.md

This file is the concise operating guide synthesized from the audited Codex sessions in this directory.

Read the companion files only when the task needs them:

- `WORKFLOW.md` for user interaction, subagents, workspace targeting, and stop modes.
- `PROJECT_MAP.md` for TrayAppDotNET topology and common entry points.
- `UI_TRAY_PLAYBOOK.md` for tray icon, flyout, tooltip, freeze, and high-frequency UI work.
- `AXAML_PLAYBOOK.md` for Avalonia resource extraction and visual tuning.
- `BUILD_GIT_RELEASE_PLAYBOOK.md` for builds, tests, Git, GitHub Actions, submodules, and releases.
- `AUDIT_SUMMARY.md` for the evidence-backed patterns this file is based on.

## Hard Rules

- Verify the intended workspace root from the latest user message before writing. This repo has multiple active clones and worktrees.
- Follow repo style over global style inside vendored or externally maintained code, especially LibreHardwareMonitor.
- Respect explicit stop modes exactly:
  - "just reply", "just answer", "dont edit", "do not edit yet", "just discuss", "pause", or "what are you doing" means answer directly and do not edit, build, commit, push, or keep investigating.
  - "that is it" or "nothing more" means do only the named action and keep the response minimal.
- Respect Git constraints exactly:
  - "dont commit" means no commit until a later explicit commit request.
  - "leave git alone" or "dont verify yourself with git commands" means no Git commands.
  - "commit but dont push" means no push.
  - "squash onto the latest commit" means amend the relevant change into the latest commit, not a broader Git cleanup.
- Assume dirty worktrees are normal. Do not revert, restore, stage, or inspect unrelated changes unless required by the task or explicitly requested.
- If the user asks "where is X controlled or located", answer with exact file, symbol/resource name, and the shortest useful explanation.
- Prefer local source analysis over web/upstream research when the local source exists. Do not search upstream for code the user already has in the workspace.

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
- Build, publish, Native AOT, runtime layout, update installer, workflow, submodule, or release tasks: read `BUILD_GIT_RELEASE_PLAYBOOK.md`.
- New sessions that need codebase orientation: read `PROJECT_MAP.md` before broad `rg --files` inventory.
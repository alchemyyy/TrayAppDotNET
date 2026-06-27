# AXAML And Avalonia Playbook

Use this for Avalonia UI primitive extraction, AXAML resources, settings pages, visual layout, custom controls, scrollbars, card sizing, and resource URI work.

For source edits that must update already-open UI, also read `AXAML_HOT_RELOAD_PLAYBOOK.md`.

## Scope Rules

- Extract presentation primitives to AXAML resources (non-exhaustive):
  - Widths
  - Heights
  - Margins
  - Paddings
  - Thicknesses
  - Corner radii
  - Font sizes
  - Opacities
  - Simple layout ratios
- Do not extract:
  - Behavior
  - Localizable strings
  - Dynamic palette/theme colors
  - Caller-provided values
  - Values that are computed from runtime state
- Preserve behavior. AXAML extraction should be a resource relocation, not a semantic rewrite.
- For broad extraction requests, list included and excluded folders before editing.
- If the user says "everything in this folder", inspect helper/control files too, not only windows.

## Established Files

- Common settings window:
  - `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs`
  - `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.axaml`
- Common bindings:
  - `TrayAppDotNETCommon/src/UI/CommonBindings.cs`
  - Matching AXAML dictionary when present.
- Common controls:
  - `TrayAppDotNETCommon/src/UI/Controls`
  - `TrayAppDotNETCommon/src/UI/Controls/ControlAXAMLResources.cs`
- Resource reader:
  - `TrayAppDotNETCommon/src/UI/HotReloadResourceReader.cs`
- Fan flyout:
  - `FanControlTrayAppDotNET/src/UI/Flyout`
- Fan curves:
  - `FanControlTrayAppDotNET/src/UI/Curves`

## Extraction Procedure

- Inventory target files and existing AXAML/resource-reader conventions.
- Search for primitives (this list is non-exhaustive):
   - `new Thickness`
   - `new CornerRadius`
   - `FontSize`
   - `Width`
   - `Height`
   - `Margin`
   - `Padding`
   - `Opacity`
   - `const double`
   - `const int`
   - `GridLength`
- Create or extend the nearest matching AXAML resource dictionary.
- Replace literals with resource reads or resource bindings without changing behavior.
- Run a bad URI sweep for accidental `avares://.../src/...` resource paths.

## URI Rules

- Use the repo's established AXAML URI shape.
- Existing common example:
  - `avares://TrayAppDotNETCommon/UI/SettingsWindowCommon.axaml`
- Do not include physical `src` in `avares://` URIs unless the repo already does so for that resource.

## Constructor Rules

- Do not blindly add runtime state initialization to AXAML/default constructors.
- If a default constructor is needed for designer or AXAML activation, keep it side-effect light.
- Guard all runtime-only fields and close handlers.
- If the user challenges a line, explain what it does, why it was added, whether it is needed, and remove or fix it if the justification is weak.

## Visual Tuning Rules

- The user cares about exact layout and visual fidelity:
  - Card alignment
  - Full-height fills
  - Pixel-level thickness
  - Custom scrollbars
  - Auto-fit width and height
  - Hover/selected/disabled states
  - Derived dimensions from one primary size variable
- Prefer explicit named constants/resources for tunable sizes, margins, padding, and colors.
- When dimensions are related, expose one primary variable and derive the rest when feasible.

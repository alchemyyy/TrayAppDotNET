# FanControlTrayAppDotNET

A tray fan controller and hardware monitor, part of [TrayAppDotNET](../README.md). It is a UI around
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (vendored as a submodule under
`vendor/LibreHardwareMonitor`, together with NCalc, HIDSharp and DiskInfoToolkit).

- A flyout of fan, group and probe cards with Manual and Curve control, per-fan properties and three profiles.
- A curve editor, sensor probe cards and speed warnings.
- Motherboard fan headers need the PawnIO driver; the app offers to download and install it (with a UAC prompt) and
  otherwise runs without it.

User documentation: [documentation/docs/apps/fan-control](../documentation/docs/apps/fan-control/index.md)
(also [Curve Editor](../documentation/docs/apps/fan-control/curve-editor.md) and
[Probe Cards](../documentation/docs/apps/fan-control/probe-cards.md)).

## Building

Build instructions, tooling and the per-app solution files are described in the [root README](../README.md#development).
Release builds publish Native AOT; see `.agents/PROJECT_MAP.md` for the build files.

Clone with submodules (`git submodule update --init --recursive`); the vendored projects build as part of the app.

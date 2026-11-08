# FanControlTrayAppDotNET

A tray fan controller and hardware monitor, part of [TrayAppDotNET](../README.md). It is a UI around
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (vendored as a submodule under
`vendor/LibreHardwareMonitor`, together with NCalc, HIDSharp and DiskInfoToolkit).

- A flyout of fan, group and probe cards with Manual and Curve control, per-fan properties and three profiles.
- A curve editor, sensor probe cards and speed warnings.
- Motherboard fan headers need the PawnIO driver; the app offers to download and install it (with a UAC prompt) and
  otherwise runs without it.
- Fan control needs administrator rights, so the system install always runs as administrator without a UAC prompt
  through Common's `TrayAppDotNETElevatedStartTask` (`AppServices.ElevatedStart`): a per-account scheduled task,
  `\TrayAppDotNET\FanControlTrayAppDotNET-<SID>`, that the system install registers for the installing account
  (`TrayAppDotNETRunAsAdministratorOption` with `IsRequired`, no installer check box and no setting). An unelevated
  plain start (no arguments, `--autostart`, `--hidden`) hands off to it in `Program.Main`; when the account has no
  usable task (another account installed the app, or the task was changed or deleted), the start registers it again
  through one UAC prompt (`--set-elevated-start on`) and then hands off. Declining starts the app unelevated. Copies
  outside Program Files and `--settings-dir` starts never use the task. A system uninstall deletes every account's task.

User documentation: [documentation/docs/apps/fan-control](../documentation/docs/apps/fan-control/index.md)
(also [Curve Editor](../documentation/docs/apps/fan-control/curve-editor.md) and
[Probe Cards](../documentation/docs/apps/fan-control/probe-cards.md)).

## Building

Build instructions, tooling and the per-app solution files are described in the [root README](../README.md#development).
Release builds publish Native AOT; see `.agents/PROJECT_MAP.md` for the build files.

Clone with submodules (`git submodule update --init --recursive`); the vendored projects build as part of the app.

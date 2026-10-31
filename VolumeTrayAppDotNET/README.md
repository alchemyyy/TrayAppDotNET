# VolumeTrayAppDotNET

A volume mixer and audio endpoint manager, part of [TrayAppDotNET](../README.md), in the spirit of EarTrumpet.

- A bottom-up flyout of device cards: each playback or recording device with its volume slider, peak meter and the
  apps playing on it, with an optional logarithmic app volume scale.
- Device management: default and communications default, renaming, sample rate and format, visibility, Equalizer APO
  install and removal.
- Bluetooth: paired headphones and speakers get a card, connect and disconnect from the flyout, battery level.
- Communications ducking, scroll-wheel volume on the tray icon (no global mouse hook) and hotkeys.

User documentation: [documentation/docs/apps/volume](../documentation/docs/apps/volume/index.md) and
[Volume Settings](../documentation/docs/apps/volume/settings.md).

## Building

Build instructions, tooling and the per-app solution files are described in the [root README](../README.md#development).
Release builds publish Native AOT; see `.agents/PROJECT_MAP.md` for the build files.

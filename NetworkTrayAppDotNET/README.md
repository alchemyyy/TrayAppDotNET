# NetworkTrayAppDotNET

A network tray icon, part of [TrayAppDotNET](../README.md).

- Left-click opens the Windows 10 network flyout that Windows still ships (or, by setting, the Windows 11 Quick
  Settings panel, the available networks list or the Wi-Fi settings page).
- The tray icon shows the connection state, in state colors or one static color (**Tray icon style**).
- The tray menu opens Network & internet settings, the adapter list (Network Connections) or the app's settings;
  modified clicks and hotkeys can open settings or the adapter settings window.

Network has no flyout of its own, so flyout undocking and flyout settings don't apply to it.

User documentation: [documentation/docs/apps/network](../documentation/docs/apps/network/index.md).

## Building

Build instructions, tooling and the per-app solution files are described in the [root README](../README.md#development).
Release builds publish Native AOT; see `.agents/PROJECT_MAP.md` for the build files.

# BrightnessTrayAppDotNET

A Windows 11 tray application for actual external monitor brightness control using DDC/CI, with some extras.

#### quick demo

https://github.com/user-attachments/assets/945e9c37-30a7-40dd-910f-842f5ea4a049

## Features
* Highly responsive DDC/CI control, with a robust recovery and verification system.
* Robust Windows Night light control
* Scrollwheel interactions with tray icon, extended mouse and modifier quick actions
* Hotkeys
* Automatic environmental controls - sunrise / sunset interactive curve editor
* Master control slider, slider offsets, synchronization, etc.
* Hot-swappable profiles
* Flyout customization (visibility of features, docking, slider tracking, user inputs)
* Themeability (live color pickers, light/dark mode, glyph customization, look and feel, etc.)
* Single packaged portable exe with fully self-contained install and update system.
* and more.

### Distant Future Features
* nightlight - it is absolutely *possible* for it to work per-monitor. but it'd diverge from the built in night light, and would be a nightmare to get working right.
* gamma lut manipulation - put in "ultra dim" mode that works the same way tools like f.lux do. this should only work on the master slider since screwing with luts is a global thing. so master slider should have the power to go negative in value or something similar.


## Thanks and Credit to:
https://github.com/udivankin/sunrise-sunset for the SPA implementation I ported to C#

https://github.com/xanderfrangos/twinkle-tray for the flyout UI inspiration

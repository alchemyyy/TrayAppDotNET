# Volume Settings

Every VolumeTrayAppDotNET-specific setting, page by page, in the order the settings window shows them. What the features actually do is on the [Volume](index.md) page. The settings window itself, its search, and the shared cards (language, run on startup, installation, rendering, update checks) are covered in [Settings Window](../../shared/settings-window.md).

Some cards only appear while the setting they depend on is on, e.g. the touchpad sensitivity only shows while **Precision touchpad** is on.

## General

### Volume Changed Ping

| Setting | Default | Notes |
| --- | --- | --- |
| **Play a sound when changing playback device volume** | On | Beeps through the device you adjusted. Recording devices stay silent. |
| **Play a sound when scrolling the tray icon** | On | Only shown while the setting above is on, and needs it to work. |
| **Play a sound when changing app volume** | On | Beep at the app's new level. |
| **Skip the sound when audio is already playing** | On | Only shown while the playback device sound is on. Applies to the app sound too. |
| **Peak meter threshold (%)** | 5 | 0 to 100. Recent audio level above this (scaled by the volume being set) skips the beep. Lower is stricter. |

### Other

| Setting | Default | Notes |
| --- | --- | --- |
| **Use logarithmic volume scale** | Off | App sliders only: a decibel curve (6 dB per 10% of travel), and their percent shows the slider position instead of the level Windows stores. Device sliders and tray icon scrolling stay as they are. See [Volume](index.md). |
| **Wheel volume step (%)** | 2 | 1 to 100. Per scroll wheel notch, over the tray icon and over flyout sliders. Same value as the card on the Tray icon page. |

## Flyout

| Setting | Default | Notes |
| --- | --- | --- |
| **Restore undock state on startup** | On | Shared undock behavior, see [Tray Icon and Flyouts](../../shared/tray-and-flyouts.md). |

### Visibility

| Setting | Default | Notes |
| --- | --- | --- |
| **Show undock button** | On | Off keeps the flyout anchored to the tray. |
| **Keep undocked flyout on screen** | On | Only shown while the undock button is on. |
| **Communications ducking button** | When ducking is on | **Always show**, **When ducking is on**, **Hidden**. |
| **Show recording devices** | On | Flyout only. **Settings** > **Devices** > **Show recording devices** still has to be on. |
| **Disconnected Bluetooth audio devices** | Always show (intermixed) | **Never show**, **Show**, **Always show**, **Always show (intermixed)**. |
| **Collapse disconnected Bluetooth devices** | On | One row per disconnected Bluetooth device instead of one per endpoint. |
| **Only show Bluetooth devices while Bluetooth is on** | On | Beats the disconnected Bluetooth setting above. |
| **Show Bluetooth power button** | On | The Bluetooth button in the flyout's title bar. |
| **Bluetooth power button click action** | Ctrl + left-click | **Left-click**, **Ctrl + left-click**, **Alt + left-click**, **Shift + left-click**. Every other click opens Bluetooth quick settings. |
| **Use dynamic playback volume glyph for playback devices in flyout** | Off | On: the mute button follows the volume level. Off: a fixed speaker glyph. |
| **Show default-format readout** | On | The channel/bit/Hz strip under each device name. |
| **Show Bluetooth codec on the format strip** | Off | Needs the app running as administrator. |
| **Sound settings button target** | Classic Sound control panel | Or **Windows Settings app**. |

### Layout

| Setting | Default | Notes |
| --- | --- | --- |
| **Device layout** | Apps above device | Or **Apps below device**. |
| **Device title row position** | Below slider | Or **Above slider**. |
| **Device sort** | Default | Or **Windows order**. |
| **Intermix recording with playback** | Off | Only shown while the flyout's **Show recording devices** is on. Off: recording devices grouped above all playback devices. |
| **Title bar at the bottom** | Off | Moves the flyout's button bar to the bottom. |

### Peak Meter

| Setting | Default | Notes |
| --- | --- | --- |
| **Unified peak meter** | On | One combined bar instead of a solid bar plus a stereo overlay. |
| **Unified meter low channel bias multiplier** | 3 | 0 to 100. Only shown with the unified meter on. 0 follows the louder channel, 1 averages both, higher leans toward the quieter channel and calms stereo flutter. |
| **Peak meter refresh rate (FPS)** | 180 | 1 to 1000. How often the meter is redrawn. |
| **Peak meter sample rate** | 90 | 1 to 1000. How often the level is read from Windows. Keep it below the refresh rate for smoothing; above it just burns CPU. |
| **Peak meter change ceiling** | 9 | 0 to 100. How far the meter can move in one redraw. 0 freezes it, 100 removes the cap. |

## Devices

| Setting | Default | Notes |
| --- | --- | --- |
| **Tie default comms device to default device** | Off | Setting a default device in this app also makes it the communications default. |
| **Show registry ghost devices** | Off | Old endpoints with no driver loaded, mostly "Unknown Device". Playback and recording. |
| **Activate recording devices for peak meters** | Off | Keeps recording devices' meters alive while the flyout is open. Windows then shows this app as using your microphones. |

### Visibility

Each of these has a **Playback** and a **Recording** switch.

| Setting | Playback | Recording | Notes |
| --- | --- | --- | --- |
| **Show recording devices** | n/a | On | Master switch for every recording device and every recording switch below. |
| **Show disabled devices** | Off | Off | Same switch as the eye button in the flyout's title bar. |
| **Show default device even if disabled** | On | On | Only shown while disabled devices are hidden. |
| **Show default comms device even if disabled** | On | On | Same. |
| **Show disconnected devices** | Off | Off | Recognized but unplugged devices. |

### Row Buttons

| Setting | Playback | Recording |
| --- | --- | --- |
| **Show exclusive-mode (lock) button** | Off | Off |
| **Show Equalizer APO button** | Off | Off |
| **Show default-device button** | On | On |
| **Show Bluetooth battery indicator** | On | On |
| **Show "Listen to this device" button** | n/a | Off |

## App Drawers

| Setting | Default | Notes |
| --- | --- | --- |
| **Expand app drawers by default** | On | Only for devices whose chevron you haven't clicked yet. |
| **Icon retry interval (ms)** | 250 | 50 to 5000. When an app's icon can't be loaded, the app retries 4 times, waiting 1x, 2x and 3x this value, then shows a generic icon. |

### Playback Drawers

| Setting | Default | Notes |
| --- | --- | --- |
| **Max apps before scrolling - full sliders** | 24 | 1 to 200. |

### Recording Drawers

| Setting | Default | Notes |
| --- | --- | --- |
| **App drawer display type** | Icon grid | Or **Full sliders**. |
| **Max apps before scrolling - full sliders** | 24 | 1 to 200. Only shown with **Full sliders**. |
| **Max rows before scrolling - icon grid** | 10 | 1 to 200. Only shown with **Icon grid**. |
| **Visually indicate actively capturing apps** | Active glyph | **Dim inactive**, **Active glyph**, **Hide inactive**, **Don't indicate**. |
| **Icon grid centering** | Off | **Off**, **Centered**, **Centered soft-max**. How the last, partly filled row lines up. |
| **Soft-max width (icons)** | 7 | 1 to 16. Only shown with **Centered soft-max**. The row stays put where a centered row of this many icons would start, until it gets longer than that, then centers. |
| **Icon scale (percent)** | 110 | 50 to 200. |
| **Icon stack direction** | Auto | **Auto**, **Top to bottom**, **Bottom to top**, **Left to right**, **Right to left**. Auto puts the first app closest to the device row. |
| **Max apps per row** | 9 | 1 to 16. Shows as **Max apps per column** with **Left to right** or **Right to left**. Same value either way. |

All the icon grid cards only show while the display type is **Icon grid**.

## Tray Icon

| Setting | Default | Notes |
| --- | --- | --- |
| **Mouse wheel** | On | Scroll over the tray icon to change the default device's volume. |
| **Wheel volume step (%)** | 2 | 1 to 100. |
| **Wheel volume step (%) - fine** | 1 | 1 to 100. |
| **Wheel volume step (%) - coarse** | 5 | 1 to 100. |
| **Precision touchpad** | On | Two-finger touchpad scrolling in 1% steps. |
| **Touchpad sensitivity** | 90 | 1 to 1000 raw touchpad units per 1% step. Lower is more sensitive. |

### Context Menu

| Setting | Default | Notes |
| --- | --- | --- |
| **Menu position** | Modern | Or **Classic** (at the cursor). |
| **Playback device names** | Name+Model | **Name and model**, **Name**, **Model**. For the per-device links. |
| **Recording device names** | Name+Model | Same. |
| **Device name max length** | 32 | 3 to 200 characters. Longer names end in `..`. |
| **Show "Recording" link in tray menu** | Off | |
| **Show "Sounds" link in tray menu** | Off | |
| **Show "Communications" link in tray menu** | Off | |
| **Show per-device links in tray menu** | Off | |

### Modified Actions

Set to **Nothing**, a Ctrl or Alt entry falls back to the plain action.

| Setting | Default | Options |
| --- | --- | --- |
| **Mouse wheel** | Wheel volume step (%) | **Nothing** or one of the three wheel steps |
| **Ctrl + mouse wheel** | Wheel volume step (%) - Fine | Same |
| **Alt + mouse wheel** | Wheel volume step (%) - Coarse | Same |
| **Ctrl + left-click** | Nothing | **Nothing**, **Open settings** |
| **Alt + left-click** | Nothing | Same |
| **Ctrl + right-click** | Nothing | Same |
| **Alt + right-click** | Nothing | Same |
| **Double-click** | Nothing | Same. **Nothing** opens the flyout. |
| **Ctrl + double-click** | Nothing | Same |
| **Alt + double-click** | Nothing | Same |

The three wheel entries are greyed out while **Mouse wheel** is off.

## Hotkeys

**Open flyout** and **Open settings**, both unbound. See [Hotkeys](../../shared/hotkeys.md).

## Theme

Most of the Theme page is shared, see [Themes](../../shared/themes.md). The Volume-only cards:

| Setting | Default | Notes |
| --- | --- | --- |
| **Font size** | 15 | 8 to 48. Size of the tray menu; everything in it scales from this. |
| **Slider indicator** | Capsule | The slider thumb shape: **Capsule**, **Circle**, **Diamond**, **Star**, **Square**, **Heart**. |
| **Peak meter color** | White | The solid peak meter bar, up to the quieter channel. |
| **Peak meter stereo color** | 50% transparent white | The overlay up to the louder channel. With **Unified peak meter** on, both bars line up. |

## Data Files

Both live in the app's data folder, see [Configuration](../../getting-started/configuration.md):

- `settings.xml`: everything above.
- `devices.xml`: per device, whether its app drawer is expanded, and the custom name of Bluetooth devices you renamed.

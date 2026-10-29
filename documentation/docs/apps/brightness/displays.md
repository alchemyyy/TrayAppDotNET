# Displays

The app changes the *actual* brightness of external monitors over DDC/CI, the control channel that runs over the same cable as the picture. Everything about the displays themselves lives in **Settings** > **Monitor options**: renaming them, limiting their range, tuning how the app talks to them, and what happens when a monitor stops answering.

DDC failures are the reason this app exists, so the app puts a lot of effort into getting a misbehaving monitor back without a restart.

## How Displays Are Controlled

Each display row in the flyout is controlled one of two ways:

- **DDC/CI**: external monitors. The app reads and writes the monitor's brightness setting directly, the same setting you'd change with the buttons on the monitor.
- **Windows brightness**: displays Windows itself can dim, i.e. laptop panels. The app goes through Windows for these. See [the main Brightness page](index.md).

### Displays Without DDC/CI

Some displays have no DDC/CI at all, or have it switched off in the monitor's own on-screen menu. TVs are the usual suspects. Such a display still shows up in the flyout, but with a warning triangle instead of the monitor icon and a locked slider. Hover the icon to see the error.

The app keeps checking these in the background, backing off to one check every 30 seconds, so it's cheap. If the monitor has a DDC/CI option in its menu, turn it on; the row unlocks on its own shortly after (opening the flyout makes it check right away).

### Helper Processes

Each DDC monitor gets its own small helper process, a copy of `BrightnessTrayAppDotNET.exe` started with `--ddc-helper-server`. Don't be surprised to see several of them in Task Manager. They exist so that one hung monitor can't freeze the app or the other monitors: if a DDC call takes too long (see [DDC operation timeout](#monitor-options)), only that monitor's helper is ended and replaced.

## Display Order and Overrides

The bottom of **Monitor options** has one card per display. That includes every display that's connected now, every display the app has seen before, and every display you've set an override on. Connected displays show their current display number, e.g. **(currently #2)**; the rest show **Disconnected**. Disconnected displays keep their settings, so they're right there when you plug the monitor back in.

Overrides are tied to the monitor itself (its EDID), not to the port or the display number, so they follow the monitor around.

### Renaming

Type a new name into **Name:** and press ++enter++ or click elsewhere. The flyout picks it up right away. Clear the box to go back to the name the monitor reports itself.

### Min and Max Brightness

**Min brightness** and **Max brightness** set a floor and a ceiling for what the app sends to that display. Defaults are 0 and 100, i.e. no limit.

The slider still runs from 0 to 100; the limit is applied to the value that goes out to the monitor. With **Max brightness** at 80, the slider at 90 or 100 both set the monitor to 80. With **Min brightness** at 20, the slider at 0 sets it to 20.

The limits apply to everything that writes brightness: sliders, the master, profiles, hotkeys, and the [day/night curve](curve.md). A change takes effect immediately. If you set the minimum above the maximum, the minimum is ignored.

### Brightness Normalization

Two different monitors at the same percentage rarely look the same. One way to even them out is to cap the brighter panel with **Max brightness** so its 100 looks like the dimmer panel's 100, and raise **Min brightness** on a panel that goes too dark at the bottom.

***Keep in mind that this clips, it doesn't rescale.*** The top (or bottom) part of the slider range just stops doing anything on that display. The other approach is to keep each display at its own level relative to the master; see [the main Brightness page](index.md) for how the master slider and per-display offsets work.

### Per-Display Timing

- **Verification delay**: overrides the global [Verification delay](#monitor-options) for this display. `-1` (default) uses the global value.
- **Brightness dwell**: overrides the global **Brightness update dwell** for this display. `-1` (default) uses the global value.

Use these when one monitor in your setup is slower than the rest, instead of slowing down all of them.

### Power Off VCP

**Power off VCP** replaces the command this display gets when you turn it off from the app. Leave it empty to use the normal command for the selected [Power off mode](#monitor-options).

It takes hex values, `0x` optional:

- `0xD6 0x05`: sends VCP code `D6` with value `05`.
- `0xD6`: just the code; the value comes from the normal command for that code.

You'd only touch this for a monitor that ignores the standard power-off command.

### Clear Displays

**Clear Displays** (after a confirmation) deletes all name overrides, the custom order, every per-display override, and the history of known displays. The list then rebuilds from the displays connected right now.

## Monitor Options

The global settings at the top of **Settings** > **Monitor options**:

| Setting | Default | Range |
|---|---|---|
| **Brightness update dwell** | 50 ms | 10 to 10000 |
| **Verification delay** | 500 ms | 0 to 10000 |
| **Verification attempts** | 4 | 1 to 20 |
| **DDC operation timeout** | 3000 ms | 500 to 60000 |
| **Allow blind DDC writes during degraded state** | On | |
| **Power off mode** | Sleep | Sleep, Power off (soft), Power off (hard) |
| **Identify displays by** | Display number | Display number, Hardware port, EDID serial |
| **Default display sort order** | Arrangement | Arrangement, Arrangement (rev), Display num, Display num (rev) |

- **Brightness update dwell**: the minimum gap between two brightness writes to the same display. While you drag a slider, values in between are skipped and only the newest one goes out. Lower feels snappier; raise it if a monitor chokes on fast changes.
- **Verification delay**: how long a display gets to apply a change before the app reads it back to check, including after a resend. While you drag, the app only reads back once the slider has rested this long. Raise it for monitors that apply changes slowly.
- **Verification attempts**: how many times a brightness change is written and read back before the display is marked unavailable. Power changes get as many read-backs, and so do detection and recovery. Higher puts up with more glitches; lower gives up sooner on a monitor that's actually stuck.
- **DDC operation timeout**: the longest any single DDC call can take. When it runs out, that monitor's helper process is ended and the call counts as failed; the other monitors carry on.
- **Allow blind DDC writes during degraded state**: see [Degraded](#degraded).
- **Power off mode**: what "turn off display" sends. **Sleep** is standby, **Power off (soft)** is DPM off, **Power off (hard)** is a write-only off. Some monitors ignore the soft one, and after a hard power-off you'll most likely have to press the monitor's power button to wake it.
- **Identify displays by**: which attribute keys each physical monitor. **Display number** is the number Windows shows in Display settings, but it can change after a reboot or a topology change. **Hardware port** stays put across reboots as long as the monitor stays on the same cable/port. **EDID serial** sticks to the panel regardless of the port, but falls back to the port for monitors that don't report a serial. Changing it re-detects every display, and the app warns that saved profile entries can get orphaned.
- **Default display sort order**: the order of display rows in the flyout. **Arrangement** goes left to right (then top to bottom) by where the displays sit in Windows display settings; **Display num** goes by display number. The (rev) versions flip it. Applied whenever displays are plugged in or removed.

## Verification, Recovery, and Fast Apply

A DDC monitor saying "OK" to a write doesn't prove it did anything. So every brightness change is written, then read back. If the monitor reports a different value, the app sends it again, up to **Verification attempts** times. If it still doesn't match, the display is marked unavailable.

Keeping that fast:

- The newest value always wins. Old values still queued for a monitor are dropped.
- While you drag, the read-back waits until the slider rests (**Verification delay**), so a drag isn't slowed down by checking every step. Windows handles DDC calls for all monitors one at a time, so reading back every step would cut how often each monitor gets updated.
- A value the monitor already confirmed isn't sent again.
- Each monitor has its own helper process, so a stuck monitor doesn't block the rest of the app.

### Unavailable

A display the app can't talk to shows a warning triangle and its slider locks. Hover the icon for the reason, or **DDC/CI needs recovery** if there isn't one.

You don't need to restart anything. The app starts trying to get the display back immediately, then every 2 seconds for the first few attempts, then less and less often, down to once every 30 seconds. It keeps going as long as the display is unavailable. Opening the flyout makes it try every unavailable display, and every display it turned off, right away.

After a monitor is plugged in, powered on, or the PC wakes up, the app waits up to 1.5 seconds before its first read. Asking some monitors too early can leave their DDC replies stuck returning garbage.

When a display comes back:

- The app sends it the brightness it's supposed to have (your slider value, or the curve's value if the curve is driving it) and reads it back.
- A display you excluded from the master stays excluded and gets nothing.
- A display you had in manual override against the curve stays in manual override. If the curve took the row back while the display was away, it follows the curve. See [Day/Night Curve](curve.md).

If you turned the display off from the app, the app keeps checking it on the same schedule, because a monitor's own power button usually switches it back on without Windows saying a word. Until then the curve leaves it alone. Once the display answers and reports that it's on, it gets the brightness it's supposed to have, like any display that comes back. Moving its slider, or the master slider, counts as turning it back on too.

Checking a display the app turned off means reading from it, which can't wake it up. The one write is after **Power off (hard)**: if the display answers with garbage, the app sends it its last confirmed brightness. Turning some monitors on leaves their DDC replies stuck returning garbage until they get a write, and a monitor that's still hard-off ignores the write until you press its button. After **Sleep** or **Power off (soft)**, the app never writes, since a write wakes some monitors from those.

### Degraded

Some monitors keep accepting writes but stop giving readable replies. The app shows these with a disconnected-display icon instead of the warning triangle.

- With **Allow blind DDC writes during degraded state** on (default), the slider stays usable and changes are still sent, but the app can't confirm they did anything. The app never saves such a value as confirmed.
- With it off, the slider locks like an unavailable display.

The app keeps trying to read from a degraded display in the background, and it goes back to normal as soon as a reply comes through. If a reply ever shows the monitor ignored a write it accepted, the display gets marked unavailable instead, and stays that way until a write is actually confirmed.

### Hard Power-Off

++ctrl++-click the warning or degraded icon of a display to send it a hard power-off over DDC. This works on a display that has worked over DDC before and supports power control; for a degraded display, **Allow blind DDC writes during degraded state** also has to be on. The tooltip shows **Ctrl+Click to Attempt Hard Power-Off** where it's available on a degraded display.

The point is power cycling: turning a monitor off and on usually clears a wedged DDC connection.

!!! warning "The monitor stays off"
    After a hard power-off, the monitor doesn't come back on by itself, and the app can't wake it. Press the monitor's physical power button. The first time, the flyout asks you to confirm (**Confirm** / **Abort**); after that it doesn't ask again.

## Troubleshooting

Logs are the first thing to look at when a monitor misbehaves; see [Configuration](../../getting-started/configuration.md) for where they are.

**A display has a warning triangle and never comes back.** Its DDC is probably wedged, which only a power cycle fixes. ++ctrl++-click the triangle for a [hard power-off](#hard-power-off) and turn the monitor back on with its button, or unplug its power for a moment. The app picks it up again on its own.

**A display you turned off from the app ignores the curve after you switch it back on with its button.** After **Sleep** or **Power off (soft)** the app only reads from it, and if the monitor came back with its DDC replies stuck returning garbage, reads alone can't get it going. Move its slider, or turn it on from the app (its row's power button or **All displays ON**). Either one writes to it, which unsticks it.

**A display shows up with a warning triangle from the start.** It either doesn't do DDC/CI or has it disabled. Look for a DDC/CI option in the monitor's on-screen menu. TVs usually don't have it.

**The slider moves but the monitor doesn't change.** Look at the icon. A degraded icon means the monitor (or something in between) accepts writes without replying properly. Active DP-to-HDMI converters/adapters are a known cause: the converter accepts every write itself, even while the monitor behind it is dark. Turn off **Allow blind DDC writes during degraded state** if you'd rather have the slider lock than send writes nobody confirms.

**Brightness sets, then the app marks the display unavailable.** The monitor is slower to apply changes than the app expects, so the read-back sees the old value. Raise **Verification delay**, ideally just for that display in its card.

**A display drops out now and then, then recovers.** Glitches on the DDC line. Raise **Verification attempts** so a single bad reply doesn't count as a failure. Having the monitor's own on-screen menu open while the app writes to it can also make writes fail.

**A stuck monitor takes ages to be marked unavailable, or a slow one keeps failing.** That's **DDC operation timeout**. Lower it so a stuck call gets cut off sooner; raise it if a slow monitor keeps getting cut off mid-call.

**Names or limits stopped applying after moving a monitor to another port.** Overrides follow the monitor's EDID serial. A monitor that doesn't report one is matched by its port instead, so on a new port it shows up as a new card with default settings. Set it up again there; the old card stays in the list as **Disconnected**.

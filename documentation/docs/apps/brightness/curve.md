# Day/Night Curve

The app can drive your display brightness and Night Light strength from a 24-hour curve that you draw yourself. The curve is tied to the sun at your location, so it shifts along with sunrise and sunset across the year instead of sitting at fixed clock times.

There are two curves, one for brightness and one for Night Light. They're stored per profile, so every profile can have its own daily shape (see [Profiles](index.md)). Your location is global and shared by all profiles.

## Turning the Curves On

The curves do nothing until you engage them in the flyout:

- The curve button on the **All Displays** row turns the brightness curve on or off (tooltip **Brightness curve**).
- The curve button on the **Night Light** row turns the Night Light curve on or off (tooltip **Night-light curve**).
- The **Environmental curves** button in the flyout footer flips both at once. If either curve is on, it turns both off; if both are off, it turns both on. This button only shows while **Settings** > **Flyout** > **Show environmental curves button** is on.

A dimmed curve button means that curve is off. Both on/off states survive an app restart.

The curves keep running while the flyout is closed. Every few seconds (see [Tick Interval](#tick-interval)) the app samples the curve for the current time and sends the result to your displays and Night Light. Values that didn't change since the last tick aren't sent again, so an idle curve costs basically nothing.

The Night Light curve only sets the strength. It doesn't turn Night Light on for you; see [Night Light](night-light.md) for how the two interact.

### How the Curve Moves Your Displays

The brightness curve drives the **All Displays** (master) value. Each individual display follows it while keeping its own difference from the master, captured at the moment the curve engages. So if your second monitor sat 10 points below the master when you turned the curve on, it stays 10 points below the curve.

If a display's difference would push it past 0% or 100%, it clips there. **Settings** > **Flyout** > **Preserve slider offsets** changes how those differences are remembered; see [the main Brightness page](index.md).

Per-display limits (**Min brightness** / **Max brightness**) still apply to curve writes. See [Displays](displays.md).

### What the Sliders Show

While the curve drives a row, the slider fill follows the curve and the thumb is dimmed; the thumb stays on your own manual value. A marker in the curve's color shows the curve's current target. That marker is tied to the **Show environmental curves button** setting: turn the footer button off and the markers go too.

## Curve Control and Manual Override

Every row that the curve can drive (each display, **All Displays**, and **Night Light**) gets a small lock button on its left while the curve is on.

- Unlocked (tooltip **Curve control**): the curve owns this row.
- Locked (tooltip **Manual override**): you own this row, the curve leaves it alone.

Click the lock to swap between the two. This is the quick way to take one row away from the curve for a while and hand it back later, without turning the whole curve off.

### What Disengages the Curve

In the default (absolute) mode, touching brightness yourself puts that row into manual override:

- Dragging or scrolling a slider in the flyout.
- Scrolling the tray icon (the wheel settings are on [the main Brightness page](index.md)).
- The brightness and Night Light hotkeys, e.g. **Increment master brightness**, **Full bright**, **Increment night light**.

The rules differ a bit by row:

- An individual display only releases itself. The master and the other displays keep following the curve.
- The master (**All Displays**) releases itself ***and every display the curve was driving***, so all rows show the same manual state.
- **Night Light** only releases itself.

When you scroll the tray icon while the curve is driving, the app starts from the curve's current value, so brightness doesn't jump back to whatever your slider held before.

A manual override sticks, including across app restarts, until you end it.

Two cases don't disengage anything:

- Inside a [disabled period](#disabled-period). The curve is asleep, so the sliders are yours anyway.
- [Offset mode](#offset-mode). The sliders set the base the curve adds to, so moving them is expected.

### Ending a Manual Override

Any of these hands the row back to the curve:

- Click the lock button again.
- Double-click the slider.
- Let the disengage timer run out (below).
- Let auto re-engage catch it (below, master only).
- Turn the curve off and back on. Turning a curve off clears every manual override for it.

Re-engaging the master also re-engages every display that was in manual override.

When you turn the brightness curve off entirely, your displays go back to their slider values.

### Disengage Timer

A row in manual override shows a stopwatch button next to it. Click it to start a countdown: when it runs out, the curve takes that row back.

- The minutes box appears once the stopwatch is running. Default is 60 minutes, range 1 to 1440 (24 hours). The arrows and the scroll wheel step by 5; typed values apply on ++enter++ or when the box loses focus.
- Changing the minutes while it runs counts from when you started it, not from now.
- Hover the stopwatch to see the time left. Click it again to cancel the countdown (the row stays in manual override).
- The countdown survives restarts. If it ran out while the app was closed, the row goes back to the curve on the next start.
- If a display's timer runs out while the master still has its own timer running, the display stays in manual override until the master's timer ends.
- If the display is off or unreachable when its timer runs out, the override still ends; the display follows the curve once it comes back.

The stopwatch only exists in absolute mode.

### Auto Re-Engage

**Settings** > **General** > **Auto-engage environmental curve** lets the curve take the master back by itself. When the curve's value reaches or crosses the value you set the master to, the master and every display in manual override go back to the curve.

Example: the curve is ramping down in the evening and sits at 70. You drag **All Displays** to 40. Later the curve comes down to 40, and from there on it's driving again.

The number box next to the toggle is a delay in seconds (default 60). After every manual brightness change, the curve has to wait this long before it can re-engage; crossings during the delay don't count. That stops a click that happens to land on the other side of the curve from snapping you straight back. Set it to 0 for no delay.

Gotchas:

- It only watches the master. If you only moved one display, the master never left curve control, so nothing re-engages that display. Use the lock button, a double-click, or the disengage timer for those.
- It doesn't apply to Night Light, offset mode, or a disabled period.

## Environmental Page

**Settings** > **Environmental** is where you edit the curves. The top of the page is the curve editor; below it are the profile picker, the mode toggles, the display toggles, the preview tools, smoothness, and your location.

Most of it is per profile. The **Profile** dropdown picks which profile's curve you're looking at (it starts on the active profile). Editing a profile that isn't active doesn't touch your displays until you switch to it. Switching profiles swaps the active curve right away.

### Curve Editor

The horizontal axis is the day, midnight to midnight. The vertical axis is brightness or Night Light strength, 0 to 100 (or -100 to +100 in offset mode). The brightness curve and the Night Light curve share the chart; the legend under it shows which color is which, and a dashed vertical line marks the current time.

Mouse:

- Click an empty spot to add a node to the nearest visible curve. Keep holding to drag it into place. Clicking within 5 minutes of an existing node moves that node instead of adding a second one.
- Drag a node to move it. A node can't pass its neighbors.
- Right-click a node to delete it.
- The two end nodes (midnight) can't be deleted and only move up and down. They're linked, so moving one moves the other and the day wraps around cleanly.

Keyboard (click the editor first; the first node gets selected):

| Keys | Action |
|---|---|
| ++tab++ / ++shift+tab++ | Select the next / previous node (brightness nodes first, then Night Light) |
| ++up++ / ++down++ | Move the selected node up / down a small step |
| ++shift+up++ / ++shift+down++ | Move it by exactly one unit on the axis |
| ++ctrl+up++ / ++ctrl+down++ | Move it by a coarse step |
| ++shift+left++ / ++shift+right++ | Move it one minute earlier / later |
| ++space++ | Add a node next to the selected one |
| ++delete++ / ++backspace++ | Delete the selected node (not the end nodes) |
| ++esc++ | Clear the selection |

Edits apply to your displays right away and save on their own.

**Reset curves** puts the brightness and Night Light curves of the selected profile back to flat lines, after a confirmation. It only resets the mode you're in: absolute curves in absolute mode, offset curves in offset mode. The defaults are a flat 75 for brightness, a flat 25 for Night Light, and a flat +0 for both offset curves. There's no undo.

### Follow the Sun

This is the part that keeps the curve lined up with the actual day across the seasons. Days get longer and shorter as the year goes on (the Earth's axial tilt), so a curve pinned to fixed clock times would dim too early in summer and too late in winter. **Follow the sun** fixes that. It's what the README calls obliquity compensation: the sun position math accounts for that axial tilt (the obliquity of the ecliptic), so the sun events it anchors to move with the seasons.

With it on (default), each node is anchored to the sun events around it: astronomical, nautical, and civil dawn, sunrise, solar noon, sunset, and the matching dusks. A node that sits halfway between civil dawn and sunrise stays halfway between them, whatever the date. The curve slides along the clock as the days change, and it also follows you if you change your location.

How it behaves:

- The editor always shows the curve as it applies today. When you edit it, today becomes the new reference point.
- Days the app wasn't running catch up on their own.
- Turning it off freezes the curve at the clock times it had when you last edited it.
- It uses the location from [Geo Location](#geo-location), so set that first.
- Near the poles, where some twilights don't happen on some dates, the app fills in the missing ones so the curve still works.

Per profile.

### Use Daylight Savings

On (default): sun times follow daylight saving time, so they line up with your wall clock all year.

Off: the app uses your timezone's standard offset all year. The sun events, and the curve with them, don't jump by an hour at the DST changes.

Per profile.

### Offset Mode

By default the curves are absolute: the brightness curve at 60 means 60%.

With **Offset mode** on, the curves become adjustments on top of your own slider values, from -100 to +100. A brightness offset of -20 at night means "20 points below wherever my sliders are". Your sliders stay fully usable and moving them doesn't disengage anything.

The absolute curves and the offset curves are stored separately, so switching back and forth doesn't lose either set. Offset mode is global, not per profile.

The disengage timer and auto re-engage only work in absolute mode.

### Disabled Period

**Disabled period** carves out a time window where the curves don't apply. Inside it, the curves sleep and your sliders are in charge. When the window starts, your displays and Night Light go back to your slider values; when it ends, the curve picks up again (rows you put in manual override stay that way).

- Drag the two white pins above the chart, or type times into **Start** and **End**. Times like `22:30`, `10:30pm`, `10p`, or `7` all work.
- A start later than the end wraps past midnight, e.g. `22:00` to `06:00`.
- The default window is 6:00 to 18:00 when you first turn it on.
- **Follow sun** under it anchors the window to sun events the same way [Follow the Sun](#follow-the-sun) does for the curve. It's off by default, so the window stays on fixed clock times.

Per profile.

### Display Toggles

- **Show brightness curve** / **Show night light curve**: hide a curve in the editor. At least one stays on. A hidden curve can't be edited.
- **Show sun overlay**: shades twilight in orange and night in greyish blue behind the curves, for the date being shown. Hidden when your location isn't set, or when the sun math has no answer for that place and date.
- **Show cursor readout**: adds a vertical scrubber and a value marker on each curve under the mouse. The time/value readout in the top-right corner shows while the mouse is over the editor either way.

These are global. The curve, grid, current-time, and backdrop colors can be changed in **Settings** > **Theme** (see [Themes](../../shared/themes.md)).

On the brightness curve you'll also see two faint dashed lines labeled **upper brightness offset degeneration** and **lower brightness offset degeneration**. They're computed from the spread between your current slider values. When the brightness curve goes above the upper line or below the lower one, your brightest or dimmest display runs into 100% or 0%, so the differences between your displays start getting squashed.

### Previewing Other Dates

**Preview Date** shows the curve as it'll apply on another day. Type a date, use the calendar button, press ++up++ / ++down++ in the box to step a day (++ctrl++ for a month), or scroll the wheel while the box has focus.

While a date other than today is shown, the editor is read-only; click **Exit Preview Mode** in its bottom-right corner to get back to today. If a curve is on, your displays also show what that date's curve would do at the current time of day, until you return to today or leave the page.

**Preview next 24 hours** plays the next 24 hours of the curve (starting from now) on your actual displays and Night Light, squeezed into 10 seconds. Click **Cancel** to stop it. It needs at least one curve turned on in the flyout; otherwise the button is greyed out. If you're previewing another date, it plays that date's curve.

### Smoothness

How rounded the lines between nodes are. 0 draws straight lines between nodes, 100 (default) draws smooth curves. Values in between blend the two. The smooth version never overshoots your nodes, so a curve that flattens at 100 doesn't bulge above it. Global.

### Geo Location

The sun calculations need your location. It's global, shared by every profile.

- **Approx. from IP** looks up your approximate location from your IP address. The request goes to Mullvad's `am.i.mullvad.net` service. A VPN will put you wherever the VPN exits.
- **Pick on map** opens a map. Click to place the pin, drag the pin, scroll to zoom, drag with the right mouse button to pan. **Apply** uses the pin's location; **Abort** (or ++esc++) closes without changing anything.
- Or type **Latitude** and **Longitude** in decimal degrees, positive for north and east. Use a dot as the decimal separator (`47.75`, not `47,75`), whatever your Windows region settings are.

The default location is a spot near Seattle. If you're not there, set this before you rely on [Follow the Sun](#follow-the-sun) or the sun overlay. A location of exactly `0`, `0` counts as not set.

## Tick Interval

**Settings** > **General** > **Environmental tick interval** is how often the curves are re-sampled and applied. Default 5000 ms. The box accepts 250 to 600000, but anything above 60000 runs at 60000.

Lower reacts faster on steep parts of the curve; higher means less background work. I'd leave it alone. Values that didn't change aren't sent to the displays, so the default is cheap.

## Settings Reference

| Setting | Where | Scope | Default |
|---|---|---|---|
| Brightness curve on/off | Flyout, **All Displays** row | Global | Off |
| Night Light curve on/off | Flyout, **Night Light** row | Global | Off |
| **Show environmental curves button** | **Settings** > **Flyout** | Global | On |
| **Auto-engage environmental curve** | **Settings** > **General** | Global | Off, 60 s delay (0 to 86400) |
| **Environmental tick interval** | **Settings** > **General** | Global | 5000 ms |
| Disengage timer minutes | Flyout, per row | Per row | 60 (1 to 1440) |
| **Offset mode** | **Settings** > **Environmental** | Global | Off |
| **Follow the sun** | **Settings** > **Environmental** | Per profile | On |
| **Use daylight savings** | **Settings** > **Environmental** | Per profile | On |
| **Disabled period** | **Settings** > **Environmental** | Per profile | Off, 6:00 to 18:00 |
| **Follow sun** (disabled period) | **Settings** > **Environmental** | Per profile | Off |
| **Show brightness curve** | **Settings** > **Environmental** | Global | On |
| **Show night light curve** | **Settings** > **Environmental** | Global | On |
| **Show sun overlay** | **Settings** > **Environmental** | Global | On |
| **Show cursor readout** | **Settings** > **Environmental** | Global | Off |
| **Smoothness** | **Settings** > **Environmental** | Global | 100 (0 to 100) |
| **Latitude** / **Longitude** | **Settings** > **Environmental** | Global | A spot near Seattle |

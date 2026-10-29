# Battery

A tray battery monitor for laptops. It shows your charge level in the tray, and its flyout gives you the stuff the Windows flyout doesn't: charge/discharge rate in watts, remaining capacity in Wh, battery health, and a couple of different time estimates. It also has a power mode slider and an Energy Saver button, so you don't need to dig through Settings for those.

## Flyout

Left-click the tray icon to open the flyout. Left-click it again to close it. Pressing ++enter++ or ++space++ on the tray icon (when it has keyboard focus) only opens it.

### Summary

The top of the flyout shows a big battery glyph, the charge percentage, and a status line:

- **On battery**
- **Plugged in**
- **Charging at X W** (just **Charging** if Windows doesn't report a charge rate)
- **Plugged in, full**
- **No battery detected**

The glyph is colored by state: charging has its own color, and on battery it turns to the low color at 20% or less and the critical color at 10% or less. Otherwise it uses the accent color.

Under the percentage there's a short time line. While charging it reads **Xh Ym until full**. On battery it reads **Xh Ym remaining**, using **Predicted life** when that's available and **Present discharge** otherwise. While plugged in and not charging, or when the battery is full, the line is hidden.

### Details

Below the summary are two columns of numbers. Hover a label in the left column for a tooltip explaining it.

| Row | What it shows |
| --- | --- |
| **Predicted life** | How long the current charge would last, based on your average power draw over the last 30 minutes you spent on battery. Shows **Learning…** until it has two minutes of battery use to work with. |
| **Present discharge** | Time remaining at the recent discharge rate. Only shows a value while discharging. |
| **Time until full** | Time to full at the recent charging rate. Only shows a value while charging. |
| **Power source** | **External** or **Battery**. |
| **Discharge rate** / **Charge rate** | Current battery power in watts. The label switches to **Charge rate** while charging. |
| **Remaining** | Remaining capacity in Wh. |

Anything Windows doesn't report shows **N/A**.

**Predicted life** is the one I'd look at. It describes how your machine actually gets used, not what it's doing this second, so it doesn't swing around every time something spikes the CPU. It's also shown while plugged in (i.e. "if I unplug now, how long do I get"), and the 30 minutes of usage don't have to be recent: time spent on external power doesn't count toward it or age it out. The learned usage is saved to `learned-battery-usage.txt` in the app's data folder, so it survives restarts. Delete that file if you want it to start learning from scratch. See [Configuration](../../getting-started/configuration.md) for where the data folder is.

**Present discharge** is the twitchier one. It averages the recent 5-second readings, and you can tune how much in **Settings** > **Flyout** > **Battery estimates** (see [Settings](#settings) below). **Time until full** uses its own fixed smoothing and ignores those settings.

### Power Mode

The **Power mode** slider has three stops, left to right: **Power Saver**, **Balanced**, **Ultimate**. Drag it, click it, or use the scroll wheel over it. The mode is applied when you let go.

These are the actual Windows power plans (Power saver, Balanced, Ultimate Performance), switched with `powercfg`. A few things to know:

- If no Ultimate Performance plan exists on your machine, picking **Ultimate** creates one by duplicating Windows' hidden built-in Ultimate Performance plan, then activates it. After that you'll see it in Control Panel's power plans like any other.
- If your active plan isn't one of those three (e.g. a plan your laptop vendor installed), the heading reads **Power mode: Custom** and the slider has no thumb until you pick a mode.
- If the app can't read the active plan at all, the heading reads **Power mode: Unknown**.
- Switching modes keeps your Energy Saver setting. Windows stores Energy Saver per plan, so without this every mode switch would also flip Energy Saver around.
- Changes made elsewhere (Control Panel, `powercfg`, etc.) show up the next time the flyout updates while it's open, or when you next open it.

### Title Bar

The flyout's title bar has these buttons:

- **Energy Saver**: shows **Energy Saver: On** or **Energy Saver: Off**. Clicking it sets Windows' Energy Saver to Always (on) or Never (off) on your active power plan. It shows as on if Energy Saver is currently running or set to Always. If the app can't read the Energy Saver threshold, the button is grayed out.
- **Battery health**: opens a small **Battery health** window with **Health** (full charge capacity as a percentage of the designed capacity), **Full charge**, and **Designed**, both in Wh. Close it with **Close** or ++esc++.
- **Power settings**: opens Windows Settings at **Power & battery**.
- **Settings**: opens the app's settings window.
- **Undock** / **Redock**: floats the flyout as a movable window, or docks it back to the tray. Undocking and keep-open work like in every TADN app, see [Tray and Flyouts](../../shared/tray-and-flyouts.md).

By default the title bar sits at the bottom of the flyout, with the undock button floating in the top right corner. Turn off **Title bar at the bottom** to move it to the top; the undock button then sits at the right end of the title bar.

## Tray Icon

The tray icon is a battery glyph filled in 10% steps, with a charging variant whenever you're plugged in. It follows the theme's text color, or your **Tray icon color** override (see [Themes](../../shared/themes.md)). With no battery, the icon is drawn in the disabled color.

Hover it for a tooltip with the charge percentage, the status (**On battery**, **Plugged in**, **Charging**, **Plugged in, full**), and, when Windows reports them, **Time remaining**, **Battery power** in watts, and **Health**. **Time remaining** here comes straight from Windows' estimate, falling back to remaining capacity divided by the discharge rate, so it won't always match the flyout's numbers.

Mouse and keyboard on the tray icon:

- Left-click: toggle the flyout.
- Double-click: open settings.
- Right-click (or ++shift+f10++ / the menu key): open the tray menu.

### Tray Menu

- **Power options**: opens the classic Control Panel Power Options.
- **Battery report**: runs `powercfg /batteryreport`, saves it as `reports\battery-report.html` in the app's data folder, and opens it in your browser. It takes a few seconds; clicking it again while a report is still being written does nothing.
- **Settings**
- **Exit**

Menu placement and the rest of the tray menu behavior are shared, see [Tray and Flyouts](../../shared/tray-and-flyouts.md).

## Settings

Double-click the tray icon, or use the **Settings** button in the flyout or the tray menu. The **General**, **Theme**, **Hotkeys**, and **About** pages are shared by every TADN app, see [Settings Window](../../shared/settings-window.md). Battery-specific settings:

### Triggers

**Settings** > **Triggers** has three cards, **Trigger 1** to **Trigger 3**. Each card pairs a condition with an action: pick a **Condition** in the left dropdown and an **Action** in the right one, and the action runs whenever that condition happens. A card with only one of the two does nothing, and all three start out empty. To clear a dropdown, pick its top entry (**Condition** or **Action**). Changes apply right away, no restart needed.

Triggers react to changes the app sees while it's running, checked on every reading (about every 5 seconds). The state the battery is in when the app starts never fires anything, and neither does a condition that's already true when you set up a card: it waits for the next time it happens. Waking from sleep counts as a normal change, e.g. if the battery went from 30% to 8% while the laptop slept, both low-battery triggers fire on the first reading after it wakes.

| Condition | Fires when |
| --- | --- |
| **Battery at 20% or less** | A reading on battery is at 20% or less. That's the same point where the flyout glyph turns to the low color. |
| **Battery at 10% or less** | A reading on battery is at 10% or less, i.e. when the glyph turns to the critical color. |
| **Battery at 80% or more** | A reading while plugged in is at 80% or more, charging or not, so it still fires if a charge limit holds the battery at exactly 80%. |
| **Charging started** | Windows goes from not charging to charging. |
| **Charging stopped** | Windows stops reporting charging. That includes unplugging, the battery getting full, and a charge limit kicking in. |
| **External power connected** | You plug in. |
| **External power disconnected** | You unplug. |
| **Battery full** | The status turns to **Plugged in, full** (plugged in, not charging, at 100%). |

The three charge levels and **Battery full** don't fire again right away. After one fires, it waits until the charge is 3 points back on the other side before it can fire again: 23% or more for **Battery at 20% or less**, 13% or more for **Battery at 10% or less**, 77% or less for **Battery at 80% or more**. **Battery full** waits until you unplug or the charge drops to 97% or less, so a battery that trickles between 99% and 100% doesn't keep firing it. That's also why starting the app at 15% won't give you a **Battery at 20% or less** until the battery has been back up to 23%, while starting at 21% fires it as soon as it drops to 20%.

**Note**: the low-battery conditions only count readings on battery. If the charge sinks to 20% while you're plugged into a charger that can't keep up, **Battery at 20% or less** fires the moment you unplug.

Actions:

- **Show notification**: a Windows notification from the tray icon. The title is the condition and the body is the current charge, e.g. **Battery at 20% or less** and **Charge: 20%**. Clicking it opens the flyout.
- **Open flyout**: shows the flyout, same as the **Open flyout** hotkey.
- **Open settings**: opens the settings window on **General**.
- **Open power settings**: opens Windows Settings at **Power & battery**, same as the flyout's **Power settings** button.

Several cards can share a condition, and each one fires. When more than one trigger fires on the same reading (e.g. unplugging while charging is both **Charging stopped** and **External power disconnected**), each action runs once and the notifications are merged into one: the first card's condition is the title, and the others are listed above the charge. Card order decides which comes first; drag a card, or press ++ctrl+up++ / ++ctrl+down++ on it, to move it.

An example setup:

- **Trigger 1**: **Battery at 20% or less** -> **Show notification**.
- **Trigger 2**: **Battery at 10% or less** -> **Open flyout**, which puts the **Energy Saver** button and the power mode slider right in front of you.
- **Trigger 3**: **Battery at 80% or more** -> **Show notification**, as a reminder to unplug.

### Flyout

| Setting | Default | What it does |
| --- | --- | --- |
| **Restore undock state on startup** | On | Reopens the flyout docked or undocked like it was last session. When off, it always opens docked. |
| **Show undock button** | On | Shows the undock button. When off, the flyout always stays anchored to the tray. |
| **Keep undocked flyout on screen** | On | Keeps the undocked flyout fully inside one monitor. Only shown while **Show undock button** is on. |
| **Title bar at the bottom** | On | Puts the flyout's title bar at the bottom instead of the top. |
| **Discharge averaging checks** | 24 | How many recent readings (about 5 seconds each) **Present discharge** averages. Range 1 to 720. 1 uses only the latest reading. |
| **Discharge decay half-life (checks)** | 6 | How fast older readings lose weight in **Present discharge**. A reading counts half as much after this many checks. Range 0.5 to 720. |

With the defaults, **Present discharge** looks at the last 2 minutes, weighted toward the last 30 seconds. Raise both if it jumps around too much for your taste; lower them if you want it to react faster. Changes apply right away to the readings already collected.

### Tray Icon

| Setting | Default | What it does |
| --- | --- | --- |
| **Menu position** | **Modern** | Where the right-click tray menu opens: **Classic** or **Modern**. See [Tray and Flyouts](../../shared/tray-and-flyouts.md). |

### Theme

On top of the shared theme settings, Battery has a **Flyout** group with **Flyout background** and **Titlebar background** color overrides (light and dark variants), and a **Font size** for the tray menu (default 15, range 8 to 48). See [Themes](../../shared/themes.md).

## Hotkeys

Battery has two hotkey actions. Neither has a default binding; add your own in **Settings** > **Hotkeys**, see [Hotkeys](../../shared/hotkeys.md).

| Action | What it does |
| --- | --- |
| **Open flyout** | Shows the flyout. Unlike clicking the tray icon, it doesn't toggle it closed. |
| **Open settings** | Opens the settings window. |

Fixed keys:

| Where | Key | Action |
| --- | --- | --- |
| Tray icon | ++enter++ / ++space++ | Open the flyout |
| Tray icon | ++shift+f10++ / ++menu++ | Open the tray menu |
| Flyout | ++enter++ / ++space++ on **Battery health** | Open the **Battery health** window |
| Battery health window | ++esc++ | Close it |

## Usage Notes

- ***No battery, no data.*** On a desktop the app still runs, but the flyout shows `--` and **No battery detected**, every number is **N/A**, and the tray icon is grayed out. The power mode slider and Energy Saver button still work there, since they're plain Windows power plan settings.
- Readings refresh about every 5 seconds. After the PC sleeps, the rate averaging starts over, and sleep time never counts toward **Predicted life**.
- How accurate any of this is depends on what your battery firmware reports. Some batteries report no rate or no designed capacity, which shows up as **N/A** (and no **Health**).
- **Energy Saver** here is Windows' battery threshold setting, switched between Always and Never. If you had it on a percentage threshold (e.g. turn on at 20%), toggling the button replaces that with Always or Never.

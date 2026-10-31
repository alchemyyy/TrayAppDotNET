# Curve Editor

A graph editor for fan curves: pick a sensor for the X axis, then drag points to set the fan speed for each reading.

Opening the curve editor puts the fan (or its group) in Curve mode. From then on the app drives the fan from this curve twice a second, with the fan's own **Offset**, **Min duty** / **Max duty**, **Max delta**, and **Jumpstart** applied on top. The order and units are on [Fan Control](index.md#curve-control).

## Opening It

- From a fan's [Fan Properties](index.md#fan-properties) window: pick a curve (or **None**) and click **Edit curve**.
- From the flyout header: the **Fan curve editor** button opens the curve of the first fan that has one, or the first fan if none do.

If the fan has no curve yet, a new one is created and named after the fan, e.g. "Front Curve". New curves start in % (or RPM, if the fan is in RPM mode), with **Smoothness** at 50 and **Monotonic** on. If the fan is in a group, the curve is assigned to the whole group.

The window title is "Fan Curve: " plus the curve name. There's no save button: every change is saved as you make it, and reaches the fan on the next sensor refresh. A point drag is saved once, when you let go; the fan follows the point while you drag it. Opening the editor for a curve that already has one open brings that window to the front instead of opening a second.

Curves are shared by name. Assign the same curve to several fans in their Fan Properties and editing it changes it for all of them.

## Data Source

The left side lists every sensor LibreHardwareMonitor reports (temperatures, fan RPMs, loads, clocks, power, etc.) with its live value, sorted by device and sensor name with numbers in natural order ("CCD6" before "CCD10"), as in the Probe Card Editor. Type in the search box to filter; it matches the device, sensor name, type, and unit. Click one to make it the curve's X axis.

Names in the list go through the same [nickname rules](probe-cards.md#nicknames) as probe cards, so "CPU: Package" instead of the full processor name.

When a curve has no data source yet, the app picks one for you, preferring temperature sensors. The X axis always spans the data source's range, and follows it when you pick another sensor or the sensor reports a different range. A reading outside the X axis range uses the curve's value at the nearest end.

If the data source stops giving valid readings for 5 seconds (or isn't found at all, e.g. after a hardware change), every fan on this curve goes back to hardware control until it reports again, or to its **Max duty** if Fan Control didn't exit cleanly earlier in the same boot. See [When a Curve Can't Run](index.md#when-a-curve-cant-run).

## Graph

The graph plots the sensor (X) against fan speed (Y). A dashed line marks the sensor's current reading, and two labels at the bottom show the reading and the curve's value at that reading, e.g. "Curve 42%".

The line is exactly what the fan receives: the same smoothing, and the curve's **Min**/**Max** applied, so points above the maximum draw the line flat at the maximum.

- Click empty space to add a point, and keep holding to drag it.
- Click a point to select it and drag it.
- Right-click a point to delete it.
- A curve always keeps at least two points.

With the graph focused:

| Key | Action |
| --- | --- |
| ++tab++ / ++shift+tab++ | Select the next / previous point. |
| ++left++ ++right++ ++up++ ++down++ | Move the selected point by 1 pixel. |
| ++ctrl++ + arrow | Move the selected point by 10 pixels. |
| ++del++ / ++backspace++ | Delete the selected point. |
| ++esc++ | Clear the selection. |

## Options

| Option | Default | What it does |
| --- | --- | --- |
| **RPM mode** | Off (On if the fan is in RPM mode) | Y axis in RPM instead of duty cycle %. |
| **Rescale curve** | | Only enabled after you change the Y axis maximum (toggling **RPM mode** or changing **Max RPM** while in RPM mode). Scales every point proportionally to the new maximum. |
| **Min RPM** | 0 | Lowest value the curve outputs in RPM mode. |
| **Max RPM** | The fan's max RPM, its current RPM, or 3000 | Top of the Y axis in RPM mode. Only shown in RPM mode. |
| **Min duty** | 0% | Lowest value the curve outputs in % mode. |
| **Max duty** | 100% | Top of the Y axis in % mode. |
| **Smoothness** | 50 | 0 draws straight lines between points; 100 draws a smooth curve through them. |
| **Monotonic** | On | The curve never goes down as the reading goes up. |

**Min** and **Max** also set the range of the flyout slider for any fan on this curve while it's in Curve mode, and they bound what the curve itself outputs. Each fan's **Offset** and then its **Min duty** / **Max duty** from [Fan Properties](index.md#fan-properties) apply after that.

**Note**: with **Monotonic** on, points that would make the curve go down are drawn flattened, but their original positions are kept. Turn **Monotonic** off and they come back. Once you drag or add a point with **Monotonic** on, the flattened positions become the real ones.

The number boxes take the mouse wheel when you hover them.

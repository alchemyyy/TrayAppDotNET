# Probe Cards

Cards in the [Fan Control](index.md) flyout that show live sensor readings: temperatures, power, load, clocks, and voltages. Pick whichever sensors you care about and they sit right next to your fans.

## In the Flyout

**Add item** (+) > **Add probe card** adds an empty card to the top of the flyout. Until you pick some sensors it just says **No probes selected**.

Each row shows an icon for the sensor type, the device and sensor name, and the value, e.g. "CPU Package: 46.50 C". Values refresh twice a second.

On the card header:

- Probe icon: opens the [Probe Card Editor](#probe-card-editor) for that card.
- Name: double-click it (or focus it and press ++enter++ / ++space++) to rename. ++enter++ or clicking away saves, ++esc++ cancels.
- Chevron: collapse or expand the rows.
- **X**: delete the card, after a confirmation.

Drag the card to move it around, same as fan and group cards. You can have as many probe cards as you like.

## Probe Card Editor

The editor window is titled "Probe Card Editor: " plus the card name. Tabs across the top: **Home**, **Temperatures**, **Power**, **Load**, **Clocks**, **Voltages**.

### Picking Sensors

Each type tab lists every sensor of that type with its live value. Flip **Enable** on a sensor (or double-click its card) to add it to the probe card; flip it off to remove it.

The **Clocks** tab also gets two extra entries the app builds itself for each CPU: **Cores (Max)** and **Cores (Max Effective)**, i.e. the highest per-core clock (and effective clock) at that moment.

The **Power** tab gets **Power Draw (Total)** under a **System** device: the best whole-system power draw the sensors can give. When a supported USB PSU (Corsair or MSI) reports its output, that total is used. Otherwise it adds up each CPU's package power (or Intel platform power, when reported), each discrete GPU's board power, and cooler pump and fan power. Integrated Intel graphics are left out because the CPU package already includes them. Anything with no sensor, such as drives, RAM, and the motherboard, isn't counted, so without a PSU reading the number comes out below what's drawn at the wall.

### Home Tab

**Profiles to display probe card on:** has a checkbox per [fan profile](index.md#profiles). The card only shows up on the checked ones. At least one has to stay checked.

**Selected probes** lists what's on the card, in display order:

- Drag a sensor to reorder it, or press ++ctrl+up++ / ++ctrl+down++ while any of its controls has focus.
- **Enable**: turn it off to take the sensor off the card.
- **Truncate**: round the value to a whole number. Values show two decimals otherwise.
- Gear button: shows an **X=** box where you can type a formula to transform the value, using `x` (or `X`) for the raw reading, e.g. `x * 1.8 + 32` for Fahrenheit. The unit label doesn't change, so that one still reads "C". ++enter++, ++esc++, or clicking away saves it. If the formula doesn't work out, the raw value is shown. The formula only changes what the card shows: a fan curve on the same sensor still reads the untransformed value, so a curve drawn in C stays in C.

Sensors that were saved before but weren't found this time (e.g. hardware you removed) stay in the list as missing entries. **Clear dead sensors** forgets all of them, here and on every other probe card.

### Nicknames

The **Home** tab also has the rule lists that shorten hardware names. They apply to probe cards, this editor, and the curve editor's sensor list.

**Device nicknames** rename the device part. Each rule has a match pattern (a regular expression, case-insensitive) and a replacement; the first rule that matches a device wins. Two special patterns match by hardware type instead of name: `{HardwareType.CPU}` and `{HardwareType.GPU}`. The defaults turn any CPU into "CPU" and any GPU into "GPU". If two devices end up with the same nickname, the second one gets a number, e.g. "GPU 2".

**Probe nicknames** clean up sensor names. Each rule is a find-and-replace (regular expression, case-insensitive) and every rule runs, top to bottom. The defaults:

| Pattern | Replacement |
| --- | --- |
| `\(Tdie\)` | (removed) |
| `\(Tctl/Tdie\)` | (removed) |
| `\(SMU\)` | (removed) |
| `CPU Core` | `Core` |

In both lists:

- **Add** adds a rule.
- **Load default nicknames** puts the default rules back at the top without deleting your own.
- Drag a rule by its arrow handle, or press ++ctrl+up++ / ++ctrl+down++ while it has focus, to change its order.
- ++enter++, ++esc++, or clicking away saves a field you're editing.
- A pattern that isn't a valid regular expression is skipped.

### Keys

| Key | Action |
| --- | --- |
| ++tab++ | Move between the tab strip and the page. |
| ++left++ / ++right++ | Move between tabs. |
| ++enter++ / ++space++ | Open the focused tab. |
| Arrow keys (on a page) | Move to the nearest control. |
| ++ctrl+up++ / ++ctrl+down++ | Move the focused selected probe or nickname rule. |

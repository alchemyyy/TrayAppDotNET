# Tray Icon and Flyouts

Every TADN app lives in the notification area. The icon, its right-click menu, and the flyout that pops up above it all share the same plumbing, so they behave the same way across apps. For the best effect, I recommend removing the stock Windows tray icons with [Windhawk](https://windhawk.net/) ([this mod](https://windhawk.net/mods/taskbar-tray-system-icon-tweaks)).

## Tray Icon

What each click does is up to the app, but the pattern is the same:

- **Left-click** toggles the app's flyout. Network opens the Windows network UI you picked in its settings instead, and Task Manager shows or hides its main window.
- **Right-click** opens the tray menu.
- **Double-click** does something app-specific, e.g. Battery opens settings. The first click of a double-click still does the normal left-click action.
- **Scroll wheel** over the icon adjusts brightness in Brightness and volume in Volume. It doesn't use a global mouse hook, and precision touchpad scrolling works too.
- **Middle-click** does nothing in any app.
- **Hovering** shows the app's tooltip, e.g. the current volume or battery level.

Brightness, Fan Control, Network, and Volume also have a **Modified actions** section on their **Tray icon** settings page. It lets you assign actions to Ctrl+ or Alt + left-click, right-click, and double-click (Brightness and Volume add the scroll wheel). Anything left at **Nothing** falls back to the normal behavior.

The keyboard works too: ++win+b++ focuses the notification area, then the arrow keys get you to the icon. See [Hotkeys](hotkeys.md#tray-icon).

If Explorer restarts or crashes, the icon comes back on its own once the taskbar is back.

Each app's exact click actions, tooltip, and scroll behavior are on its page: [Battery](../apps/battery/index.md), [Brightness](../apps/brightness/index.md), [Fan Control](../apps/fan-control/index.md), [Network](../apps/network/index.md), [Task Manager](../apps/task-manager/index.md), [Volume](../apps/volume/index.md).

## Tray Menu

Right-click the icon (or press ++shift+f10++ while it has keyboard focus) for the tray menu. Every app's menu ends with **Settings** (**App settings** in Network) and **Exit**, except Task Manager, whose menu is just **Open Task Manager** and **Exit**. What comes above that is app-specific.

- **Exit** really quits the app. The crash watcher doesn't restart it. See [Crash Recovery](crash-recovery.md).
- Clicking anywhere outside the menu, or pressing ++esc++, closes it. The full keyboard layout is in [Hotkeys](hotkeys.md#menus).

### Customizing the Menu

| Setting | Where | Default | What it does |
| --- | --- | --- | --- |
| **Menu position** | **Tray icon** page | **Modern** | **Classic** opens the menu at the cursor. **Modern** docks it above the taskbar, centered on the tray icon, like the Windows 11 system flyouts. Task Manager always uses **Modern**. |
| **Font size** | **Theme** page, **Context menu** | 15 | Size of the menu. Everything in it scales from the font size. Task Manager's tray menu has a fixed size; there the card sizes the menus inside its window. |
| **Keep tray context menu warm** | **General** page | On | Keeps the menu built in the background so it opens faster. Not in Task Manager. |

Two apps let you choose what's in the menu:

- **Brightness**, **Tray icon** page: **Show profile selectors** (on), **Show individual power selectors** (off), **Show all-displays power selector** (on).
- **Volume**, **Tray icon** page: **Show "Recording" link in tray menu**, **Show "Sounds" link in tray menu**, **Show "Communications" link in tray menu**, and **Show per-device links in tray menu**, all off by default, plus how device names are shown.

The rest have a fixed set of items. Details are on the [Brightness](../apps/brightness/index.md) and [Volume](../apps/volume/index.md) pages.

## Flyouts

Battery, Brightness, Fan Control, and Volume have their own flyout. Network uses the Windows network flyout, and Task Manager has a normal main window instead, so this section doesn't apply to those two.

### Opening and Closing

- Click the tray icon to open the flyout above it. It's kept inside the screen's work area, so it never ends up under the taskbar. Click the icon again, or press ++esc++ while the flyout has focus, to hide it.
- Clicking anywhere else hides it too, unless it's undocked (see below), a window the flyout opened is still up, or the settings window is holding it open.
- Hiding the flyout also closes any prompt it opened over itself (e.g. an update or confirmation prompt), as if you'd cancelled it.
- Opening a hidden flyout from the tray icon or the **Open flyout** hotkey always brings it up docked, even if you'd undocked it before.
- **Keep flyout warm** on the **General** page (on by default) keeps the flyout built while it's hidden, so it opens faster. Brightness always keeps its flyout warm.

### Keeping It Open While You Change Settings

There's no pin button, but the flyout knows about the settings window. Open settings from the flyout's settings button (or open the flyout while settings is up) and the two get paired:

- The flyout stays up as long as the settings window, a window it opened (e.g. a color picker), or the flyout itself is the active window. That's what makes [live theming](themes.md) practical: change a color and watch the flyout.
- Switch to another program and the docked flyout hides. Switch back to the settings window and it comes back.
- Minimize or close the settings window and the pairing ends; the docked flyout hides.

If you want a flyout that stays put no matter what, undock it.

### Undocking

The undock button (tooltip **Undock**) sits in the flyout's corner or header, depending on the app. Undocking turns the flyout into a floating, always-on-top window that doesn't hide when you click elsewhere. Click the tray icon to hide it. If you were working in another window, that click brings the floating flyout forward where it is, still undocked, and a second click hides it.

- **Click** the button to undock. The flyout jumps to its last floating position if it has one, otherwise it stays where it is. Click it again (tooltip **Redock**) to dock it back above the tray icon.
- **Drag** the button to pull the flyout off the taskbar. It undocks once you've moved it a few pixels. Drop it near its docked spot and it snaps back in and redocks; drop it anywhere else and that spot is saved as its floating position.
- While undocked, you can also move it by dragging any empty part of its frame.

**Note**: since opening the flyout from the tray always docks it, getting your floating flyout back after hiding it is a two-step thing: open it from the tray, then click the undock button. It goes straight back to its saved spot.

The undock settings are on the **Flyout** page:

| Setting | Default | What it does |
| --- | --- | --- |
| **Show undock button** | On | Shows the undock button. When off, the flyout always stays anchored to the tray, and an undocked flyout is docked back. Called **Show undock button** in Fan Control. |
| **Restore undock state on startup** | On | If the flyout was undocked when the app closed, it opens undocked at its saved spot when the app starts. When off, the flyout always starts docked. Called **Restore undock state on startup** in Fan Control. |
| **Keep undocked flyout on screen** | On | Keeps the undocked flyout fully inside one monitor's work area when it's restored or repositioned. Only shown while undocking is allowed. |

**Restore undock state on startup** only kicks in once the flyout has a saved floating spot, i.e. after you've dragged it somewhere at least once.

# Shared Features

Every TADN app is built on the same common library, so a lot of what surrounds the actual app works the same way everywhere. That shared stuff is documented once, in this section. The app pages only go into what's specific to each app.

## What Every App Has

- **Searchable settings (with fuzzing).** Every app uses the same settings window, with a search box that tolerates typos and knows synonyms. See [Settings Window](settings-window.md).
- **Live theme customization.** Light/dark mode and color overrides that apply as you pick them. See [Themes](themes.md).
- **Rebindable hotkeys.** System-wide keyboard shortcuts you bind yourself, plus the in-window keyboard controls. See [Hotkeys](hotkeys.md).
- **Update system.** Each app checks GitHub for new releases and can update itself in place. See [Updates](../getting-started/updates.md).
- **Crash recovery.** A small watcher process restarts the app if it crashes. See [Crash Recovery](crash-recovery.md).
- **Tray icon and tray menu.** The same tray icon plumbing, menu, and menu settings in every app. See [Tray Icon and Flyouts](tray-and-flyouts.md).
- **Flyout undocking (where applicable).** Apps with a flyout let you pull it off the taskbar into a floating window. See [Tray Icon and Flyouts](tray-and-flyouts.md).
- **Run on startup, install and uninstall** from the **General** page of each app's settings. See [Settings Window](settings-window.md) and [Installation](../getting-started/installation.md).

## Which App Has What

Not every app has every feature. Network has no flyout of its own (it opens the Windows network flyout), and Task Manager has a main window instead of a flyout, so anything flyout-related doesn't apply to them.

| Feature | Battery | Brightness | Fan Control | Network | Task Manager | Volume |
| --- | --- | --- | --- | --- | --- | --- |
| Own flyout | Yes | Yes | Yes | No | No (main window) | Yes |
| Flyout undocking | Yes | Yes | Yes | - | - | Yes |
| Flyout stays open while Settings is open | Yes | Yes | Yes | - | - | Yes |
| Tray menu items you can show or hide | No | Yes | No | No | No | Yes |
| Tray menu position (Classic/Modern) | Yes | Yes | Yes | Yes | No (always Modern) | Yes |
| Tray menu font size | Yes | Yes | Yes | Yes | No (the **Font size** card sizes its in-window menus) | Yes |
| Ctrl/Alt click actions on the tray icon | No | Yes | Yes | Yes | No | Yes |
| Scroll wheel on the tray icon | No | Yes | No | No | No | Yes |
| Rebindable global hotkeys | Yes | Yes | Yes | Yes | No | Yes |
| Hotkey bound by default | None | **Full bright**: ++ctrl+alt+win+f++ | None | None | - | None |
| Theme color overrides | Yes | Yes | Yes | Yes | Yes (text and background) | Yes |
| Searchable settings | Yes | Yes | Yes | Yes | Yes | Yes |
| Update checks | Yes | Yes | Yes | Yes | Yes | Yes |
| Update button in the flyout or main window | No | Yes | Yes | No | Yes | Yes |
| Crash watcher | Yes | Yes | Yes | Yes | Yes | Yes |

The details of each app's tray icon, menu items, and hotkeys are on its own page: [Battery](../apps/battery/index.md), [Brightness](../apps/brightness/index.md), [Fan Control](../apps/fan-control/index.md), [Network](../apps/network/index.md), [Task Manager](../apps/task-manager/index.md), [Volume](../apps/volume/index.md).

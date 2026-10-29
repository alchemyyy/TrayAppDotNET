# Hotkeys

There are two kinds of keyboard control in TADN. **Global hotkeys** are system-wide shortcuts registered with Windows, so they work while the app sits in the tray and something else has focus. You bind those yourself. **In-window key bindings** are the keys that work inside the flyout, menus, and settings window. Those are fixed.

## Global Hotkeys

Global hotkeys live on the **Hotkeys** page of the app's settings. The page heading is **Hotkeys** in Battery and **Keyboard shortcuts** in Brightness, Fan Control, Network, and Volume. Each action gets a card with its name, a short description, a modifier dropdown, a key box, and an **Add** button. Which actions exist depends on the app (e.g. Brightness has full bright, full dim, night light and profile actions); the app pages list them.

Task Manager has no global hotkeys. Its **Hotkeys** page lists the fixed table shortcuts instead, and its optional Ctrl+Shift+Esc takeover is a separate setting. See [Task Manager](../apps/task-manager/index.md).

### Adding a Hotkey

1. Pick the modifiers from the dropdown. It has every combination of Ctrl, Alt, Shift, and Win, from **Alt** up to **Ctrl + Alt + Shift + Win**.
2. Click the key box and press the key you want. Its name shows up in the box. Modifier keys, ++esc++, and ++f12++ are ignored, and ++tab++ moves on to the next control as usual.
3. Click **Add**.

The new binding shows up under the action with its own delete button, and it's live right away. An action can have as many bindings as you like. If the action already has that exact combination, the button says **Exists** and won't add it twice.

- Every hotkey needs at least one modifier. You can't bind a bare key.
- ++f12++ can't be used. Windows reserves it for debuggers.
- The **Search** box at the top of the page filters the action cards by name and description. It's a plain text filter, not the fuzzy settings search.

### Conflicts

Windows hands out each key combination to one program at a time, first come first served. If something else already owns the combination, including another TADN app, the binding gets a warning icon. Hover it to see why, e.g. `Already in use by another app.` The binding stays saved and is tried again whenever the app re-applies its hotkeys, i.e. the next time it starts or you change a setting.

### Defaults

| App | Bound by default |
| --- | --- |
| Battery | Nothing |
| Brightness | **Full bright**: ++ctrl+alt+win+f++ |
| Fan Control | Nothing |
| Network | Nothing |
| Volume | Nothing |

**Note**: older Fan Control and Network builds bound **Open settings** to ++ctrl+alt+win+s++. Updating removes that binding once. If you want it back, bind it again on the Hotkeys page and it stays.

### Removing and Resetting

Click the delete button next to a binding to remove it. Deleting one of the default bindings above is remembered, so it doesn't come back the next time the app starts.

There's no reset button. To get a deleted default back, exit the app, open its `settings.xml`, and remove the `<Binding>` entry inside `<Hotkeys>` that has `RemovedByUser="true"`. The default is added back on the next start. Resetting all of the app's settings also does it; see [Configuration](../getting-started/configuration.md).

## In-Window Key Bindings

These work the same in every app and can't be changed. App-specific keys (flyout sliders, Task Manager's tables, etc.) are on the app pages.

### Settings Window

| Keys | Where | What it does |
| --- | --- | --- |
| ++space++ or ++right++ | Page list | Opens the selected page and moves focus into it |
| ++left++ or ++esc++ | Page | Back to the page list, when nothing on the page uses the key |
| ++esc++ | **Search settings** box | Clears the search |
| ++enter++ | **Search settings** box | Moves focus into the search results |
| ++esc++ | Confirmation overlay | Cancels it |
| ++ctrl++ + drag | Right edge of the page list | Resizes the page list |
| ++ctrl++ + right-click | Right edge of the page list | Resets the page list width |
| Double-click | Title bar | Maximizes or restores the window |
| Scroll wheel | Page | Scrolls the page |

### Controls

| Keys | Control | What it does |
| --- | --- | --- |
| ++enter++ or ++space++ | Buttons, toggles, page list rows | Activates them |
| ++enter++, ++space++, or ++down++ | Dropdown | Opens it |
| ++up++ / ++down++, ++home++ / ++end++ | Open dropdown | Moves through the list |
| ++enter++ or ++space++ | Open dropdown | Picks the item |
| ++esc++ | Open dropdown | Closes it without changing anything |
| ++up++ / ++down++ | Number box | Steps the value |
| ++ctrl+up++ / ++ctrl+down++ | Number box | Bigger step |
| ++ctrl+shift+up++ / ++ctrl+shift+down++ | Number box | Even bigger step |
| ++enter++ | Number box | Commits what you typed |
| ++esc++ | Number box | Puts back the value from before you started typing |
| Arrow keys, ++page-up++ / ++page-down++, ++home++ / ++end++ | Sliders | Move the slider |

### Menus

These apply to every tray menu and to the menu you get by right-clicking a scroll bar.

| Keys | What it does |
| --- | --- |
| ++up++ / ++down++ | Previous / next row, wrapping around at the ends |
| ++home++ / ++end++ | First / last row |
| ++enter++ or ++space++ | Runs the selected row |
| ++right++, ++enter++, or ++space++ on a submenu row | Opens the submenu and selects its first row |
| ++left++ | Closes the submenu |
| ++esc++ | Closes the menu |

### Tray Icon

Press ++win+b++ to put keyboard focus on the notification area, then use the arrow keys to get to the icon.

| Keys | What it does |
| --- | --- |
| ++enter++ or ++space++ | Windows 11 treats these as a left click on the icon. The classic notification area sends them as a separate key press, and the app answers that by showing its flyout or window. |
| ++shift+f10++ or ++menu++ | Opens the tray menu |

### Dialogs

++esc++ closes the update prompt, the installer, and the uninstaller windows without doing anything, same as their **Cancel** or **Close** buttons. The color picker has no window-level keys; its sliders work like any other slider, and the scroll wheel moves them too.

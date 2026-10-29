# Themes

Every TADN app follows Windows light/dark mode out of the gate, and every app except Task Manager lets you override its colors. Color changes are live: the settings window, the flyout, and the tray icon update while you drag the color around, before you've committed anything.

## Where It Lives

Open the app's settings and go to **Theme** (**Appearance** in Task Manager). The cards below are the ones most apps share. Each app adds its own on top, e.g. card colors in Fan Control, curve colors in Brightness, or peak meter colors in Volume; those are on the app pages.

Since the flyout stays open while the settings window has focus (see [Tray Icon and Flyouts](tray-and-flyouts.md)), the easiest way to theme an app is to open the flyout, open settings from the flyout's settings button, and watch the flyout while you pick.

## Theme Mode

The theme mode card is called **Theme** in Battery, **Theme style** in Brightness, Fan Control, Network, and Volume, and **Theme mode** in Task Manager. The choices are **System**, **Light**, and **Dark**. The default is **System**.

***HERE IS THE TRICK*** **System** follows the Windows mode, i.e. **Choose your default Windows mode** in Windows' Personalization > Colors settings (the one the taskbar and Start use), not the app mode. If you run Windows dark with light apps, TADN comes out dark. Set **Light** or **Dark** here if you want it the other way. A change of the Windows mode is picked up while the app is running.

## Shared Cards

| Card | Default | What it does | Apps |
| --- | --- | --- | --- |
| **Font size** | 15 | Size of the right-click tray menu. Everything in the menu scales from it. | All but Task Manager |
| **Windows 11-style settings menu** | Off (On in Task Manager) | Shows page glyphs and uses a Windows 11 style layout for the settings window's page list. | All |
| **Text color** | App default | Overrides the text color. | All but Task Manager |
| **Background color** | App default | Overrides the background color. | All but Task Manager |
| **Rounded corners** | On | Rounded corners on the flyout, settings cards, buttons, and menus. Turn it off for a squared-off look. | All |
| **Animations** | **System** | Whether tooltip fades and other UI animations are allowed. **System** follows the **Animation effects** setting in Windows; **Disabled** and **Enabled** override it. | Battery, Network, Task Manager, Volume |
| **Tooltip delay** | 750 ms | How long the pointer has to hover before a tooltip appears, 0 to 10000 ms. | Battery, Network, Task Manager, Volume |
| Tray icon color | App default | Overrides the tray icon color. Called **Tray icon color**, **Static icon color**, or **Static tray icon color** depending on the app. | All but Task Manager |

Battery and Fan Control also have flyout background and flyout title bar colors, and Brightness has a footer background color.

## Picking a Color

Each color card has a color swatch and a **Reset** button.

- The card only shows the swatch for the theme that's in effect right now. A color you pick in dark mode is the dark variant; the light variant is untouched, and vice versa. To set the other variant, switch the theme mode to it first.
- Click the swatch to open the color picker. Its title says which variant you're editing, e.g. **Text color (dark)**.
- **Reset** on the card clears both the light and the dark override and goes back to the app's default color.

The color picker has a saturation/brightness square, **hue**, **alpha**, **r**, **g**, and **b** sliders, and two hex boxes: **rgba hex:** and **argb hex:**. Type a full hex value into either box and it applies.

- **Default** sets the picker to the app's default color.
- **Reset** sets it back to the color it had when you opened the picker.
- Everything previews live while the picker is open. The color is saved when you close the picker, and only if it actually changed.
- The picker isn't modal. You can leave it open and keep using the settings window. Closing the settings window closes any open pickers and keeps what they had.

A few cards hold a single color that doesn't depend on light/dark mode (e.g. the peak meter colors in Volume). Those have one swatch and a **Reset** button, and work the same way otherwise.

## Where It's Saved

Everything on the Theme page is saved in the app's `settings.xml` along with the rest of its settings, as you change it. There's no separate theme file to manage. See [Configuration](../getting-started/configuration.md) for where that file lives.

### The theme.xml File

Each app also reads a `theme.xml` from its data folder once, at startup, if one exists. The app never creates or writes this file, so normally it isn't there. The Theme page overrides win over anything in it. If you put one there and the app's colors look wrong, delete it and restart the app.

## Resetting

- One color: click **Reset** on its card.
- The other cards have no reset button; set them back to the defaults listed above.
- Everything at once: reset the app's settings as described in [Configuration](../getting-started/configuration.md). That resets all of the app's settings, not just the theme.

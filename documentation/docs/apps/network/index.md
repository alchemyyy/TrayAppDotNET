# Network

As simple as it gets. The still-embedded Win10 Windows Network flyout works great, so NetworkTrayAppDotNET just invokes it. It still has some quick-access stuff in its tray context menu, and its tray icon shows your connection state.

## Left-Click Flyout

Left-click the tray icon and you get the Windows 10 network flyout: your connections, the Wi-Fi list, and the Wi-Fi / Airplane mode / Mobile hotspot buttons. That's the Windows UI, not mine, so everything in it works like it always did.

If you'd rather get something else, change **Settings** > **General** > **Left-click flyout**:

- **Windows 10 network flyout** (default): the classic network flyout.
- **Windows 11 control center**: the Windows 11 Quick Settings panel, opened directly.
- **Quick Settings (Action Center)**: the same panel, opened through the `ms-actioncenter:` link.
- **Available networks (ms-availablenetworks)**: the Windows Wi-Fi network list.
- **Settings > Network & Wi-Fi**: the Wi-Fi page of Windows Settings.

The first two talk to undocumented parts of the Windows shell. They work on current builds, but Microsoft can break them whenever it likes. When the option you picked fails to open, the app opens **Available Networks** instead, so a click always does something.

## Tray Icon

The icon follows your connection and updates on its own when Windows reports a network change. The app also re-checks every 3 seconds.

| State | Icon | Default color |
| --- | --- | --- |
| Wi-Fi with internet | Wi-Fi arcs, 0 to 4 bars by signal strength | White (dark theme) / black (light theme) |
| Wi-Fi without internet | Wi-Fi arcs by signal strength | Amber |
| Wi-Fi connecting | 1 bar | Amber |
| Wi-Fi on but not connected | Empty Wi-Fi arcs | Gray |
| Ethernet with internet | Ethernet | White / black |
| Ethernet without internet (local only, or a captive portal) | Ethernet | Amber |
| Ethernet disconnected | Ethernet | Gray |
| No network at all | No-network glyph | Gray |

Below 4 bars, the unlit arcs are drawn faintly behind the lit ones. Windows can report 5 bars; that shows as full signal.

Hover the icon for a tooltip that lists every active connection by name, each with **Internet access** or **No internet**. With nothing connected it says what's going on instead: **Wi-Fi Connecting...**, **Wi-Fi Disconnected**, **Ethernet Disconnected**, or **No network connection**.

### Icon Colors

**Settings** > **Theme** > **Tray icon colors** starts with **Tray icon style**. **Dynamic (state colors)**, the default, colors the icon by connection state with these three colors:

- **Connected color**: an interface has internet access.
- **No-internet color**: connected locally, no internet. Also used while Wi-Fi is connecting.
- **Disconnected color**: no usable interface.

Each has a light and a dark theme variant. Leave one unset and it uses the default from the table above. Color picking itself works like everywhere else in the suite, see [Themes](../../shared/themes.md).

**Static** draws the icon in the single **Tray icon color** for every state instead; the card for it only shows while **Static** is picked, and the three state colors only while **Dynamic** is.

## Tray Context Menu

Right-click the tray icon (or press ++shift+f10++ / the menu key while the icon has keyboard focus):

- **Network settings**: opens **Network & internet** in Windows Settings.
- **Adapter settings**: opens the Network Connections window, i.e. the list of adapters where you change IPv4 settings, disable an adapter, etc.
- **App settings**: opens this app's settings window.
- **Exit**: closes the app.

By default the menu docks in the bottom-right corner like a Windows 11 flyout. Set **Settings** > **Tray icon** > **Menu position** to **Classic** if you want it at the mouse instead. The rest of the menu customization is shared, see [Tray Icon and Flyouts](../../shared/tray-and-flyouts.md).

### Adapter Settings Window

**Settings** > **General** > **Adapter settings window** picks which window **Adapter settings** opens:

- **Explorer (Network Connections)** (default): Network Connections as an Explorer window.
- **Control Panel (ncpa.cpl)**: the classic Control Panel applet.

Both show the same adapters. Windows may open either one in a separate background `explorer.exe` and leave it running after you close the window. The app watches for that and ends the leftover process once you close the window, but only when that process has no other visible window left, so you don't pile up a stray `explorer.exe` every time you open it.

## Click Actions

Clicking with modifiers held, or double-clicking, runs a different action. Set them in **Settings** > **Tray icon** > **Modified actions**. Each one can be **Nothing**, **Open app settings**, or **Open adapter settings**. **Nothing** falls through to the plain click (or the plain double-click for the modified double-clicks).

| Click | Default |
| --- | --- |
| **Ctrl + left-click** | Open adapter settings |
| **Alt + left-click** | Open app settings |
| **Ctrl + right-click** | Nothing (opens the context menu) |
| **Alt + right-click** | Nothing (opens the context menu) |
| **Double-click** | Open adapter settings |
| **Ctrl + double-click** | Nothing (uses **Double-click**) |
| **Alt + double-click** | Nothing (uses **Double-click**) |

***Heads up***: a double-click starts with a normal click, so it opens the left-click flyout first and then runs the double-click action. With the defaults you get the network flyout and the Network Connections window together. If that bugs you, set **Double-click** to **Nothing** and use **Ctrl + left-click** for adapter settings.

## Hotkeys

Global hotkeys work like in every TADN app, see [Hotkeys](../../shared/hotkeys.md). The **Hotkeys** page has two rows:

| Action | Default |
| --- | --- |
| **Open flyout** (opens whatever **Left-click flyout** is set to) | Unbound |
| **Open settings** | Unbound |

??? note "Hotkeys for Network Settings and Adapter Settings"
    The app also has `OpenNetworkSettings` and `OpenAdapterSettings` hotkey actions. They do the same as the matching context menu items, but they have no row on the **Hotkeys** page, so you add them by hand in `settings.xml` (see [Configuration](../../getting-started/configuration.md)). Exit the app first, then add a `Binding` line inside `<Hotkeys>`:

    ```xml
    <Binding Action="OpenAdapterSettings" Parameter="" Modifiers="10" VirtualKey="78" Enabled="true" BindingID="0" />
    ```

    `Modifiers` is the sum of Alt = `1`, Ctrl = `2`, Shift = `4`, Win = `8`, and needs at least one of them. `VirtualKey` is the Windows virtual-key code in decimal (letters are their uppercase ASCII code, e.g. `78` for N). The example binds ++ctrl+win+n++.

## Differences from the Other Apps

The app has no window or flyout of its own, the left-click belongs to Windows. So there's no flyout undocking, keep-open, or flyout settings here, and no scroll wheel action on the tray icon.

**Settings** > **General** > **Rendering backend** defaults to **Software** in this app, where most of the suite defaults to **GPU preferred**. Switch it like in any other app, see [Settings Window](../../shared/settings-window.md).

Everything else (settings window, themes, language, run on startup, installation, updates, crash recovery) is the shared stuff, see [Shared Features](../../shared/index.md).

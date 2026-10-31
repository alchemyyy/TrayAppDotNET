# Night Light

The **Night Light** row in the Brightness flyout drives the real Windows Night Light, the one in **Settings** > **System** > **Display** > **Night light**. With the default backends the app doesn't draw a filter of its own. It changes the Windows setting, so it's one system-wide tint for all your displays, and it's still there if you exit the app. The exception is the **Gamma ramp** backend, where the app tints the displays itself, see [Gamma Ramp](#gamma-ramp).

## Flyout Row

- **Bulb icon** (tooltip **Toggle night light**): turns Night Light on or off. It's greyed out when the app can't control Night Light on this machine, see [Backends](#backends).
- **Slider**: the strength, 0 to 100. 0 is 6500 K (no tint), 100 is 1200 K (as warm as Windows goes), linear in between. The slider is greyed out while Night Light is off, so turn it on first.
- **Curve button**: turns the Night Light curve on or off, see [Curve and Schedules](#curve-and-schedules).

Turning Night Light on when its strength is 0 would change nothing you can see, so the app restores the last non-zero strength you picked instead (50 the very first time). Turning it off keeps the strength, and the next toggle on brings back the same warmth.

**Settings** > **Flyout** > **Show kelvin label on night light slider** (off by default) adds the color temperature to the row title, e.g. **Night Light 4500K**. **Settings** > **General** > **Enable Night Light control** (on by default) hides the whole row when you turn it off.

## Other Controls

- **Tray icon**: ++ctrl++ + scroll over the tray icon changes the strength by **Mouse wheel step** per notch (2% by default). That's the default for **Ctrl + mouse wheel** in **Settings** > **Tray icon** > **Modified actions**, and you can put Night Light on the plain wheel or ++alt++ + wheel there too. See [Brightness](index.md#tray-icon).
- **Tooltip**: while Night Light is on, the tray tooltip has a **Night Light: N%** line.
- **Hotkeys**: **Toggle night light**, **Increment night light** and **Decrement night light** (2% per press). All unbound by default, see [Hotkeys](index.md#hotkeys).
- **Profiles**: every profile saves the Night Light strength and applies it when you switch to it. Profiles don't save or change whether Night Light is on. See [Profiles](index.md#profiles).

Only the bulb icon and the **Toggle night light** hotkey turn Night Light on. The slider, the wheel, the increment/decrement hotkeys, profiles and the curve only change the strength, and only while Night Light is already on. While it's off they do nothing.

## Curve and Schedules

The Night Light curve sets the strength by time of day, next to the brightness curve. How to draw it and how it follows the sun is on [Day/Night Curve](curve.md). What matters here:

- The curve only changes the strength. It never turns Night Light on or off (but see the gotcha below).
- It only writes while Night Light is on. If it's off, the curve waits.
- If you turn Night Light on with the bulb while the Night Light curve is on and in control, it starts right at the curve's current value instead of your last manual strength.
- In the normal (absolute) curve mode, moving the Night Light slider by hand while the curve is on takes the row off the curve (manual override). How offset mode differs, and how and when the row goes back, is on [Day/Night Curve](curve.md).

The app never touches the Windows Night Light schedule. If you set one in Windows Settings, Windows keeps switching Night Light on and off on its own schedule, and the curve takes over the strength on its next tick once Night Light is on. So you can let Windows decide *when* and the curve decide *how warm*. Or skip the Windows schedule, leave Night Light on, and have the curve go down to 0 during the day.

***Don't combine that last setup with Turn off at zero strength.*** That option turns Night Light off whenever the strength reaches 0, including when the curve sets it. The curve can't turn it back on, so it stays off until you click the bulb.

The **Gamma ramp** backend is different: the Windows schedule can't turn the app's tint on, and the app switches Windows Night Light back off when the schedule turns it on. There, leave the tint on and let the curve do the timing. See [Gamma Ramp](#gamma-ramp).

## Settings

In **Settings** > **General** under **Night Light**:

| Setting | Default | What it does |
| --- | --- | --- |
| **Enable Night Light control** | On | Shows the **Night Light** row in the flyout. |
| **Invert night light slider** | Off | Reverses the slider, so dragging left makes it warmer. Profiles and the tooltip still use the real strength. |
| **Turn off at zero strength** | Off | Dragging Night Light to 0 also turns it off. The next toggle on restores the last non-zero strength. |
| **Night-light backend** | Settings handler | How the app applies Night Light, see [Backends](#backends). |
| **PDB download timeout** | 60 s | How long the symbol download for `SettingsHandlers_Display.dll` may take (5 to 600), see [Settings Handler](#settings-handler). |

**Environmental tick interval** sits in the same group but belongs to the curves, see [Day/Night Curve](curve.md).

With **Turn off at zero strength** on and the kelvin label shown, the row title says **off** instead of a temperature at 0.

## Backends

The app has three backends. Pick one in **Settings** > **General** > **Night-light backend**. A change takes effect right away, no restart.

With **Settings handler** and **Registry**, reading the current state always comes from the same place (Windows' Night Light data in the registry), and turning Night Light *off* always works through that path. **Gamma ramp** doesn't use Windows Night Light at all.

### Settings Handler

The default. The app drives the same internal Windows component (`SettingsHandlers_Display.dll`) that the Night Light slider in Windows Settings uses, from a small helper process. That way Windows Settings shows the same strength as the flyout, and it can turn Night Light on even on a fresh Windows profile where it was never used.

The catch: it calls internal functions of that DLL, so the app has to know where they are in your exact Windows build. Builds it knows about are built in, and listed in `nightlight_known_rvas.xml` in the `nightlight` folder inside the app's data folder (see [Configuration](../../getting-started/configuration.md)), where you can add or correct a build; an entry with a missing or zero location is ignored. When a Windows update brings a DLL it doesn't know, the app downloads the matching debug symbols from Microsoft's public symbol server (`msdl.microsoft.com`), works out the locations, and caches the result in the same folder. That download happens once per DLL build, and the symbols of earlier builds are deleted afterwards.

The helper starts in the background the first time you change Night Light, so the tray and flyout never wait for it, even during a symbol download; the change is applied as soon as the helper is ready.

If the download can't happen (no internet, the symbol server is blocked, an air-gapped machine) or the symbols don't resolve, the Settings handler backend is unavailable. ***There's no automatic fallback to Registry.*** You'll see the bulb fail to turn Night Light on and the slider do nothing. Switch the backend to **Registry**.

**PDB download timeout** (60 s by default, 5 to 600) caps the whole download. On a slow or metered connection, raise it. After a failed start the next change retries, at first after 5 seconds and then with longer waits (up to 5 minutes), so raising the timeout takes effect without a restart. When the symbols resolve but your Windows build doesn't have the functions at all, the backend stays off for the rest of the session.

### Registry

The app reads and writes Windows' Night Light data directly in the registry. No DLL internals, no downloads.

Two things to know:

- Windows has to have set up Night Light on your account first. On a profile where Night Light was never turned on, the row stays greyed out. Turn it on once in Windows Settings (or use the Settings handler backend once), and from then on Registry works.
- On Windows 11 24H2 and build 26200, Windows itself ignores plain strength changes in the registry (Microsoft's own UI is hit by this too). The app works around it by writing each change in a way that forces Windows to re-apply the tint.

### Gamma Ramp

The app leaves Windows Night Light alone and tints the displays itself, by writing a warmer color ramp straight into each display's gamma table on the graphics card. Pick it when Windows Night Light is the problem: a locked-down profile, or a stuck Night Light service that ignores changes.

How it differs from the other two:

- The app owns the on/off state and the strength. Windows doesn't know about this tint, so Windows Settings still shows Night Light as off. The app saves both and puts the tint back when it starts. Exiting the app removes the tint.
- If the app is killed or crashes instead of exiting, nothing is left to remove the tint, so it stays on screen. It goes away when you sign out, or when you start the app again and turn Night Light off with the bulb (the app first puts your saved state back). Uninstalling the app also removes it.
- The strength scale is the same: 0 is 6500 K (no tint), 100 is 1200 K.
- Switching to **Gamma ramp** takes over from Windows Night Light: if it was on, the tint comes back at the same strength, now drawn by the app. Switching away removes the tint and leaves Windows Night Light off, so click the bulb to turn it back on.
- While the tint is on, the app keeps Windows Night Light off, since both would tint the same displays on top of each other. If Windows Night Light gets turned on anyway (by you, or by its schedule), the app turns it back off within about 5 seconds. With this backend, use the Night Light curve for timing instead of the Windows schedule.
- The tint is written again after display changes, after the PC wakes up, and after you unlock it. On top of that, the app checks every 5 seconds whether something else (a game, a GPU driver) reset the gamma table, and puts the tint back if so.

Limits:

- HDR displays aren't tinted. Windows ignores the gamma table on a display running in HDR, so the app skips those. Your SDR displays still get the tint.
- Windows refuses gamma tables that stray too far from neutral. When a display refuses a strength, the app uses the strongest one it accepts instead, so at high strength the tint can stop getting warmer. Setting the `GdiIcmGammaRange` DWORD to `256` under `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM` lifts that limit. The app tries the full strength again the next time you turn Night Light on or a display changes.
- Other software that writes the gamma table (games, GPU control panels, other tint apps) overwrites the tint. The app puts it back within 5 seconds, so expect a short flash. Two tint apps running at once keep fighting, so close one.
- The app replaces whatever gamma table the display had, including a calibration curve loaded by Windows color management or calibration software. Turning the tint off puts back a plain neutral table, not your calibration. Displays the app never tinted are left alone.

## Reapplying After Display Changes

When displays are plugged in, unplugged or rearranged, and after the PC wakes up or you unlock it, the app re-sends the current Night Light strength, since the tint doesn't always survive a display change.

## Troubleshooting

- **Bulb greyed out**: on **Registry**, Night Light hasn't been set up on this account yet, see [Registry](#registry). On **Settings handler**, the app couldn't find `SettingsHandlers_Display.dll` in `C:\Windows\System32`.
- **Bulb does nothing or the slider doesn't change the tint**: on **Settings handler** after a Windows update, the symbol download probably failed. Switch to **Registry**.
- **Night Light unresponsive**: the **About** page's fix for Night Light corruption is to press ++ctrl+shift+win+b++ (Windows' graphics driver reset; the screen flashes black for a moment), then sign out and back in.
- **Gamma ramp tint is weaker than the slider says**: the display refuses stronger gamma tables, see the `GdiIcmGammaRange` note in [Gamma Ramp](#gamma-ramp).
- **Gamma ramp does nothing on one display**: it's running in HDR. Turn HDR off for that display, or use another backend.

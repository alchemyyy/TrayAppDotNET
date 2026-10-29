# Configuration

In theory, any app settings you may need are properly exposed in the app's own settings UI (see [Settings Window](../shared/settings-window.md)). If this is not enough, the ***vast*** majority of parameters/constants for any given app are stored to and read from that app's own `settings.xml` file.

## The Data Folder

Each app keeps its settings, logs and other data in its own folder:

```text
%LocalAppData%\TrayAppDotNET\<App>
```

e.g. `%LocalAppData%\TrayAppDotNET\VolumeTrayAppDotNET`. It's the same folder whether the app is installed or portable. The quickest way there is **Settings** > **About** > **Files** > **Open** (the card is called **Open settings folder**).

What you'll find in it:

| File | What it is |
| --- | --- |
| `settings.xml` | Every setting the app saves. |
| `active.log` | The current log. |
| `old.log` | The previous log. The log rolls over once `active.log` reaches 10 MB or is 7 days old. |

Some apps keep a few more files next to those:

| App | Files |
| --- | --- |
| Battery | `learned-battery-usage.txt`, and `reports\battery-report.html` once you've generated a battery report. |
| Brightness | `profiles.xml` (your profiles), `displays.json` (displays it has seen), and a `nightlight` folder used by the Night Light integration. |
| Volume | `devices.xml` (per-device state). |

Two things in `%LocalAppData%\TrayAppDotNET` itself are shared by all apps: `versions.xml`, the cached release info for [Updates](updates.md), and `apps`, the install folder of Local installs.

## Editing settings.xml

Each app reads `settings.xml` once at startup and writes it back whenever you change something in the app. It doesn't watch the file. So:

1. Exit the app (**Exit** in its tray menu).
2. Edit `settings.xml`.
3. Start the app again.

If you edit the file while the app is running, your change is ignored until the next start, and the app overwrites it the next time it saves.

!!! warning "Keep a copy before editing"
    If `settings.xml` isn't valid XML anymore, the app logs the error and quietly starts with default settings. The next time it saves, it overwrites your broken file with those defaults, and every setting you had is gone. Copy the file somewhere before you edit it.

## Resetting to Defaults

There's no button that resets everything (theme colors have their own **Reset**, see [Themes](../shared/themes.md)). To start fresh, exit the app, delete or rename `settings.xml`, and start it again. Deleting the whole data folder resets the app-specific files listed above too.

***A fresh `settings.xml` turns Run on startup back on***, since it's on by default and gets applied when the app runs without a settings file. Turn it off again afterwards if you don't want it.

## Portable vs Installed

To these apps, there is not much difference between running portable and being installed:

- Both use the same data folder, so a portable copy and an installed copy of the same app share their settings.
- Both update themselves in place, see [Updates](updates.md).
- Only installed copies get an entry in Windows Settings > Apps, Start Menu entries, the optional desktop shortcut, and show up in the installer.
- [Run on startup](#run-on-startup) prefers an installed copy over the one you're running.
- Only one copy of an app runs at a time, no matter where it's installed. See [One Copy at a Time](#one-copy-at-a-time).

Installing a portable copy is covered in [Installation](installation.md#installing-from-a-portable-copy).

## Run on Startup

**Settings** > **General** > **Run on startup** (**Run on startup automatically** in Brightness, Fan Control, Network and Volume) starts the app when you sign in. It's on by default: the first time an app runs without a `settings.xml`, it turns this on by itself.

It works with a plain shortcut, `<App>.lnk`, in your Startup folder (type `shell:startup` into the Run dialog to open it). The shortcut passes `--autostart`, which is how an app knows it was started at sign-in. The toggle just mirrors whether that shortcut exists, so deleting the shortcut by hand turns the setting off too. The card shows where the shortcut currently points.

The shortcut always points at the best copy it can find, in this order:

1. The System install (`C:\Program Files\TrayAppDotNET\<App>.exe`).
2. The Local install (`%LocalAppData%\TrayAppDotNET\apps\<App>.exe`).
3. The exe you're running, i.e. a portable copy.

So turning it on from a portable copy still points it at an installed copy if you have one. Installing or uninstalling a copy moves the shortcut along. If the shortcut ends up pointing at an exe that's gone, an installed copy repoints it at itself on its next start.

The Startup folder and the data folder are both per Windows user. With a System install, each user gets their own shortcut the first time they run the app, and turning it off for one user doesn't affect anyone else.

Task Manager has a **Start minimized** option under this setting. It only applies to sign-in launches; starting the app yourself always opens the window. See [Task Manager](../apps/task-manager/index.md).

## Using a Different Data Folder

Launching an app with `--settings-dir <path>` makes it use `<path>` instead of `%LocalAppData%\TrayAppDotNET\<App>` for `settings.xml`, the logs and its other data files. Handy for keeping a second set of settings around, or keeping a portable copy's settings next to it.

```text
VolumeTrayAppDotNET.exe --settings-dir "D:\Portable\VolumeSettings"
VolumeTrayAppDotNET.exe --settings-dir %USERPROFILE%\tadn\volume
```

How it behaves:

- Relative paths resolve against the folder you launch from, and environment variables like `%USERPROFILE%` get expanded.
- The folder is created if it doesn't exist yet.
- A path that ends in a backslash must not be quoted. `"C:\dir\"` swallows the closing quote under Windows' quoting rules, and the app refuses to start. Write `"C:\dir"` or `C:\dir\` instead.
- If the path is missing or unusable, the app shows an error and exits.
- The override sticks across crash recovery restarts, the restart after changing the rendering backend, and the restart after an update.
- Turning on Run on startup from an instance with an override writes the override into the startup shortcut. A brand-new override folder doesn't turn Run on startup on by itself.
- `versions.xml` and the install folder are unaffected. Only the per-app folder moves.
- Uninstalling from Windows Settings with **Delete my settings** removes only the default folder, never the override.

The full list of arguments is in [Launch Arguments](launch-arguments.md).

## One Copy at a Time

Each app runs once per Windows session. Starting it again, from any copy, replaces the running one: the old copy is ended and the new one takes over. That includes a second copy with a different `--settings-dir`, so you can't run two sets of settings side by side. (The install and uninstall arguments are the exception, they run alongside without replacing anything.)

You'll still see two processes per app in Task Manager. That's the crash watcher and the app itself, see [Crash Recovery](../shared/crash-recovery.md).

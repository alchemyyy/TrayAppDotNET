# Installation

Grab the latest installer for whichever app(s) you'd like, or download the bundled suite. Then install it. Done. Every app also runs portably, so installing is optional.

## Requirements

- Windows 11, 64-bit (x64). There are no ARM64 or 32-bit builds.
- Nothing else to install. The apps are self-contained native executables (no .NET runtime needed), and the installer runs on .NET Framework 4.8, which ships with Windows 11.
- An internet connection for update checks, see [Updates](updates.md).

## Downloading

Everything lives on the [Downloads Page](https://github.com/alchemyyy/TrayAppDotNET/releases). Each release has:

- `Installer_<App>_<version>.exe`: a single-app installer, e.g. `Installer_VolumeTrayAppDotNET_<version>.exe`.
- `Installer_TrayAppDotNET_<version>.exe`: the app bundle, with a selector for which apps to install.
- `<App>_<version>.zip`: the bare app, for running portably. This is also the file the built-in updater downloads.

All apps share one release number. How that number works is explained under [Versioning](updates.md#versioning).

## The Installer

Both installers open the same one-page window. The title says **Install TrayAppDotNET** for the bundle or **Install &lt;App&gt;** for a single app, with the version (or the number of bundled apps) right below it. Pick your options from top to bottom and click **Install**.

### Apps to Install

Only the bundle has this list. Every app starts checked except BatteryTrayAppDotNET on a machine without a battery: it starts unchecked with a **No battery detected** hint next to it. You can still check it yourself.

Apps that are already installed get a label at the end of their row, e.g. **Installed (Local)** or **Installed (System and Local)**. Once you click **Install**, the selected rows switch to their progress instead: **Waiting**, **Installing...**, **Installation complete**, **Installation failed** or **Not installed**.

### Installation Type

| Type | What it does | Where the apps go |
| --- | --- | --- |
| **Local** | Installs for the current user. No administrator rights needed. | `%LocalAppData%\TrayAppDotNET\apps` |
| **System** | Installs for all users. Windows shows one UAC prompt for the whole run. | `C:\Program Files\TrayAppDotNET` |
| **Portable** | Extracts the apps to a folder of your choice without installing them. | Your pick |

Local and System put every app's exe side by side in that one folder, and the **Install location** box just shows it. For Portable the box becomes editable and **Browse...** turns on. The default is a `TrayAppDotNET` folder (or `<App>` for a single-app installer) inside the folder the installer was started from. It has to be a full path, otherwise you get **Enter a full folder path for the portable copy.**

If you decline the UAC prompt for a System install, nothing gets installed and the status reads **Installation was cancelled because administrator approval was declined.**

A System install needs administrator rights for your own account. If Windows would ask for a different administrator's credentials, the installer refuses up front and says so, because the install would then run as that account. The System files are unpacked inside `C:\Program Files\TrayAppDotNET` itself, never in your temp folder, so nothing outside that protected folder runs elevated.

You can have both a Local and a System copy of the same app. Their Start Menu entries then get a **(Local)** and **(System)** suffix so you can tell them apart.

### Existing Installs

The installer looks for each app in the Local and System folders. If it finds any, a notice shows up above **Installation type**, e.g. "VolumeTrayAppDotNET is already installed for the current user in ..." followed by **The installation type is set to match, so installing replaces the installed copy.** The bundle just counts them and leaves the details to the row labels.

The installation type starts on whichever type holds the most installed copies (System wins a tie), so clicking **Install** right away updates what you already have instead of adding a second copy. A running copy gets stopped before its files are replaced. Your settings aren't in the install folder, so they survive a reinstall.

Portable copies leave no record anywhere, so the installer can't see them.

### Windhawk Notice

If Windhawk isn't installed, the installer shows a caution card with **Get Windhawk** and **View the mod** links. It only checks for Windhawk itself, not for the mod. See [Hiding the Stock Tray Icons](#hiding-the-stock-tray-icons) for why I recommend it.

### Options

- **Create a desktop shortcut**: starts checked only when the selected apps already have one.
- **Add a Start Menu entry**: starts checked, except for an installed app whose Start Menu entry you removed.
- **Launch when finished**: on by default.

The two shortcut boxes start out matching what's on the machine. If you leave one as it is, the install leaves those shortcuts alone; only a box you change is applied, so re-running the installer to update never deletes a shortcut you have.

The two shortcut options are greyed out for Portable.

### When It Finishes

The progress bar and status line follow along. **Cancel** stays available: apps that are already installing finish, so none is left half copied, and the ones still waiting are skipped. Closing the window does the same and closes it once the run has ended, after starting the apps that finished (with **Launch when finished** checked); it stays open if something failed that you need to see. An app installer that prints nothing for three minutes is treated as hung and ended: the status says that app may be incomplete and to run the installer again, and other apps it had stopped are started again. On success the status reads **Installation complete. Installed to ...** and **Install** turns into **Close**.

With **Launch when finished** checked, every installed app starts hidden in the tray. No windows pop up, which really only matters for Task Manager since it's the only app that opens a window at startup.

The apps turn on **Run on startup** the first time they run, so from now on they start with Windows. See [Run on Startup](configuration.md#run-on-startup) if you don't want that.

If something fails, the status line shows the error. Apps that finished installing before the failure are started again anyway (with **Launch when finished** checked), since their install stopped them. The installer writes a log to `%TEMP%\TrayAppDotNETInstaller\installer.log`; a System install's elevated part sends its lines into the same log. An installer that was itself started elevated writes `installer-<process>-<id>.log` in that folder instead.

If the installer says **This installer is damaged (...). Download it again.**, the download is incomplete or was altered; nothing can be installed from it.

## Installing from a Portable Copy

A portable copy can install itself, no installer download needed. Open its settings window (see [Settings Window](../shared/settings-window.md)) and go to **Settings** > **General** > **Installation**. There's one card for a per-user install and one for a system-wide install (titled e.g. **Local user** and **System-wide**; the exact wording varies a little between apps). Each card tells you where things stand:

- **Not installed at ...**, with **Requires administrator approval.** added on the system-wide card.
- **Build N installed at ...**, with an **Uninstall** button.
- **Installed build N; current build is M.**, with an **Update** button that copies the build you're running over the older one.
- **Currently running from ...**, when the copy you're looking at is the installed one.

Click **Install**, confirm the **Install app?** (or **Install app system-wide?**) prompt, and watch the progress under the card. The system-wide card triggers a UAC prompt. When it's done you get **Installation complete** with two choices: **Restart** starts the installed copy and closes the one you're running, **Not now** keeps the current copy running.

What gets installed is the exe you're running. Your settings already live in the shared data folder (see [Configuration](configuration.md)), so the installed copy picks them up as-is.

You can also run `<App>.exe --installer` to get a small **Install &lt;App&gt;** window: pick **Local** or **System** under **Install location**, choose **Desktop shortcut** and **Start Menu entry** (both start out matching what is already there), and click **Install**. The installed copy starts once it's done. For scripted installs, see [Launch Arguments](launch-arguments.md).

## Running Portable

Extract `<App>_<version>.zip` (or use the installer's **Portable** type) wherever you like and run the exe. To these apps, there isn't much difference between running portable and being installed. Settings and logs go to the same `%LocalAppData%\TrayAppDotNET\<App>` folder either way, and updates replace the portable exe in place.

What you don't get: no entry in Windows Settings > Apps, no Start Menu entry, and the bundle installer won't know the copy exists.

***Don't rename the exe.*** The updater looks for a file with the same name inside the release zip, so a renamed exe can't be updated.

More on how portable and installed copies interact (shared settings, which copy Run on startup points at, one copy at a time) is in [Configuration](configuration.md#portable-vs-installed).

## Hiding the Stock Tray Icons

These apps replace, and add to, the Windows 11 tray system apps. For the best effect, I recommend removing the stock Windows tray icons with [Windhawk](https://windhawk.net/) and its [Taskbar Tray System Icon Tweaks](https://windhawk.net/mods/taskbar-tray-system-icon-tweaks) mod. Otherwise you end up with two volume icons, two network icons, etc.

## Uninstalling

There are three ways in:

- Windows Settings > Apps > Installed apps. Each installed app has its own entry under its own name (e.g. **VolumeTrayAppDotNET**), and **Uninstall** opens that app's uninstaller.
- **Settings** > **General** > **Installation** > **Uninstall** in the app itself.
- `--uninstall-headless` from a command line, see [Launch Arguments](launch-arguments.md).

### The Uninstaller

The **Uninstall &lt;App&gt;** window says which folder it's about to remove and asks what to do with your settings:

- **Keep my settings** (the default): leaves `settings.xml` in place so a future install picks it up.
- **Delete my settings**: also removes the app's data folder, which the option spells out.

Click **Uninstall** to go, or **Cancel** (or ++esc++) to back out. A System install asks for UAC after you click **Uninstall**. If you're uninstalling the copy that's currently running, it closes itself.

The uninstall is run by the installed copy itself (elevated for a System install), never by a script or a copy from your user folders. It removes the startup shortcut, the Start Menu entries, the desktop shortcut and the Windows uninstall entry, stops the app, removes the app's files, and deletes the settings if you asked for that. Its own exe goes a moment after it exits; if something can't be removed, the uninstaller says what is left. If you have both a Local and a System copy, a startup shortcut that pointed at the removed copy moves over to the other one (keeping its arguments, e.g. a `--settings-dir`) instead of being deleted; a startup shortcut that points at a portable copy isn't touched. Files the apps share stay in the install folder until the last app in it is uninstalled.

**Note**: **Delete my settings** deletes the data folder of the copy you started the uninstall from, so uninstalling from the app's own **Settings** > **General** > **Installation** respects its [`--settings-dir`](configuration.md#using-a-different-data-folder). Windows Settings > Apps starts the uninstaller without one and always targets the default `%LocalAppData%\TrayAppDotNET\<App>` folder; delete a custom folder yourself.

A System uninstall needs administrator rights for your own account. If Windows would ask for a different administrator's password, the uninstall is refused with an explanation instead, because that account's profile would get the per-user cleanup.

An app can also stop its uninstall before anything is removed when removing it would break something for everyone. Task Manager does this when Windows Task Manager is still redirected to the copy being removed and the uninstall can't remove that redirect (a current-user copy registered by an older version, see [Replace Windows Task Manager](../apps/task-manager/index.md#replace-windows-task-manager)). The uninstaller window stays open with the reason, `--uninstall-headless` prints it and exits with code `1`, and the app keeps running.

### What Stays After Uninstalling

Two apps make machine-wide changes that outlive the app on purpose, because other software relies on them:

- **Fan Control** installs the **PawnIO** driver to read and drive the fans. Other hardware tools use the same driver, so uninstalling Fan Control leaves it installed. Remove it from Windows Settings > Apps (**PawnIO**) if nothing else needs it. Fan Control's cached copy of the PawnIO setup is deleted with the app.
- **Volume** registers Equalizer APO on the audio devices you enable it for, following Equalizer APO's own backup scheme. Uninstalling Volume leaves those registrations in place; run Equalizer APO's **Configurator** to remove them from a device, or uninstall Equalizer APO.

### Removing a Portable Copy

There's nothing to uninstall. Turn off **Run on startup** first (or delete `<App>.lnk` from your Startup folder, `shell:startup`), exit the app, then delete its folder. If you want the settings gone too, delete `%LocalAppData%\TrayAppDotNET\<App>`.

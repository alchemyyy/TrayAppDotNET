# Launch Arguments

Every TADN app exe takes the same set of command-line arguments, for starting it a certain way or for installing and uninstalling it without clicking through a window. Argument names aren't case-sensitive.

These go to the app exe (e.g. `VolumeTrayAppDotNET.exe`), not to the `Installer_*.exe` files. The installers only have their window.

## User Arguments

| Argument | What it does |
| --- | --- |
| *(none)* | Starts the app normally, under the [crash watcher](../shared/crash-recovery.md). |
| `--autostart` | Marks a sign-in launch. The [Run on startup](configuration.md#run-on-startup) shortcut passes it. Task Manager's **Start minimized** option only applies when it's present. |
| `--hidden` | Starts in the tray without showing a window, whatever the startup settings say. The installer passes it to the apps it launches. Task Manager is the only app that opens a window at startup, so it's the only one this changes. |
| `--settings-dir <path>` | Uses `<path>` instead of `%LocalAppData%\TrayAppDotNET\<App>` for `settings.xml`, the logs and the app's other data files. Details in [Using a Different Data Folder](configuration.md#using-a-different-data-folder). |
| `--installer` | Opens the small **Install &lt;App&gt;** window for the exe you ran. **Local** is selected by default; **System** shows a UAC prompt after you click **Install**. The installed copy starts afterwards. `--install-gui` does the same. |
| `--install-headless <local/system>` | Installs the exe you ran without opening a window, prints progress, then exits. `system` shows a UAC prompt. Doesn't start the installed copy. |
| `--install <local/system>` | Same as `--install-headless`, kept for compatibility. |
| `--installlocal` | Same as `--install-headless local`, then starts the installed copy. |
| `--installsystem` | Same as `--install-headless system`, then starts the installed copy. |
| `--desktop-shortcut <true/false>` | Whether a headless install creates a desktop shortcut. Default `false`. |
| `--start-menu-shortcut <true/false>` | Whether a headless install creates a Start Menu entry. Default `true`. |
| `--uninstall-headless <scope>` | Uninstalls without opening a window and prints progress. `<scope>` is `local` (or `user`, `localappdata`) or `system` (or `programfiles`). `system` shows a UAC prompt. |
| `--delete-settings <true/false>` | Whether `--uninstall-headless` also deletes the app's data folder. Default `false`. |
| `--uninstall <folder> --scope <scope>` | Opens the uninstaller window. This is what the entry in Windows Settings > Apps runs. `--uninstall-gui` does the same. |

`true`/`false` values also accept `1`/`0`.

### Install Locations

- `local` installs to `%LocalAppData%\TrayAppDotNET\apps`, no administrator rights needed.
- `system` installs to `C:\Program Files\TrayAppDotNET` and asks for UAC.

What gets installed is always the exe you ran, so run these from the downloaded or portable copy. An installed copy that's already running in that location gets stopped first.

### Output and Exit Codes

The install and uninstall arguments print one line per stage, like `[NN%] Stopping running instances`, to the console you started them from (or to wherever you redirected the output). Failures print as `[FAIL] ...`, and a finished install ends with `Installed to <path>`.

| Exit code | Meaning |
| --- | --- |
| `0` | Done. |
| `1` | Failed, or the UAC prompt was declined. |
| `2` | Bad arguments, e.g. an unknown scope or an unusable `--settings-dir` path. |

The apps are Windows programs, not console programs, so an interactive command prompt doesn't wait for them to finish. In a batch script, use `start "" /wait` when you need the result:

```bat
start "" /wait VolumeTrayAppDotNET.exe --install-headless system --desktop-shortcut true
echo %ERRORLEVEL%

start "" /wait VolumeTrayAppDotNET.exe --uninstall-headless local --delete-settings true
```

??? note "Internal helper arguments"
    The apps start copies of themselves with these to do work in the background: the crash watcher, elevated install steps, updates. They're listed so you know what you're looking at in a process list. Don't call them yourself.

    | Argument | Used for |
    | --- | --- |
    | `--watcher` | The crash watcher process. |
    | `--monitored --watcher-pid <pid>` | The app process the crash watcher starts and watches. |
    | `--install-system --source <exe> --build <number>` | The elevated half of a system install, after the UAC prompt. |
    | `--sync-start-menu [--remove-scope <scope>]` | Updates the all-users Start Menu entries from an elevated process. |
    | `--uninstall-prepare --scope <scope> [--delete-settings <true/false>]` | Removes shortcuts and the uninstall entry, stops the app and optionally deletes settings before the files are removed. |
    | `--progress-pipe <name>` | Lets an elevated install or uninstall step report its progress back to the window that started it. |
    | `--update-apply` | Replaces the files during an update, after the running app exits. Elevated only when the install folder needs it. |
    | `--update-restart` | Restarts the app after an update or a rollback and cleans up the downloaded files. Never elevated. |
    | `--worker` | Not an app argument: `Installer_*.exe` starts an elevated copy of itself with it for the System part of an install. |

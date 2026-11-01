# Launch Arguments

Every TADN app exe takes the same set of command-line arguments, for starting it a certain way or for installing and uninstalling it without clicking through a window. Argument names aren't case-sensitive.

These go to the app exe (e.g. `VolumeTrayAppDotNET.exe`), not to the `Installer_*.exe` files. The installers only have their window.

## User Arguments

| Argument | What it does |
| --- | --- |
| *(none)* | Starts the app normally, under the [crash watcher](../shared/crash-recovery.md). |
| `--autostart` | Marks a sign-in launch. The [Run on startup](configuration.md#run-on-startup) shortcut passes it. Task Manager's **Start minimized** option only applies when it's present. |
| `--hidden` | Starts in the tray without showing a window, whatever the startup settings say. The installer passes it to the apps it launches. Task Manager is the only app that opens a window at startup, so it's the only one this changes. |
| `--settings-dir <path>` | Uses `<path>` instead of `%LocalAppData%\TrayAppDotNET\<App>` for `settings.xml`, the logs and the app's other data files. The crash watcher, the restarts from **Settings** (rendering backend, **Restart from installed**), the update restart and the app's helper processes all keep using it. Details in [Using a Different Data Folder](configuration.md#using-a-different-data-folder). |
| `--installer` | Opens the small **Install &lt;App&gt;** window for the exe you ran. **Local** is selected by default; **System** shows a UAC prompt after you click **Install**. The installed copy starts afterwards. `--install-gui` does the same. |
| `--install-headless <local/system>` | Installs the exe you ran without opening a window, prints progress, then exits. `system` shows a UAC prompt. Doesn't start the installed copy. The scope also accepts `user`/`localappdata` and `programfiles`, like `--uninstall-headless`. |
| `--install <local/system>` | Same as `--install-headless`, kept for compatibility. |
| `--installlocal` | Same as `--install-headless local`, then starts the installed copy. |
| `--installsystem` | Same as `--install-headless system`, then starts the installed copy. |
| `--desktop-shortcut <true/false>` | Whether a headless install creates (`true`) or removes (`false`) the desktop shortcut. Left out, an existing shortcut stays as it is and none is added. |
| `--start-menu-shortcut <true/false>` | Whether a headless install keeps a Start Menu entry. Left out, a fresh install gets one and a reinstall leaves the entries as they are, so an entry you removed doesn't come back. |
| `--uninstall-headless <scope>` | Uninstalls without opening a window and prints progress. `<scope>` is `local` (or `user`, `localappdata`) or `system` (or `programfiles`). `system` shows a UAC prompt. |
| `--delete-settings <true/false>` | Whether `--uninstall-headless` also deletes the app's data folder. Default `false`. |
| `--uninstall [<folder>] --scope <scope>` | Opens the uninstaller window. This is what the entry in Windows Settings > Apps runs. The folder is only shown, never trusted; the removed folder always comes from the install location of `<scope>`. `--uninstall-gui` does the same. |

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
| `2` | Bad arguments, e.g. an unknown scope, an unusable `--settings-dir` path, or a `--desktop-shortcut`, `--start-menu-shortcut` or `--delete-settings` without `true`/`false`. |

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
    | `--sync-start-menu [--remove-scope <scope>]` | Updates the Start Menu entries of every profile. Refuses to run unelevated (exit code `1`) and rejects an unknown `--remove-scope` (exit code `2`). |
    | `--uninstall-prepare --scope <scope> [--delete-settings <true/false>] [--settings-dir <path>]` | The uninstall itself, run by the installed exe of the scope being removed (elevated for `system`): removes shortcuts and the uninstall entry, stops the app, deletes the app's files and optionally its settings, then leaves a hidden `cmd.exe` from the Windows folder to delete its own exe once it has exited. |
    | `--progress-pipe <name>` | Lets an elevated install or uninstall step report its progress back to the window that started it. |
    | `--update-apply` | Replaces the files during an update, after the running app exits. It is the installed exe itself (elevated only when the install folder needs it): it copies the downloaded package into a staging folder inside the install folder, checks its SHA-256, and replaces the files as a journaled transaction. |
    | `--update-restart` | Restarts the app after an update or a rollback and cleans up the downloaded files. Never elevated. |
    | `--worker` | Not an app argument: `Installer_*.exe` starts an elevated copy of itself with it for the System part of an install. |

    Brightness's Night Light and DDC helpers are started the same way. They receive the app's `--settings-dir` and log to their own file beside the app's log. Task Manager's elevated helpers (the broker and the changes behind **Replace Windows Task Manager** and **Always run as administrator**) get no `--settings-dir` and write no files; the broker sends its log lines to Task Manager's own log.

    Task Manager's **Always run as administrator** task starts it with `--elevated-start-task`, followed by nothing but `--autostart` and `--hidden` (any other argument after it makes the app exit with code `2`). See [Always Run as Administrator](../apps/task-manager/index.md#always-run-as-administrator).

    The watcher reads the app's exit code: `0` or `1` means the app exited on purpose, `3` while the watcher sees Windows ending the session means a sign-out or shutdown (the watcher restarts the app only if it was cancelled), and anything else, including `3` at any other time, is a crash. Every restart counts toward the repeated-crash limit.

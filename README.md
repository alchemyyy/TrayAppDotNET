# TrayAppDotNET

TrayAppDotNET is a multi-app Avalonia tray application workspace. The root solution builds the shared
`TrayAppDotNETCommon` library and these apps:

- `BatteryTrayAppDotNET`
- `BrightnessTrayAppDotNET`
- `FanControlTrayAppDotNET`
- `NetworkTrayAppDotNET`
- `TaskManagerTrayAppDotNET`
- `VolumeTrayAppDotNET`

All six apps use the same startup and installation argument handling.

## Launch Arguments

Run any app executable with no arguments to start the tray app normally. In normal mode the first process starts the
crash watcher, then the watcher starts the monitored app instance.

| Argument | Intended use | Behavior |
| --- | --- | --- |
| No arguments | User | Start the app normally under the crash watcher. |
| `--installer` or `--install-gui` | User | Open the one-page installer. Local installation is selected by default. Selecting system installation causes Windows to display a UAC prompt after Install is clicked. |
| `--install-headless <scope>` | User/script | Install without opening a window, print the result to the parent console when attached, then exit. System scope causes Windows to display a UAC prompt. |
| `--install <scope>` | User/script | Compatibility alias for `--install-headless <scope>`. |
| `--installlocal` | User/script | Install locally without opening a window, then start the installed instance. |
| `--installsystem` | User/script | Install system-wide without opening a window, display the Windows UAC prompt, then start the installed instance. |
| `--desktop-shortcut <true|false>` | User/script | Choose whether a headless install creates a desktop shortcut. The default is `false`. |
| `--start-menu-shortcut <true|false>` | User/script | Choose whether a headless install creates a Start Menu entry. The default is `true`. |
| `--uninstall <installDir> --scope <scope>` | App/Windows uninstall entry | Open the uninstaller for the supplied installation. Confirming a system uninstall causes Windows to display a UAC prompt. |
| `--uninstall-gui <installDir> --scope <scope>` | App/Windows uninstall entry | Alias for `--uninstall`. |
| `--uninstall-headless <scope>` | User/script | Uninstall without opening a window. System scope causes Windows to display a UAC prompt. |
| `--delete-settings <true|false>` | User/script | Choose whether `--uninstall-headless` also removes the application's settings. The default is `false`. |
| `--scope <scope>` | App helper | Select an installation for uninstall operations. Accepted values are `user`, `local`, `localappdata`, `system`, `programfiles`, `store`, and `windowsstore`. |
| `--watcher` | App helper | Run the crash watcher process. |
| `--monitored --watcher-pid <pid>` | App helper | Run the monitored app process owned by the watcher with the supplied watcher PID. |
| `--watcher-pid <pid>` | App helper | Supplies the watcher PID to a monitored app instance. |
| `--install-system --source <sourceExe> --build <buildNumber>` | App helper | Continue a system installation after Windows has displayed the UAC prompt, then write system uninstall and shortcut metadata. |
| `--sync-start-menu [--remove-scope <scope>]` | App helper | Reconcile all-user Start Menu shortcuts from a process started through the Windows UAC prompt. |
| `--uninstall-prepare --scope <scope>` | App helper | Remove shell integration and stop the installed process before the batch cleanup stage. For system scope, the owning batch is started through the Windows UAC prompt. |
| `--remove-scope <scope>` | App helper | Scope value consumed by `--sync-start-menu` when removing shortcuts for an uninstalling scope. |
| `--update-apply` | App helper | Apply a staged update after the running app exits. Windows displays a UAC prompt only when the installation directory requires elevation. |
| `--update-restart` | App helper | Restart the installed app after update commit or rollback, then clean the staging directory. This process is deliberately not elevated. |

## Examples

```console
VolumeTrayAppDotNET.exe --installer
VolumeTrayAppDotNET.exe --install-headless local
VolumeTrayAppDotNET.exe --install-headless system --desktop-shortcut true
VolumeTrayAppDotNET.exe --uninstall-headless local --delete-settings false
```

`--installlocal` and `--installsystem` are the install-and-run compatibility arguments. `--install` is the
compatibility alias for `--install-headless`.

Install argument exit codes:

- `0`: success.
- `1`: install or post-install launch failed, or the UAC prompt was cancelled.
- `2`: invalid install usage.

## License

Copyright (C) 2026 alchemyyy.

Project-authored material is licensed under the [GNU General Public License,
version 3 or later](LICENSE) (`GPL-3.0-or-later`). Third-party components and
assets retain their own licenses; see
[NOTICE](NOTICE).

# TrayAppDotNET

TrayAppDotNET is a multi-app Avalonia tray application workspace. The root solution builds the shared
`TrayAppDotNETCommon` library and these apps:

- `BatteryTrayAppDotNET`
- `BrightnessTrayAppDotNET`
- `FanControlTrayAppDotNET`
- `NetworkTrayAppDotNET`
- `VolumeTrayAppDotNET`

All five apps use the same startup and installation argument handling.

## Launch Arguments

Run any app executable with no arguments to start the tray app normally. In normal mode the first process starts the
crash watcher, then the watcher starts the monitored app instance.

| Argument | Intended use | Behavior |
| --- | --- | --- |
| No arguments | User | Start the app normally under the crash watcher. |
| `--install local` | User/script | Install the app to `%LOCALAPPDATA%\TrayAppDotNET\<AppName>.exe`, register uninstall/start-menu metadata, print the result to the parent console when one is attached, then exit. |
| `--install system` | User/script | Install the app to `%ProgramFiles%\TrayAppDotNET\<AppName>.exe`, register uninstall/start-menu metadata, print the result to the parent console when one is attached, then exit. This may trigger UAC. |
| `--installlocal` | User/script | Install to `%LOCALAPPDATA%\TrayAppDotNET\<AppName>.exe`, then start the installed instance after a successful install. |
| `--installsystem` | User/script | Install to `%ProgramFiles%\TrayAppDotNET\<AppName>.exe`, then start the installed instance after a successful install. This may trigger UAC. |
| `--uninstall <installDir> --scope <scope>` | App/Windows uninstall entry | Start the app in uninstaller UI mode for the supplied install directory and scope. |
| `--scope <scope>` | App helper | Scope value consumed by `--uninstall`. Accepted values are `user`, `local`, `localappdata`, `system`, `programfiles`, `store`, and `windowsstore`. |
| `--watcher` | App helper | Run the crash watcher process. |
| `--monitored --watcher-pid <pid>` | App helper | Run the monitored app process owned by the watcher with the supplied watcher PID. |
| `--watcher-pid <pid>` | App helper | Supplies the watcher PID to a monitored app instance. |
| `--admin-action install-system <sourceExe> <buildNumber>` | App helper | Elevated helper action used by system-wide install. Copies the payload into Program Files and writes system uninstall/start-menu metadata. |
| `--admin-action sync-startmenu [--remove-scope <scope>]` | App helper | Elevated helper action used to reconcile all-user Start Menu shortcuts. |
| `--remove-scope <scope>` | App helper | Scope value consumed by `--admin-action sync-startmenu` when removing shortcuts for an uninstalling scope. |

## Examples

```powershell
.\bin\Release\VolumeTrayAppDotNET.exe --install local
.\bin\Release\VolumeTrayAppDotNET.exe --install system
.\bin\Release\VolumeTrayAppDotNET.exe --installlocal
.\bin\Release\VolumeTrayAppDotNET.exe --installsystem
```

`--installlocal` and `--installsystem` are the install-and-run variants. `--install local` and `--install system`
install only and leave startup to the caller.

Install argument exit codes:

- `0`: success.
- `1`: install or post-install launch failed, or the UAC prompt was cancelled.
- `2`: invalid install usage.

<h1 align="center"><span>TrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/tadnlogo.png" alt="TrayAppDotNET logo" width="48" align="middle"></sup></h1>
<p align="center"><em>A suite of Windows 11 apps focused around one of the most under-loved pieces of UX ever.</em></p>

<p align="center">
  <a href="https://github.com/alchemyyy/TrayAppDotNET/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/alchemyyy/TrayAppDotNET?style=for-the-badge" alt="License">
  </a>
  <a href="https://github.com/alchemyyy/TrayAppDotNET/releases/latest">
    <img
      src="https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fapi.github.com%2Frepos%2Falchemyyy%2FTrayAppDotNET%2Freleases%2Flatest&amp;search=%22tag_name%22%3A%5Cs%2A%22TrayAppDotNET_%28%5B%5E%22%5D%2B%29%22&amp;replace=%241&amp;label=release&amp;style=for-the-badge"
      alt="Latest release">
  </a>
  <a href="https://github.com/alchemyyy/TrayAppDotNET/stargazers">
    <img src="https://img.shields.io/github/stars/alchemyyy/TrayAppDotNET?style=for-the-badge&amp;logo=github" alt="GitHub stars">
  </a>
  <a href="https://discord.gg/nzg7jm8yKC">
    <img src="https://img.shields.io/discord/1501706636535009390?style=for-the-badge&amp;logo=discord&amp;logoColor=white&amp;label=Discord&amp;cacheSeconds=300" alt="Discord members">
  </a>
  <a href="https://trayapp.net">
    <img src="https://img.shields.io/badge/Website-trayapp.net-0078D4?style=for-the-badge&amp;logo=windows11&amp;logoColor=white" alt="Website">
  </a>
</p>

## Overview

These are my projects for replacing, and adding to, the Windows 11 tray system apps. For the best effect, it's recommended to pair those apps which replace an existing Windows 11 counterpart, with [Windhawk's Taskbar Tray System Icon Tweaks Mod](https://windhawk.net/mods/taskbar-tray-system-icon-tweaks) in order to hide the stock OS tray icons.

<figure align="center">
  <img src="./documentation/readme_images/tray_sbs.png" alt="Preview" width="80%">
  <figcaption><em>This is an incomplete example. Stock tray icons were removed with Windhawk.</em></figcaption>
</figure>




### Installation

These apps are all portable executables, and they serve as their own installers. You can manage an app's installation by opening Settings -> About. 

Every release also ships a single-file installer per app, named `Installer_<AppName>_<version>.exe`, plus `Installer_TrayAppDotNET_<version>.exe` for the whole suite. The installers need nothing installed first, since they run on the .NET Framework that ships with Windows. They offer three installation types: Local (current user, no administrator rights), System (all users, one UAC prompt), and Portable (extract to a folder of your choice). Desktop and Start Menu shortcuts and launching the app when finished are optional. The suite installer lets you choose which apps to install and unselects BatteryTrayAppDotNET automatically when no battery is present. All installers show a notice with a link to Windhawk when it is not detected.

All app settings are saved to `%LocalAppData%\TrayAppDotNET`.

Each app is a Native AOT executable with the .NET runtime compiled in. The only loose files next to it are the ANGLE, Skia, and HarfBuzz rendering libraries plus the license notices.

#### Project status

These apps should all be considered to be in alpha. I personally use all of them without major issues, but there is still a lot that needs to be done. Also, some apps are much further along than others.

## Gallery

---

<h3><span>BatteryTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_batadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

---

<h3><span>BrightnessTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_btadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

This app lets you control the *actual* brightness of external displays, as well as Windows brightness (laptops) and Windows Night Light.

I kept running into DDC failures with Twinkle Tray, and I'm not a fan of how much memory Electron uses. I took inspiration from the fluent layout it used, so many thanks to Xander Franfangos for that! BrightnessTrayAppDotNET was the first "major" app I started working on. There are quite a few features.

<details>
<summary><strong>Features</strong></summary>
  
* User profiles
* Windows Night Light integration
* Brightness synchronization
* Control disengagement
* Day/Night curve
	* Auto re-engage
	* Quick-swap
	* Disengage timer
	* Graphical curve editor
    * Obliquity compensation
* Robust DDC recovery and fast apply
* Windows brightness support
* Display management
	* Renaming
	* Brightness normalization
	* DDC tuning
* Mouse wheel tray icon
	* Works with touchpad
	* No global mouse hook
* Dynamic tray icon
	* Solar eclipse visual shows current brightness level
</details>

<p align="center">
  <img src="./documentation/readme_images/ui_btadn.png" alt="Preview" width="80%" align="center">
</p>

---

<h3><span>FanControlTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_fctadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

Not to be confused with the very popular "Fan Control" project. The former to me is a bit cumbersome to use, and I find it odd its not open source. As the author of Fan Control states, that project and this one are both essentially UX wrappers around LibreHardwareMonitor, and nothing more.

---

<h3><span>NetworkTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_ntadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

This is as simple as it gets. I've been fine with the old Windows Network flyout, so this little app just invokes that and keeps track of its tray icon status. One feature I did add is a quick access tray menu entry that lets you open an explorer shell (dark theme supported) directly to the classic network adapters panel.

<p align="center">
  <img src="./documentation/readme_images/flyout_ntadn.png" alt="Preview" width="40%" align="center">
</p>

---

<h3><span>TaskManagerTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_tmtadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

A "reimplementation" of the Windows 11 Task Manager. The official Task Manager has become far too stuttery for me. While "fixing that", some extra bells and whistles were added.

<details>
<summary><strong>Features</strong></summary>
  
* Combined process + details view
	* Zoom + stretch
	* Dynamic sum totals
	* Multi-select
	* View options
	* Searchable columns
	* Column customization
* Enhanced search
	* Search saving
* Enhanced performance view
	* Preview graphs
	* Draggable device list
	* Live cursor graph hover
	* Physical memory layout
	* Single core metrics
* Background mode
* Modern tray icon with display modes
</details>

<p align="center">
  <img src="./documentation/readme_images/flyout_tmtadn.png" alt="Preview" width="80%" align="center">
</p>

---

<h3><span>VolumeTrayAppDotNET</span>&nbsp;<sup><img src="./documentation/readme_images/app_vtadn.ico" alt="Preview" width="32" align="middle"></sup></h3>

EarTrumpet users might find this familiar. I took inspiration from it in this design. I had originally modified EarTrumpet to show devices and app mixing from the bottom-up, to minimize mouse movement distance. Since that project seems to have been abandoned, I took a crack at the whole thing.

<details>
<summary><strong>Features</strong></summary>

* Configurable flyout structure
* Recording device support
* Bluetooth management
* Device management
	* Renaming
	* Defaulting
	* Visibility
* Smooth peak meters
	* The visual indicator of the current volume level
* Touchpad scroll tray icon
	* No global mouse hook
</details>

<p align="center">
  <img src="./documentation/readme_images/flyout_vtadn.png" alt="Preview" width="40%" align="center">
</p>

---

All apps come with the following features:

* Flyout undocking (where applicable)
* Searchable settings (with fuzzing)
* Live theme customization
* Rebindable Hotkeys
* Update system
* Crash recovery
* Customizable tray menus (where applicable)

## Development

I use Visual Studio 2026 with this project. Here are the necessary tools to compile it yourself.

* Visual Studio Build Tools 17 with:
  * Desktop C++ environment
  * MSVC v143
  * Windows 11 SDK 10

## Misc Details

#### Launch Arguments

| Argument | Intended use | Behavior |
| --- | --- | --- |
| No arguments | User | Start the app normally under the crash watcher. |
| `--autostart` | Startup shortcut | Mark a sign-in launch. The Run on startup shortcut passes it, the watcher forwards it to every monitored process it starts, and apps with a Start minimized option only honor that option when it is present. |
| `--hidden` | Installer | Start in the notification area without showing a window, whatever the startup settings say. The installer passes it to the apps it launches when it finishes, and the watcher forwards it to every monitored process it starts. Only Task Manager opens a window at startup, so it is the only app this changes. |
| `--installer` or `--install-gui` | User | Open the one-page installer. Local installation is selected by default. Selecting system installation causes Windows to display a UAC prompt after Install is clicked. |
| `--install-headless <scope>` | User/script | Install without opening a window, print progress lines (`[NN%] stage`) and the result to the parent console or redirected standard output, then exit. System scope causes Windows to display a UAC prompt. |
| `--install <scope>` | User/script | Compatibility alias for `--install-headless <scope>`. |
| `--installlocal` | User/script | Install locally without opening a window, then start the installed instance. |
| `--installsystem` | User/script | Install system-wide without opening a window, display the Windows UAC prompt, then start the installed instance. |
| `--desktop-shortcut <true|false>` | User/script | Choose whether a headless install creates a desktop shortcut. The default is `false`. |
| `--start-menu-shortcut <true|false>` | User/script | Choose whether a headless install creates a Start Menu entry. The default is `true`. |
| `--uninstall <installDir> --scope <scope>` | App/Windows uninstall entry | Open the uninstaller for the supplied installation. Confirming a system uninstall causes Windows to display a UAC prompt. |
| `--uninstall-gui <installDir> --scope <scope>` | App/Windows uninstall entry | Alias for `--uninstall`. |
| `--uninstall-headless <scope>` | User/script | Uninstall without opening a window, printing progress lines (`[NN%] stage`) to the parent console or redirected standard output. System scope causes Windows to display a UAC prompt. |
| `--delete-settings <true|false>` | User/script | Choose whether `--uninstall-headless` also removes the application's settings. The default is `false`. |
| `--scope <scope>` | App helper | Select an installation for uninstall operations. Accepted values are `user`, `local`, `localappdata`, `system`, `programfiles`, `store`, and `windowsstore`. |
| `--watcher` | App helper | Run the crash watcher process. |
| `--monitored --watcher-pid <pid>` | App helper | Run the monitored app process owned by the watcher with the supplied watcher PID. |
| `--watcher-pid <pid>` | App helper | Supplies the watcher PID to a monitored app instance. |
| `--install-system --source <sourceExe> --build <buildNumber>` | App helper | Continue a system installation after Windows has displayed the UAC prompt, then write system uninstall and shortcut metadata. |
| `--sync-start-menu [--remove-scope <scope>]` | App helper | Reconcile all-user Start Menu shortcuts from a process started through the Windows UAC prompt. |
| `--uninstall-prepare --scope <scope> [--delete-settings <true|false>]` | App helper | Remove shell integration, stop the installed process, and optionally delete settings before the batch cleanup stage. For system scope, the owning batch is started through the Windows UAC prompt. |
| `--progress-pipe <name>` | App helper | Named pipe through which an elevated install or uninstall helper reports its progress stages to the process that started it. |
| `--remove-scope <scope>` | App helper | Scope value consumed by `--sync-start-menu` when removing shortcuts for an uninstalling scope. |
| `--update-apply` | App helper | Apply a staged update after the running app exits. Windows displays a UAC prompt only when the installation directory requires elevation. |
| `--update-restart` | App helper | Restart the installed app after update commit or rollback, then clean the staging directory. This process is deliberately not elevated. |

#### Build Architecture

Each app is written in .NET 10 with Avalonia 12 and compiled with Native AOT. While the apps do share a common framework, they are totally independent from each other. This makes it much simpler to distribute them and manage privileges, and keeps each app isolated from one another in case of instability or performance issues. Memory overhead of this and these apps has been taken into account, especially given they are background applications, and care has been taken to make sure they are memory efficient. The entire suite uses roughly the same amount of RAM as one medium-sized electron app.

A small set of native libraries next to each executable makes up the Skia rendering backend Avalonia uses. These ship as shared DLLs instead of being compiled straight in because they're large enough in my opinion to warrant Windows re-using them via shared working set memory, to lower the overall memory footprint from running multiple of these tray apps together. 

The installers are produced by a factory. A single installer binary is built per release, carrying no app inside it. Stamping an installer then means copying that binary, swapping its icon, and appending the app's package to the end of the file, which takes seconds instead of a whole compilation. The installer finds its payload at startup by reading a trailer at the end of its own executable.

The appended package is not a zip. The factory repacks it into a solid payload, which is one LZMA stream over the concatenated file data of every entry with a dictionary of up to 32 MB, rather than a zip's separate DEFLATE stream per entry with a 32 KB window, so matches reach across the whole package. VolumeTrayAppDotNET measures 50,329,628 bytes raw, 20,222,033 as a zip, and 16,899,152 as a solid payload, which took its stamped installer from 21,207,583 to 17,202,910 bytes, an 18.9 percent reduction. Stamping one installer costs about 11 seconds and unpacking the payload at install time about 1.1 seconds. The LZMA encoder and decoder are written by hand and are conformant LZMA1, checked both directions against liblzma. Every stamp decompresses its own output and compares it to the SHA-256 recorded in the payload header, so no installer is written without one proven decode pass.

The installers themselves are WPF apps on .NET Framework 4.8 rather than Native AOT. That framework and its renderer are already part of Windows, so an installer is a few hundred kilobytes rather than the 28 MB an Avalonia build cost, where over half the weight was an embedded copy of Skia. The apps stay on .NET 10 with Native AOT; only the installers use the in-box framework.

#### AI Usage

I use frontier LLM's *heavily* in this project. Disclaimer: I am actually a software engineer; I do actually read and review what gets written, as well as use my fingers to write code myself sometimes, not just prompts. What I am not about to do is spend 6 months doing grunt work on a thousand Windows API's just for one feature to barely work, or another 2 months refactoring the entire codebase for any number of reasons. With that said, all the architecture and design is mine. I allow LLMs complete reign over spam generating test code since any broken tests will inevitably be cleaned by an LLM, and the more coverage the merrier. Beyond this, I'm fairly loose with scrutinizing code comments unless its something I *really* care about. I generally keep a defined stylesheet to minimize the amount of garbage.

If you look around the codebase you'll see some markdown files for LLM agents to assist them with context, mechanisms, etc. If you want to try and work on something in here with AI, I'd recommend reading at least the AGENTS.md file yourself. There are some acronyms in there (among other things), that make it a bit easier to communicate with your LLM of choice.

#### Versions

These projects don't use semver. There is no API contract to uphold, and I would prefer not assigning bizzare meaning to semvers that would cause them to look like they're flying all over. The TrayAppDotNET version increments every single release. The individual app versions incremenet only when they themselves have a new release. If an app is not scheduled to have a new version, or has no changes, the release generator will go grab the previous release artifact for that app and re-publish it. This is done so releases remain consistent and there are no tag spiderwebs.

#### Project Name - "Why TrayAppDotNET?"

I cooked up the network app first, which amounted to little more than a single chat message to Claude to whip up a WPF tray icon and a Windows shell call. I couldn't imagine a real name that wouldn't be outlandishly indescriptive so I called it what it was; "NetworkTrayAppWPF". I stuck with this scheme since it lets someone know immediately that these different apps are all from the same project, and because, again, they're quite descriptive. It's also quick and handy to refer to them by their acronyms.

## Translation

This project uses [Weblate](https://hosted.weblate.org/projects/feishin/) for translations. If you would like to contribute, please visit the link and submit a translation.

## License

[GNU General Public License v3.0](https://github.com/alchemyyy/TrayAppDotNET/blob/main/LICENSE)

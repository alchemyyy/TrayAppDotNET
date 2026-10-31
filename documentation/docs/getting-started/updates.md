# Updates

Every app checks for new releases by itself and can update itself in place, installed or portable. Nothing happens without you confirming it first.

## Versioning

These projects don't use semver. There is no API contract to uphold, and I would prefer not assigning bizarre meaning to version numbers that would make them look like they're flying all over.

The TrayAppDotNET version increments every single release. If an app has no changes, or isn't scheduled for a new release, the publish workflow grabs that app's previous release artifact and re-publishes it. That way every release has a complete set of downloads.

The installers don't have a version of their own. They line up with the latest TrayAppDotNET release, and any re-published apps get packaged into the latest installer.

## When It Checks

- The first check runs 5 seconds after the app starts, then once every **Update check interval** (60 minutes by default).
- If a check fails, the next one comes after 10 minutes, or after the interval if that's shorter.
- Clicking **Check for updates** always asks right away and restarts the interval timer.
- With **Check for updates automatically** turned off, the app never checks by itself. **Check for updates** still works.

The release info comes from `https://version.trayapp.net/versions.xml`, and the update itself is downloaded from the GitHub release.

All TADN apps share one copy of that release info in `%LocalAppData%\TrayAppDotNET\versions.xml`. An app only goes online when that copy is older than its own interval, so running six apps doesn't mean six requests. When one app fetches a new release, the others (the ones with automatic checks on) notice within a second and notify you as well.

## Getting Notified

The **Updates** section of **Settings** > **About** always shows where things stand: **Update available: ...**, **You're up to date. Last checked ...**, **Update check failed. Last tried ...**, etc. The install button next to it reads **Install update** when there's something to install, otherwise **Up to date**, **Check failed**, or **Version stale** (the last check is older than the interval, plus a few seconds of grace).

On top of that:

- **Show notification for updates** (off by default) raises a tray notification, **Update available**, once per new release. Clicking it opens the **About** page in Battery, Fan Control and Network, the flyout in Volume, the flyout plus the update prompt in Brightness, and the main window in Task Manager.
- **Show update button in flyout** (on by default) puts an **Update!** button in the Brightness and Volume flyouts, and an **Update available** card at the top of the Fan Control flyout. Battery and Network don't have this option.
- Task Manager shows an **Update!** entry at the bottom of its window's sidebar whenever an update is available.

## Installing an Update

Clicking **Install update** (or any of the buttons above) opens **Proceed with update?**. It shows the app, **New version available: N**, **Current running version: M**, links to **view releases** and **trayapp.net**, and a reminder: **Updating will cause app to restart.** The buttons are **Install update**, **Skip release** and **Close**.

After **Install update**:

1. The app downloads the release zip for itself (up to 3 attempts; a download that receives nothing for 30 seconds is abandoned and retried). The zip is only used when its SHA-256 matches the one the release publishes; a missing or different hash stops the update before anything is extracted.
2. If the install folder needs administrator rights, Windows shows a UAC prompt. That's the case for `C:\Program Files` (i.e. System installs) and any other folder you can't write to. Local installs and portable copies in your own folders update without one. Declining the prompt leaves the running app untouched. The update needs administrator rights for your own account: if Windows would ask for a different administrator's password, the update is refused with an explanation.
3. The app closes, its files get replaced, and it starts again.

If the update includes a file that the other TADN apps in the same folder also use, those apps get stopped and restarted along with it.

If the download fails, you get **Update failed** / **The update could not be downloaded. Check the log for details.** and the app keeps running as before. If the same app is already installing an update (say from a second click), you get **Another update of this app is already being installed** instead. Updates of different apps in the same folder don't block each other.

Once you click **Install update**, the update finishes even if you close the flyout or settings window you started it from. A failure message then opens on its own.

### Rollback

The files are replaced as a journaled transaction: each old file is moved aside before the new one takes its place, and a journal in the install folder records every step first. If replacing the files fails partway, the previous version is put back and restarted, and a message box tells you so: "The update could not be installed. The previous version was restored and restarted." It includes the path to a log with the details.

!!! warning "Incomplete rollback"
    In the rare case the rollback itself can't finish, or the update helper is killed midway, the app is ***not*** restarted, so it never runs from a half-updated folder. The journal and the old files stay in the install folder, and the next update or installation of the app finishes the rollback first. Reinstalling the app with the installer gets you back to a working state.

The downloaded zip, the extracted files and the update log go to `%LocalAppData%\Temp` while the update runs (named `<App>_update_<build>_...`) and get cleaned up afterwards. The helper that replaces the files is the installed exe itself: it copies the package into a hidden `.tadn-update-<App>-...` folder inside the install folder, checks its SHA-256 again there, and removes that folder when it's done. After a successful update, the build number shown in Windows Settings > Apps is updated too.

Copies installed before this way of updating update one last time the older way: their update starts the downloaded app to replace the files, which this release still accepts for that single step, copying the files through the same protected staging folder. Every update after that uses the checks described above.

## Skipping a Release

**Skip release** in the update prompt, or **Skip this release** on the **About** page, asks first: **Skip release N?** Release N won't be offered again, but newer releases still will. The skip is stored per app, so it never affects another app. Declining the confirmation from the update prompt takes you back to the prompt.

While the skipped release is the newest one, the **About** page shows **Release N skipped. You'll be notified when a newer release is available.** and an **Undo skip** button, which clears the skip and shows the update again right away.

## Backdating

The **Backdate app** card on the **About** page installs the release before the one you're running. It looks up the previous release on GitHub when the page opens, then shows **Previous available version: N. Installing it will restart the app.** (or **No previous release is available for this app.**).

Clicking **Backdate** confirms with **Backdate to N?**, then asks **Skip the current version?** Pick **Yes** if you don't want the version you're leaving to be offered back to you as an update right away. After that it's the same download, replace and restart as a normal update. The skip choice is only saved once the download has been handed to the installer, so a backdate that fails keeps whatever release you had skipped before.

## Update Settings

All of these are in **Settings** > **About** > **Updates**. The `settings.xml` keys are listed for completeness, see [Configuration](configuration.md) before editing that file.

| Setting | Default | What it does | `settings.xml` key |
| --- | --- | --- | --- |
| **Update check interval** | 60 min | How often the background check runs, 1 to 1440 minutes. | `UpdateCheckIntervalMs` (milliseconds) |
| **Check for updates automatically** | On | Turns the background check on or off. | `CheckForUpdatesEnabled` |
| **Show notification for updates** | Off | Tray notification when a new release shows up. | `ShowUpdateNotificationsEnabled` |
| **Show update button in flyout** | On | Brightness, Fan Control and Volume only. | `ShowUpdateButtonInFlyout` |

**Note**: older Network builds defaulted to a 0 interval, which the app treated as the 1-minute minimum, so it checked every minute. Updating moves a 0 to the 60-minute default. An interval you picked yourself is left alone.

The skipped release is stored as `SkippedUpdateVersion` (`0` means nothing is skipped).

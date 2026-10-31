# Crash Recovery

Every TADN app runs as two processes: a small watcher and the app itself. If the app crashes, the watcher starts it again, so a crash costs you about a second instead of a missing tray icon.

## How It Works

When you start an app normally, the exe starts its watcher, and the watcher starts the actual app. That's why you'll see each app's exe twice in Task Manager. The one with almost no memory use is the watcher.

- If the app crashes, the watcher waits one second and starts a fresh copy. The restart gets the same launch arguments as the original start, so an app that started hidden at sign-in comes back hidden, and a `--settings-dir` folder stays in use. See [Launch Arguments](../getting-started/launch-arguments.md).
- Quitting with **Exit** from the tray menu is not a crash. The watcher quits along with the app.
- When Windows signs out or shuts down, the app exits and tells the watcher so. If you then cancel the sign-out (for example because another program blocked it), the watcher waits five seconds, waits until Windows no longer reports the session as ending, and starts the app again. These restarts count toward the same repeated-crash limit as crash restarts.
- If the watcher itself goes away, e.g. you end it in Task Manager, the app notices right away and shuts down too.
- An error inside the UI that isn't fatal gets written to the log in full, with its stack trace, and the app keeps running. So does an error in background work that nothing waited for.

### Restart Limit

If the app crashes five times within 30 seconds, the watcher stops trying and shows a message box titled `<App> crash handler`, where `<App>` is the app's name (e.g. `VolumeTrayAppDotNET`):

`<App> has crashed repeatedly. The crash handler will not attempt further restarts. Please check for issues and restart manually.`

Crashes older than 30 seconds don't count toward the limit, so an app that crashes once in a blue moon gets restarted every time. If the watcher can't launch the app at all, it shows `Failed to start <App>.` (on the first start) or `Failed to restart <App>.` (after a crash) instead.

### Restarting by Hand

Just start the app again, from the Start menu or its exe. A new start replaces the running copy, so you never end up with two copies of the same app running. The new start first asks the running copy to exit, and it exits the same way **Exit** does: Fan Control hands the fans back to the firmware, Brightness removes its Night Light tint, and settings get saved. Only if the running copy hasn't exited after five seconds (because it's stuck) does the new start end it by force. Installing, updating and uninstalling stop a running copy the same way.

Every way the app exits runs that cleanup once: **Exit**, a sign-out, the watcher going away, installing an update, and the restarts after changing the rendering backend or after installing from **Settings** > **General**.

## Rendering Problems

The apps draw their UI with the GPU by default (Network defaults to software rendering). **GPU preferred** already falls back to software rendering when the GPU path doesn't work, but TADN won't switch the setting for you after a crash. If you suspect the GPU path, set **Settings** > **General** > **Rendering backend** to **Software** and restart the app. See [Settings Window](settings-window.md#rendering).

If the app crashes before you can get to its settings, exit it (or let the watcher give up), open its `settings.xml`, and set the `RenderingBackend` element to `Software`:

```xml
<RenderingBackend>Software</RenderingBackend>
```

The backend is read from that file before anything else starts. Where `settings.xml` lives is covered in [Configuration](../getting-started/configuration.md).

## Logs

Each app writes its log into its data folder, `%LocalAppData%\TrayAppDotNET\<App>` (e.g. `%LocalAppData%\TrayAppDotNET\VolumeTrayAppDotNET`), or the folder you passed with `--settings-dir`. **Settings** > **About** > **Open** next to **Open settings folder** takes you there.

- `active.log` is the current log.
- `old.log` is the previous one. When `active.log` hits 10 MB or is 7 days old, it becomes `old.log` (replacing the previous `old.log`) and a fresh `active.log` starts.
- Helper processes write their own log in the same folder: Brightness writes `nightlight-helper.log` and `ddc-helper.log`, each with its own `.old.log`. Task Manager's elevated helpers write no file: the elevated broker sends its log lines to Task Manager, which writes them into `active.log` marked `[elevated broker]`, so nothing running as administrator writes into your folder.
- The log is written to disk about every two seconds, so a hard kill loses at most the last couple of seconds. A write that fails because something else has the file open is retried and kept for the next write.
- A crash shows up as a line starting with `FATAL`, followed by the error.

The apps don't write crash dumps. The log is what I need for a bug report.

## Reporting a Problem

Open an issue on [GitHub](https://github.com/alchemyyy/TrayAppDotNET/issues), or ask on the [Discord](https://discord.gg/nzg7jm8yKC). Include:

- Which app, and its **Build** number from **Settings** > **About**.
- Your Windows version.
- What you did, what you expected, and what happened instead.
- `active.log` and `old.log` from the app's data folder. Look them over before you post them publicly.

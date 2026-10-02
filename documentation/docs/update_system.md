# Update system

The update system reads `https://version.trayapp.net/versions.xml` and uses
`%LOCALAPPDATA%\TrayAppDotNET\versions.xml` as a shared, timestamp-anchored
cache for all TrayAppDotNET applications.

The public endpoint is a Cloudflare Workers Static Assets deployment. Automatic
checks never use the GitHub releases API as a fallback. If a published manifest
omits an app, the Cloudflare endpoint updater walks backward through older
release manifests and records the release tag that owns the app's ZIP. Clients
still make only one endpoint request.

## Automatic checks

For application `i`, define:

- `m` as the manifest file's modification time
- `t` as the current time
- `I_i` as the application's configured update interval

The manifest age is:

```text
A = t - m
```

An automatic check follows these rules:

1. If the file exists, is valid for the application, and `A < I_i`, use it
   without making a web request.
2. After a cached check, schedule the next automatic check after:

   ```text
   max(0, I_i - A)
   ```

   Therefore, the next check remains anchored at `m + I_i`. Reading the file
   does not extend its lifetime.
3. If the file is missing, invalid, unusable, or `A >= I_i`, request the latest
   aggregate manifest from the web.
4. After successfully receiving and parsing the aggregate manifest, replace the
   shared file atomically and give it a new modification time.

## Manual checks

Manual checks bypass the shared file unconditionally. They request the latest
manifest from the web and replace the shared file after a successful response.
The requesting application's next automatic check uses its full interval.

## Resulting cadence

The file modification time acts as a shared renewal point, while each
application applies its own interval to that point. Among continuously running
applications, the shortest configured interval generally determines how often
the shared manifest is refreshed from the web. Manual checks are external
renewal events that move the shared timestamp forward.

## Change notifications

Every running application watches the shared file. One atomic replace raises
several file system events, so a watcher waits for a 250 ms quiet period after
the last one and then reads the file once. An application adopts the file's
release when the file is younger than its own `I_i` and the release differs
from the one it last saw. Its own writes and rewrites of an unchanged manifest
therefore change nothing. An adopted release counts as a successful check and
raises the same update notification a scheduled check would, so a release one
application fetches reaches the others within a second, without a web request.

The watcher does not move any schedule. Each application still runs its next
automatic check at `m + I_i`, where it reads the same file. Applications with
automatic checks turned off ignore these changes. Any writer counts, including
older builds that predate the watcher. If watching fails, for example because
the folder was deleted, the application logs it and keeps its scheduled checks.

## Skipped releases

Skipping a release, from the About page or from the update prompt, asks for
confirmation first, and declining it from the update prompt returns to that
prompt. Each application stores the skipped build in its own settings, so a
skip never affects another application, and a newer release is still offered.
While the skipped release is the newest one, the About page shows Undo skip in
place of Skip this release. Undo skip clears the stored build and offers the
release again at once.

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

Applications are not notified when another application rewrites the file. They
observe the new timestamp when their own next automatic check runs and then
recalculate their remaining delay from that timestamp.

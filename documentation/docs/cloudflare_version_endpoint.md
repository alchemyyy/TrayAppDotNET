# Cloudflare version endpoint

The public update manifest is served at:

```text
https://version.trayapp.net/versions.xml
```

This is a Workers Static Assets deployment. Requests matching `versions.xml`
are served directly by Cloudflare without invoking Worker code.
The Worker rewrites `https://version.trayapp.net/` to the same static XML asset,
so the domain root and the client-facing `/versions.xml` URL return the same
manifest.
[Static asset requests are free and unlimited](https://developers.cloudflare.com/workers/platform/pricing/#static-assets),
so client traffic does not consume the Workers Free daily request quota. GitHub
is contacted only by the updater during publication.

## Publication flow

1. Publishing a stable GitHub release produces two independent event-driven
   deliveries: the `Trigger Version Endpoint Publish` Actions workflow and a
   repository Releases webhook sent directly to Cloudflare.
2. The Actions workflow posts to its capability URL, checks the updater's
   verified release tag, and retries when the endpoint has not reached the
   release that emitted the event.
3. The repository webhook is authenticated with GitHub's HMAC-SHA256 signature.
   The updater accepts only stable `published` releases from this repository and
   uses the exact release tag in the signed payload.
4. If either delivery succeeds, the updater prepares the published release's
   `versions.xml`. Duplicate deliveries are harmless because publication is
   content-based and idempotent.
5. If an app entry is missing, the updater walks backward through numbered
   release tags and uses the first manifest containing that app. Each app entry
   records the release tag that owns its ZIP.
6. The updater uploads the completed XML directly to the
   `trayapp-version-endpoint` static-asset Worker through Cloudflare's API.
7. The updater verifies the public endpoint before returning success.

No repository clone, Cloudflare Build, periodic poll, or GitHub-side
Cloudflare credential is involved.

## One-time manual bootstrap

### 1. Prepare Cloudflare

1. Confirm that `trayapp.net` is an active Cloudflare zone.
2. Ensure that no DNS record currently uses `version.trayapp.net`.
3. Create a Cloudflare API token with **Account -> Workers Scripts -> Edit**,
   restricted to the account containing `trayapp.net`.
4. Copy the account ID from the Cloudflare dashboard.

The API token will be stored only as a secret on the updater Worker.

### 2-5. Run the Python bootstrap

From `.github/.cloudflare/version-endpoint-updater`:

```console
python scripts/bootstrap.py
```

The script:

1. Installs dependencies and runs the focused TypeScript checks.
2. Opens Wrangler's browser login only when the local session is not already
   authenticated.
3. Prepares the complete manifest, deploys the static endpoint at
   `version.trayapp.net`, and deploys the updater Worker.
4. Prompts for the Cloudflare account ID and API token, generates a fresh
   trigger token, and configures the three correctly named Worker secrets.
5. Offers to remove incorrectly named secret bindings left by an earlier
   bootstrap attempt.
6. Removes generated `versions.xml` and `.wrangler` state before exiting.
7. Prints the complete trigger URL needed by GitHub.

Secret values are sent to Wrangler over standard input. They are never placed
in command arguments, shell history, repository files, or temporary files.
The API token prompt is hidden. Do not post or screenshot the resulting trigger
URL.

### 6. Configure the Actions trigger secret

Create this repository Actions secret:

```text
VERSION_ENDPOINT_TRIGGER_URL=<full trigger URL>
```

The URL authorizes only an updater invocation. The updater ignores request
content and always fetches the latest published release from the fixed GitHub
repository. If the URL leaks, it can cause redundant invocations but does not
expose the Cloudflare API token or permit arbitrary content deployment.

### 7. Configure the signed repository webhook

Use the updater's non-secret `workers.dev` origin printed by Wrangler:

```console
npm run configure-webhook -- --updater-url https://<worker>.<subdomain>.workers.dev
```

This command generates a fresh webhook secret, stores it in the updater Worker
as `GITHUB_WEBHOOK_SECRET`, creates or updates the repository webhook for
Release events, and verifies a signed ping delivery. The secret is passed only
over subprocess standard input and is never printed or written to a file.

The command requires authenticated Wrangler and GitHub CLI sessions. The GitHub
identity must be allowed to administer repository webhooks.

### 8. Verify

Trigger the updater directly once, replacing the placeholder with the URL
printed by the bootstrap script:

```console
curl.exe --fail --request POST "<full trigger URL>"
curl.exe -i https://version.trayapp.net/versions.xml
curl.exe -i https://version.trayapp.net/
```

Then manually run `Trigger Version Endpoint Publish` from GitHub Actions. Both
paths should report either `published` or `unchanged` for the latest release.

## Relevant files

- `.github/workflows/trigger-version-endpoint.yml`: release event notification
- `.github/.cloudflare/version-endpoint-updater/scripts/bootstrap.py`: one-time bootstrap
- `.github/.cloudflare/version-endpoint-updater/scripts/configure-webhook.ts`: signed webhook setup
- `.github/.cloudflare/version-endpoint/wrangler.jsonc`: public static endpoint
- `.github/.cloudflare/version-endpoint-updater/src/index.ts`: secret trigger handler
- `.github/.cloudflare/version-endpoint-updater/src/manifest.ts`: validation and repair
- `.github/.cloudflare/version-endpoint-updater/src/cloudflare-deploy.ts`: direct asset deployment

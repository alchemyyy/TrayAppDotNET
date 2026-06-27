import { spawnSync, type SpawnSyncReturns } from "node:child_process";
import { randomBytes } from "node:crypto";
import { fileURLToPath } from "node:url";

const REPOSITORY = "alchemyyy/TrayAppDotNET";
const GITHUB_WEBHOOK_PATH = "/github-release-webhook";
const GITHUB_WEBHOOK_SECRET_NAME = "GITHUB_WEBHOOK_SECRET";
const WEBHOOK_DELIVERY_ATTEMPTS = 15;
const WEBHOOK_DELIVERY_DELAY_MILLISECONDS = 1_000;

interface GitHubHook {
  id?: unknown;
  config?: unknown;
}

interface GitHubDelivery {
  id?: unknown;
  event?: unknown;
  status_code?: unknown;
}

/** Configures a signed release webhook and verifies a live ping delivery. */
async function main(): Promise<void> {
  const updaterBaseUrl: URL = parseUpdaterBaseUrl(process.argv.slice(2));
  const webhookUrl: string = new URL(GITHUB_WEBHOOK_PATH, updaterBaseUrl).toString();
  const webhookSecretBytes: Uint8Array = randomBytes(32);
  const webhookSecret: string = Array.from(
    webhookSecretBytes,
    (value: number): string => value.toString(16).padStart(2, "0"),
  ).join("");
  const nodeExecutable: string = process.execPath;
  const wranglerCliPath: string = fileURLToPath(
    new URL("../node_modules/wrangler/bin/wrangler.js", import.meta.url),
  );
  const githubExecutable: string = process.platform === "win32" ? "gh.exe" : "gh";

  runCommand(
    nodeExecutable,
    [wranglerCliPath, "secret", "put", GITHUB_WEBHOOK_SECRET_NAME],
    `${webhookSecret}\n`,
    webhookSecret,
  );

  const hooks: GitHubHook[] = githubHooks(githubExecutable);
  const matchingHooks: GitHubHook[] = hooks.filter(
    (hook: GitHubHook): boolean => webhookUrlForHook(hook) === webhookUrl,
  );
  if (matchingHooks.length > 1) {
    throw new Error(`Found ${matchingHooks.length} duplicate updater webhooks`);
  }

  const hookConfiguration: Record<string, string> = {
    url: webhookUrl,
    content_type: "json",
    secret: webhookSecret,
    insecure_ssl: "0",
  };
  const createPayload: string = JSON.stringify({
    name: "web",
    active: true,
    events: ["release"],
    config: hookConfiguration,
  });
  const updatePayload: string = JSON.stringify({
    active: true,
    events: ["release"],
    config: hookConfiguration,
  });
  const hook: GitHubHook = matchingHooks.length === 0
    ? createHook(githubExecutable, createPayload, webhookSecret)
    : updateHook(
      githubExecutable,
      requiredPositiveInteger(matchingHooks[0].id, "GitHub webhook ID"),
      updatePayload,
      webhookSecret,
    );
  const hookId: number = requiredPositiveInteger(hook.id, "GitHub webhook ID");

  const previousDeliveryIds: Set<string> = new Set(
    githubDeliveries(githubExecutable, hookId).map(
      (delivery: GitHubDelivery): string =>
        requiredIdentifier(delivery.id, "GitHub webhook delivery ID"),
    ),
  );
  runCommand(
    githubExecutable,
    ["api", "--method", "POST", `repos/${REPOSITORY}/hooks/${hookId}/pings`],
  );
  await verifyPingDelivery(githubExecutable, hookId, previousDeliveryIds);

  console.log(`Configured signed GitHub release webhook ${hookId}: ${webhookUrl}`);
  console.log("Verified GitHub-to-Cloudflare ping delivery.");
}

/** Parses and validates the updater's public workers.dev base URL. */
function parseUpdaterBaseUrl(argumentsList: string[]): URL {
  const optionIndex: number = argumentsList.indexOf("--updater-url");
  if (optionIndex < 0 || optionIndex + 1 >= argumentsList.length) {
    throw new Error(
      "Usage: npm run configure-webhook -- --updater-url https://<worker>.<subdomain>.workers.dev",
    );
  }

  const updaterBaseUrl: URL = new URL(argumentsList[optionIndex + 1]);
  if (
    updaterBaseUrl.protocol !== "https:"
    || !updaterBaseUrl.hostname.endsWith(".workers.dev")
    || updaterBaseUrl.username !== ""
    || updaterBaseUrl.password !== ""
    || (updaterBaseUrl.pathname !== "/" && updaterBaseUrl.pathname !== "")
    || updaterBaseUrl.search !== ""
    || updaterBaseUrl.hash !== ""
  ) {
    throw new Error("Updater URL must be an HTTPS workers.dev origin without a path");
  }
  updaterBaseUrl.pathname = "/";
  return updaterBaseUrl;
}

/** Returns repository webhooks without logging their potentially sensitive configuration. */
function githubHooks(githubExecutable: string): GitHubHook[] {
  const output: string = runCommand(
    githubExecutable,
    ["api", `repos/${REPOSITORY}/hooks?per_page=100`],
  );
  const value: unknown = JSON.parse(output) as unknown;
  if (!Array.isArray(value)) throw new Error("GitHub returned an invalid webhook list");
  return value as GitHubHook[];
}

function webhookUrlForHook(hook: GitHubHook): string {
  if (typeof hook.config !== "object" || hook.config === null || Array.isArray(hook.config)) {
    return "";
  }
  const config: Record<string, unknown> = hook.config as Record<string, unknown>;
  return typeof config.url === "string" ? config.url : "";
}

function createHook(
  githubExecutable: string,
  hookPayload: string,
  webhookSecret: string,
): GitHubHook {
  const output: string = runCommand(
    githubExecutable,
    ["api", "--method", "POST", `repos/${REPOSITORY}/hooks`, "--input", "-"],
    hookPayload,
    webhookSecret,
  );
  return parseHook(output);
}

function updateHook(
  githubExecutable: string,
  hookId: number,
  hookPayload: string,
  webhookSecret: string,
): GitHubHook {
  const output: string = runCommand(
    githubExecutable,
    ["api", "--method", "PATCH", `repos/${REPOSITORY}/hooks/${hookId}`, "--input", "-"],
    hookPayload,
    webhookSecret,
  );
  return parseHook(output);
}

function parseHook(output: string): GitHubHook {
  const value: unknown = JSON.parse(output) as unknown;
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("GitHub returned an invalid webhook");
  }
  return value as GitHubHook;
}

function githubDeliveries(githubExecutable: string, hookId: number): GitHubDelivery[] {
  const output: string = runCommand(
    githubExecutable,
    [
      "api",
      `repos/${REPOSITORY}/hooks/${hookId}/deliveries?per_page=20`,
      "--jq",
      "map(.id = (.id | tostring))",
    ],
  );
  const value: unknown = JSON.parse(output) as unknown;
  if (!Array.isArray(value)) throw new Error("GitHub returned an invalid webhook delivery list");
  return value as GitHubDelivery[];
}

async function verifyPingDelivery(
  githubExecutable: string,
  hookId: number,
  previousDeliveryIds: ReadonlySet<string>,
): Promise<void> {
  const observedDeliveryIds: Set<string> = new Set(previousDeliveryIds);
  let lastFailureStatusCode: number | null = null;
  for (let attempt: number = 1; attempt <= WEBHOOK_DELIVERY_ATTEMPTS; attempt += 1) {
    const deliveries: GitHubDelivery[] = githubDeliveries(githubExecutable, hookId);
    const newPingDelivery: GitHubDelivery | undefined = deliveries.find(
      (delivery: GitHubDelivery): boolean => {
        const deliveryId: string = requiredIdentifier(
          delivery.id,
          "GitHub webhook delivery ID",
        );
        return !observedDeliveryIds.has(deliveryId) && delivery.event === "ping";
      },
    );
    if (newPingDelivery !== undefined) {
      observedDeliveryIds.add(
        requiredIdentifier(newPingDelivery.id, "GitHub webhook delivery ID"),
      );
      const statusCode: unknown = newPingDelivery.status_code;
      if (statusCode === 200) return;
      if (typeof statusCode === "number" && statusCode > 0) {
        lastFailureStatusCode = statusCode;
        if (attempt < WEBHOOK_DELIVERY_ATTEMPTS) {
          await delay(WEBHOOK_DELIVERY_DELAY_MILLISECONDS);
          runCommand(
            githubExecutable,
            ["api", "--method", "POST", `repos/${REPOSITORY}/hooks/${hookId}/pings`],
          );
          continue;
        }
      }
    }
    if (attempt < WEBHOOK_DELIVERY_ATTEMPTS) {
      await delay(WEBHOOK_DELIVERY_DELAY_MILLISECONDS);
    }
  }
  if (lastFailureStatusCode !== null) {
    throw new Error(`GitHub webhook ping failed with HTTP ${lastFailureStatusCode}`);
  }
  throw new Error("GitHub webhook ping delivery did not complete in time");
}

/** Runs a subprocess while keeping secrets out of arguments, output, and errors. */
function runCommand(
  executable: string,
  argumentsList: string[],
  input: string | undefined = undefined,
  sensitiveValue: string | undefined = undefined,
): string {
  const completed: SpawnSyncReturns<string> = spawnSync(executable, argumentsList, {
    encoding: "utf8",
    input,
    windowsHide: true,
  });
  const standardOutput: string = completed.stdout ?? "";
  const standardError: string = completed.stderr ?? "";
  if (completed.error !== undefined || completed.status !== 0) {
    const rawMessage: string = completed.error?.message
      ?? `${standardError}\n${standardOutput}`.trim();
    const safeMessage: string = sensitiveValue === undefined
      ? rawMessage
      : rawMessage.replaceAll(sensitiveValue, "<redacted>");
    throw new Error(`${executable} failed: ${safeMessage}`);
  }
  return standardOutput;
}

function requiredPositiveInteger(value: unknown, description: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`Expected ${description} to be a positive integer`);
  }
  return value;
}

function requiredIdentifier(value: unknown, description: string): string {
  if (typeof value !== "string" || !/^\d+$/.test(value)) {
    throw new Error(`Expected ${description} to contain decimal digits`);
  }
  return value;
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise<void>((resolve: () => void): void => {
    setTimeout(resolve, milliseconds);
  });
}

await main();

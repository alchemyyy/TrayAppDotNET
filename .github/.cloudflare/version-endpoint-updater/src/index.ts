import { deployVersionEndpoint } from "./cloudflare-deploy";
import {
  prepareEndpointManifestForRelease,
  prepareLatestEndpointManifest,
  VERSION_ENDPOINT_URL,
  type FetchFunction,
  type PreparedEndpointManifest,
} from "./manifest";

const VERIFICATION_ATTEMPTS = 10;
const VERIFICATION_DELAY_MILLISECONDS = 1_000;
const GITHUB_WEBHOOK_PATH = "/github-release-webhook";
const GITHUB_SIGNATURE_PREFIX = "sha256=";
const GITHUB_RELEASE_EVENT = "release";
const GITHUB_PING_EVENT = "ping";
const EXPECTED_REPOSITORY = "alchemyyy/TrayAppDotNET";
const MAXIMUM_WEBHOOK_PAYLOAD_SIZE_BYTES = 1_048_576;

interface Environment {
  CLOUDFLARE_ACCOUNT_ID: string;
  CLOUDFLARE_API_TOKEN: string;
  GITHUB_WEBHOOK_SECRET: string;
  TRIGGER_TOKEN: string;
}

interface PublishResult {
  status: "published" | "unchanged";
  releaseTag: string;
}

export default {
  async fetch(request: Request, environment: Environment): Promise<Response> {
    const requestUrl: URL = new URL(request.url);
    if (request.method !== "POST") {
      return new Response("Not found", { status: 404 });
    }

    if (requestUrl.pathname === GITHUB_WEBHOOK_PATH) {
      return handleGitHubWebhook(request, environment);
    }

    const triggerToken: string = environment.TRIGGER_TOKEN?.trim() ?? "";
    if (triggerToken === "") {
      console.error("Version endpoint updater has no trigger token");
      return new Response("Not configured", { status: 503 });
    }
    if (requestUrl.pathname !== `/${triggerToken}`) {
      return new Response("Not found", { status: 404 });
    }

    return publicationResponse((): Promise<PublishResult> =>
      publishLatestVersionEndpoint(environment));
  },
} satisfies ExportedHandler<Environment>;

/** Fetches the latest release and publishes it when the endpoint content differs. */
export async function publishLatestVersionEndpoint(
  environment: Environment,
  fetchFunction: FetchFunction = fetch,
): Promise<PublishResult> {
  return publishPreparedVersionEndpoint(
    environment,
    (): Promise<PreparedEndpointManifest> => prepareLatestEndpointManifest(fetchFunction),
    fetchFunction,
  );
}

async function publishReleaseVersionEndpoint(
  environment: Environment,
  releaseTag: string,
  fetchFunction: FetchFunction = fetch,
): Promise<PublishResult> {
  return publishPreparedVersionEndpoint(
    environment,
    (): Promise<PreparedEndpointManifest> =>
      prepareEndpointManifestForRelease(releaseTag, fetchFunction),
    fetchFunction,
  );
}

async function publishPreparedVersionEndpoint(
  environment: Environment,
  prepareManifest: () => Promise<PreparedEndpointManifest>,
  fetchFunction: FetchFunction,
): Promise<PublishResult> {
  const accountID: string = requiredSecret(
    environment.CLOUDFLARE_ACCOUNT_ID,
    "CLOUDFLARE_ACCOUNT_ID",
  );
  const cloudflareAPIToken: string = requiredSecret(
    environment.CLOUDFLARE_API_TOKEN,
    "CLOUDFLARE_API_TOKEN",
  );
  const preparedManifest: PreparedEndpointManifest = await prepareManifest();
  const currentXml: string | null = await fetchCurrentEndpoint(fetchFunction);
  if (currentXml === preparedManifest.xml) {
    console.log(`Version endpoint already serves ${preparedManifest.releaseTag}`);
    return { status: "unchanged", releaseTag: preparedManifest.releaseTag };
  }

  await deployVersionEndpoint(
    preparedManifest.xml,
    accountID,
    cloudflareAPIToken,
    fetchFunction,
  );
  await verifyEndpoint(preparedManifest, fetchFunction);
  console.log(`Published version endpoint for ${preparedManifest.releaseTag}`);
  return { status: "published", releaseTag: preparedManifest.releaseTag };
}

async function handleGitHubWebhook(
  request: Request,
  environment: Environment,
): Promise<Response> {
  let webhookSecret: string;
  try {
    webhookSecret = requiredSecret(
      environment.GITHUB_WEBHOOK_SECRET,
      "GITHUB_WEBHOOK_SECRET",
    );
  } catch (error: unknown) {
    const message: string = errorMessage(error);
    console.error(`GitHub webhook is not configured: ${message}`);
    return jsonResponse({ status: "failed", message }, 503);
  }
  const payloadBytes: Uint8Array = new Uint8Array(await request.arrayBuffer());
  if (payloadBytes.byteLength > MAXIMUM_WEBHOOK_PAYLOAD_SIZE_BYTES) {
    return jsonResponse({ status: "rejected", message: "Webhook payload is too large" }, 413);
  }

  const signatureHeader: string = request.headers.get("X-Hub-Signature-256") ?? "";
  const signatureValid: boolean = await verifyGitHubWebhookSignature(
    payloadBytes,
    signatureHeader,
    webhookSecret,
  );
  if (!signatureValid) {
    return jsonResponse({ status: "rejected", message: "Invalid webhook signature" }, 401);
  }

  const githubEvent: string = request.headers.get("X-GitHub-Event")?.trim() ?? "";
  switch (githubEvent) {
    case GITHUB_PING_EVENT:
      return jsonResponse({ status: "ready" }, 200);
    case GITHUB_RELEASE_EVENT:
      break;
    default:
      return jsonResponse({ status: "ignored", event: githubEvent }, 202);
  }

  let payload: unknown;
  try {
    payload = JSON.parse(new TextDecoder().decode(payloadBytes)) as unknown;
  } catch (error: unknown) {
    return jsonResponse(
      { status: "rejected", message: `Invalid webhook JSON: ${errorMessage(error)}` },
      400,
    );
  }

  let releaseTag: string | null;
  try {
    releaseTag = publishedReleaseTag(payload);
  } catch (error: unknown) {
    return jsonResponse({ status: "rejected", message: errorMessage(error) }, 400);
  }
  if (releaseTag === null) {
    return jsonResponse({ status: "ignored", event: GITHUB_RELEASE_EVENT }, 202);
  }

  return publicationResponse((): Promise<PublishResult> =>
    publishReleaseVersionEndpoint(environment, releaseTag));
}

/** Verifies GitHub's HMAC-SHA256 signature over the unmodified request body. */
export async function verifyGitHubWebhookSignature(
  payloadBytes: Uint8Array,
  signatureHeader: string,
  webhookSecret: string,
): Promise<boolean> {
  if (!signatureHeader.startsWith(GITHUB_SIGNATURE_PREFIX)) return false;
  const signatureBytes: Uint8Array | null = hexBytes(
    signatureHeader.slice(GITHUB_SIGNATURE_PREFIX.length),
  );
  if (signatureBytes === null) return false;

  const verificationKey: CryptoKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(webhookSecret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["verify"],
  );
  return crypto.subtle.verify(
    "HMAC",
    verificationKey,
    copyToArrayBuffer(signatureBytes),
    copyToArrayBuffer(payloadBytes),
  );
}

/** Returns the exact stable release tag from a validated GitHub release payload. */
export function publishedReleaseTag(payload: unknown): string | null {
  const payloadRecord: Record<string, unknown> = requiredRecord(payload, "webhook payload");
  if (payloadRecord.action !== "published") return null;

  const repositoryRecord: Record<string, unknown> = requiredRecord(
    payloadRecord.repository,
    "webhook repository",
  );
  const repositoryName: string = requiredText(
    repositoryRecord.full_name,
    "webhook repository name",
  );
  if (repositoryName.toLowerCase() !== EXPECTED_REPOSITORY.toLowerCase()) {
    throw new Error(`Unexpected webhook repository: ${repositoryName}`);
  }

  const releaseRecord: Record<string, unknown> = requiredRecord(
    payloadRecord.release,
    "webhook release",
  );
  if (releaseRecord.draft !== false || releaseRecord.prerelease !== false) return null;
  return requiredText(releaseRecord.tag_name, "webhook release tag");
}

async function publicationResponse(
  publish: () => Promise<PublishResult>,
): Promise<Response> {
  try {
    const result: PublishResult = await publish();
    return jsonResponse(result, 200);
  } catch (error: unknown) {
    const message: string = errorMessage(error);
    console.error(`Version endpoint publication failed: ${message}`);
    return jsonResponse({ status: "failed", message }, 502);
  }
}

async function fetchCurrentEndpoint(fetchFunction: FetchFunction): Promise<string | null> {
  const cacheKey: string = crypto.randomUUID();
  const response: Response = await fetchFunction(`${VERSION_ENDPOINT_URL}?check=${cacheKey}`, {
    headers: {
      Accept: "application/xml",
      "Cache-Control": "no-cache",
    },
  });
  return response.ok ? response.text() : null;
}

async function verifyEndpoint(
  expected: PreparedEndpointManifest,
  fetchFunction: FetchFunction,
): Promise<void> {
  for (let attempt: number = 1; attempt <= VERIFICATION_ATTEMPTS; attempt += 1) {
    const cacheKey: string = crypto.randomUUID();
    const response: Response = await fetchFunction(
      `${VERSION_ENDPOINT_URL}?release=${encodeURIComponent(expected.releaseTag)}&check=${cacheKey}`,
      {
        headers: {
          Accept: "application/xml",
          "Cache-Control": "no-cache",
        },
      },
    );
    if (response.ok && await response.text() === expected.xml) return;
    if (attempt < VERIFICATION_ATTEMPTS) {
      await delay(VERIFICATION_DELAY_MILLISECONDS);
    }
  }
  throw new Error(`Endpoint did not serve ${expected.releaseTag} after deployment`);
}

function jsonResponse(value: unknown, status: number): Response {
  return new Response(`${JSON.stringify(value)}\n`, {
    status,
    headers: {
      "Cache-Control": "no-store",
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise<void>((resolve: () => void): void => {
    setTimeout(resolve, milliseconds);
  });
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function hexBytes(hex: string): Uint8Array | null {
  if (!/^[0-9a-fA-F]{64}$/.test(hex)) return null;
  const bytes: Uint8Array = new Uint8Array(hex.length / 2);
  for (let byteIndex: number = 0; byteIndex < bytes.length; byteIndex += 1) {
    bytes[byteIndex] = Number.parseInt(hex.slice(byteIndex * 2, byteIndex * 2 + 2), 16);
  }
  return bytes;
}

function copyToArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  const buffer: ArrayBuffer = new ArrayBuffer(bytes.byteLength);
  new Uint8Array(buffer).set(bytes);
  return buffer;
}

function requiredRecord(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`Expected ${description} to be an object`);
  }
  return value as Record<string, unknown>;
}

function requiredText(value: unknown, description: string): string {
  const normalized: string = typeof value === "string" ? value.trim() : "";
  if (normalized === "") throw new Error(`Missing ${description}`);
  return normalized;
}

function requiredSecret(value: string | undefined, name: string): string {
  const normalized: string = value?.trim() ?? "";
  if (normalized === "") throw new Error(`${name} is not configured`);
  return normalized;
}

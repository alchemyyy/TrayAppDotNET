import type { FetchFunction } from "./manifest";

const CLOUDFLARE_API_ROOT = "https://api.cloudflare.com/client/v4";
const TARGET_WORKER_NAME = "trayapp-version-endpoint";
const TARGET_MODULE_NAME = "version-endpoint.js";
const TARGET_COMPATIBILITY_DATE = "2026-08-20";
const VERSION_ASSET_PATH = "/versions.xml";
const VERSION_ASSET_EXTENSION = "xml";
const VERSION_ASSET_CONTENT_TYPE = "application/xml; charset=utf-8";
const ASSETS_BINDING_NAME = "ASSETS";
const VERSION_ENDPOINT_WORKER_SOURCE = `const VERSION_ASSET_PATH = "/versions.xml";

export default {
  fetch(request, environment) {
    const requestUrl = new URL(request.url);
    if (requestUrl.pathname === "/") {
      requestUrl.pathname = VERSION_ASSET_PATH;
      return environment.ASSETS.fetch(new Request(requestUrl.toString(), request));
    }

    return new Response("Not found", {
      status: 404,
      headers: { "Content-Type": "text/plain; charset=utf-8" },
    });
  },
};
`;

interface AssetUploadSession {
  uploadJWT: string;
  buckets: string[][];
}

/** Replaces the static endpoint Worker's sole asset with the supplied XML. */
export async function deployVersionEndpoint(
  xml: string,
  accountID: string,
  cloudflareAPIToken: string,
  fetchFunction: FetchFunction = fetch,
): Promise<void> {
  if (accountID.trim() === "") throw new Error("Cloudflare account ID is empty");
  if (cloudflareAPIToken.trim() === "") throw new Error("Cloudflare API token is empty");

  const assetBytes: Uint8Array = new TextEncoder().encode(xml);
  const base64Content: string = bytesToBase64(assetBytes);
  const assetHash: string = await calculateAssetHash(base64Content, VERSION_ASSET_EXTENSION);
  const session: AssetUploadSession = await createAssetUploadSession(
    accountID,
    cloudflareAPIToken,
    assetHash,
    assetBytes.byteLength,
    fetchFunction,
  );
  const completionJWT: string = session.buckets.length === 0
    ? session.uploadJWT
    : await uploadRequestedAsset(
      accountID,
      session,
      assetHash,
      base64Content,
      fetchFunction,
    );

  await deployEndpointWorker(
    accountID,
    cloudflareAPIToken,
    completionJWT,
    fetchFunction,
  );
}

/** Calculates the 32-character content identifier required by Workers Static Assets. */
export async function calculateAssetHash(
  base64Content: string,
  extension: string,
): Promise<string> {
  const hashInput: Uint8Array = new TextEncoder().encode(base64Content + extension);
  const hashInputBuffer: ArrayBuffer = new ArrayBuffer(hashInput.byteLength);
  new Uint8Array(hashInputBuffer).set(hashInput);
  const digest: ArrayBuffer = await crypto.subtle.digest("SHA-256", hashInputBuffer);
  const digestBytes: Uint8Array = new Uint8Array(digest);
  return Array.from(
    digestBytes.subarray(0, 16),
    (value: number): string => value.toString(16).padStart(2, "0"),
  ).join("");
}

async function createAssetUploadSession(
  accountID: string,
  cloudflareAPIToken: string,
  assetHash: string,
  assetSize: number,
  fetchFunction: FetchFunction,
): Promise<AssetUploadSession> {
  const result: Record<string, unknown> = await callCloudflareAPI(
    `${CLOUDFLARE_API_ROOT}/accounts/${encodeURIComponent(accountID)}`
      + `/workers/scripts/${TARGET_WORKER_NAME}/assets-upload-session`,
    cloudflareAPIToken,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        manifest: {
          [VERSION_ASSET_PATH]: {
            hash: assetHash,
            size: assetSize,
          },
        },
      }),
    },
    fetchFunction,
  );
  const uploadJWT: string = requiredString(result.jwt, "asset upload JWT");
  if (!Array.isArray(result.buckets)) {
    throw new Error("Cloudflare asset upload session did not return buckets");
  }

  const buckets: string[][] = [];
  for (const bucketValue of result.buckets) {
    if (!Array.isArray(bucketValue)) {
      throw new Error("Cloudflare asset upload session returned an invalid bucket");
    }
    const bucket: string[] = [];
    for (const hashValue of bucketValue) {
      bucket.push(requiredString(hashValue, "requested asset hash"));
    }
    buckets.push(bucket);
  }
  return { uploadJWT, buckets };
}

async function uploadRequestedAsset(
  accountID: string,
  session: AssetUploadSession,
  assetHash: string,
  base64Content: string,
  fetchFunction: FetchFunction,
): Promise<string> {
  const requestedHashes: string[] = session.buckets.flat();
  if (requestedHashes.length !== 1 || requestedHashes[0] !== assetHash) {
    throw new Error(
      `Cloudflare requested unexpected asset hashes: ${requestedHashes.join(", ")}`,
    );
  }

  const formData: FormData = new FormData();
  formData.append(
    assetHash,
    new Blob([base64Content], { type: VERSION_ASSET_CONTENT_TYPE }),
    "versions.xml",
  );
  const result: Record<string, unknown> = await callCloudflareAPI(
    `${CLOUDFLARE_API_ROOT}/accounts/${encodeURIComponent(accountID)}`
      + "/workers/assets/upload?base64=true",
    session.uploadJWT,
    {
      method: "POST",
      body: formData,
    },
    fetchFunction,
  );
  return requiredString(result.jwt, "asset completion JWT");
}

async function deployEndpointWorker(
  accountID: string,
  cloudflareAPIToken: string,
  completionJWT: string,
  fetchFunction: FetchFunction,
): Promise<void> {
  const metadata: Record<string, unknown> = {
    main_module: TARGET_MODULE_NAME,
    compatibility_date: TARGET_COMPATIBILITY_DATE,
    assets: {
      jwt: completionJWT,
      config: {
        html_handling: "none",
        not_found_handling: "none",
        run_worker_first: false,
      },
    },
    bindings: [
      {
        name: ASSETS_BINDING_NAME,
        type: "assets",
      },
    ],
  };
  const formData: FormData = new FormData();
  formData.append(
    "metadata",
    new Blob([JSON.stringify(metadata)], { type: "application/json" }),
    "metadata.json",
  );
  formData.append(
    TARGET_MODULE_NAME,
    new Blob([VERSION_ENDPOINT_WORKER_SOURCE], { type: "application/javascript+module" }),
    TARGET_MODULE_NAME,
  );

  await callCloudflareAPI(
    `${CLOUDFLARE_API_ROOT}/accounts/${encodeURIComponent(accountID)}`
      + `/workers/scripts/${TARGET_WORKER_NAME}`,
    cloudflareAPIToken,
    {
      method: "PUT",
      body: formData,
    },
    fetchFunction,
  );
}

async function callCloudflareAPI(
  url: string,
  bearerToken: string,
  request: RequestInit,
  fetchFunction: FetchFunction,
): Promise<Record<string, unknown>> {
  const headers: Headers = new Headers(request.headers);
  headers.set("Authorization", `Bearer ${bearerToken}`);
  const response: Response = await fetchFunction(url, { ...request, headers });
  const responseText: string = await response.text();
  let payload: unknown;
  try {
    payload = JSON.parse(responseText) as unknown;
  } catch (error: unknown) {
    throw new Error(
      `Cloudflare API returned non-JSON HTTP ${response.status}: ${errorMessage(error)}`,
    );
  }

  const envelope: Record<string, unknown> = requiredRecord(payload, "Cloudflare API response");
  if (!response.ok || envelope.success !== true) {
    throw new Error(
      `Cloudflare API request failed with HTTP ${response.status}: ${cloudflareErrors(envelope)}`,
    );
  }
  return requiredRecord(envelope.result, "Cloudflare API result");
}

function bytesToBase64(bytes: Uint8Array): string {
  const chunkSize = 8_192;
  let binary = "";
  for (let offset: number = 0; offset < bytes.length; offset += chunkSize) {
    const chunk: Uint8Array = bytes.subarray(offset, offset + chunkSize);
    binary += String.fromCharCode(...chunk);
  }
  return btoa(binary);
}

function cloudflareErrors(envelope: Record<string, unknown>): string {
  if (!Array.isArray(envelope.errors)) return "unknown Cloudflare API error";
  const messages: string[] = [];
  for (const errorValue of envelope.errors) {
    if (typeof errorValue !== "object" || errorValue === null || Array.isArray(errorValue)) {
      continue;
    }
    const errorRecord: Record<string, unknown> = errorValue as Record<string, unknown>;
    if (typeof errorRecord.message === "string") messages.push(errorRecord.message);
  }
  return messages.length > 0 ? messages.join("; ") : "unknown Cloudflare API error";
}

function requiredRecord(value: unknown, description: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`Expected ${description} to be an object`);
  }
  return value as Record<string, unknown>;
}

function requiredString(value: unknown, description: string): string {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`Expected ${description} to be a non-empty string`);
  }
  return value;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

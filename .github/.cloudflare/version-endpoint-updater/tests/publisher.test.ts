import assert from "node:assert/strict";
import { createHash, createHmac } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { renderGeneratedEndpointWorkerModule } from "../scripts/generate-endpoint-worker";
import { calculateAssetHash, deployVersionEndpoint } from "../src/cloudflare-deploy";
import { VERSION_ENDPOINT_WORKER_SOURCE } from "../src/generated/version-endpoint-worker";
import updaterWorker, {
  publishedReleaseTag,
  publishLatestVersionEndpoint,
  verifyGitHubWebhookSignature,
} from "../src/index";
import {
  CATALOG_APPLICATION_IDS,
  prepareEndpointManifestForRelease,
  prepareLatestEndpointManifest,
  servedEndpointVersion,
  VERSION_ENDPOINT_URL,
  type FetchFunction,
  type PreparedEndpointManifest,
} from "../src/manifest";

const LATEST_MANIFEST_URL =
  "https://github.com/alchemyyy/TrayAppDotNET/releases/latest/download/versions.xml";
const PRE_TASK_MANAGER_APPLICATION_IDS: string[] = CATALOG_APPLICATION_IDS.filter(
  (applicationId: string): boolean => applicationId !== "TaskManagerTrayAppDotNET",
);

test("a complete latest manifest requires no historical requests", async (): Promise<void> => {
  const requestUrls: string[] = [];
  const latestXml: string = manifestXml(125, [...CATALOG_APPLICATION_IDS]);
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    return xmlResponse(latestXml);
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  assert.deepEqual(requestUrls, [LATEST_MANIFEST_URL]);
  assert.equal(prepared.releaseTag, "TrayAppDotNET_125");
  for (const applicationId of CATALOG_APPLICATION_IDS) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), "TrayAppDotNET_125");
  }
});

test("an exact release event bypasses the latest-release redirect", async (): Promise<void> => {
  const releaseTag: string = "TrayAppDotNET_124";
  const requestUrls: string[] = [];
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    assert.equal(input, taggedManifestUrl(124));
    return xmlResponse(manifestXml(124, PRE_TASK_MANAGER_APPLICATION_IDS));
  };

  const prepared: PreparedEndpointManifest = await prepareEndpointManifestForRelease(
    releaseTag,
    fetchFunction,
  );

  assert.equal(prepared.releaseTag, releaseTag);
  assert.deepEqual(requestUrls, [taggedManifestUrl(124)]);
  assert.equal(prepared.applicationReleaseTags.has("TaskManagerTrayAppDotNET"), false);
  assert.doesNotMatch(prepared.xml, /appId="TaskManagerTrayAppDotNET"/);
});

test("an exact release event rejects invalid or mismatched tags", async (): Promise<void> => {
  await assert.rejects(
    prepareEndpointManifestForRelease("release-124", async (): Promise<Response> => {
      throw new Error("Invalid tags must be rejected before fetching");
    }),
    /Invalid release tag/,
  );
  await assert.rejects(
    prepareEndpointManifestForRelease(
      "TrayAppDotNET_124",
      async (): Promise<Response> =>
        xmlResponse(manifestXml(123, [...CATALOG_APPLICATION_IDS])),
    ),
    /Unexpected aggregate version: 123/,
  );
});

test("missing apps are filled by walking older release manifests", async (): Promise<void> => {
  const currentApplications: string[] = [...CATALOG_APPLICATION_IDS.slice(0, 2)];
  const release126Applications: string[] = [CATALOG_APPLICATION_IDS[2]];
  const release125Applications: string[] = [...CATALOG_APPLICATION_IDS.slice(3)];
  const requestUrls: string[] = [];
  const responses: Map<string, string> = new Map([
    [LATEST_MANIFEST_URL, manifestXml(127, currentApplications)],
    [taggedManifestUrl(126), manifestXml(126, release126Applications)],
    [taggedManifestUrl(125), manifestXml(125, release125Applications)],
  ]);
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    const xml: string | undefined = responses.get(input);
    return xml === undefined ? new Response("Not found", { status: 404 }) : xmlResponse(xml);
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  assert.deepEqual(requestUrls, [
    LATEST_MANIFEST_URL,
    taggedManifestUrl(126),
    taggedManifestUrl(125),
  ]);
  for (const applicationId of currentApplications) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), "TrayAppDotNET_127");
  }
  assert.equal(
    prepared.applicationReleaseTags.get(CATALOG_APPLICATION_IDS[2]),
    "TrayAppDotNET_126",
  );
  for (const applicationId of release125Applications) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), "TrayAppDotNET_125");
  }

  for (const applicationId of CATALOG_APPLICATION_IDS) {
    const artifactOccurrences: number = prepared.xml
      .split(`appId="${applicationId}"`)
      .length - 1;
    assert.equal(artifactOccurrences, 1);
  }
});

test("missing release tags are skipped while repairing", async (): Promise<void> => {
  const firstApplication: string = CATALOG_APPLICATION_IDS[0];
  const remainingApplications: string[] = [...CATALOG_APPLICATION_IDS.slice(1)];
  const requestUrls: string[] = [];
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    switch (input) {
      case LATEST_MANIFEST_URL:
        return xmlResponse(manifestXml(127, [firstApplication]));
      case taggedManifestUrl(126):
        return new Response("Not found", { status: 404 });
      case taggedManifestUrl(125):
        return xmlResponse(manifestXml(125, remainingApplications));
      default:
        throw new Error(`Unexpected request: ${input}`);
    }
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  assert.equal(prepared.applicationReleaseTags.get(firstApplication), "TrayAppDotNET_127");
  for (const applicationId of remainingApplications) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), "TrayAppDotNET_125");
  }
  assert.equal(requestUrls.length, 3);
});

test("installer rows from the release manifest are left out of the endpoint", async (): Promise<void> => {
  const releaseTag: string = "TrayAppDotNET_205";
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    assert.equal(input, taggedManifestUrl(205));
    return xmlResponse(manifestXml(205, [...CATALOG_APPLICATION_IDS], true));
  };

  const prepared: PreparedEndpointManifest = await prepareEndpointManifestForRelease(
    releaseTag,
    fetchFunction,
  );

  assert.equal(prepared.releaseTag, releaseTag);
  assert.doesNotMatch(prepared.xml, /kind="installer"/);
  assert.doesNotMatch(prepared.xml, /Installer_/);
  for (const applicationId of CATALOG_APPLICATION_IDS) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), releaseTag);
    const artifactOccurrences: number = prepared.xml
      .split(`appId="${applicationId}"`)
      .length - 1;
    assert.equal(artifactOccurrences, 1);
  }
});

test("an artifact row without a kind is still rejected", async (): Promise<void> => {
  const kindlessXml: string = manifestXml(205, [...CATALOG_APPLICATION_IDS])
    .replace(` kind="app" appId="${CATALOG_APPLICATION_IDS[0]}"`, ` appId="${CATALOG_APPLICATION_IDS[0]}"`);

  await assert.rejects(
    prepareEndpointManifestForRelease(
      "TrayAppDotNET_205",
      async (): Promise<Response> => xmlResponse(kindlessXml),
    ),
    /Missing artifact kind/,
  );
});

test("a declared app set is served as declared, including apps the bundled catalog lacks", async (): Promise<void> => {
  const declared: string[] = [...CATALOG_APPLICATION_IDS.slice(0, 2), "NewTrayAppDotNET"];
  const requestUrls: string[] = [];
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    return xmlResponse(manifestXml(240, declared, false, declared));
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  // The catalog apps the release does not declare are not searched for
  assert.deepEqual(requestUrls, [LATEST_MANIFEST_URL]);
  assert.deepEqual([...prepared.applicationReleaseTags.keys()], declared);
  assert.match(prepared.xml, /appId="NewTrayAppDotNET"/);
});

test("declared apps missing from the latest manifest are filled from older releases", async (): Promise<void> => {
  const declared: string[] = [CATALOG_APPLICATION_IDS[0], CATALOG_APPLICATION_IDS[1]];
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    switch (input) {
      case LATEST_MANIFEST_URL:
        return xmlResponse(manifestXml(240, [declared[0]], false, declared));
      case taggedManifestUrl(239):
        return xmlResponse(manifestXml(239, [declared[1]]));
      default:
        throw new Error(`Unexpected request: ${input}`);
    }
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  assert.equal(prepared.applicationReleaseTags.get(declared[0]), "TrayAppDotNET_240");
  assert.equal(prepared.applicationReleaseTags.get(declared[1]), "TrayAppDotNET_239");
});

test("an app the bundled catalog lacks does not block a manifest without a declaration", async (): Promise<void> => {
  const applications: string[] = [...CATALOG_APPLICATION_IDS, "NewTrayAppDotNET"];
  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(
    async (): Promise<Response> => xmlResponse(manifestXml(240, applications)),
  );

  assert.deepEqual([...prepared.applicationReleaseTags.keys()], applications);
});

test("a malformed application id is rejected", async (): Promise<void> => {
  await assert.rejects(
    prepareLatestEndpointManifest(
      async (): Promise<Response> =>
        xmlResponse(manifestXml(240, [CATALOG_APPLICATION_IDS[0]], false, ["../Escape"])),
    ),
    /Invalid application id: \.\.\/Escape/,
  );
});

test("static asset hash matches Cloudflare's documented algorithm", async (): Promise<void> => {
  const content: string = "<versions />\n";
  const base64Content: string = Buffer.from(content, "utf8").toString("base64");
  const expectedHash: string = createHash("sha256")
    .update(base64Content + "xml")
    .digest("hex")
    .slice(0, 32);

  const actualHash: string = await calculateAssetHash(base64Content, "xml");

  assert.equal(actualHash, expectedHash);
});

test("GitHub webhook signatures are verified over the raw payload", async (): Promise<void> => {
  const webhookSecret: string = "test-webhook-secret";
  const payloadBytes: Uint8Array = new TextEncoder().encode("{\"zen\":\"Keep it secure.\"}");
  const signature: string = createHmac("sha256", webhookSecret)
    .update(payloadBytes)
    .digest("hex");

  assert.equal(
    await verifyGitHubWebhookSignature(
      payloadBytes,
      `sha256=${signature}`,
      webhookSecret,
    ),
    true,
  );
  assert.equal(
    await verifyGitHubWebhookSignature(
      new TextEncoder().encode("modified"),
      `sha256=${signature}`,
      webhookSecret,
    ),
    false,
  );
  assert.equal(
    await verifyGitHubWebhookSignature(payloadBytes, "sha256=invalid", webhookSecret),
    false,
  );
});

test("the updater accepts only signed GitHub webhook pings", async (): Promise<void> => {
  const webhookSecret: string = "test-webhook-secret";
  const payload: string = "{\"zen\":\"Keep it secure.\"}";
  const signature: string = createHmac("sha256", webhookSecret)
    .update(payload)
    .digest("hex");
  const environment: {
    CLOUDFLARE_ACCOUNT_ID: string;
    CLOUDFLARE_API_TOKEN: string;
    GITHUB_WEBHOOK_SECRET: string;
    TRIGGER_TOKEN: string;
  } = {
    CLOUDFLARE_ACCOUNT_ID: "unused-account-id",
    CLOUDFLARE_API_TOKEN: "unused-api-token",
    GITHUB_WEBHOOK_SECRET: webhookSecret,
    TRIGGER_TOKEN: "unused-trigger-token",
  };

  const acceptedResponse: Response = await updaterWorker.fetch(
    new Request("https://updater.test/github-release-webhook", {
      method: "POST",
      headers: {
        "X-GitHub-Event": "ping",
        "X-Hub-Signature-256": `sha256=${signature}`,
      },
      body: payload,
    }),
    environment,
  );
  const rejectedResponse: Response = await updaterWorker.fetch(
    new Request("https://updater.test/github-release-webhook", {
      method: "POST",
      headers: {
        "X-GitHub-Event": "ping",
        "X-Hub-Signature-256": "sha256=invalid",
      },
      body: payload,
    }),
    environment,
  );

  assert.equal(acceptedResponse.status, 200);
  assert.deepEqual(await acceptedResponse.json(), { status: "ready" });
  assert.equal(rejectedResponse.status, 401);
});

test("only stable published releases from the expected repository are accepted", (): void => {
  const stablePayload: Record<string, unknown> = releaseWebhookPayload("published", false);

  assert.equal(publishedReleaseTag(stablePayload), "TrayAppDotNET_124");
  assert.equal(publishedReleaseTag(releaseWebhookPayload("edited", false)), null);
  assert.equal(publishedReleaseTag(releaseWebhookPayload("published", true)), null);
  assert.throws(
    (): string | null => publishedReleaseTag({
      ...stablePayload,
      repository: { full_name: "someone-else/TrayAppDotNET" },
    }),
    /Unexpected webhook repository/,
  );
});

test("endpoint deployment performs the direct static asset upload sequence", async (): Promise<void> => {
  const xml: string = manifestXml(120, [...CATALOG_APPLICATION_IDS]);
  const base64Content: string = Buffer.from(xml, "utf8").toString("base64");
  const expectedHash: string = await calculateAssetHash(base64Content, "xml");
  const requestDescriptions: string[] = [];
  const fetchFunction: FetchFunction = async (
    input: string,
    request: RequestInit = {},
  ): Promise<Response> => {
    const method: string = request.method ?? "GET";
    requestDescriptions.push(`${method} ${input}`);

    if (input.endsWith("/assets-upload-session")) {
      assert.equal(request.headers instanceof Headers, true);
      assert.equal((request.headers as Headers).get("Authorization"), "Bearer cloudflare-token");
      const body: Record<string, unknown> = JSON.parse(String(request.body)) as Record<string, unknown>;
      const manifest: Record<string, unknown> = body.manifest as Record<string, unknown>;
      assert.deepEqual(manifest["/versions.xml"], {
        hash: expectedHash,
        size: Buffer.byteLength(xml, "utf8"),
      });
      return cloudflareResponse({ jwt: "upload-jwt", buckets: [[expectedHash]] });
    }

    if (input.includes("/workers/assets/upload")) {
      assert.equal((request.headers as Headers).get("Authorization"), "Bearer upload-jwt");
      assert.equal(request.body instanceof FormData, true);
      const uploadedAsset: FormDataEntryValue | null =
        (request.body as FormData).get(expectedHash);
      assert.equal(uploadedAsset instanceof Blob, true);
      assert.equal(await (uploadedAsset as Blob).text(), base64Content);
      assert.equal((uploadedAsset as Blob).type, "application/xml; charset=utf-8");
      return cloudflareResponse({ jwt: "completion-jwt" }, 201);
    }

    if (input.endsWith("/workers/scripts/trayapp-version-endpoint")) {
      assert.equal(method, "PUT");
      assert.equal((request.headers as Headers).get("Authorization"), "Bearer cloudflare-token");
      assert.equal(request.body instanceof FormData, true);
      const metadataPart: FormDataEntryValue | null =
        (request.body as FormData).get("metadata");
      assert.equal(metadataPart instanceof Blob, true);
      const metadata: Record<string, unknown> = JSON.parse(
        await (metadataPart as Blob).text(),
      ) as Record<string, unknown>;
      const assets: Record<string, unknown> = metadata.assets as Record<string, unknown>;
      assert.equal(assets.jwt, "completion-jwt");
      assert.deepEqual(metadata.bindings, [{ name: "ASSETS", type: "assets" }]);

      const workerPart: FormDataEntryValue | null =
        (request.body as FormData).get("version-endpoint.js");
      assert.equal(workerPart instanceof Blob, true);
      const workerSource: string = await (workerPart as Blob).text();
      assert.equal(workerSource, VERSION_ENDPOINT_WORKER_SOURCE);
      return cloudflareResponse({ id: "trayapp-version-endpoint" });
    }

    throw new Error(`Unexpected request: ${method} ${input}`);
  };

  await deployVersionEndpoint(xml, "account-id", "cloudflare-token", fetchFunction);

  assert.equal(requestDescriptions.length, 3);
});

test("the embedded endpoint Worker is generated from version-endpoint/src/index.ts", async (): Promise<void> => {
  const generatedModule: string = await readFile(
    new URL("../src/generated/version-endpoint-worker.ts", import.meta.url),
    "utf8",
  );

  // A Windows checkout may carry CRLF line endings; the generated content is what has to match.
  assert.equal(
    generatedModule.replace(/\r\n/g, "\n"),
    (await renderGeneratedEndpointWorkerModule()).replace(/\r\n/g, "\n"),
  );
});

test("the embedded endpoint Worker serves versions.xml at the root only", async (): Promise<void> => {
  const workerModule: { default: { fetch: (request: Request, environment: unknown) => Promise<Response> | Response } } =
    await import(`data:text/javascript;base64,${Buffer.from(VERSION_ENDPOINT_WORKER_SOURCE).toString("base64")}`);
  const assetRequests: string[] = [];
  const environment = {
    ASSETS: {
      fetch: async (request: Request): Promise<Response> => {
        assetRequests.push(request.url);
        return new Response("<versions />");
      },
    },
  };

  const rootResponse: Response = await workerModule.default.fetch(
    new Request("https://version.trayapp.net/?check=1"),
    environment,
  );
  const otherResponse: Response = await workerModule.default.fetch(
    new Request("https://version.trayapp.net/other"),
    environment,
  );

  assert.equal(await rootResponse.text(), "<versions />");
  assert.deepEqual(assetRequests, ["https://version.trayapp.net/versions.xml?check=1"]);
  assert.equal(otherResponse.status, 404);
});

test("the served endpoint version is read from versions@version", (): void => {
  assert.equal(servedEndpointVersion(manifestXml(130, [...CATALOG_APPLICATION_IDS])), 130);
  assert.equal(servedEndpointVersion("<other />"), null);
  assert.equal(servedEndpointVersion("not xml <<"), null);
});

test("a lower release is refused without force", async (): Promise<void> => {
  const scenario: PublishScenario = await publishScenario(125, 130);

  const result = await publishLatestVersionEndpoint(TEST_ENVIRONMENT, scenario.fetchFunction);

  assert.equal(result.status, "refused");
  assert.match(result.message ?? "", /serves 130.*lower version 125/);
  assert.equal(scenario.deployed(), false);
});

test("a lower release is deployed with force", async (): Promise<void> => {
  const scenario: PublishScenario = await publishScenario(125, 130);

  const result = await publishLatestVersionEndpoint(
    TEST_ENVIRONMENT,
    scenario.fetchFunction,
    { force: true },
  );

  assert.equal(result.status, "published");
  assert.equal(result.releaseTag, "TrayAppDotNET_125");
  assert.equal(scenario.deployed(), true);
});

test("an identical release leaves the endpoint unchanged", async (): Promise<void> => {
  const scenario: PublishScenario = await publishScenario(125, 125);

  const result = await publishLatestVersionEndpoint(TEST_ENVIRONMENT, scenario.fetchFunction);

  assert.equal(result.status, "unchanged");
  assert.equal(scenario.deployed(), false);
});

test("a higher release is deployed without force", async (): Promise<void> => {
  const scenario: PublishScenario = await publishScenario(130, 125);

  const result = await publishLatestVersionEndpoint(TEST_ENVIRONMENT, scenario.fetchFunction);

  assert.equal(result.status, "published");
  assert.equal(result.releaseTag, "TrayAppDotNET_130");
  assert.equal(scenario.deployed(), true);
});

test("the trigger URL forwards force=true and answers a refusal with 409", async (): Promise<void> => {
  const scenario: PublishScenario = await publishScenario(125, 130);
  const originalFetch: typeof fetch = globalThis.fetch;
  globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit): Promise<Response> =>
    scenario.fetchFunction(String(input), init)) as typeof fetch;
  try {
    const refused: Response = await updaterWorker.fetch(
      new Request("https://updater.example/trigger-token", { method: "POST" }),
      { ...TEST_ENVIRONMENT, TRIGGER_TOKEN: "trigger-token" },
    );
    assert.equal(refused.status, 409);
    assert.equal(scenario.deployed(), false);

    const forced: Response = await updaterWorker.fetch(
      new Request("https://updater.example/trigger-token?force=true", { method: "POST" }),
      { ...TEST_ENVIRONMENT, TRIGGER_TOKEN: "trigger-token" },
    );
    assert.equal(forced.status, 200);
    assert.equal(scenario.deployed(), true);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

const TEST_ENVIRONMENT = {
  CLOUDFLARE_ACCOUNT_ID: "account-id",
  CLOUDFLARE_API_TOKEN: "cloudflare-token",
  GITHUB_WEBHOOK_SECRET: "webhook-secret",
  TRIGGER_TOKEN: "trigger-token",
};

interface PublishScenario {
  fetchFunction: FetchFunction;
  deployed: () => boolean;
}

/** Serves `latestVersion` as the newest release while the endpoint serves `servedVersion`. */
async function publishScenario(latestVersion: number, servedVersion: number): Promise<PublishScenario> {
  const releaseFetch = (version: number): FetchFunction =>
    async (): Promise<Response> => xmlResponse(manifestXml(version, [...CATALOG_APPLICATION_IDS]));
  const latest: PreparedEndpointManifest = await prepareLatestEndpointManifest(releaseFetch(latestVersion));
  const served: PreparedEndpointManifest = await prepareLatestEndpointManifest(releaseFetch(servedVersion));
  let servedXml: string = served.xml;
  let deployed: boolean = false;

  const fetchFunction: FetchFunction = async (input: string, request: RequestInit = {}): Promise<Response> => {
    if (input === LATEST_MANIFEST_URL) {
      return xmlResponse(manifestXml(latestVersion, [...CATALOG_APPLICATION_IDS]));
    }
    if (input.startsWith(`${VERSION_ENDPOINT_URL}?`)) return xmlResponse(servedXml);
    if (input.endsWith("/assets-upload-session")) {
      return cloudflareResponse({ jwt: "upload-jwt", buckets: [] });
    }
    if (input.endsWith("/workers/scripts/trayapp-version-endpoint") && request.method === "PUT") {
      deployed = true;
      servedXml = latest.xml;
      return cloudflareResponse({ id: "trayapp-version-endpoint" });
    }
    throw new Error(`Unexpected request: ${request.method ?? "GET"} ${input}`);
  };
  return { fetchFunction, deployed: (): boolean => deployed };
}

function manifestXml(
  version: number,
  applicationIds: readonly string[],
  includeInstallers: boolean = false,
  declaredApplicationIds?: readonly string[],
): string {
  const artifactLines: string[] = [
    artifactXml("aggregate", "TrayAppDotNET", version),
  ];
  for (let index: number = 0; index < applicationIds.length; index += 1) {
    artifactLines.push(artifactXml("app", applicationIds[index], 200 + index));
  }
  // Mirrors publish.py, which writes one installer row per app plus the suite installer
  if (includeInstallers) {
    for (let index: number = 0; index < applicationIds.length; index += 1) {
      artifactLines.push(installerArtifactXml(applicationIds[index], 200 + index));
    }
    artifactLines.push(installerArtifactXml("TrayAppDotNET", version));
  }
  // Manifests from before publish.py declared the released apps carry no applications element
  const applicationsXml: string = declaredApplicationIds === undefined
    ? ""
    : `  <applications>\n${declaredApplicationIds
      .map((applicationId: string): string => `    <application appId="${applicationId}" />\n`)
      .join("")}  </applications>\n`;
  return `<?xml version="1.0" encoding="utf-8"?>
<versions version="${version}" runtime="win-x64">
  <release repository="alchemyyy/TrayAppDotNET" tag="TrayAppDotNET_${version}" name="TrayAppDotNET ${version}" />
${applicationsXml}  <artifacts>
${artifactLines.join("\n")}
  </artifacts>
</versions>
`;
}

function artifactXml(kind: "aggregate" | "app", applicationId: string, version: number): string {
  return `    <artifact profile="release" profileName="Release" kind="${kind}" appId="${applicationId}" version="${version}" fileName="${applicationId}_${version}.zip" sha256="${"a".repeat(64)}" size="123" source="test" commitHash="${"b".repeat(40)}" />`;
}

function installerArtifactXml(applicationId: string, version: number): string {
  return `    <artifact profile="release" profileName="Release" kind="installer" appId="${applicationId}" version="${version}" fileName="Installer_${applicationId}_${version}.exe" sha256="${"c".repeat(64)}" size="456" source="test" commitHash="${"b".repeat(40)}" />`;
}

function taggedManifestUrl(version: number): string {
  return "https://github.com/alchemyyy/TrayAppDotNET/releases/download/"
    + `TrayAppDotNET_${version}/versions.xml`;
}

function releaseWebhookPayload(action: string, prerelease: boolean): Record<string, unknown> {
  return {
    action,
    repository: { full_name: "alchemyyy/TrayAppDotNET" },
    release: {
      draft: false,
      prerelease,
      tag_name: "TrayAppDotNET_124",
    },
  };
}

function xmlResponse(xml: string): Response {
  return new Response(xml, {
    status: 200,
    headers: { "Content-Type": "application/xml" },
  });
}

function cloudflareResponse(result: Record<string, unknown>, status = 200): Response {
  return new Response(JSON.stringify({ success: true, result, errors: [], messages: [] }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

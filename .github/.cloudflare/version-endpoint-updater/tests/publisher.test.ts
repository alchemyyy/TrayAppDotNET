import assert from "node:assert/strict";
import { createHash, createHmac } from "node:crypto";
import test from "node:test";
import { calculateAssetHash, deployVersionEndpoint } from "../src/cloudflare-deploy";
import updaterWorker, {
  publishedReleaseTag,
  verifyGitHubWebhookSignature,
} from "../src/index";
import {
  EXPECTED_APPLICATION_IDS,
  prepareEndpointManifestForRelease,
  prepareLatestEndpointManifest,
  type ApplicationId,
  type FetchFunction,
  type PreparedEndpointManifest,
} from "../src/manifest";

const LATEST_MANIFEST_URL =
  "https://github.com/alchemyyy/TrayAppDotNET/releases/latest/download/versions.xml";
const PRE_TASK_MANAGER_APPLICATION_IDS: ApplicationId[] = EXPECTED_APPLICATION_IDS.filter(
  (applicationId: ApplicationId): boolean => applicationId !== "TaskManagerTrayAppDotNET",
);

test("a complete latest manifest requires no historical requests", async (): Promise<void> => {
  const requestUrls: string[] = [];
  const latestXml: string = manifestXml(125, [...EXPECTED_APPLICATION_IDS]);
  const fetchFunction: FetchFunction = async (input: string): Promise<Response> => {
    requestUrls.push(input);
    return xmlResponse(latestXml);
  };

  const prepared: PreparedEndpointManifest = await prepareLatestEndpointManifest(fetchFunction);

  assert.deepEqual(requestUrls, [LATEST_MANIFEST_URL]);
  assert.equal(prepared.releaseTag, "TrayAppDotNET_125");
  for (const applicationId of EXPECTED_APPLICATION_IDS) {
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
        xmlResponse(manifestXml(123, [...EXPECTED_APPLICATION_IDS])),
    ),
    /Unexpected aggregate version: 123/,
  );
});

test("missing apps are filled by walking older release manifests", async (): Promise<void> => {
  const currentApplications: ApplicationId[] = [...EXPECTED_APPLICATION_IDS.slice(0, 2)];
  const release126Applications: ApplicationId[] = [EXPECTED_APPLICATION_IDS[2]];
  const release125Applications: ApplicationId[] = [...EXPECTED_APPLICATION_IDS.slice(3)];
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
    prepared.applicationReleaseTags.get(EXPECTED_APPLICATION_IDS[2]),
    "TrayAppDotNET_126",
  );
  for (const applicationId of release125Applications) {
    assert.equal(prepared.applicationReleaseTags.get(applicationId), "TrayAppDotNET_125");
  }

  for (const applicationId of EXPECTED_APPLICATION_IDS) {
    const artifactOccurrences: number = prepared.xml
      .split(`appId="${applicationId}"`)
      .length - 1;
    assert.equal(artifactOccurrences, 1);
  }
});

test("missing release tags are skipped while repairing", async (): Promise<void> => {
  const firstApplication: ApplicationId = EXPECTED_APPLICATION_IDS[0];
  const remainingApplications: ApplicationId[] = [...EXPECTED_APPLICATION_IDS.slice(1)];
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
  const xml: string = manifestXml(120, [...EXPECTED_APPLICATION_IDS]);
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
      assert.match(workerSource, /requestUrl\.pathname === "\/"/);
      assert.match(workerSource, /environment\.ASSETS\.fetch/);
      assert.match(workerSource, /VERSION_ASSET_PATH = "\/versions\.xml"/);
      return cloudflareResponse({ id: "trayapp-version-endpoint" });
    }

    throw new Error(`Unexpected request: ${method} ${input}`);
  };

  await deployVersionEndpoint(xml, "account-id", "cloudflare-token", fetchFunction);

  assert.equal(requestDescriptions.length, 3);
});

function manifestXml(version: number, applicationIds: readonly string[]): string {
  const artifactLines: string[] = [
    artifactXml("aggregate", "TrayAppDotNET", version),
  ];
  for (let index: number = 0; index < applicationIds.length; index += 1) {
    artifactLines.push(artifactXml("app", applicationIds[index], 200 + index));
  }
  return `<?xml version="1.0" encoding="utf-8"?>
<versions version="${version}" runtime="win-x64">
  <release repository="alchemyyy/TrayAppDotNET" tag="TrayAppDotNET_${version}" name="TrayAppDotNET ${version}" />
  <artifacts>
${artifactLines.join("\n")}
  </artifacts>
</versions>
`;
}

function artifactXml(kind: "aggregate" | "app", applicationId: string, version: number): string {
  return `    <artifact profile="release" profileName="Release" kind="${kind}" appId="${applicationId}" version="${version}" fileName="${applicationId}_${version}.zip" sha256="${"a".repeat(64)}" size="123" source="test" commitHash="${"b".repeat(40)}" />`;
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

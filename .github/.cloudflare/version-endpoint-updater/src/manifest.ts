import { XMLBuilder, XMLParser } from "fast-xml-parser";

export const VERSION_ENDPOINT_URL = "https://version.trayapp.net/versions.xml";

const REPOSITORY = "alchemyyy/TrayAppDotNET";
const LATEST_MANIFEST_URL =
  `https://github.com/${REPOSITORY}/releases/latest/download/versions.xml`;
const RELEASE_TAG_PATTERN = /^TrayAppDotNET_(\d+)$/;
const MINIMUM_RELEASE_VERSION = 100;
const TASK_MANAGER_MINIMUM_RELEASE_VERSION = 125;
const MAXIMUM_MANIFEST_SIZE_BYTES = 1_048_576;

export const EXPECTED_APPLICATION_IDS = [
  "BatteryTrayAppDotNET",
  "BrightnessTrayAppDotNET",
  "FanControlTrayAppDotNET",
  "NetworkTrayAppDotNET",
  "TaskManagerTrayAppDotNET",
  "VolumeTrayAppDotNET",
] as const;

export type ApplicationId = (typeof EXPECTED_APPLICATION_IDS)[number];
export type FetchFunction = (input: string, init?: RequestInit) => Promise<Response>;

const APPLICATION_MINIMUM_RELEASE_VERSIONS: Readonly<Record<ApplicationId, number>> = {
  BatteryTrayAppDotNET: MINIMUM_RELEASE_VERSION,
  BrightnessTrayAppDotNET: MINIMUM_RELEASE_VERSION,
  FanControlTrayAppDotNET: MINIMUM_RELEASE_VERSION,
  NetworkTrayAppDotNET: MINIMUM_RELEASE_VERSION,
  TaskManagerTrayAppDotNET: TASK_MANAGER_MINIMUM_RELEASE_VERSION,
  VolumeTrayAppDotNET: MINIMUM_RELEASE_VERSION,
};

interface VersionArtifact {
  profile: string;
  profileName: string;
  kind: "aggregate" | "app";
  applicationId: string;
  version: number;
  fileName: string;
  sha256: string;
  size: number;
  source: string;
  commitHash: string;
  releaseTag: string;
}

interface ReleaseManifest {
  version: number;
  runtime: string;
  release: {
    repository: string;
    tag: string;
    name: string;
  };
  aggregate: VersionArtifact;
  applications: Map<ApplicationId, VersionArtifact>;
}

interface XmlArtifact {
  "@_profile"?: unknown;
  "@_profileName"?: unknown;
  "@_kind"?: unknown;
  "@_appId"?: unknown;
  "@_version"?: unknown;
  "@_fileName"?: unknown;
  "@_sha256"?: unknown;
  "@_size"?: unknown;
  "@_source"?: unknown;
  "@_commitHash"?: unknown;
  "@_releaseTag"?: unknown;
}

interface XmlDocument {
  versions?: {
    "@_version"?: unknown;
    "@_runtime"?: unknown;
    release?: {
      "@_repository"?: unknown;
      "@_tag"?: unknown;
      "@_name"?: unknown;
    };
    artifacts?: {
      artifact?: XmlArtifact | XmlArtifact[];
    };
  };
}

export interface PreparedEndpointManifest {
  xml: string;
  releaseTag: string;
  applicationReleaseTags: ReadonlyMap<ApplicationId, string>;
}

const xmlParser = new XMLParser({
  ignoreAttributes: false,
  parseAttributeValue: false,
  parseTagValue: false,
  processEntities: false,
  trimValues: true,
});

const xmlBuilder = new XMLBuilder({
  format: true,
  ignoreAttributes: false,
  indentBy: "  ",
  processEntities: false,
  suppressEmptyNode: true,
});

/** Builds a complete endpoint manifest, searching older releases only for missing apps. */
export async function prepareLatestEndpointManifest(
  fetchFunction: FetchFunction = fetch,
): Promise<PreparedEndpointManifest> {
  return prepareEndpointManifest(LATEST_MANIFEST_URL, undefined, fetchFunction);
}

/** Builds a complete endpoint manifest from an exact published release tag. */
export async function prepareEndpointManifestForRelease(
  releaseTag: string,
  fetchFunction: FetchFunction = fetch,
): Promise<PreparedEndpointManifest> {
  const releaseVersion: number = releaseVersionFromTag(releaseTag);
  return prepareEndpointManifest(
    releaseManifestUrl(releaseTag),
    releaseVersion,
    fetchFunction,
  );
}

async function prepareEndpointManifest(
  primaryManifestUrl: string,
  expectedVersion: number | undefined,
  fetchFunction: FetchFunction,
): Promise<PreparedEndpointManifest> {
  const primaryXml: string | null = await fetchManifest(
    primaryManifestUrl,
    fetchFunction,
    false,
  );
  if (primaryXml === null) throw new Error("Published release manifest was not found");
  const latest: ReleaseManifest = parseReleaseManifest(primaryXml, expectedVersion);
  const requiredApplicationIds: ApplicationId[] = EXPECTED_APPLICATION_IDS.filter(
    (applicationId: ApplicationId): boolean =>
      APPLICATION_MINIMUM_RELEASE_VERSIONS[applicationId] <= latest.version,
  );
  const selected: Map<ApplicationId, VersionArtifact> = new Map();
  addMissingApplications(selected, latest, requiredApplicationIds);

  for (
    let releaseVersion: number = latest.version - 1;
    selected.size < requiredApplicationIds.length
      && releaseVersion >= MINIMUM_RELEASE_VERSION;
    releaseVersion -= 1
  ) {
    const releaseTag: string = `TrayAppDotNET_${releaseVersion}`;
    const releaseUrl: string = releaseManifestUrl(releaseTag);
    const historicalXml: string | null = await fetchManifest(releaseUrl, fetchFunction, true);
    if (historicalXml === null) continue;

    const historical: ReleaseManifest = parseReleaseManifest(historicalXml, releaseVersion);
    addMissingApplications(selected, historical, requiredApplicationIds);
  }

  const missing: ApplicationId[] = requiredApplicationIds.filter(
    (applicationId: ApplicationId): boolean => !selected.has(applicationId),
  );
  if (missing.length > 0) {
    throw new Error(`Could not find version artifacts for: ${missing.join(", ")}`);
  }

  const applications: VersionArtifact[] = requiredApplicationIds.map(
    (applicationId: ApplicationId): VersionArtifact => requiredArtifact(selected, applicationId),
  );
  const aggregate: VersionArtifact = { ...latest.aggregate, releaseTag: latest.release.tag };
  const applicationReleaseTags: Map<ApplicationId, string> = new Map();
  for (const applicationId of requiredApplicationIds) {
    applicationReleaseTags.set(
      applicationId,
      requiredArtifact(selected, applicationId).releaseTag,
    );
  }

  return {
    xml: buildEndpointXml(latest, aggregate, applications),
    releaseTag: latest.release.tag,
    applicationReleaseTags,
  };
}

function releaseVersionFromTag(releaseTag: string): number {
  const match: RegExpExecArray | null = RELEASE_TAG_PATTERN.exec(releaseTag.trim());
  const releaseVersion: number = match === null ? 0 : Number.parseInt(match[1], 10);
  if (!Number.isSafeInteger(releaseVersion) || releaseVersion < MINIMUM_RELEASE_VERSION) {
    throw new Error(`Invalid release tag: ${releaseTag}`);
  }
  return releaseVersion;
}

function releaseManifestUrl(releaseTag: string): string {
  return `https://github.com/${REPOSITORY}/releases/download/`
    + `${encodeURIComponent(releaseTag)}/versions.xml`;
}

function parseReleaseManifest(xml: string, expectedVersion?: number): ReleaseManifest {
  const size: number = new TextEncoder().encode(xml).byteLength;
  if (size === 0 || size > MAXIMUM_MANIFEST_SIZE_BYTES) {
    throw new Error(`Version manifest has an invalid size: ${size}`);
  }
  if (xml.toLowerCase().includes("<!doctype")) {
    throw new Error("Version manifest must not contain a document type declaration");
  }

  const document: XmlDocument = xmlParser.parse(xml) as XmlDocument;
  const versions: NonNullable<XmlDocument["versions"]> | undefined = document.versions;
  if (versions === undefined) throw new Error("Version manifest has no versions root");

  const version: number = positiveInteger(versions["@_version"], "aggregate version");
  if (version < MINIMUM_RELEASE_VERSION || (expectedVersion !== undefined && version !== expectedVersion)) {
    throw new Error(`Unexpected aggregate version: ${version}`);
  }
  const runtime: string = requiredText(versions["@_runtime"], "runtime");
  if (runtime !== "win-x64") throw new Error(`Unsupported runtime: ${runtime}`);

  const releaseXml: NonNullable<typeof versions.release> | undefined = versions.release;
  if (releaseXml === undefined) throw new Error("Version manifest has no release element");
  const repository: string = requiredText(releaseXml["@_repository"], "repository");
  const tag: string = requiredText(releaseXml["@_tag"], "release tag");
  const name: string = requiredText(releaseXml["@_name"], "release name");
  if (repository.toLowerCase() !== REPOSITORY.toLowerCase()) {
    throw new Error(`Unexpected repository: ${repository}`);
  }
  if (tag !== `TrayAppDotNET_${version}`) {
    throw new Error(`Release tag ${tag} does not match version ${version}`);
  }

  const rawArtifacts: XmlArtifact | XmlArtifact[] | undefined = versions.artifacts?.artifact;
  const artifactValues: XmlArtifact[] = rawArtifacts === undefined
    ? []
    : Array.isArray(rawArtifacts) ? rawArtifacts : [rawArtifacts];
  const applications: Map<ApplicationId, VersionArtifact> = new Map();
  let aggregate: VersionArtifact | null = null;
  for (const artifactValue of artifactValues) {
    const artifact: VersionArtifact = parseArtifact(artifactValue);
    if (artifact.kind === "aggregate") {
      if (aggregate !== null || artifact.applicationId !== "TrayAppDotNET") {
        throw new Error("Version manifest has an invalid aggregate artifact");
      }
      if (artifact.version !== version) {
        throw new Error("Aggregate artifact version does not match its release");
      }
      aggregate = artifact;
      continue;
    }

    if (!isApplicationId(artifact.applicationId)) {
      throw new Error(`Unexpected application artifact: ${artifact.applicationId}`);
    }
    if (applications.has(artifact.applicationId)) {
      throw new Error(`Duplicate application artifact: ${artifact.applicationId}`);
    }
    applications.set(artifact.applicationId, artifact);
  }
  if (aggregate === null) throw new Error("Version manifest has no aggregate artifact");

  return {
    version,
    runtime,
    release: { repository, tag, name },
    aggregate,
    applications,
  };
}

function parseArtifact(xml: XmlArtifact): VersionArtifact {
  const profile: string = requiredText(xml["@_profile"], "artifact profile");
  if (profile !== "release") throw new Error(`Unsupported artifact profile: ${profile}`);
  const kindText: string = requiredText(xml["@_kind"], "artifact kind");
  if (kindText !== "aggregate" && kindText !== "app") {
    throw new Error(`Unsupported artifact kind: ${kindText}`);
  }

  const applicationId: string = requiredText(xml["@_appId"], "artifact appId");
  const version: number = positiveInteger(xml["@_version"], `${applicationId} version`);
  const fileName: string = requiredText(xml["@_fileName"], `${applicationId} file name`);
  if (fileName !== `${applicationId}_${version}.zip`) {
    throw new Error(`Unexpected file name for ${applicationId}: ${fileName}`);
  }
  const sha256: string = requiredText(xml["@_sha256"], `${applicationId} SHA-256`);
  if (!/^[0-9a-fA-F]{64}$/.test(sha256)) {
    throw new Error(`Invalid SHA-256 for ${applicationId}`);
  }
  const commitHash: string = text(xml["@_commitHash"]);
  if (commitHash !== "" && !/^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$/.test(commitHash)) {
    throw new Error(`Invalid commit hash for ${applicationId}`);
  }

  return {
    profile,
    profileName: text(xml["@_profileName"]) || "Release",
    kind: kindText,
    applicationId,
    version,
    fileName,
    sha256: sha256.toLowerCase(),
    size: positiveInteger(xml["@_size"], `${applicationId} size`),
    source: text(xml["@_source"]),
    commitHash: commitHash.toLowerCase(),
    releaseTag: text(xml["@_releaseTag"]),
  };
}

function addMissingApplications(
  selected: Map<ApplicationId, VersionArtifact>,
  manifest: ReleaseManifest,
  applicationIds: readonly ApplicationId[],
): void {
  for (const applicationId of applicationIds) {
    if (selected.has(applicationId)) continue;
    const artifact: VersionArtifact | undefined = manifest.applications.get(applicationId);
    if (artifact !== undefined) {
      selected.set(applicationId, { ...artifact, releaseTag: manifest.release.tag });
    }
  }
}

function buildEndpointXml(
  latest: ReleaseManifest,
  aggregate: VersionArtifact,
  applications: VersionArtifact[],
): string {
  const document: Record<string, unknown> = {
    versions: {
      "@_version": String(latest.version),
      "@_runtime": latest.runtime,
      release: {
        "@_repository": latest.release.repository,
        "@_tag": latest.release.tag,
        "@_name": latest.release.name,
      },
      artifacts: {
        artifact: [aggregate, ...applications].map(artifactXmlObject),
      },
    },
  };
  return `<?xml version="1.0" encoding="utf-8"?>\n${xmlBuilder.build(document).trimEnd()}\n`;
}

function artifactXmlObject(artifact: VersionArtifact): Record<string, string> {
  const result: Record<string, string> = {
    "@_profile": artifact.profile,
    "@_profileName": artifact.profileName,
    "@_kind": artifact.kind,
    "@_appId": artifact.applicationId,
    "@_version": String(artifact.version),
    "@_fileName": artifact.fileName,
    "@_sha256": artifact.sha256,
    "@_size": String(artifact.size),
    "@_source": artifact.source,
    "@_releaseTag": artifact.releaseTag,
  };
  if (artifact.commitHash !== "") result["@_commitHash"] = artifact.commitHash;
  return result;
}

async function fetchManifest(
  url: string,
  fetchFunction: FetchFunction,
  optional: boolean,
): Promise<string | null> {
  const response: Response = await fetchFunction(url, {
    headers: { Accept: "application/xml", "Cache-Control": "no-cache" },
    redirect: "follow",
  });
  if (optional && response.status === 404) return null;
  if (!response.ok) throw new Error(`Manifest request failed with HTTP ${response.status}: ${url}`);

  const xml: string = await response.text();
  if (new TextEncoder().encode(xml).byteLength > MAXIMUM_MANIFEST_SIZE_BYTES) {
    throw new Error(`Manifest exceeds ${MAXIMUM_MANIFEST_SIZE_BYTES} bytes: ${url}`);
  }
  return xml;
}

function requiredArtifact(
  applications: ReadonlyMap<ApplicationId, VersionArtifact>,
  applicationId: ApplicationId,
): VersionArtifact {
  const artifact: VersionArtifact | undefined = applications.get(applicationId);
  if (artifact === undefined) throw new Error(`Missing selected artifact for ${applicationId}`);
  return artifact;
}

function requiredText(value: unknown, description: string): string {
  const normalized: string = text(value);
  if (normalized === "") throw new Error(`Missing ${description}`);
  return normalized;
}

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function positiveInteger(value: unknown, description: string): number {
  const normalized: string = text(value);
  const parsed: number = /^\d+$/.test(normalized) ? Number.parseInt(normalized, 10) : 0;
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`Invalid ${description}`);
  }
  return parsed;
}

function isApplicationId(value: string): value is ApplicationId {
  return EXPECTED_APPLICATION_IDS.some(
    (applicationId: ApplicationId): boolean => applicationId === value,
  );
}

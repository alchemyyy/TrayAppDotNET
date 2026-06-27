import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import {
  prepareLatestEndpointManifest,
  type PreparedEndpointManifest,
} from "../src/manifest";

async function main(): Promise<void> {
  const outputPath: string = outputArgument(process.argv.slice(2));
  const preparedManifest: PreparedEndpointManifest = await prepareLatestEndpointManifest();
  const absoluteOutputPath: string = resolve(outputPath);
  await mkdir(dirname(absoluteOutputPath), { recursive: true });
  await writeFile(absoluteOutputPath, preparedManifest.xml, "utf8");
  console.log(`Prepared ${preparedManifest.releaseTag}: ${absoluteOutputPath}`);
  for (const [applicationId, releaseTag] of preparedManifest.applicationReleaseTags) {
    console.log(`- ${applicationId}: ${releaseTag}`);
  }
}

function outputArgument(argumentsList: string[]): string {
  const outputIndex: number = argumentsList.indexOf("--output");
  if (outputIndex < 0 || outputIndex + 1 >= argumentsList.length) {
    throw new Error("Usage: npm run prepare-endpoint -- --output <versions.xml>");
  }
  const outputPath: string = argumentsList[outputIndex + 1].trim();
  if (outputPath === "") throw new Error("Output path must not be empty");
  return outputPath;
}

await main();

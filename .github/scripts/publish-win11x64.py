#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


@dataclass(frozen=True)
class App:
    name: str
    project: str
    buildnumber: str


@dataclass(frozen=True)
class Profile:
    id: str
    display_name: str
    build_source: str
    publish_aot: bool
    self_contained: bool
    legacy_names: bool


@dataclass
class AppPackage:
    app: App
    profile: Profile
    version: int
    zip_path: Path
    source: str


APPS = [
    App("BatteryTrayAppDotNET", "BatteryTrayAppDotNET/src/BatteryTrayAppDotNET.csproj", "BatteryTrayAppDotNET/buildnumber.txt"),
    App("BrightnessTrayAppDotNET", "BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj", "BrightnessTrayAppDotNET/buildnumber.txt"),
    App("FanControlTrayAppDotNET", "FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj", "FanControlTrayAppDotNET/buildnumber.txt"),
    App("NetworkTrayAppDotNET", "NetworkTrayAppDotNET/src/NetworkTrayAppDotNET.csproj", "NetworkTrayAppDotNET/buildnumber.txt"),
    App("VolumeTrayAppDotNET", "VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj", "VolumeTrayAppDotNET/buildnumber.txt"),
]

PROFILES = {
    "release": Profile(
        id="release",
        display_name="Release",
        build_source="built-linux-release",
        publish_aot=False,
        self_contained=False,
        legacy_names=True,
    ),
    "native-aot": Profile(
        id="native-aot",
        display_name="Native AOT",
        build_source="built-windows-native-aot",
        publish_aot=True,
        self_contained=True,
        legacy_names=False,
    ),
}


def run(cmd: list[str], *, cwd: Path | None = None, capture: bool = False, check: bool = True) -> subprocess.CompletedProcess[str]:
    print("+ " + " ".join(cmd))
    result = subprocess.run(
        cmd,
        cwd=str(cwd) if cwd else None,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
        check=False,
    )
    if check and result.returncode != 0:
        if result.stdout:
            print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if result.stderr:
            print(result.stderr, file=sys.stderr, end="" if result.stderr.endswith("\n") else "\n")
        raise SystemExit(result.returncode)
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build and publish TrayAppDotNET Win11 x64 release assets.")
    parser.add_argument(
        "--phase",
        choices=["build-profile", "publish-release"],
        required=True,
        help="Build one profile package set, or publish a release from built profile package sets.",
    )
    parser.add_argument("--profile", choices=sorted(PROFILES), default="release", help="Profile to build in build-profile phase.")
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", "alchemyyy/TrayAppDotNET"))
    parser.add_argument("--tray-version", default="", help="Aggregate TrayAppDotNET version. Defaults to max selected app version.")
    parser.add_argument("--release-tag", default="", help="Release tag. Defaults to TrayAppDotNET_<version>_x64Win.")
    parser.add_argument("--target", default=os.environ.get("GITHUB_SHA", ""), help="Target commit for a new release.")
    parser.add_argument("--output-root", default=".artifacts/publish-win11x64")
    parser.add_argument("--input-root", default=".artifacts/publish-win11x64/collected")
    parser.add_argument("--force-rebuild", action="store_true", help="Build every app even if latest release has an equal/newer app zip.")
    return parser.parse_args()


def read_buildnumber(path: Path) -> int:
    value = path.read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"\d+", value):
        raise SystemExit(f"Expected numeric build number in {path}, got: {value!r}")
    return int(value)


def latest_release(repo: str) -> dict | None:
    result = run(["gh", "api", f"repos/{repo}/releases/latest"], capture=True, check=False)
    if result.returncode != 0:
        print("No latest published release found; all apps will be built.")
        return None
    return json.loads(result.stdout)


def app_asset_name(app: App, version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"{app.name}_{version}_x64Win.zip"
    token = profile_asset_token(profile)
    return f"{app.name}_{version}_{token}_x64Win.zip"


def aggregate_asset_name(tray_version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"TrayAppDotNET_{tray_version}_x64Win.zip"
    token = profile_asset_token(profile)
    return f"TrayAppDotNET_{tray_version}_{token}_x64Win.zip"


def profile_asset_token(profile: Profile) -> str:
    if profile.id == "native-aot":
        return "NativeAOT"
    return profile.id.replace("-", "").title()


def app_asset_pattern(app: App, profile: Profile) -> re.Pattern[str]:
    if profile.legacy_names:
        return re.compile(rf"^{re.escape(app.name)}_(\d+)_x64Win\.zip$")
    token = profile_asset_token(profile)
    return re.compile(rf"^{re.escape(app.name)}_(\d+)_{re.escape(token)}_x64Win\.zip$")


def latest_app_assets(release: dict | None, profile: Profile) -> dict[str, tuple[int, str]]:
    if not release:
        return {}

    found: dict[str, tuple[int, str]] = {}
    patterns = {app.name: app_asset_pattern(app, profile) for app in APPS}

    for asset in release.get("assets", []):
        name = asset.get("name", "")
        for app_name, pattern in patterns.items():
            match = pattern.fullmatch(name)
            if not match:
                continue
            version = int(match.group(1))
            existing = found.get(app_name)
            if existing is None or version > existing[0]:
                found[app_name] = (version, name)

    return found


def zip_directory(source_dir: Path, zip_path: Path) -> str:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    with ZipFile(zip_path, "w", ZIP_DEFLATED) as archive:
        for path in sorted(source_dir.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(source_dir).as_posix())

    return sha256_file(zip_path)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download_latest_asset(repo: str, tag_name: str, asset_name: str, destination_dir: Path) -> Path:
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination = destination_dir / asset_name
    if destination.exists():
        destination.unlink()
    run(["gh", "release", "download", tag_name, "--repo", repo, "--pattern", asset_name, "--dir", str(destination_dir)])
    if not destination.exists():
        raise SystemExit(f"Expected downloaded asset was not found: {destination}")
    return destination


def publish_command(app: App, publish_dir: Path, profile: Profile) -> list[str]:
    cmd = [
        "dotnet",
        "publish",
        app.project,
        "--configuration",
        "Release",
        "--runtime",
        "win-x64",
        "--self-contained",
        str(profile.self_contained).lower(),
        "--output",
        str(publish_dir),
        f"-p:PublishAot={str(profile.publish_aot).lower()}",
        "-p:PublishSingleFile=false",
        f"-p:SelfContained={str(profile.self_contained).lower()}",
        "-p:IncludeNativeLibrariesForSelfExtract=false",
        "-p:IncludeAllContentForSelfExtract=false",
        "-p:UseAppHost=true",
        "-p:EnableWindowsTargeting=true",
        "-p:SkipPublishAfterBuild=true",
        "-p:SkipKillRunningInstance=true",
        "-p:ContinuousIntegrationBuild=true",
    ]

    if not profile.publish_aot:
        cmd.insert(-7, "-p:PublishTrimmed=false")

    return cmd


def validate_publish_dir(app: App, publish_dir: Path, profile: Profile) -> None:
    expected_exe = publish_dir / f"{app.name}.exe"
    app_dll = publish_dir / f"{app.name}.dll"

    if not expected_exe.exists():
        raise SystemExit(
            f"{app.name} {profile.display_name} publish did not produce {expected_exe.name}. "
            "Refusing to package a failed publish."
        )

    if profile.publish_aot:
        if app_dll.exists():
            raise SystemExit(
                f"{app.name} Native AOT publish produced {app_dll.name}. "
                "Refusing to package a managed publish as Native AOT."
            )
        return

    if not app_dll.exists():
        raise SystemExit(
            f"{app.name} Release publish did not produce {app_dll.name}. "
            "Refusing to package a single-file or incomplete publish."
        )

    bundled_runtime = [name for name in ("coreclr.dll", "hostfxr.dll", "hostpolicy.dll") if (publish_dir / name).exists()]
    if bundled_runtime:
        joined = ", ".join(bundled_runtime)
        raise SystemExit(
            f"{app.name} Release publish contains bundled runtime files ({joined}). "
            "Refusing to package a self-contained publish."
        )


def build_app(app: App, version: int, output_root: Path, profile: Profile) -> Path:
    publish_dir = output_root / profile.id / "publish" / app.name
    package_dir = output_root / profile.id / "packages"
    zip_path = package_dir / app_asset_name(app, version, profile)

    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)

    run(["dotnet", "restore", app.project, "-p:EnableWindowsTargeting=true"])
    run(publish_command(app, publish_dir, profile))
    validate_publish_dir(app, publish_dir, profile)
    zip_directory(publish_dir, zip_path)
    return zip_path


def selected_packages(repo: str, output_root: Path, force_rebuild: bool, profile: Profile) -> list[AppPackage]:
    release = latest_release(repo)
    latest_tag = release.get("tag_name") if release else ""
    latest_assets = latest_app_assets(release, profile)

    packages: list[AppPackage] = []
    reuse_dir = output_root / profile.id / "reused"

    for app in APPS:
        current_version = read_buildnumber(Path(app.buildnumber))
        latest = latest_assets.get(app.name)

        if not force_rebuild and latest and current_version <= latest[0]:
            latest_version, latest_asset = latest
            print(
                f"{app.name} {profile.display_name}: current {current_version} is not greater than "
                f"latest {latest_version}; reusing {latest_asset}."
            )
            zip_path = download_latest_asset(repo, latest_tag, latest_asset, reuse_dir)
            packages.append(AppPackage(app, profile, latest_version, zip_path, "reused"))
            continue

        if latest:
            print(f"{app.name} {profile.display_name}: current {current_version} is greater than latest {latest[0]}; building.")
        else:
            print(f"{app.name} {profile.display_name}: no latest app asset found; building current {current_version}.")
        zip_path = build_app(app, current_version, output_root, profile)
        packages.append(AppPackage(app, profile, current_version, zip_path, profile.build_source))

    return packages


def create_flat_aggregate(packages: list[AppPackage], aggregate_zip: Path) -> str:
    aggregate_zip.parent.mkdir(parents=True, exist_ok=True)
    if aggregate_zip.exists():
        aggregate_zip.unlink()

    seen: dict[str, str] = {}
    with ZipFile(aggregate_zip, "w", ZIP_DEFLATED) as aggregate:
        for package in packages:
            with ZipFile(package.zip_path, "r") as app_zip:
                for entry in sorted(app_zip.infolist(), key=lambda item: item.filename):
                    if entry.is_dir():
                        continue
                    content = app_zip.read(entry.filename)
                    digest = hashlib.sha256(content).hexdigest()
                    existing_digest = seen.get(entry.filename)
                    if existing_digest == digest:
                        continue
                    if existing_digest is not None and existing_digest != digest:
                        print(f"Warning: aggregate path collision for {entry.filename}; keeping first copy.")
                        continue
                    seen[entry.filename] = digest
                    aggregate.writestr(entry.filename, content)

    return sha256_file(aggregate_zip)


def profile_manifest_data(
    profile: Profile,
    packages: list[AppPackage],
    aggregate_zip: Path,
    aggregate_sha: str,
    tray_version: int,
) -> dict:
    return {
        "profile": profile.id,
        "displayName": profile.display_name,
        "version": tray_version,
        "runtime": "x64Win",
        "aggregate": {
            "fileName": aggregate_zip.name,
            "sha256": aggregate_sha,
            "source": profile.build_source,
        },
        "apps": [
            {
                "appId": package.app.name,
                "version": package.version,
                "fileName": package.zip_path.name,
                "sha256": sha256_file(package.zip_path),
                "source": package.source,
            }
            for package in packages
        ],
    }


def build_profile(args: argparse.Namespace) -> int:
    profile = PROFILES[args.profile]
    output_root = Path(args.output_root)
    profile_root = output_root / profile.id

    if profile_root.exists():
        shutil.rmtree(profile_root)
    profile_root.mkdir(parents=True, exist_ok=True)

    packages = selected_packages(args.repo, output_root, args.force_rebuild, profile)
    tray_version = int(args.tray_version) if args.tray_version.strip() else max(package.version for package in packages)

    package_dir = profile_root / "packages"
    aggregate_zip = package_dir / aggregate_asset_name(tray_version, profile)
    aggregate_sha = create_flat_aggregate(packages, aggregate_zip)

    manifest_path = package_dir / f"profile-{profile.id}.json"
    manifest_path.write_text(
        json.dumps(profile_manifest_data(profile, packages, aggregate_zip, aggregate_sha, tray_version), indent=2) + "\n",
        encoding="utf-8",
    )

    print(f"Built {profile.display_name} package set:")
    for package in packages:
        print(f"- {package.zip_path}")
    print(f"- {aggregate_zip}")
    print(f"- {manifest_path}")
    return 0


def load_profile_manifests(input_root: Path) -> list[tuple[Path, dict]]:
    manifests: list[tuple[Path, dict]] = []
    for path in sorted(input_root.rglob("profile-*.json")):
        manifests.append((path.parent, json.loads(path.read_text(encoding="utf-8"))))

    if not manifests:
        raise SystemExit(f"No profile manifests found under {input_root}")

    profile_ids = {manifest["profile"] for _root, manifest in manifests}
    missing = set(PROFILES) - profile_ids
    if missing:
        raise SystemExit(f"Missing profile manifest(s): {', '.join(sorted(missing))}")

    return manifests


def profile_asset_paths(manifest_root: Path, manifest: dict) -> list[Path]:
    paths = [manifest_root / app["fileName"] for app in manifest["apps"]]
    paths.append(manifest_root / manifest["aggregate"]["fileName"])
    missing = [str(path) for path in paths if not path.exists()]
    if missing:
        raise SystemExit("Missing built asset(s):\n" + "\n".join(missing))
    return paths


def artifact_rows(manifests: list[tuple[Path, dict]]) -> list[dict]:
    rows: list[dict] = []
    for _root, manifest in sorted(manifests, key=lambda item: item[1]["profile"]):
        rows.append(
            {
                "profile": manifest["displayName"],
                "appId": "TrayAppDotNET",
                "version": manifest["version"],
                "fileName": manifest["aggregate"]["fileName"],
                "sha256": manifest["aggregate"]["sha256"],
                "source": manifest["aggregate"]["source"],
                "kind": "aggregate",
            }
        )
        for app in manifest["apps"]:
            rows.append(
                {
                    "profile": manifest["displayName"],
                    "appId": app["appId"],
                    "version": app["version"],
                    "fileName": app["fileName"],
                    "sha256": app["sha256"],
                    "source": app["source"],
                    "kind": "app",
                }
            )
    return rows


def artifact_table(rows: list[dict]) -> list[str]:
    lines = [
        "| Profile | Kind | App | Version | Asset | Source |",
        "| --- | --- | --- | ---: | --- | --- |",
    ]
    for row in rows:
        lines.append(
            f"| {row['profile']} | {row['kind']} | {row['appId']} | {row['version']} | "
            f"`{row['fileName']}` | {row['source']} |"
        )
    return lines


def ensure_release(repo: str, tag: str, title: str, target: str, notes_path: Path) -> None:
    exists = run(["gh", "release", "view", tag, "--repo", repo], check=False)
    if exists.returncode == 0:
        print(f"Release already exists for {tag}; notes will be updated and assets uploaded with --clobber.")
        run(["gh", "release", "edit", tag, "--repo", repo, "--draft", "--title", title, "--notes-file", str(notes_path)])
        return

    cmd = [
        "gh",
        "release",
        "create",
        tag,
        "--repo",
        repo,
        "--draft",
        "--title",
        title,
        "--notes-file",
        str(notes_path),
    ]
    if target:
        cmd.extend(["--target", target])
    run(cmd)


def write_notes(path: Path, rows: list[dict]) -> None:
    lines = [
        "# TrayAppDotNET Win11 x64",
        "",
        "This draft contains both build profiles:",
        "",
        "- Release: framework-dependent Release publish from the Linux runners, with app DLLs and dependency DLLs side by side.",
        "- Native AOT: win-x64 Native AOT publish from the Windows runners, with native sidecar DLLs left beside the app executables.",
        "",
        *artifact_table(rows),
        "",
    ]
    path.write_text("\n".join(lines), encoding="utf-8")


def write_summary(rows: list[dict]) -> None:
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return
    lines = [
        "## Publish Win11x64 Artifacts",
        "",
        *artifact_table(rows),
        "",
    ]
    with Path(summary_path).open("a", encoding="utf-8") as summary:
        summary.write("\n".join(lines))


def write_updates_manifest(path: Path, manifests: list[tuple[Path, dict]], rows: list[dict], tray_version: int) -> None:
    by_profile = {manifest["profile"]: manifest for _root, manifest in manifests}
    release_profile = by_profile["release"]
    data = {
        "version": tray_version,
        "runtime": "x64Win",
        "aggregate": release_profile["aggregate"],
        "apps": release_profile["apps"],
        "profiles": by_profile,
    }
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")

    artifact_list = {
        "version": tray_version,
        "runtime": "x64Win",
        "artifacts": rows,
    }
    (path.parent / "artifact-list.json").write_text(json.dumps(artifact_list, indent=2) + "\n", encoding="utf-8")


def publish_release(args: argparse.Namespace) -> int:
    input_root = Path(args.input_root)
    manifests = load_profile_manifests(input_root)
    tray_versions = {manifest["version"] for _root, manifest in manifests}
    tray_version = int(args.tray_version) if args.tray_version.strip() else max(tray_versions)
    release_tag = args.release_tag.strip() or f"TrayAppDotNET_{tray_version}_x64Win"

    rows = artifact_rows(manifests)
    final_dir = input_root / "_release"
    final_dir.mkdir(parents=True, exist_ok=True)

    notes_path = final_dir / "release-notes.md"
    updates_path = final_dir / "updates.json"
    artifact_list_path = final_dir / "artifact-list.json"
    write_notes(notes_path, rows)
    write_updates_manifest(updates_path, manifests, rows, tray_version)
    write_summary(rows)

    ensure_release(
        args.repo,
        release_tag,
        f"TrayAppDotNET {tray_version} x64Win",
        args.target,
        notes_path,
    )

    upload_assets: list[Path] = []
    for manifest_root, manifest in manifests:
        upload_assets.extend(profile_asset_paths(manifest_root, manifest))
    upload_assets.extend([updates_path, artifact_list_path])

    run(["gh", "release", "upload", release_tag, *[str(path) for path in upload_assets], "--repo", args.repo, "--clobber"])

    print("Published draft release assets:")
    for row in rows:
        print(f"- {row['profile']}: {row['fileName']}")
    print(f"- {updates_path.name}")
    print(f"- {artifact_list_path.name}")
    return 0


def main() -> int:
    args = parse_args()
    if args.phase == "build-profile":
        return build_profile(args)
    return publish_release(args)


if __name__ == "__main__":
    raise SystemExit(main())

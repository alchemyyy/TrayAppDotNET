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
import tempfile
from dataclasses import dataclass
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


@dataclass(frozen=True)
class App:
    name: str
    project: str
    buildnumber: str


APPS = [
    App("BatteryTrayAppDotNET", "BatteryTrayAppDotNET/src/BatteryTrayAppDotNET.csproj", "BatteryTrayAppDotNET/buildnumber.txt"),
    App("BrightnessTrayAppDotNET", "BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj", "BrightnessTrayAppDotNET/buildnumber.txt"),
    App("FanControlTrayAppDotNET", "FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj", "FanControlTrayAppDotNET/buildnumber.txt"),
    App("NetworkTrayAppDotNET", "NetworkTrayAppDotNET/src/NetworkTrayAppDotNET.csproj", "NetworkTrayAppDotNET/buildnumber.txt"),
    App("VolumeTrayAppDotNET", "VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj", "VolumeTrayAppDotNET/buildnumber.txt"),
]


@dataclass
class AppPackage:
    app: App
    version: int
    zip_path: Path
    source: str


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
    parser = argparse.ArgumentParser(description="Publish TrayAppDotNET Win11 x64 release assets.")
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", "alchemyyy/TrayAppDotNET"))
    parser.add_argument("--tray-version", default="", help="Aggregate TrayAppDotNET version. Defaults to max selected app version.")
    parser.add_argument("--release-tag", default="", help="Release tag. Defaults to TrayAppDotNET_<version>_x64Win.")
    parser.add_argument("--target", default=os.environ.get("GITHUB_SHA", ""), help="Target commit for a new release.")
    parser.add_argument("--output-root", default=".artifacts/publish-win11x64")
    parser.add_argument("--force-rebuild", action="store_true", help="Build every app even if the latest release has an equal/newer app zip.")
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


def latest_app_assets(release: dict | None) -> dict[str, tuple[int, str]]:
    if not release:
        return {}

    found: dict[str, tuple[int, str]] = {}
    patterns = {
        app.name: re.compile(rf"^{re.escape(app.name)}_(\d+)_x64Win\.zip$")
        for app in APPS
    }

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


def build_app(app: App, version: int, output_root: Path) -> Path:
    publish_dir = output_root / "publish" / app.name
    package_dir = output_root / "packages"
    zip_path = package_dir / f"{app.name}_{version}_x64Win.zip"

    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)

    run(["dotnet", "restore", app.project, "-p:EnableWindowsTargeting=true"])
    run(
        [
            "dotnet",
            "publish",
            app.project,
            "--configuration",
            "Release",
            "--runtime",
            "win-x64",
            "--self-contained",
            "false",
            "--output",
            str(publish_dir),
            "-p:PublishAot=false",
            "-p:SelfContained=false",
            "-p:EnableWindowsTargeting=true",
            "-p:SkipPublishAfterBuild=true",
            "-p:SkipKillRunningInstance=true",
            "-p:ContinuousIntegrationBuild=true",
        ]
    )

    zip_directory(publish_dir, zip_path)
    return zip_path


def selected_packages(repo: str, output_root: Path, force_rebuild: bool) -> list[AppPackage]:
    release = latest_release(repo)
    latest_tag = release.get("tag_name") if release else ""
    latest_assets = latest_app_assets(release)

    packages: list[AppPackage] = []
    reuse_dir = output_root / "reused"

    for app in APPS:
        current_version = read_buildnumber(Path(app.buildnumber))
        latest = latest_assets.get(app.name)

        if not force_rebuild and latest and current_version <= latest[0]:
            latest_version, latest_asset = latest
            print(f"{app.name}: current {current_version} is not greater than latest {latest_version}; reusing {latest_asset}.")
            zip_path = download_latest_asset(repo, latest_tag, latest_asset, reuse_dir)
            packages.append(AppPackage(app, latest_version, zip_path, "reused"))
            continue

        if latest:
            print(f"{app.name}: current {current_version} is greater than latest {latest[0]}; building.")
        else:
            print(f"{app.name}: no latest app asset found; building current {current_version}.")
        zip_path = build_app(app, current_version, output_root)
        packages.append(AppPackage(app, current_version, zip_path, "built"))

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


def ensure_release(repo: str, tag: str, title: str, target: str, notes_path: Path) -> None:
    exists = run(["gh", "release", "view", tag, "--repo", repo], check=False)
    if exists.returncode == 0:
        print(f"Release already exists for {tag}; assets will be uploaded with --clobber.")
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


def write_notes(path: Path, packages: list[AppPackage], aggregate_name: str) -> None:
    lines = [
        "# TrayAppDotNET Win11 x64",
        "",
        f"Aggregate asset: `{aggregate_name}`",
        "",
        "| App | Version | Source |",
        "| --- | ---: | --- |",
    ]
    for package in packages:
        lines.append(f"| {package.app.name} | {package.version} | {package.source} |")
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def write_manifest(path: Path, packages: list[AppPackage], aggregate_zip: Path, aggregate_sha: str, tray_version: int) -> None:
    data = {
        "version": tray_version,
        "runtime": "x64Win",
        "aggregate": {
            "fileName": aggregate_zip.name,
            "sha256": aggregate_sha,
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
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    output_root = Path(args.output_root)

    if output_root.exists():
        shutil.rmtree(output_root)
    output_root.mkdir(parents=True, exist_ok=True)

    packages = selected_packages(args.repo, output_root, args.force_rebuild)
    tray_version = int(args.tray_version) if args.tray_version.strip() else max(package.version for package in packages)
    release_tag = args.release_tag.strip() or f"TrayAppDotNET_{tray_version}_x64Win"

    package_dir = output_root / "packages"
    aggregate_zip = package_dir / f"TrayAppDotNET_{tray_version}_x64Win.zip"
    aggregate_sha = create_flat_aggregate(packages, aggregate_zip)

    notes_path = output_root / "release-notes.md"
    manifest_path = package_dir / "updates.json"
    write_notes(notes_path, packages, aggregate_zip.name)
    write_manifest(manifest_path, packages, aggregate_zip, aggregate_sha, tray_version)

    ensure_release(
        args.repo,
        release_tag,
        f"TrayAppDotNET {tray_version} x64Win",
        args.target,
        notes_path,
    )

    upload_assets = [str(package.zip_path) for package in packages]
    upload_assets.append(str(aggregate_zip))
    upload_assets.append(str(manifest_path))
    run(["gh", "release", "upload", release_tag, *upload_assets, "--repo", args.repo, "--clobber"])

    print("Published draft release assets:")
    for asset in upload_assets:
        print(f"- {asset}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

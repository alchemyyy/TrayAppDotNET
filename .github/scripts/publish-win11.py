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
    symbols_zip_path: Path | None = None


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
    parser = argparse.ArgumentParser(description="Build and publish TrayAppDotNET Win11 release assets.")
    parser.add_argument(
        "--phase",
        choices=["build-profile", "build-app-profile", "publish-release"],
        required=True,
        help="Build one profile package set, build one app profile package, or publish a release from built packages.",
    )
    parser.add_argument("--profile", choices=sorted(PROFILES), default="release", help="Profile to build in build-profile phase.")
    parser.add_argument("--app-name", default="", help="App name to build in build-app-profile phase.")
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", "alchemyyy/TrayAppDotNET"))
    parser.add_argument(
        "--tray-version",
        default="",
        help="Aggregate TrayAppDotNET version. Defaults to latest published TrayAppDotNET release + 1, with a floor of 100.",
    )
    parser.add_argument("--release-tag", default="", help="Release tag. Defaults to TrayAppDotNET_<version>.")
    parser.add_argument("--target", default=os.environ.get("GITHUB_SHA", ""), help="Target commit for a new release.")
    parser.add_argument("--output-root", default=".artifacts/publish-win11")
    parser.add_argument("--input-root", default=".artifacts/publish-win11/collected")
    parser.add_argument("--force-rebuild", action="store_true", help="Build every app even if latest release has an equal/newer app zip.")
    return parser.parse_args()


def read_buildnumber(path: Path) -> int:
    value = path.read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"\d+", value):
        raise SystemExit(f"Expected numeric build number in {path}, got: {value!r}")
    return int(value)


def latest_release(repo: str, *, missing_message: str = "No latest published release found; all apps will be built.") -> dict | None:
    result = run(["gh", "api", f"repos/{repo}/releases/latest"], capture=True, check=False)
    if result.returncode != 0:
        if missing_message:
            print(missing_message)
        return None
    return json.loads(result.stdout)


def release_version(release: dict | None) -> int | None:
    if not release:
        return None

    candidates: list[int] = []
    for value in (release.get("tag_name", ""), release.get("name", "")):
        match = re.search(r"\bTrayAppDotNET_(\d+)\b", str(value))
        if match:
            candidates.append(int(match.group(1)))

    aggregate_pattern = re.compile(r"^TrayAppDotNET_(\d+)(?:_NativeAOT)?(?:_Symbols)?\.zip$")
    for asset in release.get("assets", []):
        match = aggregate_pattern.fullmatch(str(asset.get("name", "")))
        if match:
            candidates.append(int(match.group(1)))

    return max(candidates) if candidates else None


def default_tray_version(repo: str) -> int:
    latest = latest_release(repo, missing_message="No latest published release found; defaulting TrayAppDotNET version to 100.")
    latest_version = release_version(latest)
    if latest_version is None:
        return 100
    return max(latest_version + 1, 100)


def app_asset_name(app: App, version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"{app.name}_{version}.zip"
    token = profile_asset_token(profile)
    return f"{app.name}_{version}_{token}.zip"


def app_symbols_asset_name(app: App, version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"{app.name}_{version}_Symbols.zip"
    token = profile_asset_token(profile)
    return f"{app.name}_{version}_{token}_Symbols.zip"


def aggregate_asset_name(tray_version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"TrayAppDotNET_{tray_version}.zip"
    token = profile_asset_token(profile)
    return f"TrayAppDotNET_{tray_version}_{token}.zip"


def aggregate_symbols_asset_name(tray_version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"TrayAppDotNET_{tray_version}_Symbols.zip"
    token = profile_asset_token(profile)
    return f"TrayAppDotNET_{tray_version}_{token}_Symbols.zip"


def profile_asset_token(profile: Profile) -> str:
    if profile.id == "native-aot":
        return "NativeAOT"
    return profile.id.replace("-", "").title()


def app_asset_pattern(app: App, profile: Profile) -> re.Pattern[str]:
    if profile.legacy_names:
        return re.compile(rf"^{re.escape(app.name)}_(\d+)\.zip$")
    token = profile_asset_token(profile)
    return re.compile(rf"^{re.escape(app.name)}_(\d+)_{re.escape(token)}\.zip$")


def app_symbols_asset_pattern(app: App, profile: Profile) -> re.Pattern[str]:
    if profile.legacy_names:
        return re.compile(rf"^{re.escape(app.name)}_(\d+)_Symbols\.zip$")
    token = profile_asset_token(profile)
    return re.compile(rf"^{re.escape(app.name)}_(\d+)_{re.escape(token)}_Symbols\.zip$")


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


def latest_app_symbols_assets(release: dict | None, profile: Profile) -> dict[tuple[str, int], str]:
    if not release:
        return {}

    found: dict[tuple[str, int], str] = {}
    patterns = {app.name: app_symbols_asset_pattern(app, profile) for app in APPS}

    for asset in release.get("assets", []):
        name = asset.get("name", "")
        for app_name, pattern in patterns.items():
            match = pattern.fullmatch(name)
            if match:
                found[(app_name, int(match.group(1)))] = name

    return found


def is_pdb_name(name: str) -> bool:
    return name.lower().endswith(".pdb")


def zip_directory(source_dir: Path, zip_path: Path, *, pdbs: bool | None = None) -> Path | None:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    files = []
    for path in sorted(source_dir.rglob("*")):
        if not path.is_file():
            continue
        is_pdb = path.suffix.lower() == ".pdb"
        if pdbs is True and not is_pdb:
            continue
        if pdbs is False and is_pdb:
            continue
        files.append(path)

    if not files:
        return None

    with ZipFile(zip_path, "w", ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.relative_to(source_dir).as_posix())

    return zip_path


def split_runtime_and_symbols_zip(source_zip: Path, runtime_zip: Path, symbols_zip: Path) -> Path | None:
    runtime_zip.parent.mkdir(parents=True, exist_ok=True)
    symbols_zip.parent.mkdir(parents=True, exist_ok=True)
    if runtime_zip.exists():
        runtime_zip.unlink()
    if symbols_zip.exists():
        symbols_zip.unlink()

    runtime_count = 0
    symbols_count = 0
    with ZipFile(source_zip, "r") as source, ZipFile(runtime_zip, "w", ZIP_DEFLATED) as runtime:
        symbols: ZipFile | None = None
        try:
            for entry in sorted(source.infolist(), key=lambda item: item.filename):
                if entry.is_dir():
                    continue
                content = source.read(entry.filename)
                if is_pdb_name(entry.filename):
                    if symbols is None:
                        symbols = ZipFile(symbols_zip, "w", ZIP_DEFLATED)
                    symbols.writestr(entry.filename, content)
                    symbols_count += 1
                    continue
                runtime.writestr(entry.filename, content)
                runtime_count += 1
        finally:
            if symbols is not None:
                symbols.close()

    if runtime_count == 0:
        raise SystemExit(f"{source_zip} did not contain any runtime files after removing PDBs.")
    if symbols_count == 0:
        if symbols_zip.exists():
            symbols_zip.unlink()
        return None
    return symbols_zip


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
        "-m:1",
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


def build_app(app: App, version: int, output_root: Path, profile: Profile) -> AppPackage:
    publish_dir = output_root / profile.id / "publish" / app.name
    package_dir = output_root / profile.id / "packages"
    zip_path = package_dir / app_asset_name(app, version, profile)
    symbols_zip_path = package_dir / app_symbols_asset_name(app, version, profile)

    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)

    run(["dotnet", "restore", app.project, "--disable-parallel", "-p:EnableWindowsTargeting=true"])
    run(publish_command(app, publish_dir, profile))
    validate_publish_dir(app, publish_dir, profile)
    if zip_directory(publish_dir, zip_path, pdbs=False) is None:
        raise SystemExit(f"{app.name} {profile.display_name} publish produced no runtime files.")
    symbols_zip = zip_directory(publish_dir, symbols_zip_path, pdbs=True)
    return AppPackage(app, profile, version, zip_path, profile.build_source, symbols_zip)


def reuse_app_package(
    repo: str,
    latest_tag: str,
    latest_asset: str,
    latest_symbols_asset: str | None,
    app: App,
    version: int,
    output_root: Path,
    profile: Profile,
) -> AppPackage:
    package_dir = output_root / profile.id / "packages"
    download_dir = output_root / profile.id / "downloaded" / app.name
    downloaded_zip = download_latest_asset(repo, latest_tag, latest_asset, download_dir)

    runtime_zip = package_dir / app_asset_name(app, version, profile)
    symbols_zip = package_dir / app_symbols_asset_name(app, version, profile)
    extracted_symbols_zip = split_runtime_and_symbols_zip(downloaded_zip, runtime_zip, symbols_zip)
    symbols_zip_path = extracted_symbols_zip

    if latest_symbols_asset:
        symbols_zip_path = download_latest_asset(repo, latest_tag, latest_symbols_asset, package_dir)

    return AppPackage(app, profile, version, runtime_zip, "reused", symbols_zip_path)


def selected_packages(repo: str, output_root: Path, force_rebuild: bool, profile: Profile) -> list[AppPackage]:
    release = latest_release(repo)
    latest_tag = release.get("tag_name") if release else ""
    latest_assets = latest_app_assets(release, profile)
    latest_symbols_assets = latest_app_symbols_assets(release, profile)

    packages: list[AppPackage] = []

    for app in APPS:
        current_version = read_buildnumber(Path(app.buildnumber))
        latest = latest_assets.get(app.name)

        if not force_rebuild and latest and current_version <= latest[0]:
            latest_version, latest_asset = latest
            print(
                f"{app.name} {profile.display_name}: current {current_version} is not greater than "
                f"latest {latest_version}; reusing {latest_asset}."
            )
            packages.append(
                reuse_app_package(
                    repo,
                    latest_tag,
                    latest_asset,
                    latest_symbols_assets.get((app.name, latest_version)),
                    app,
                    latest_version,
                    output_root,
                    profile,
                )
            )
            continue

        if latest:
            print(f"{app.name} {profile.display_name}: current {current_version} is greater than latest {latest[0]}; building.")
        else:
            print(f"{app.name} {profile.display_name}: no latest app asset found; building current {current_version}.")
        packages.append(build_app(app, current_version, output_root, profile))

    return packages


def app_by_name(app_name: str) -> App:
    for app in APPS:
        if app.name == app_name:
            return app
    valid = ", ".join(app.name for app in APPS)
    raise SystemExit(f"Unknown app name: {app_name!r}. Expected one of: {valid}")


def selected_app_package(repo: str, output_root: Path, force_rebuild: bool, profile: Profile, app: App) -> AppPackage:
    release = latest_release(repo)
    latest_tag = release.get("tag_name") if release else ""
    latest_assets = latest_app_assets(release, profile)
    latest_symbols_assets = latest_app_symbols_assets(release, profile)

    current_version = read_buildnumber(Path(app.buildnumber))
    latest = latest_assets.get(app.name)

    if not force_rebuild and latest and current_version <= latest[0]:
        latest_version, latest_asset = latest
        print(
            f"{app.name} {profile.display_name}: current {current_version} is not greater than "
            f"latest {latest_version}; reusing {latest_asset}."
        )
        return reuse_app_package(
            repo,
            latest_tag,
            latest_asset,
            latest_symbols_assets.get((app.name, latest_version)),
            app,
            latest_version,
            output_root,
            profile,
        )

    if latest:
        print(f"{app.name} {profile.display_name}: current {current_version} is greater than latest {latest[0]}; building.")
    else:
        print(f"{app.name} {profile.display_name}: no latest app asset found; building current {current_version}.")
    return build_app(app, current_version, output_root, profile)


def app_manifest_data(package: AppPackage) -> dict:
    app_data = {
        "appId": package.app.name,
        "version": package.version,
        "fileName": package.zip_path.name,
        "sha256": sha256_file(package.zip_path),
        "source": package.source,
    }
    if package.symbols_zip_path:
        app_data["symbols"] = {
            "fileName": package.symbols_zip_path.name,
            "sha256": sha256_file(package.symbols_zip_path),
            "source": package.source,
        }

    return {
        "profile": package.profile.id,
        "displayName": package.profile.display_name,
        "runtime": "win-x64",
        "app": app_data,
    }


def build_app_profile(args: argparse.Namespace) -> int:
    if not args.app_name.strip():
        raise SystemExit("--app-name is required for build-app-profile phase.")

    profile = PROFILES[args.profile]
    app = app_by_name(args.app_name.strip())
    output_root = Path(args.output_root)
    package_dir = output_root / profile.id / "packages"
    package_dir.mkdir(parents=True, exist_ok=True)

    package = selected_app_package(args.repo, output_root, args.force_rebuild, profile, app)
    manifest_path = package_dir / f"app-{profile.id}-{app.name}.json"
    manifest_path.write_text(json.dumps(app_manifest_data(package), indent=2) + "\n", encoding="utf-8")

    print(f"Built {profile.display_name} package:")
    print(f"- {package.zip_path}")
    if package.symbols_zip_path:
        print(f"- {package.symbols_zip_path}")
    print(f"- {manifest_path}")
    return 0


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
                    if is_pdb_name(entry.filename):
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


def create_symbols_aggregate(sources: list[tuple[str, Path]], aggregate_zip: Path) -> str | None:
    if not sources:
        if aggregate_zip.exists():
            aggregate_zip.unlink()
        return None

    aggregate_zip.parent.mkdir(parents=True, exist_ok=True)
    if aggregate_zip.exists():
        aggregate_zip.unlink()

    count = 0
    with ZipFile(aggregate_zip, "w", ZIP_DEFLATED) as aggregate:
        for app_id, zip_path in sorted(sources, key=lambda item: item[0]):
            with ZipFile(zip_path, "r") as symbol_zip:
                for entry in sorted(symbol_zip.infolist(), key=lambda item: item.filename):
                    if entry.is_dir() or not is_pdb_name(entry.filename):
                        continue
                    aggregate.writestr(f"{app_id}/{entry.filename}", symbol_zip.read(entry.filename))
                    count += 1

    if count == 0:
        aggregate_zip.unlink()
        return None
    return sha256_file(aggregate_zip)


def profile_manifest_data(
    profile: Profile,
    packages: list[AppPackage],
    aggregate_zip: Path,
    aggregate_sha: str,
    aggregate_symbols_zip: Path | None,
    aggregate_symbols_sha: str | None,
    tray_version: int,
) -> dict:
    manifest = {
        "profile": profile.id,
        "displayName": profile.display_name,
        "version": tray_version,
        "runtime": "win-x64",
        "aggregate": {
            "fileName": aggregate_zip.name,
            "sha256": aggregate_sha,
            "source": profile.build_source,
        },
        "apps": [],
    }

    if aggregate_symbols_zip and aggregate_symbols_sha:
        manifest["aggregateSymbols"] = {
            "fileName": aggregate_symbols_zip.name,
            "sha256": aggregate_symbols_sha,
            "source": profile.build_source,
        }

    for package in packages:
        app_data = {
            "appId": package.app.name,
            "version": package.version,
            "fileName": package.zip_path.name,
            "sha256": sha256_file(package.zip_path),
            "source": package.source,
        }
        if package.symbols_zip_path:
            app_data["symbols"] = {
                "fileName": package.symbols_zip_path.name,
                "sha256": sha256_file(package.symbols_zip_path),
                "source": package.source,
            }
        manifest["apps"].append(app_data)

    return manifest


def build_profile(args: argparse.Namespace) -> int:
    profile = PROFILES[args.profile]
    output_root = Path(args.output_root)
    profile_root = output_root / profile.id

    if profile_root.exists():
        shutil.rmtree(profile_root)
    profile_root.mkdir(parents=True, exist_ok=True)

    packages = selected_packages(args.repo, output_root, args.force_rebuild, profile)
    tray_version = int(args.tray_version) if args.tray_version.strip() else default_tray_version(args.repo)

    package_dir = profile_root / "packages"
    aggregate_zip = package_dir / aggregate_asset_name(tray_version, profile)
    aggregate_sha = create_flat_aggregate(packages, aggregate_zip)
    aggregate_symbols_zip = package_dir / aggregate_symbols_asset_name(tray_version, profile)
    aggregate_symbols_sha = create_symbols_aggregate(
        [(package.app.name, package.symbols_zip_path) for package in packages if package.symbols_zip_path],
        aggregate_symbols_zip,
    )

    manifest_path = package_dir / f"profile-{profile.id}.json"
    manifest_path.write_text(
        json.dumps(
            profile_manifest_data(
                profile,
                packages,
                aggregate_zip,
                aggregate_sha,
                aggregate_symbols_zip if aggregate_symbols_sha else None,
                aggregate_symbols_sha,
                tray_version,
            ),
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )

    print(f"Built {profile.display_name} package set:")
    for package in packages:
        print(f"- {package.zip_path}")
        if package.symbols_zip_path:
            print(f"- {package.symbols_zip_path}")
    print(f"- {aggregate_zip}")
    if aggregate_symbols_sha:
        print(f"- {aggregate_symbols_zip}")
    print(f"- {manifest_path}")
    return 0


def create_flat_aggregate_from_zips(zip_paths: list[Path], aggregate_zip: Path) -> str:
    aggregate_zip.parent.mkdir(parents=True, exist_ok=True)
    if aggregate_zip.exists():
        aggregate_zip.unlink()

    seen: dict[str, str] = {}
    with ZipFile(aggregate_zip, "w", ZIP_DEFLATED) as aggregate:
        for zip_path in zip_paths:
            with ZipFile(zip_path, "r") as app_zip:
                for entry in sorted(app_zip.infolist(), key=lambda item: item.filename):
                    if entry.is_dir():
                        continue
                    if is_pdb_name(entry.filename):
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


def load_collected_profiles(input_root: Path) -> dict[str, dict]:
    groups: dict[str, dict] = {}

    def add_app(profile_id: str, display_name: str, app_data: dict, root: Path) -> None:
        if profile_id not in PROFILES:
            raise SystemExit(f"Unknown profile in manifest: {profile_id}")

        zip_path = root / app_data["fileName"]
        if not zip_path.exists():
            raise SystemExit(f"Missing built asset for {app_data['appId']}: {zip_path}")

        symbols_path = None
        symbols = app_data.get("symbols")
        if symbols:
            symbols_path = root / symbols["fileName"]
            if not symbols_path.exists():
                raise SystemExit(f"Missing built symbols asset for {app_data['appId']}: {symbols_path}")

        group = groups.setdefault(
            profile_id,
            {
                "profile": profile_id,
                "displayName": display_name,
                "runtime": "win-x64",
                "apps": [],
            },
        )
        app_copy = dict(app_data)
        app_copy["zipPath"] = zip_path
        if symbols_path:
            app_copy["symbolsZipPath"] = symbols_path
        group["apps"].append(app_copy)

    app_manifests = sorted(input_root.rglob("app-*.json"))
    if app_manifests:
        for path in app_manifests:
            manifest = json.loads(path.read_text(encoding="utf-8"))
            add_app(manifest["profile"], manifest["displayName"], manifest["app"], path.parent)
    else:
        for path in sorted(input_root.rglob("profile-*.json")):
            manifest = json.loads(path.read_text(encoding="utf-8"))
            for app_data in manifest["apps"]:
                add_app(manifest["profile"], manifest["displayName"], app_data, path.parent)

    if not groups:
        raise SystemExit(f"No app or profile manifests found under {input_root}")

    missing_profiles = set(PROFILES) - set(groups)
    if missing_profiles:
        raise SystemExit(f"Missing profile manifest(s): {', '.join(sorted(missing_profiles))}")

    expected_apps = {app.name for app in APPS}
    for profile_id, group in groups.items():
        found_apps = {app["appId"] for app in group["apps"]}
        missing_apps = expected_apps - found_apps
        if missing_apps:
            raise SystemExit(f"{profile_id} is missing app package(s): {', '.join(sorted(missing_apps))}")
        group["apps"].sort(key=lambda item: item["appId"])

    return groups


def profile_manifest_from_group(
    group: dict,
    aggregate_zip: Path,
    aggregate_sha: str,
    aggregate_symbols_zip: Path | None,
    aggregate_symbols_sha: str | None,
    tray_version: int,
) -> dict:
    profile = PROFILES[group["profile"]]
    apps = []
    for app_data in group["apps"]:
        app_entry = {
            "appId": app_data["appId"],
            "version": app_data["version"],
            "source": app_data["source"],
        }
        if profile.id == "native-aot":
            app_entry["fileName"] = app_data["fileName"]
            app_entry["sha256"] = app_data["sha256"]
        apps.append(app_entry)

    manifest = {
        "profile": profile.id,
        "displayName": profile.display_name,
        "version": tray_version,
        "runtime": "win-x64",
        "aggregate": {
            "fileName": aggregate_zip.name,
            "sha256": aggregate_sha,
            "source": profile.build_source,
        },
        "apps": apps,
    }
    if aggregate_symbols_zip and aggregate_symbols_sha:
        manifest["aggregateSymbols"] = {
            "fileName": aggregate_symbols_zip.name,
            "sha256": aggregate_symbols_sha,
            "source": profile.build_source,
        }
    return manifest


def artifact_rows(manifests: list[dict]) -> list[dict]:
    rows: list[dict] = []
    for manifest in sorted(manifests, key=lambda item: item["profile"]):
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
        if "aggregateSymbols" in manifest:
            rows.append(
                {
                    "profile": manifest["displayName"],
                    "appId": "TrayAppDotNET",
                    "version": manifest["version"],
                    "fileName": manifest["aggregateSymbols"]["fileName"],
                    "sha256": manifest["aggregateSymbols"]["sha256"],
                    "source": manifest["aggregateSymbols"]["source"],
                    "kind": "symbols",
                }
            )
        if manifest["profile"] == "native-aot":
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


def prune_release_assets(repo: str, tag: str, keep_names: set[str]) -> None:
    result = run(
        ["gh", "release", "view", tag, "--repo", repo, "--json", "assets"],
        capture=True,
        check=False,
    )
    if result.returncode != 0:
        return

    release = json.loads(result.stdout)
    for asset in release.get("assets", []):
        asset_name = str(asset.get("name", ""))
        if asset_name and asset_name not in keep_names:
            run(["gh", "release", "delete-asset", tag, asset_name, "--repo", repo, "--yes"])


def write_notes(path: Path, rows: list[dict]) -> None:
    lines = [
        "# TrayAppDotNET Win11",
        "",
        "This draft contains both build profiles:",
        "",
        "- Release: framework-dependent Release publish from the Linux runners, with app DLLs and dependency DLLs side by side.",
        "- Native AOT: win-x64 Native AOT publish from the Windows runners, with native sidecar DLLs left beside the app executables.",
        "- Symbols: PDB files are excluded from app zips and attached in separate aggregate Symbols zips.",
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
        "## Publish Win11 Artifacts",
        "",
        *artifact_table(rows),
        "",
    ]
    with Path(summary_path).open("a", encoding="utf-8") as summary:
        summary.write("\n".join(lines))


def write_updates_manifest(path: Path, manifests: list[dict], rows: list[dict], tray_version: int) -> None:
    by_profile = {manifest["profile"]: manifest for manifest in manifests}
    release_profile = by_profile["release"]
    runtime_apps = []
    for app in release_profile["apps"]:
        app_copy = dict(app)
        app_copy.pop("symbols", None)
        runtime_apps.append(app_copy)

    data = {
        "version": tray_version,
        "runtime": "win-x64",
        "aggregate": release_profile["aggregate"],
        "apps": runtime_apps,
        "profiles": by_profile,
    }
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")

    artifact_list = {
        "version": tray_version,
        "runtime": "win-x64",
        "artifacts": rows,
    }
    (path.parent / "artifact-list.json").write_text(json.dumps(artifact_list, indent=2) + "\n", encoding="utf-8")


def publish_release(args: argparse.Namespace) -> int:
    input_root = Path(args.input_root)
    groups = load_collected_profiles(input_root)
    tray_version = int(args.tray_version) if args.tray_version.strip() else default_tray_version(args.repo)
    release_tag = args.release_tag.strip() or f"TrayAppDotNET_{tray_version}"

    final_dir = input_root / "_release"
    final_dir.mkdir(parents=True, exist_ok=True)

    manifests: list[dict] = []
    upload_assets: list[Path] = []
    for profile_id in sorted(groups):
        group = groups[profile_id]
        profile = PROFILES[profile_id]
        aggregate_zip = final_dir / aggregate_asset_name(tray_version, profile)
        app_zip_paths = [app_data["zipPath"] for app_data in group["apps"]]
        aggregate_sha = create_flat_aggregate_from_zips(app_zip_paths, aggregate_zip)
        symbol_sources = [
            (app_data["appId"], app_data["symbolsZipPath"])
            for app_data in group["apps"]
            if app_data.get("symbolsZipPath")
        ]
        aggregate_symbols_zip = final_dir / aggregate_symbols_asset_name(tray_version, profile)
        aggregate_symbols_sha = create_symbols_aggregate(symbol_sources, aggregate_symbols_zip)
        manifests.append(
            profile_manifest_from_group(
                group,
                aggregate_zip,
                aggregate_sha,
                aggregate_symbols_zip if aggregate_symbols_sha else None,
                aggregate_symbols_sha,
                tray_version,
            )
        )
        upload_assets.append(aggregate_zip)
        if aggregate_symbols_sha:
            upload_assets.append(aggregate_symbols_zip)
        if profile.id == "native-aot":
            upload_assets.extend(app_zip_paths)

    rows = artifact_rows(manifests)

    notes_path = final_dir / "release-notes.md"
    updates_path = final_dir / "updates.json"
    artifact_list_path = final_dir / "artifact-list.json"
    write_notes(notes_path, rows)
    write_updates_manifest(updates_path, manifests, rows, tray_version)
    write_summary(rows)

    ensure_release(
        args.repo,
        release_tag,
        f"TrayAppDotNET {tray_version}",
        args.target,
        notes_path,
    )

    upload_assets.extend([updates_path, artifact_list_path])
    upload_assets = list(dict.fromkeys(upload_assets))
    prune_release_assets(args.repo, release_tag, {path.name for path in upload_assets})

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
    if args.phase == "build-app-profile":
        return build_app_profile(args)
    return publish_release(args)


if __name__ == "__main__":
    raise SystemExit(main())

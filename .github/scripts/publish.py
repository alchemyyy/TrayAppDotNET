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
import uuid
import xml.etree.ElementTree as ET
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


@dataclass(frozen=True)
class CommitEntry:
    full_sha: str
    short_sha: str
    subject: str


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
        build_source="built-windows-native-aot",
        publish_aot=True,
        self_contained=True,
        legacy_names=True,
    ),
}

REPO_ROOT = Path(__file__).resolve().parents[2]
INSTALL_ALL_SCRIPT_PATH = REPO_ROOT / ".github" / "install-all.bat"
INSTALL_ALL_ARCHIVE_NAME = "install-all.bat"
APP_INSTALL_ARCHIVE_NAME = "install.bat"
GENERATOR_PROJECT = "TrayAppDotNETCommon/generators/XmlSourceGenerator/TrayAppDotNETCommon.XmlSourceGenerator.csproj"


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
        choices=[
            "build-profile",
            "build-app-profile",
            "stage-app-profile",
            "collect-staged-profiles",
            "clean-staged-profiles",
            "publish-release",
        ],
        required=True,
        help="Build, stage, collect, clean, or publish TrayAppDotNET release packages.",
    )
    parser.add_argument("--profile", choices=sorted(PROFILES), default="release", help="Profile to build in build-profile phase.")
    parser.add_argument(
        "--profiles",
        default=",".join(PROFILES),
        help="Comma-separated profiles to collect and publish. Defaults to all profiles.",
    )
    parser.add_argument("--app-name", default="", help="App name to build in build-app-profile phase.")
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", "alchemyyy/TrayAppDotNET"))
    parser.add_argument(
        "--tray-version",
        default="",
        help="Aggregate TrayAppDotNET version. Defaults to latest published TrayAppDotNET release + 1, with a floor of 100.",
    )
    parser.add_argument("--release-tag", default="", help="Release tag. Defaults to TrayAppDotNET_<version>.")
    parser.add_argument("--target", default=os.environ.get("GITHUB_SHA", ""), help="Target commit for a new release.")
    parser.add_argument("--output-root", default=".artifacts/publish")
    parser.add_argument("--input-root", default=".artifacts/publish/collected")
    parser.add_argument("--stage-root", default=os.environ.get("TRAYAPP_PUBLISH_STAGE_ROOT", ""))
    parser.add_argument("--run-id", default=os.environ.get("GITHUB_RUN_ID", "local"))
    parser.add_argument("--run-attempt", default=os.environ.get("GITHUB_RUN_ATTEMPT", "1"))
    parser.add_argument("--force-rebuild", action="store_true", help="Build every app even if latest release has an equal/newer app zip.")
    return parser.parse_args()


def parse_profile_ids(value: str) -> list[str]:
    requested = [part.strip() for part in value.split(",") if part.strip()]
    if not requested:
        raise SystemExit("--profiles must include at least one profile")

    unknown = sorted(set(requested) - set(PROFILES))
    if unknown:
        raise SystemExit(f"Unknown profile(s): {', '.join(unknown)}")

    requested_set = set(requested)
    return [profile_id for profile_id in PROFILES if profile_id in requested_set]


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

    aggregate_pattern = re.compile(r"^TrayAppDotNET_(\d+)\.zip$")
    for asset in release.get("assets", []):
        match = aggregate_pattern.fullmatch(str(asset.get("name", "")))
        if match:
            candidates.append(int(match.group(1)))

    return max(candidates) if candidates else None


def release_target_ref(release: dict | None) -> str:
    if not release:
        return ""
    for key in ("target_commitish", "targetCommitish", "tag_name", "tagName"):
        value = str(release.get(key, "")).strip()
        if value:
            return value
    return ""


def release_display_ref(release: dict | None) -> str:
    if not release:
        return "last release"
    for key in ("tag_name", "tagName", "name"):
        value = str(release.get(key, "")).strip()
        if value:
            return value
    return "last release"


def commits_since_release(previous_release: dict | None, target: str) -> list[CommitEntry]:
    target_ref = target.strip() or "HEAD"
    base_ref = release_target_ref(previous_release)
    revision_range = f"{base_ref}..{target_ref}" if base_ref else target_ref
    result = run(
        ["git", "log", "--pretty=format:%H%x09%h%x09%s", revision_range],
        capture=True,
        check=False,
    )
    if result.returncode != 0 and base_ref:
        fallback_ref = str(previous_release.get("tag_name", "") if previous_release else "").strip()
        if fallback_ref and fallback_ref != base_ref:
            revision_range = f"{fallback_ref}..{target_ref}"
            result = run(
                ["git", "log", "--pretty=format:%H%x09%h%x09%s", revision_range],
                capture=True,
                check=False,
            )

    if result.returncode != 0:
        print(f"Warning: could not determine commits for {revision_range}.")
        return []

    commits: list[CommitEntry] = []
    for line in result.stdout.splitlines():
        parts = line.split("\t", 2)
        if len(parts) == 3:
            commits.append(CommitEntry(parts[0], parts[1], parts[2]))
    return commits


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


def aggregate_asset_name(tray_version: int, profile: Profile) -> str:
    if profile.legacy_names:
        return f"TrayAppDotNET_{tray_version}.zip"
    token = profile_asset_token(profile)
    return f"TrayAppDotNET_{tray_version}_{token}.zip"


def profile_asset_token(profile: Profile) -> str:
    return profile.id.replace("-", "").title()


def is_pdb_name(name: str) -> bool:
    return name.lower().endswith(".pdb")


def remove_pdb_files(root: Path) -> int:
    count = 0
    for path in sorted(root.rglob("*")):
        if path.is_file() and path.suffix.lower() == ".pdb":
            path.unlink()
            count += 1
    return count


def remove_native_aot_sidecars(publish_dir: Path) -> None:
    for name in ("Dia2Lib.dll", "TraceReloggerLib.dll"):
        path = publish_dir / name
        if path.exists():
            path.unlink()

    for name in ("amd64", "win-x64"):
        path = publish_dir / name
        if path.exists():
            shutil.rmtree(path)


def app_build_output_dir(app: App) -> Path:
    return (REPO_ROOT / app.project).parent.parent / "bin" / "Release"


def is_root_app_install_name(name: str) -> bool:
    return name.replace("\\", "/").lower() == APP_INSTALL_ARCHIVE_NAME


def write_unique_zip_entry(archive: ZipFile, seen: dict[str, str], name: str, content: bytes) -> None:
    digest = hashlib.sha256(content).hexdigest()
    existing_digest = seen.get(name)
    if existing_digest == digest:
        return
    if existing_digest is not None and existing_digest != digest:
        print(f"Warning: zip path collision for {name}; keeping first copy.")
        return
    seen[name] = digest
    archive.writestr(name, content)


def write_install_all_script(archive: ZipFile, seen: dict[str, str]) -> None:
    if not INSTALL_ALL_SCRIPT_PATH.exists():
        raise SystemExit(f"Missing aggregate installer script: {INSTALL_ALL_SCRIPT_PATH}")
    write_unique_zip_entry(
        archive,
        seen,
        INSTALL_ALL_ARCHIVE_NAME,
        INSTALL_ALL_SCRIPT_PATH.read_bytes(),
    )


def app_install_script_path(app_name: str) -> Path:
    return REPO_ROOT / app_name / APP_INSTALL_ARCHIVE_NAME


def read_app_install_script(app_name: str) -> bytes:
    path = app_install_script_path(app_name)
    if not path.exists():
        raise SystemExit(f"Missing app installer script for {app_name}: {path}")
    return path.read_bytes()


def app_install_entries(app_name: str) -> dict[str, bytes]:
    return {APP_INSTALL_ARCHIVE_NAME: read_app_install_script(app_name)}


def aggregate_app_install_archive_name(app_name: str) -> str:
    return f"install-{app_name}.bat"


def write_aggregate_app_install_script(archive: ZipFile, seen: dict[str, str], app_name: str) -> None:
    write_unique_zip_entry(
        archive,
        seen,
        aggregate_app_install_archive_name(app_name),
        read_app_install_script(app_name),
    )


def zip_directory(
    source_dir: Path,
    zip_path: Path,
    *,
    extra_entries: dict[str, bytes] | None = None,
) -> Path | None:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    files = []
    for path in sorted(source_dir.rglob("*")):
        if not path.is_file():
            continue
        if path.suffix.lower() == ".pdb":
            raise SystemExit(f"Refusing to package PDB file: {path}")
        files.append(path)

    if not files and not extra_entries:
        return None

    seen: dict[str, str] = {}
    with ZipFile(zip_path, "w", ZIP_DEFLATED) as archive:
        for name, content in (extra_entries or {}).items():
            write_unique_zip_entry(archive, seen, name, content)
        for path in files:
            write_unique_zip_entry(archive, seen, path.relative_to(source_dir).as_posix(), path.read_bytes())

    return zip_path


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def publish_command(app: App, publish_dir: Path, profile: Profile) -> list[str]:
    cmd = [
        "dotnet",
        "publish",
        app.project,
        "--no-restore",
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
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:UseAppHost=true",
        "-p:EnableWindowsTargeting=true",
        "-p:SkipPublishAfterBuild=true",
        "-p:SkipKillRunningInstance=true",
        "-p:ContinuousIntegrationBuild=true",
    ]

    return cmd


def generator_restore_command() -> list[str]:
    return ["dotnet", "restore", GENERATOR_PROJECT, "--disable-parallel"]


def restore_command(app: App, profile: Profile) -> list[str]:
    cmd = [
        "dotnet",
        "restore",
        app.project,
        "--runtime",
        "win-x64",
        "--disable-parallel",
        f"-p:SelfContained={str(profile.self_contained).lower()}",
        "-p:EnableWindowsTargeting=true",
    ]

    if not (profile.publish_aot and app.name == "FanControlTrayAppDotNET"):
        cmd.append(f"-p:PublishAot={str(profile.publish_aot).lower()}")

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
            f"{app.name} {profile.display_name} publish did not produce {app_dll.name}. "
            "Refusing to package a single-file or incomplete publish."
        )

    bundled_runtime = [name for name in ("coreclr.dll", "hostfxr.dll", "hostpolicy.dll") if (publish_dir / name).exists()]
    if bundled_runtime and not profile.self_contained:
        joined = ", ".join(bundled_runtime)
        raise SystemExit(
            f"{app.name} Release publish contains bundled runtime files ({joined}). "
            "Refusing to package a self-contained publish."
        )
    if profile.self_contained and not bundled_runtime:
        raise SystemExit(
            f"{app.name} {profile.display_name} publish did not contain bundled runtime files. "
            "Refusing to package a framework-dependent publish as self-contained."
        )


def build_app(app: App, version: int, output_root: Path, profile: Profile) -> AppPackage:
    publish_dir = output_root / profile.id / "publish" / app.name
    package_dir = output_root / profile.id / "packages"
    zip_path = package_dir / app_asset_name(app, version, profile)

    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)

    run(generator_restore_command())
    run(restore_command(app, profile))
    run(publish_command(app, publish_dir, profile))
    if profile.publish_aot:
        remove_native_aot_sidecars(publish_dir)
    removed_publish_pdbs = remove_pdb_files(publish_dir)
    build_output_dir = app_build_output_dir(app)
    removed_build_pdbs = remove_pdb_files(build_output_dir) if build_output_dir.exists() else 0
    removed_pdbs = removed_publish_pdbs + removed_build_pdbs
    if removed_pdbs:
        print(
            f"{app.name} {profile.display_name}: removed {removed_pdbs} PDB file(s) "
            f"({removed_publish_pdbs} publish, {removed_build_pdbs} build output)."
        )
    validate_publish_dir(app, publish_dir, profile)
    if zip_directory(publish_dir, zip_path, extra_entries=app_install_entries(app.name)) is None:
        raise SystemExit(f"{app.name} {profile.display_name} publish produced no runtime files.")
    return AppPackage(app, profile, version, zip_path, profile.build_source)


def selected_packages(repo: str, output_root: Path, force_rebuild: bool, profile: Profile) -> list[AppPackage]:
    packages: list[AppPackage] = []
    for app in APPS:
        current_version = read_buildnumber(Path(app.buildnumber))
        print(f"{app.name} {profile.display_name}: building current {current_version}.")
        packages.append(build_app(app, current_version, output_root, profile))
    return packages


def app_by_name(app_name: str) -> App:
    for app in APPS:
        if app.name == app_name:
            return app
    valid = ", ".join(app.name for app in APPS)
    raise SystemExit(f"Unknown app name: {app_name!r}. Expected one of: {valid}")


def selected_app_package(repo: str, output_root: Path, force_rebuild: bool, profile: Profile, app: App) -> AppPackage:
    current_version = read_buildnumber(Path(app.buildnumber))
    print(f"{app.name} {profile.display_name}: building current {current_version}.")
    return build_app(app, current_version, output_root, profile)


def app_manifest_data(package: AppPackage) -> dict:
    app_data = {
        "appId": package.app.name,
        "version": package.version,
        "fileName": package.zip_path.name,
        "sha256": sha256_file(package.zip_path),
        "size": package.zip_path.stat().st_size,
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
    print(f"- {manifest_path}")
    return 0


def stage_root(args: argparse.Namespace) -> Path:
    if not args.stage_root.strip():
        raise SystemExit("--stage-root or TRAYAPP_PUBLISH_STAGE_ROOT is required for staging phases.")
    return Path(args.stage_root)


def safe_stage_token(value: str) -> str:
    token = re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip())
    return token.strip("._") or "unknown"


def staged_run_root(args: argparse.Namespace) -> Path:
    return (
        stage_root(args)
        / safe_stage_token(args.repo)
        / safe_stage_token(str(args.run_id))
        / safe_stage_token(str(args.run_attempt))
    )


def set_github_output(name: str, value: str) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if output_path:
        with Path(output_path).open("a", encoding="utf-8") as output:
            output.write(f"{name}={value}\n")
    print(f"{name}={value}")


def validate_manifest_file_name(file_name: str, manifest_path: Path) -> str:
    if Path(file_name).name != file_name:
        raise SystemExit(f"Manifest {manifest_path} contains unsafe file name: {file_name!r}")
    return file_name


def app_manifest_files(package_dir: Path, profile: Profile, app: App) -> tuple[Path, dict, list[Path]]:
    manifest_path = package_dir / f"app-{profile.id}-{app.name}.json"
    if not manifest_path.exists():
        raise SystemExit(f"Missing app manifest: {manifest_path}")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("profile") != profile.id:
        raise SystemExit(f"Expected profile {profile.id} in {manifest_path}, got {manifest.get('profile')!r}")

    app_data = manifest.get("app", {})
    if app_data.get("appId") != app.name:
        raise SystemExit(f"Expected app {app.name} in {manifest_path}, got {app_data.get('appId')!r}")

    files = [manifest_path]
    file_name = validate_manifest_file_name(str(app_data.get("fileName", "")), manifest_path)
    zip_path = package_dir / file_name
    if not zip_path.exists():
        raise SystemExit(f"Missing staged app zip referenced by {manifest_path}: {zip_path}")
    files.append(zip_path)

    return manifest_path, manifest, files


def stage_marker_data(args: argparse.Namespace, profile: Profile, app: App, manifest: dict) -> dict:
    return {
        "repo": args.repo,
        "runId": str(args.run_id),
        "runAttempt": str(args.run_attempt),
        "profile": profile.id,
        "appId": app.name,
        "manifest": manifest,
    }


def stage_app_profile(args: argparse.Namespace) -> int:
    if not args.app_name.strip():
        raise SystemExit("--app-name is required for stage-app-profile phase.")

    profile = PROFILES[args.profile]
    app = app_by_name(args.app_name.strip())
    package_dir = Path(args.output_root) / profile.id / "packages"
    _manifest_path, manifest, files = app_manifest_files(package_dir, profile, app)

    destination = staged_run_root(args) / profile.id / app.name
    temp_destination = destination.parent / f".{destination.name}.{uuid.uuid4().hex}.tmp"
    if temp_destination.exists():
        shutil.rmtree(temp_destination)
    temp_destination.mkdir(parents=True, exist_ok=True)

    for path in files:
        shutil.copy2(path, temp_destination / path.name)
    (temp_destination / ".stage-complete.json").write_text(
        json.dumps(stage_marker_data(args, profile, app, manifest), indent=2) + "\n",
        encoding="utf-8",
    )

    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        shutil.rmtree(destination)
    temp_destination.replace(destination)

    print(f"Staged {profile.display_name} package for {app.name}: {destination}")
    return 0


def staged_app_ready(source_dir: Path, profile: Profile, app: App) -> tuple[bool, str]:
    marker = source_dir / ".stage-complete.json"
    if not marker.exists():
        return False, f"missing completion marker: {marker}"

    try:
        marker_data = json.loads(marker.read_text(encoding="utf-8"))
        if marker_data.get("profile") != profile.id or marker_data.get("appId") != app.name:
            return False, f"completion marker does not match {profile.id}/{app.name}: {marker}"
        app_manifest_files(source_dir, profile, app)
    except (OSError, json.JSONDecodeError, SystemExit) as exc:
        return False, str(exc)

    return True, ""


def collect_staged_profiles(args: argparse.Namespace) -> int:
    source_root = staged_run_root(args)
    destination_root = Path(args.input_root)
    profile_ids = parse_profile_ids(args.profiles)

    if destination_root.exists():
        shutil.rmtree(destination_root)
    destination_root.mkdir(parents=True, exist_ok=True)

    complete = True
    staged_count = 0
    for profile in (PROFILES[profile_id] for profile_id in profile_ids):
        for app in APPS:
            source_dir = source_root / profile.id / app.name
            ready, reason = staged_app_ready(source_dir, profile, app)
            if not ready:
                complete = False
                print(f"Local stage missing {profile.id}/{app.name}: {reason}")
                continue

            destination_dir = destination_root / profile.id / app.name
            if destination_dir.exists():
                shutil.rmtree(destination_dir)
            shutil.copytree(source_dir, destination_dir)
            staged_count += 1
            print(f"Collected staged {profile.id}/{app.name}: {destination_dir}")

    expected_count = len(APPS) * len(profile_ids)
    set_github_output("complete", "true" if complete and staged_count == expected_count else "false")
    set_github_output("staged_count", str(staged_count))
    set_github_output("expected_count", str(expected_count))
    return 0


def clean_staged_profiles(args: argparse.Namespace) -> int:
    run_root = staged_run_root(args)
    if run_root.exists():
        shutil.rmtree(run_root)
        print(f"Removed staged publish packages: {run_root}")
    else:
        print(f"No staged publish packages to remove: {run_root}")

    root = stage_root(args).resolve()
    current = run_root.parent
    while current != root and root in current.resolve().parents:
        try:
            current.rmdir()
        except OSError:
            break
        current = current.parent
    return 0


def create_flat_aggregate(packages: list[AppPackage], aggregate_zip: Path) -> str:
    aggregate_zip.parent.mkdir(parents=True, exist_ok=True)
    if aggregate_zip.exists():
        aggregate_zip.unlink()

    seen: dict[str, str] = {}
    with ZipFile(aggregate_zip, "w", ZIP_DEFLATED) as aggregate:
        write_install_all_script(aggregate, seen)
        for package in packages:
            write_aggregate_app_install_script(aggregate, seen, package.app.name)
        for package in packages:
            with ZipFile(package.zip_path, "r") as app_zip:
                for entry in sorted(app_zip.infolist(), key=lambda item: item.filename):
                    if entry.is_dir():
                        continue
                    if is_pdb_name(entry.filename):
                        raise SystemExit(f"Refusing to include PDB file in aggregate: {entry.filename}")
                    if is_root_app_install_name(entry.filename):
                        continue
                    content = app_zip.read(entry.filename)
                    write_unique_zip_entry(aggregate, seen, entry.filename, content)

    return sha256_file(aggregate_zip)


def profile_manifest_data(
    profile: Profile,
    packages: list[AppPackage],
    aggregate_zip: Path,
    aggregate_sha: str,
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
            "size": aggregate_zip.stat().st_size,
            "source": profile.build_source,
        },
        "apps": [],
    }

    for package in packages:
        app_data = {
            "appId": package.app.name,
            "version": package.version,
            "fileName": package.zip_path.name,
            "sha256": sha256_file(package.zip_path),
            "size": package.zip_path.stat().st_size,
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

    manifest_path = package_dir / f"profile-{profile.id}.json"
    manifest_path.write_text(
        json.dumps(
            profile_manifest_data(
                profile,
                packages,
                aggregate_zip,
                aggregate_sha,
                tray_version,
            ),
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )

    print(f"Built {profile.display_name} package set:")
    for package in packages:
        print(f"- {package.zip_path}")
    print(f"- {aggregate_zip}")
    print(f"- {manifest_path}")
    return 0


def create_flat_aggregate_from_zips(app_zips: list[tuple[str, Path]], aggregate_zip: Path) -> str:
    aggregate_zip.parent.mkdir(parents=True, exist_ok=True)
    if aggregate_zip.exists():
        aggregate_zip.unlink()

    seen: dict[str, str] = {}
    with ZipFile(aggregate_zip, "w", ZIP_DEFLATED) as aggregate:
        write_install_all_script(aggregate, seen)
        for app_name, _ in app_zips:
            write_aggregate_app_install_script(aggregate, seen, app_name)
        for _, zip_path in app_zips:
            with ZipFile(zip_path, "r") as app_zip:
                for entry in sorted(app_zip.infolist(), key=lambda item: item.filename):
                    if entry.is_dir():
                        continue
                    if is_pdb_name(entry.filename):
                        raise SystemExit(f"Refusing to include PDB file in aggregate: {entry.filename}")
                    if is_root_app_install_name(entry.filename):
                        continue
                    content = app_zip.read(entry.filename)
                    write_unique_zip_entry(aggregate, seen, entry.filename, content)

    return sha256_file(aggregate_zip)


def load_collected_profiles(input_root: Path, profile_ids: list[str]) -> dict[str, dict]:
    required_profile_ids = set(profile_ids)
    groups: dict[str, dict] = {}

    def add_app(profile_id: str, display_name: str, app_data: dict, root: Path) -> None:
        if profile_id not in PROFILES:
            raise SystemExit(f"Unknown profile in manifest: {profile_id}")
        if profile_id not in required_profile_ids:
            return

        zip_path = root / app_data["fileName"]
        if not zip_path.exists():
            raise SystemExit(f"Missing built asset for {app_data['appId']}: {zip_path}")

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
        raise SystemExit(
            f"No selected app or profile manifests found under {input_root} "
            f"for profile(s): {', '.join(profile_ids)}"
        )

    missing_profiles = required_profile_ids - set(groups)
    if missing_profiles:
        missing = [profile_id for profile_id in profile_ids if profile_id in missing_profiles]
        raise SystemExit(f"Missing profile manifest(s): {', '.join(missing)}")

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
        app_entry["fileName"] = app_data["fileName"]
        app_entry["sha256"] = app_data["sha256"]
        app_entry["size"] = app_data.get("size", app_data["zipPath"].stat().st_size)
        apps.append(app_entry)

    manifest = {
        "profile": profile.id,
        "displayName": profile.display_name,
        "version": tray_version,
        "runtime": "win-x64",
        "aggregate": {
            "fileName": aggregate_zip.name,
            "sha256": aggregate_sha,
            "size": aggregate_zip.stat().st_size,
            "source": profile.build_source,
        },
        "apps": apps,
    }
    return manifest


def artifact_rows(manifests: list[dict]) -> list[dict]:
    rows: list[dict] = []
    for manifest in sorted(manifests, key=lambda item: item["profile"]):
        rows.append(
            {
                "profileId": manifest["profile"],
                "profile": manifest["displayName"],
                "appId": "TrayAppDotNET",
                "version": manifest["version"],
                "fileName": manifest["aggregate"]["fileName"],
                "sha256": manifest["aggregate"]["sha256"],
                "size": manifest["aggregate"]["size"],
                "source": manifest["aggregate"]["source"],
                "kind": "aggregate",
            }
        )
        for app in manifest["apps"]:
            rows.append(
                {
                    "profileId": manifest["profile"],
                    "profile": manifest["displayName"],
                    "appId": app["appId"],
                    "version": app["version"],
                    "fileName": app["fileName"],
                    "sha256": app["sha256"],
                    "size": app["size"],
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
        cmd = ["gh", "release", "edit", tag, "--repo", repo, "--draft", "--title", title, "--notes-file", str(notes_path)]
        if target:
            cmd.extend(["--target", target])
        run(cmd)
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


def commit_list(repo: str, commits: list[CommitEntry]) -> list[str]:
    if not commits:
        return ["- No commits found."]

    return [
        f"- [`{commit.short_sha}`](https://github.com/{repo}/commit/{commit.full_sha}) {commit.subject}"
        for commit in commits
    ]


def write_notes(path: Path, rows: list[dict], repo: str, previous_release: dict | None, commits: list[CommitEntry]) -> None:
    lines = [
        "This release is auto-generated. the following artifacts have been compiled against Windows 11 as self-contained Native AOT applications",
        "",
        *artifact_table(rows),
        "",
        f"Commits since {release_display_ref(previous_release)}:",
        "",
        *commit_list(repo, commits),
        "",
    ]
    path.write_text("\n".join(lines), encoding="utf-8")


def write_summary(rows: list[dict]) -> None:
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return
    lines = [
        "## Publish Artifacts",
        "",
        *artifact_table(rows),
        "",
    ]
    with Path(summary_path).open("a", encoding="utf-8") as summary:
        summary.write("\n".join(lines))


def write_versions_manifest(path: Path, rows: list[dict], tray_version: int, repo: str, release_tag: str) -> None:
    root = ET.Element(
        "versions",
        {
            "version": str(tray_version),
            "runtime": "win-x64",
        },
    )
    ET.SubElement(
        root,
        "release",
        {
            "repository": repo,
            "tag": release_tag,
            "name": f"TrayAppDotNET {tray_version}",
        },
    )
    artifacts = ET.SubElement(root, "artifacts")
    for row in rows:
        ET.SubElement(
            artifacts,
            "artifact",
            {
                "profile": str(row["profileId"]),
                "profileName": str(row["profile"]),
                "kind": str(row["kind"]),
                "appId": str(row["appId"]),
                "version": str(row["version"]),
                "fileName": str(row["fileName"]),
                "sha256": str(row["sha256"]),
                "size": str(row["size"]),
                "source": str(row["source"]),
            },
        )

    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ")
    tree.write(path, encoding="utf-8", xml_declaration=True)


def publish_release(args: argparse.Namespace) -> int:
    input_root = Path(args.input_root)
    profile_ids = parse_profile_ids(args.profiles)
    groups = load_collected_profiles(input_root, profile_ids)
    previous_release = latest_release(
        args.repo,
        missing_message="No latest published release found; defaulting TrayAppDotNET version to 100.",
    )
    if args.tray_version.strip():
        tray_version = int(args.tray_version)
    else:
        latest_version = release_version(previous_release)
        tray_version = max((latest_version or 99) + 1, 100)
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
        app_zips = [(app_data["appId"], app_data["zipPath"]) for app_data in group["apps"]]
        aggregate_sha = create_flat_aggregate_from_zips(app_zips, aggregate_zip)
        manifests.append(
            profile_manifest_from_group(
                group,
                aggregate_zip,
                aggregate_sha,
                tray_version,
            )
        )
        upload_assets.append(aggregate_zip)
        upload_assets.extend(app_zip_paths)

    rows = artifact_rows(manifests)

    notes_path = final_dir / "release-notes.md"
    versions_path = final_dir / "versions.xml"
    commits = commits_since_release(previous_release, args.target)
    write_notes(notes_path, rows, args.repo, previous_release, commits)
    write_versions_manifest(versions_path, rows, tray_version, args.repo, release_tag)
    write_summary(rows)

    ensure_release(
        args.repo,
        release_tag,
        f"TrayAppDotNET {tray_version}",
        args.target,
        notes_path,
    )

    upload_assets.append(versions_path)
    upload_assets = list(dict.fromkeys(upload_assets))
    prune_release_assets(args.repo, release_tag, {path.name for path in upload_assets})

    run(["gh", "release", "upload", release_tag, *[str(path) for path in upload_assets], "--repo", args.repo, "--clobber"])

    print("Published draft release assets:")
    for row in rows:
        print(f"- {row['profile']}: {row['fileName']}")
    print(f"- {versions_path.name}")
    return 0


def main() -> int:
    args = parse_args()
    if args.phase == "build-profile":
        return build_profile(args)
    if args.phase == "build-app-profile":
        return build_app_profile(args)
    if args.phase == "stage-app-profile":
        return stage_app_profile(args)
    if args.phase == "collect-staged-profiles":
        return collect_staged_profiles(args)
    if args.phase == "clean-staged-profiles":
        return clean_staged_profiles(args)
    return publish_release(args)


if __name__ == "__main__":
    raise SystemExit(main())

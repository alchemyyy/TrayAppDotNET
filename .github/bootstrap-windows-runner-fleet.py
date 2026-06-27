#!/usr/bin/env python3
"""
Bootstrap a Windows host as a GitHub Actions runner fleet for TrayAppDotNET.

This script intentionally uses only Python's standard library. It installs:
  - Git for Windows
  - GitHub CLI
  - the .NET SDK major version detected from the repository
  - optional Visual Studio Build Tools with the C++ workload
  - N repository-scoped GitHub Actions runners as Windows services

Run this from an elevated Administrator shell on the Windows machine that will
host the runners.

Prerequisites before running:
  - Windows x64 or Windows ARM64 host
  - Python 3 already installed and available as `python`
  - Administrator shell if installing packages, configuring services, or nuking runners
  - winget/App Installer available, unless using --skip-system-packages
  - GitHub account with admin permission on the target repo
  - Network access to github.com, cli.github.com, Microsoft winget sources, and runner downloads
  - If using a custom Windows service account, that account and password must already exist

Installed or verified by this script:
  - Git for Windows
  - GitHub CLI
  - .NET SDK matching the repo's highest TargetFramework major version
  - optional Visual Studio Build Tools with the C++ workload
  - GitHub Actions runner services
"""

from __future__ import annotations

import argparse
import ctypes
import getpass
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path


DEFAULT_REPO = "alchemyyy/TrayAppDotNET"
DEFAULT_COUNT = 16
DEFAULT_RUNNER_ROOT = r"C:\actions-runners\trayappdotnet"
DEFAULT_LABELS = "trayapp,windows"
DEFAULT_DOTNET_SDK = "auto"
FALLBACK_DOTNET_SDK = "10.0"
DEFAULT_SERVICE_ACCOUNT = r"NT AUTHORITY\NETWORK SERVICE"


def print_prerequisites() -> None:
    print("Windows runner fleet bootstrap prerequisites:")
    print("  Required before running:")
    print("    - Windows x64 or Windows ARM64 host")
    print("    - Python 3 available as `python`")
    print("    - Administrator shell for package install, service setup, or nuke operations")
    print("    - winget/App Installer available, unless --skip-system-packages is used")
    print("    - GitHub account with admin permission on the target repo")
    print("    - Network access to GitHub, Microsoft winget sources, and runner downloads")
    print("    - Existing Windows service account/password if using a custom service account")
    print("  Installed or verified by this script:")
    print("    - Git for Windows")
    print("    - GitHub CLI")
    print("    - .NET SDK")
    print("    - Visual Studio Build Tools C++ workload, unless skipped")
    print("    - GitHub Actions runner services")
    print()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Install Windows prerequisites and register a fleet of GitHub Actions runners."
    )
    parser.add_argument("--repo", default=DEFAULT_REPO, help=f"GitHub repo owner/name. Default: {DEFAULT_REPO}")
    parser.add_argument("--count", type=int, default=DEFAULT_COUNT, help=f"Number of runners. Default: {DEFAULT_COUNT}")
    parser.add_argument("--runner-root", default=DEFAULT_RUNNER_ROOT, help=f"Runner parent directory. Default: {DEFAULT_RUNNER_ROOT}")
    parser.add_argument("--labels", default=DEFAULT_LABELS, help=f"Comma-separated custom labels. Default: {DEFAULT_LABELS}")
    parser.add_argument("--name-prefix", default="", help="Runner name prefix. Default: <hostname>-trayapp")
    parser.add_argument("--dotnet-sdk", default=DEFAULT_DOTNET_SDK, help="SDK package version, such as 10.0, or auto. Default: auto")
    parser.add_argument("--runner-version", default="latest", help="Runner version or latest. Default: latest")
    parser.add_argument("--skip-system-packages", action="store_true", help="Do not install winget packages.")
    parser.add_argument("--skip-vs-build-tools", action="store_true", help="Do not install Visual Studio Build Tools C++ workload.")
    parser.add_argument("--skip-gh-auth", action="store_true", help="Do not prompt for gh authentication.")
    parser.add_argument("--skip-existing", action="store_true", help="Skip existing configured runner directories instead of aborting.")
    parser.add_argument("--nuke-existing", action="store_true", help="Delete the existing local fleet and matching GitHub runner records before setup.")
    parser.add_argument("--nuke-only", action="store_true", help="Delete the existing local fleet and matching GitHub runner records, then exit.")
    parser.add_argument("--no-service", action="store_true", help="Configure runners but do not install them as Windows services.")
    parser.add_argument(
        "--windows-logon-account",
        default=DEFAULT_SERVICE_ACCOUNT,
        help=f"Windows account for runner services. Default: {DEFAULT_SERVICE_ACCOUNT}",
    )
    parser.add_argument(
        "--windows-logon-password",
        default="",
        help="Windows service account password. Omit for built-in accounts; prompts when needed.",
    )
    parser.add_argument("--yes", action="store_true", help="Do not prompt before making changes.")
    return parser.parse_args()


def quote_cmd_part(part: str) -> str:
    return subprocess.list2cmdline([part])


def redact_cmd(cmd: list[str], secrets_to_redact: set[str]) -> str:
    rendered: list[str] = []
    for part in cmd:
        rendered.append("***" if part in secrets_to_redact else quote_cmd_part(part))
    return " ".join(rendered)


def run(
    cmd: list[str],
    *,
    cwd: Path | str | None = None,
    capture: bool = False,
    check: bool = True,
    env: dict[str, str] | None = None,
    secrets_to_redact: set[str] | None = None,
) -> subprocess.CompletedProcess[str]:
    secrets_to_redact = secrets_to_redact or set()
    location = f" (cwd={cwd})" if cwd else ""
    print(f"+ {redact_cmd(cmd, secrets_to_redact)}{location}")

    completed = subprocess.run(
        cmd,
        cwd=str(cwd) if cwd else None,
        env=env,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
        check=False,
    )

    if check and completed.returncode != 0:
        if capture:
            if completed.stdout:
                print(completed.stdout, file=sys.stdout, end="" if completed.stdout.endswith("\n") else "\n")
            if completed.stderr:
                print(completed.stderr, file=sys.stderr, end="" if completed.stderr.endswith("\n") else "\n")
        raise subprocess.CalledProcessError(
            completed.returncode,
            cmd,
            output=completed.stdout,
            stderr=completed.stderr,
        )

    return completed


def run_cmd_script(
    script: Path,
    args: list[str],
    *,
    cwd: Path | str | None = None,
    capture: bool = False,
    check: bool = True,
    secrets_to_redact: set[str] | None = None,
) -> subprocess.CompletedProcess[str]:
    return run(
        ["cmd.exe", "/d", "/c", str(script), *args],
        cwd=cwd,
        capture=capture,
        check=check,
        secrets_to_redact=secrets_to_redact,
    )


def command_exists(name: str) -> bool:
    return shutil.which(name) is not None


def require_windows() -> None:
    if platform.system().lower() != "windows":
        raise SystemExit(f"This script is intended for Windows. Found: {platform.system()}")


def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def require_admin(reason: str) -> None:
    if not is_admin():
        raise SystemExit(f"Run this script from an elevated Administrator shell. Required for: {reason}")


def append_known_tool_paths() -> None:
    candidates = [
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "cmd",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "bin",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "GitHub CLI",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet",
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
        / "Microsoft Visual Studio"
        / "Installer",
    ]
    current = os.environ.get("PATH", "")
    parts = current.split(os.pathsep) if current else []
    normalized = {str(Path(part)).lower() for part in parts if part}
    for candidate in candidates:
        candidate_text = str(candidate)
        if candidate.exists() and candidate_text.lower() not in normalized:
            parts.append(candidate_text)
            normalized.add(candidate_text.lower())
    os.environ["PATH"] = os.pathsep.join(parts)


def download(url: str, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "TrayAppDotNET-runner-bootstrap"})
    try:
        with urllib.request.urlopen(request) as response, destination.open("wb") as output:
            shutil.copyfileobj(response, output)
    except urllib.error.HTTPError as exc:
        raise SystemExit(f"Download failed: {url} returned HTTP {exc.code}") from exc


def winget_install(package_id: str, display_name: str, *, override: str = "") -> None:
    if not command_exists("winget"):
        raise SystemExit(
            "winget was not found. Install App Installer from Microsoft Store, "
            "or install prerequisites manually and rerun with --skip-system-packages."
        )

    cmd = [
        "winget",
        "install",
        "--id",
        package_id,
        "--exact",
        "--silent",
        "--accept-source-agreements",
        "--accept-package-agreements",
    ]
    if override:
        cmd.extend(["--override", override])

    result = run(cmd, check=False)
    if result.returncode != 0:
        raise SystemExit(f"Failed to install {display_name} with winget. Package id: {package_id}")


def install_base_packages() -> None:
    require_admin("installing Git, GitHub CLI, .NET SDK, and Visual Studio Build Tools")

    append_known_tool_paths()
    if command_exists("git"):
        print("Git is already installed.")
    else:
        winget_install("Git.Git", "Git for Windows")
        append_known_tool_paths()

    if command_exists("gh"):
        print("GitHub CLI is already installed.")
    else:
        winget_install("GitHub.cli", "GitHub CLI")
        append_known_tool_paths()


def ensure_gh_auth(skip_auth: bool) -> None:
    if skip_auth:
        return

    append_known_tool_paths()
    status = run(["gh", "auth", "status", "-h", "github.com"], check=False)
    if status.returncode == 0:
        print("GitHub CLI is already authenticated.")
        return

    print()
    print("GitHub CLI is not authenticated. Starting interactive gh auth login.")
    print("Use an account with admin access to the target repository.")
    input("Press Enter to continue...")
    run(["gh", "auth", "login", "-h", "github.com", "-p", "https", "-s", "repo,workflow"])


def detect_dotnet_sdk(repo: str, override: str) -> str:
    if override != "auto":
        return override

    print(f"Detecting .NET SDK major version from {repo}...")
    append_known_tool_paths()
    if not command_exists("git"):
        print(f"Git is unavailable; falling back to .NET SDK {FALLBACK_DOTNET_SDK}.")
        return FALLBACK_DOTNET_SDK

    with tempfile.TemporaryDirectory() as temp_dir:
        clone_dir = Path(temp_dir) / "repo"
        clone_url = f"https://github.com/{repo}.git"
        clone = run(["git", "clone", "--depth", "1", clone_url, str(clone_dir)], check=False)
        if clone.returncode != 0:
            print(f"Unable to clone {clone_url}; falling back to .NET SDK {FALLBACK_DOTNET_SDK}.")
            return FALLBACK_DOTNET_SDK

        versions: set[tuple[int, int]] = set()
        pattern = re.compile(r"net(?P<major>\d+)\.(?P<minor>\d+)(?:-[A-Za-z0-9_.]+)?")
        for csproj in clone_dir.rglob("*.csproj"):
            text = csproj.read_text(encoding="utf-8", errors="ignore")
            for match in pattern.finditer(text):
                versions.add((int(match.group("major")), int(match.group("minor"))))

    if not versions:
        print(f"No TargetFramework values found; falling back to .NET SDK {FALLBACK_DOTNET_SDK}.")
        return FALLBACK_DOTNET_SDK

    major, _minor = max(versions)
    sdk = f"{major}.0"
    print(f"Detected .NET SDK package family: Microsoft.DotNet.SDK.{major}")
    return sdk


def sdk_major(sdk_version: str) -> int:
    match = re.match(r"^\s*(\d+)", sdk_version)
    if not match:
        raise SystemExit(f"Unable to parse .NET SDK version: {sdk_version}")
    return int(match.group(1))


def dotnet_sdk_installed(sdk_version: str) -> bool:
    append_known_tool_paths()
    if not command_exists("dotnet"):
        return False

    major = sdk_major(sdk_version)
    result = run(["dotnet", "--list-sdks"], capture=True, check=False)
    if result.returncode != 0:
        return False

    return any(line.startswith(f"{major}.") for line in result.stdout.splitlines())


def install_dotnet_sdk(sdk_version: str) -> None:
    if dotnet_sdk_installed(sdk_version):
        print(f".NET SDK {sdk_major(sdk_version)}.x is already installed.")
        return

    require_admin("installing the .NET SDK")
    package_id = f"Microsoft.DotNet.SDK.{sdk_major(sdk_version)}"
    winget_install(package_id, f".NET SDK {sdk_version}")
    append_known_tool_paths()


def vswhere_path() -> Path:
    return (
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
        / "Microsoft Visual Studio"
        / "Installer"
        / "vswhere.exe"
    )


def visual_cpp_tools_installed() -> bool:
    append_known_tool_paths()
    vswhere = vswhere_path()
    if not vswhere.exists():
        return False

    result = run(
        [
            str(vswhere),
            "-latest",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ],
        capture=True,
        check=False,
    )
    if result.returncode != 0 or not result.stdout.strip():
        return False

    install_path = Path(result.stdout.strip().splitlines()[0])
    return (install_path / "VC" / "Auxiliary" / "Build" / "vcvarsall.bat").exists()


def install_visual_cpp_build_tools(skip: bool) -> None:
    if skip:
        return

    if visual_cpp_tools_installed():
        print("Visual Studio C++ build tools are already installed.")
        return

    require_admin("installing Visual Studio Build Tools C++ workload")
    winget_install(
        "Microsoft.VisualStudio.2022.BuildTools",
        "Visual Studio 2022 Build Tools",
        override=(
            "--wait --quiet --norestart "
            "--add Microsoft.VisualStudio.Workload.VCTools "
            "--includeRecommended"
        ),
    )

    if not visual_cpp_tools_installed():
        raise SystemExit(
            "Visual Studio Build Tools installed, but the C++ workload was not detected. "
            "Open Visual Studio Installer and confirm Desktop development with C++ / MSVC tools are installed."
        )


def runner_arch() -> str:
    machine = platform.machine().lower()
    if machine in {"x86_64", "amd64"}:
        return "x64"
    if machine in {"aarch64", "arm64"}:
        return "arm64"
    raise SystemExit(f"Unsupported Windows runner CPU architecture: {platform.machine()}")


def gh_api_json(path: str, *extra: str) -> dict:
    result = run(["gh", "api", path, *extra], capture=True)
    return json.loads(result.stdout)


def gh_api_text(path: str, *extra: str) -> str:
    result = run(["gh", "api", path, *extra], capture=True)
    return result.stdout.strip()


def verify_repo_admin(repo: str) -> None:
    print(f"Verifying GitHub admin permission on {repo}...")
    try:
        viewer = gh_api_text("user", "--jq", ".login")
        is_admin_user = gh_api_text(f"repos/{repo}", "--jq", ".permissions.admin // false")
    except subprocess.CalledProcessError as exc:
        raise SystemExit(
            "Unable to query the repository with GitHub CLI. Check `gh auth status` and repository access.\n"
            f"Failed command: {redact_cmd(list(exc.cmd), set())}"
        ) from exc

    if is_admin_user.strip().lower() != "true":
        raise SystemExit(
            f"GitHub user `{viewer}` does not have admin permission on `{repo}`.\n"
            "Creating repository self-hosted runner registration tokens requires repository admin access.\n"
            "Authenticate gh as an admin account, then rerun:\n"
            "  gh auth login -h github.com -p https -s repo,workflow\n"
            f"  gh api repos/{repo}/actions/runners/registration-token --method POST --jq .token"
        )

    print(f"GitHub user `{viewer}` has admin permission on {repo}.")


def get_runner_asset(version: str, arch: str) -> tuple[str, str]:
    if version == "latest":
        release = gh_api_json("repos/actions/runner/releases/latest")
    else:
        release = gh_api_json(f"repos/actions/runner/releases/tags/v{version.lstrip('v')}")

    expected_prefix = f"actions-runner-win-{arch}-"
    for asset in release.get("assets", []):
        name = asset.get("name", "")
        if name.startswith(expected_prefix) and name.endswith(".zip"):
            return name, asset["browser_download_url"]

    available = ", ".join(asset.get("name", "") for asset in release.get("assets", []))
    raise SystemExit(f"No runner asset found for win-{arch}. Available assets: {available}")


def download_runner_package(runner_root: Path, version: str, arch: str) -> Path:
    name, url = get_runner_asset(version, arch)
    package = Path(tempfile.gettempdir()) / name

    if package.exists():
        print(f"Runner package already downloaded: {package}")
        return package

    download(url, package)
    return package


def registration_token(repo: str) -> str:
    token = gh_api_text(f"repos/{repo}/actions/runners/registration-token", "--method", "POST", "--jq", ".token")
    if not token:
        raise SystemExit("GitHub did not return a runner registration token.")
    return token


def removal_token(repo: str) -> str:
    token = gh_api_text(f"repos/{repo}/actions/runners/remove-token", "--method", "POST", "--jq", ".token")
    if not token:
        raise SystemExit("GitHub did not return a runner removal token.")
    return token


def github_runner_records(repo: str) -> list[tuple[int, str]]:
    output = gh_api_text(
        f"repos/{repo}/actions/runners",
        "--paginate",
        "--jq",
        ".runners[] | [.id, .name] | @tsv",
    )
    records: list[tuple[int, str]] = []
    for line in output.splitlines():
        if not line.strip():
            continue
        runner_id, runner_name = line.split("\t", 1)
        records.append((int(runner_id), runner_name))
    return records


def delete_github_runners(repo: str, runner_names: set[str]) -> None:
    if not runner_names:
        return

    for runner_id, runner_name in github_runner_records(repo):
        if runner_name not in runner_names:
            continue
        print(f"Deleting GitHub runner record: {runner_name} ({runner_id})")
        run(["gh", "api", f"repos/{repo}/actions/runners/{runner_id}", "--method", "DELETE"], capture=True)


def ensure_runner_root(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def normalized_account(account: str) -> str:
    return account.strip().replace("/", "\\").lower()


def is_builtin_service_account(account: str) -> bool:
    value = normalized_account(account)
    return value in {
        r"nt authority\network service",
        r"nt authority\local service",
        r"nt authority\system",
        "localsystem",
        "localservice",
        "networkservice",
    }


def service_password_for(args: argparse.Namespace) -> str:
    if args.no_service:
        return ""
    account = args.windows_logon_account.strip()
    if not account or is_builtin_service_account(account):
        return ""
    if args.windows_logon_password:
        return args.windows_logon_password
    return getpass.getpass(f"Windows service password for {account}: ")


def grant_service_account_access(path: Path, account: str) -> None:
    if not account:
        return
    run(["icacls", str(path), "/grant", f"{account}:(OI)(CI)F", "/T"])


def extract_runner_package(package: Path, runner_dir: Path) -> None:
    runner_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(package, "r") as archive:
        archive.extractall(runner_dir)


def service_name_from_runner_dir(repo: str, runner_name: str, runner_dir: Path) -> str:
    service_file = runner_dir / ".service"
    if service_file.exists():
        for line in service_file.read_text(encoding="utf-8", errors="ignore").splitlines():
            candidate = line.strip()
            if candidate:
                return candidate
    repo_part = repo.replace("/", "-")
    return f"actions.runner.{repo_part}.{runner_name}"


def stop_delete_service(service_name: str) -> None:
    if not service_name:
        return
    print(f"Stopping/removing Windows service: {service_name}")
    run(["sc.exe", "stop", service_name], check=False)
    run(["sc.exe", "delete", service_name], check=False)


def safe_nuke_path(path: Path) -> None:
    resolved = path.resolve()
    drive = Path(resolved.anchor)
    forbidden = {
        drive,
        Path.home().resolve(),
        Path(os.environ.get("WINDIR", r"C:\Windows")).resolve(),
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")).resolve(),
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")).resolve(),
        Path(os.environ.get("ProgramData", r"C:\ProgramData")).resolve(),
    }
    if resolved in forbidden:
        raise SystemExit(f"Refusing to delete unsafe path: {resolved}")
    if len(resolved.parts) < 3:
        raise SystemExit(f"Refusing to delete shallow path: {resolved}")
    if not resolved.exists():
        return

    shutil.rmtree(resolved)


def nuke_runner_fleet(repo: str, runner_root: Path, runner_names: list[str]) -> None:
    require_admin("removing Windows runner services")
    print("Nuking existing runner fleet...")

    for index, runner_name in enumerate(runner_names, start=1):
        runner_dir = runner_root / f"runner-{index:02d}"
        service_name = service_name_from_runner_dir(repo, runner_name, runner_dir)
        config_cmd = runner_dir / "config.cmd"

        if config_cmd.exists():
            try:
                token = removal_token(repo)
                run_cmd_script(
                    config_cmd,
                    ["remove", "--unattended", "--token", token],
                    cwd=runner_dir,
                    check=False,
                    secrets_to_redact={token},
                )
            except subprocess.CalledProcessError:
                pass

        stop_delete_service(service_name)

    delete_github_runners(repo, set(runner_names))
    safe_nuke_path(runner_root)
    print("Existing runner fleet removed.")


def configure_runner(
    *,
    repo: str,
    runner_dir: Path,
    runner_name: str,
    labels: str,
    package: Path,
    run_as_service: bool,
    service_account: str,
    service_password: str,
) -> None:
    if (runner_dir / ".runner").exists():
        print(f"Already configured, skipping: {runner_dir}")
        return

    extract_runner_package(package, runner_dir)

    token = registration_token(repo)
    repo_url = f"https://github.com/{repo}"
    config_args = [
        "--unattended",
        "--url",
        repo_url,
        "--token",
        token,
        "--name",
        runner_name,
        "--labels",
        labels,
        "--work",
        "_work",
        "--replace",
    ]

    if run_as_service:
        config_args.append("--runasservice")
        if service_account:
            config_args.extend(["--windowslogonaccount", service_account])
        if service_password:
            config_args.extend(["--windowslogonpassword", service_password])

    run_cmd_script(
        runner_dir / "config.cmd",
        config_args,
        cwd=runner_dir,
        secrets_to_redact={token, service_password} if service_password else {token},
    )

    if run_as_service:
        service_name = service_name_from_runner_dir(repo, runner_name, runner_dir)
        run(["sc.exe", "start", service_name], check=False)
        run(["sc.exe", "query", service_name], check=False)


def existing_configured_dirs(root: Path) -> list[Path]:
    if not root.exists():
        return []
    return sorted(path for path in root.glob("runner-*") if (path / ".runner").exists())


def confirm(args: argparse.Namespace, sdk_version: str, runner_root: Path, run_as_service: bool) -> None:
    if args.yes:
        return

    print()
    print("Planned changes:")
    print(f"  repo:          {args.repo}")
    print(f"  runners:       {args.count}")
    print(f"  runner root:   {runner_root}")
    print(f"  labels:        {args.labels}")
    print(f"  .NET SDK:      Microsoft.DotNet.SDK.{sdk_major(sdk_version)}")
    print(f"  VS C++ tools:  {'skip' if args.skip_vs_build_tools else 'install/detect'}")
    print(f"  service mode:  {'yes' if run_as_service else 'no'}")
    if run_as_service:
        print(f"  service acct:  {args.windows_logon_account}")
    if args.nuke_existing or args.nuke_only:
        print("  reset mode:    NUKE existing matching runners first")
    if args.nuke_only:
        print("  nuke only:     yes, exit after removal")
    print()
    answer = input("Proceed? [y/N] ").strip().lower()
    if answer not in {"y", "yes"}:
        raise SystemExit("Aborted.")


def main() -> int:
    args = parse_args()
    print_prerequisites()

    if args.count < 1:
        raise SystemExit("--count must be at least 1.")

    require_windows()
    append_known_tool_paths()
    run_as_service = not args.no_service
    if run_as_service:
        require_admin("configuring runners as Windows services")

    if not args.skip_system_packages:
        install_base_packages()

    ensure_gh_auth(args.skip_gh_auth)
    verify_repo_admin(args.repo)

    sdk_version = detect_dotnet_sdk(args.repo, args.dotnet_sdk)
    runner_root = Path(args.runner_root)
    hostname = re.sub(r"[^A-Za-z0-9_.-]+", "-", platform.node()).strip("-") or "windows"
    name_prefix = args.name_prefix.strip() or f"{hostname}-trayapp"
    runner_names = [f"{name_prefix}-{index:02d}" for index in range(1, args.count + 1)]

    confirm(args, sdk_version, runner_root, run_as_service)

    if args.nuke_existing or args.nuke_only:
        nuke_runner_fleet(args.repo, runner_root, runner_names)
        if args.nuke_only:
            return 0

    if not args.skip_system_packages:
        install_dotnet_sdk(sdk_version)
        install_visual_cpp_build_tools(args.skip_vs_build_tools)

    ensure_runner_root(runner_root)
    service_password = service_password_for(args)
    if run_as_service:
        grant_service_account_access(runner_root, args.windows_logon_account)

    existing = existing_configured_dirs(runner_root)
    if existing and not args.skip_existing:
        existing_list = "\n".join(f"  {path}" for path in existing)
        raise SystemExit(
            "Configured runner directories already exist. Re-run with --skip-existing to leave them untouched.\n"
            + existing_list
        )

    arch = runner_arch()
    package = download_runner_package(runner_root, args.runner_version, arch)

    for index in range(1, args.count + 1):
        runner_dir = runner_root / f"runner-{index:02d}"
        runner_name = runner_names[index - 1]
        if (runner_dir / ".runner").exists() and args.skip_existing:
            print(f"Already configured, skipping: {runner_dir}")
            continue

        configure_runner(
            repo=args.repo,
            runner_dir=runner_dir,
            runner_name=runner_name,
            labels=args.labels,
            package=package,
            run_as_service=run_as_service,
            service_account=args.windows_logon_account,
            service_password=service_password,
        )

    print()
    print("Runner fleet setup complete.")
    if run_as_service:
        print("Registered Windows services:")
        for index, runner_name in enumerate(runner_names, start=1):
            runner_dir = runner_root / f"runner-{index:02d}"
            service_name = service_name_from_runner_dir(args.repo, runner_name, runner_dir)
            run(["sc.exe", "query", service_name], check=False)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nAborted.")
        raise SystemExit(130)

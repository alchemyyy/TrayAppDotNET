#!/usr/bin/env python3
"""
Bootstrap a Debian host as a GitHub Actions runner fleet for TrayAppDotNET.

This script intentionally uses only Python's standard library. It installs:
  - base system tools
  - GitHub CLI
  - Microsoft package feed
  - the .NET SDK major version detected from the repository
  - N repository-scoped GitHub Actions runners as systemd services

Run this on the Debian machine that will host the runners.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path


DEFAULT_REPO = "alchemyyy/TrayAppDotNET"
DEFAULT_COUNT = 16
DEFAULT_RUNNER_ROOT = "/opt/actions-runners/trayappdotnet"
DEFAULT_RUNNER_USER = "github-runner"
DEFAULT_LABELS = "trayapp,debian"
DEFAULT_DOTNET_SDK = "auto"
FALLBACK_DOTNET_SDK = "10.0"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Install Debian prerequisites and register a fleet of GitHub Actions runners."
    )
    parser.add_argument("--repo", default=DEFAULT_REPO, help=f"GitHub repo owner/name. Default: {DEFAULT_REPO}")
    parser.add_argument("--count", type=int, default=DEFAULT_COUNT, help=f"Number of runners. Default: {DEFAULT_COUNT}")
    parser.add_argument("--runner-root", default=DEFAULT_RUNNER_ROOT, help=f"Runner parent directory. Default: {DEFAULT_RUNNER_ROOT}")
    parser.add_argument("--runner-user", default=DEFAULT_RUNNER_USER, help=f"System user for runner services. Default: {DEFAULT_RUNNER_USER}")
    parser.add_argument("--labels", default=DEFAULT_LABELS, help=f"Comma-separated custom labels. Default: {DEFAULT_LABELS}")
    parser.add_argument("--name-prefix", default="", help="Runner name prefix. Default: <hostname>-trayapp")
    parser.add_argument("--dotnet-sdk", default=DEFAULT_DOTNET_SDK, help="SDK package version, such as 10.0, or auto. Default: auto")
    parser.add_argument("--runner-version", default="latest", help="Runner version or latest. Default: latest")
    parser.add_argument("--skip-system-packages", action="store_true", help="Do not install apt packages or feeds.")
    parser.add_argument("--skip-gh-auth", action="store_true", help="Do not prompt for gh authentication.")
    parser.add_argument("--skip-existing", action="store_true", help="Skip existing configured runner directories instead of aborting.")
    parser.add_argument("--nuke-existing", action="store_true", help="Delete the existing local fleet and matching GitHub runner records before setup.")
    parser.add_argument("--nuke-only", action="store_true", help="Delete the existing local fleet and matching GitHub runner records, then exit.")
    parser.add_argument("--yes", action="store_true", help="Do not prompt before making changes.")
    return parser.parse_args()


def redact_cmd(cmd: list[str], secrets_to_redact: set[str]) -> str:
    rendered: list[str] = []
    for part in cmd:
        rendered.append("***" if part in secrets_to_redact else shlex.quote(part))
    return " ".join(rendered)


def run(
    cmd: list[str],
    *,
    cwd: Path | str | None = None,
    sudo: bool = False,
    user: str | None = None,
    capture: bool = False,
    check: bool = True,
    env: dict[str, str] | None = None,
    secrets_to_redact: set[str] | None = None,
) -> subprocess.CompletedProcess[str]:
    actual = list(cmd)
    if user:
        actual = ["sudo", "-H", "-u", user] + actual
    elif sudo and os.geteuid() != 0:
        actual = ["sudo"] + actual

    secrets_to_redact = secrets_to_redact or set()
    location = f" (cwd={cwd})" if cwd else ""
    print(f"+ {redact_cmd(actual, secrets_to_redact)}{location}")

    completed = subprocess.run(
        actual,
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
            actual,
            output=completed.stdout,
            stderr=completed.stderr,
        )

    return completed


def command_exists(name: str) -> bool:
    return shutil.which(name) is not None


def read_os_release() -> dict[str, str]:
    values: dict[str, str] = {}
    path = Path("/etc/os-release")
    if not path.exists():
        return values

    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" not in line or line.startswith("#"):
            continue
        key, value = line.split("=", 1)
        values[key] = value.strip().strip('"')
    return values


def require_debian() -> dict[str, str]:
    os_release = read_os_release()
    if os_release.get("ID") != "debian":
        found = os_release.get("PRETTY_NAME", "unknown OS")
        raise SystemExit(f"This script is intended for Debian. Found: {found}")

    version_id = os_release.get("VERSION_ID")
    if not version_id:
        raise SystemExit("Unable to read Debian VERSION_ID from /etc/os-release.")

    return os_release


def download(url: str, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "TrayAppDotNET-runner-bootstrap"})
    try:
        with urllib.request.urlopen(request) as response, destination.open("wb") as output:
            shutil.copyfileobj(response, output)
    except urllib.error.HTTPError as exc:
        raise SystemExit(f"Download failed: {url} returned HTTP {exc.code}") from exc


def sudo_write_file(path: str, content: str, mode: str = "644") -> None:
    with tempfile.NamedTemporaryFile("w", delete=False, encoding="utf-8") as temp:
        temp.write(content)
        temp_path = temp.name
    try:
        run(["install", "-m", mode, temp_path, path], sudo=True)
    finally:
        Path(temp_path).unlink(missing_ok=True)


def apt_install(packages: list[str]) -> None:
    run(["apt-get", "update"], sudo=True)
    run(["apt-get", "install", "-y", *packages], sudo=True)


def install_base_packages() -> None:
    apt_install(
        [
            "apt-transport-https",
            "ca-certificates",
            "curl",
            "git",
            "gnupg",
            "jq",
            "lsb-release",
            "python3",
            "sudo",
            "tar",
            "wget",
        ]
    )


def install_github_cli() -> None:
    if command_exists("gh"):
        print("GitHub CLI is already installed.")
        return

    with tempfile.TemporaryDirectory() as temp_dir:
        keyring = Path(temp_dir) / "githubcli-archive-keyring.gpg"
        download("https://cli.github.com/packages/githubcli-archive-keyring.gpg", keyring)
        run(["mkdir", "-p", "-m", "755", "/etc/apt/keyrings"], sudo=True)
        run(["install", "-m", "644", str(keyring), "/etc/apt/keyrings/githubcli-archive-keyring.gpg"], sudo=True)

    arch = run(["dpkg", "--print-architecture"], capture=True).stdout.strip()
    sudo_write_file(
        "/etc/apt/sources.list.d/github-cli.list",
        f"deb [arch={arch} signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main\n",
    )
    apt_install(["gh"])


def ensure_gh_auth(skip_auth: bool) -> None:
    if skip_auth:
        return

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
    print(f"Detected .NET SDK package family: dotnet-sdk-{sdk}")
    return sdk


def install_microsoft_dotnet_feed(os_release: dict[str, str]) -> None:
    version_id = os_release["VERSION_ID"]
    with tempfile.TemporaryDirectory() as temp_dir:
        deb = Path(temp_dir) / "packages-microsoft-prod.deb"
        download(f"https://packages.microsoft.com/config/debian/{version_id}/packages-microsoft-prod.deb", deb)
        run(["dpkg", "-i", str(deb)], sudo=True)


def install_dotnet_sdk(sdk_version: str, os_release: dict[str, str]) -> None:
    package = f"dotnet-sdk-{sdk_version}"
    installed = run(["bash", "-lc", f"dpkg-query -W -f='${{Status}}' {shlex.quote(package)} 2>/dev/null | grep -q 'install ok installed'"], check=False)
    if installed.returncode == 0:
        print(f"{package} is already installed.")
        return

    install_microsoft_dotnet_feed(os_release)
    apt_install([package])


def runner_arch() -> str:
    machine = platform.machine().lower()
    if machine in {"x86_64", "amd64"}:
        return "x64"
    if machine in {"aarch64", "arm64"}:
        return "arm64"
    if machine.startswith("arm"):
        return "arm"
    raise SystemExit(f"Unsupported runner CPU architecture: {platform.machine()}")


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
        is_admin = gh_api_text(f"repos/{repo}", "--jq", ".permissions.admin // false")
    except subprocess.CalledProcessError as exc:
        raise SystemExit(
            "Unable to query the repository with GitHub CLI. Check `gh auth status` and repository access.\n"
            f"Failed command: {redact_cmd(list(exc.cmd), set())}"
        ) from exc

    if is_admin.strip().lower() != "true":
        raise SystemExit(
            f"GitHub user `{viewer}` does not have admin permission on `{repo}`.\n"
            "Creating repository self-hosted runner registration tokens requires repository admin access.\n"
            "Authenticate gh as an admin account, then rerun:\n"
            "  gh auth login -h github.com -p https -s repo,workflow\n"
            f"  gh api repos/{repo}/actions/runners/registration-token --method POST --jq .token"
        )

    print(f"GitHub user `{viewer}` has admin permission on {repo}.")


def get_runner_asset(repo: str, version: str, arch: str) -> tuple[str, str]:
    if version == "latest":
        release = gh_api_json("repos/actions/runner/releases/latest")
    else:
        release = gh_api_json(f"repos/actions/runner/releases/tags/v{version.lstrip('v')}")

    expected_prefix = f"actions-runner-linux-{arch}-"
    for asset in release.get("assets", []):
        name = asset.get("name", "")
        if name.startswith(expected_prefix) and name.endswith(".tar.gz"):
            return name, asset["browser_download_url"]

    available = ", ".join(asset.get("name", "") for asset in release.get("assets", []))
    raise SystemExit(f"No runner asset found for linux-{arch}. Available assets: {available}")


def download_runner_package(runner_root: Path, version: str, arch: str) -> Path:
    name, url = get_runner_asset("actions/runner", version, arch)
    package = Path(tempfile.gettempdir()) / name

    if package.exists():
        print(f"Runner package already downloaded: {package}")
        return package

    download(url, package)
    package.chmod(0o644)
    return package


def ensure_runner_user(user: str) -> None:
    exists = run(["id", "-u", user], check=False, capture=True)
    if exists.returncode == 0:
        print(f"Runner user exists: {user}")
        return

    run(["useradd", "--system", "--create-home", "--shell", "/bin/bash", user], sudo=True)


def ensure_runner_root(path: Path, user: str) -> None:
    run(["mkdir", "-p", str(path)], sudo=True)
    run(["chown", f"{user}:{user}", str(path)], sudo=True)


def registration_token(repo: str) -> str:
    token = gh_api_text(f"repos/{repo}/actions/runners/registration-token", "--method", "POST", "--jq", ".token")
    if not token:
        raise SystemExit("GitHub did not return a runner registration token.")
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


def safe_nuke_path(path: Path) -> None:
    resolved = path.resolve()
    forbidden = {
        Path("/"),
        Path("/bin"),
        Path("/boot"),
        Path("/dev"),
        Path("/etc"),
        Path("/home"),
        Path("/lib"),
        Path("/lib64"),
        Path("/opt"),
        Path("/proc"),
        Path("/root"),
        Path("/run"),
        Path("/sbin"),
        Path("/sys"),
        Path("/tmp"),
        Path("/usr"),
        Path("/var"),
    }
    if resolved in forbidden:
        raise SystemExit(f"Refusing to delete unsafe path: {resolved}")
    if not str(resolved).startswith("/"):
        raise SystemExit(f"Refusing to delete non-absolute path: {resolved}")
    if not resolved.exists():
        return

    run(["rm", "-rf", "--one-file-system", str(resolved)], sudo=True)


def matching_unit_files(runner_name: str) -> list[Path]:
    systemd_dir = Path("/etc/systemd/system")
    candidates = [systemd_dir / f"github-actions-{runner_name}.service"]
    candidates.extend(systemd_dir.glob(f"actions.runner.*{runner_name}*.service"))
    return sorted({path for path in candidates if path.exists()})


def remove_systemd_units_for_runner(runner_name: str) -> None:
    for unit_path in matching_unit_files(runner_name):
        unit_name = unit_path.name
        print(f"Removing systemd unit: {unit_name}")
        run(["systemctl", "stop", unit_name], sudo=True, check=False)
        run(["systemctl", "disable", unit_name], sudo=True, check=False)
        run(["rm", "-f", str(unit_path)], sudo=True)


def nuke_runner_fleet(repo: str, runner_root: Path, runner_names: list[str]) -> None:
    print("Nuking existing runner fleet...")

    for index, runner_name in enumerate(runner_names, start=1):
        runner_dir = runner_root / f"runner-{index:02d}"
        svc_script = runner_dir / "svc.sh"
        if svc_script.exists():
            run(["./svc.sh", "stop"], cwd=runner_dir, sudo=True, check=False)
            run(["./svc.sh", "uninstall"], cwd=runner_dir, sudo=True, check=False)
        remove_systemd_units_for_runner(runner_name)

    run(["systemctl", "daemon-reload"], sudo=True, check=False)
    delete_github_runners(repo, set(runner_names))
    safe_nuke_path(runner_root)
    print("Existing runner fleet removed.")


def configure_runner(
    *,
    repo: str,
    runner_dir: Path,
    runner_user: str,
    runner_name: str,
    labels: str,
    package: Path,
    install_dependencies: bool,
) -> None:
    if (runner_dir / ".runner").exists():
        print(f"Already configured, skipping: {runner_dir}")
        return

    run(["mkdir", "-p", str(runner_dir)], sudo=True)
    run(["chown", f"{runner_user}:{runner_user}", str(runner_dir)], sudo=True)

    run(["tar", "-xzf", str(package), "-C", str(runner_dir)], user=runner_user)

    deps_script = runner_dir / "bin" / "installdependencies.sh"
    if install_dependencies and deps_script.exists():
        run([str(deps_script)], sudo=True)

    token = registration_token(repo)
    repo_url = f"https://github.com/{repo}"
    config_cmd = [
        "./config.sh",
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
    run(config_cmd, cwd=runner_dir, user=runner_user, secrets_to_redact={token})

    run(["./svc.sh", "install", runner_user], cwd=runner_dir, sudo=True)
    run(["./svc.sh", "start"], cwd=runner_dir, sudo=True)


def existing_configured_dirs(root: Path) -> list[Path]:
    if not root.exists():
        return []
    return sorted(path for path in root.glob("runner-*") if (path / ".runner").exists())


def confirm(args: argparse.Namespace, sdk_version: str, runner_root: Path) -> None:
    if args.yes:
        return

    print()
    print("Planned changes:")
    print(f"  repo:          {args.repo}")
    print(f"  runners:       {args.count}")
    print(f"  runner root:   {runner_root}")
    print(f"  runner user:   {args.runner_user}")
    print(f"  labels:        {args.labels}")
    print(f"  .NET SDK:      dotnet-sdk-{sdk_version}")
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

    if args.count < 1:
        raise SystemExit("--count must be at least 1.")

    os_release = require_debian()

    if not args.skip_system_packages:
        install_base_packages()
        install_github_cli()

    ensure_gh_auth(args.skip_gh_auth)
    verify_repo_admin(args.repo)

    sdk_version = detect_dotnet_sdk(args.repo, args.dotnet_sdk)
    runner_root = Path(args.runner_root)
    hostname = re.sub(r"[^A-Za-z0-9_.-]+", "-", platform.node()).strip("-") or "debian"
    name_prefix = args.name_prefix.strip() or f"{hostname}-trayapp"
    runner_names = [f"{name_prefix}-{index:02d}" for index in range(1, args.count + 1)]

    confirm(args, sdk_version, runner_root)

    if args.nuke_existing or args.nuke_only:
        nuke_runner_fleet(args.repo, runner_root, runner_names)
        if args.nuke_only:
            return 0

    if not args.skip_system_packages:
        install_dotnet_sdk(sdk_version, os_release)

    ensure_runner_user(args.runner_user)
    ensure_runner_root(runner_root, args.runner_user)

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
            runner_user=args.runner_user,
            runner_name=runner_name,
            labels=args.labels,
            package=package,
            install_dependencies=index == 1,
        )

    print()
    print("Runner fleet setup complete.")
    print("Service status:")
    run(["systemctl", "list-units", "actions.runner.*", "--no-pager"], sudo=True, check=False)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nAborted.")
        raise SystemExit(130)

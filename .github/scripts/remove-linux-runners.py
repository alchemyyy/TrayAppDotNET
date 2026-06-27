#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
from pathlib import Path


DEFAULT_REPO = os.environ.get("GITHUB_REPOSITORY", "alchemyyy/TrayAppDotNET")
DEFAULT_LABELS = "trayapp,debian"
DEFAULT_NAME_REGEX = r"^debian-trayapp-\d+$"
DEFAULT_NAME_PREFIX = "debian-trayapp"
DEFAULT_COUNT = 16
DEFAULT_RUNNER_ROOT = "/opt/actions-runners/trayappdotnet"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Remove TrayAppDotNET Debian/Linux self-hosted runners.")
    parser.add_argument("--repo", default=DEFAULT_REPO, help=f"GitHub repo owner/name. Default: {DEFAULT_REPO}")
    parser.add_argument(
        "--required-labels",
        default=DEFAULT_LABELS,
        help=f"Comma-separated labels a GitHub runner must have to be deleted. Default: {DEFAULT_LABELS}",
    )
    parser.add_argument(
        "--name-regex",
        default=DEFAULT_NAME_REGEX,
        help=f"GitHub runner name regex to delete. Default: {DEFAULT_NAME_REGEX}",
    )
    parser.add_argument("--name-prefix", default=DEFAULT_NAME_PREFIX, help=f"Local runner name prefix. Default: {DEFAULT_NAME_PREFIX}")
    parser.add_argument("--count", type=int, default=DEFAULT_COUNT, help=f"Local runner count. Default: {DEFAULT_COUNT}")
    parser.add_argument("--runner-root", default=DEFAULT_RUNNER_ROOT, help=f"Local Linux runner root. Default: {DEFAULT_RUNNER_ROOT}")
    parser.add_argument("--github-only", action="store_true", help="Only delete GitHub runner records.")
    parser.add_argument("--local-only", action="store_true", help="Only remove local Linux runner services and files.")
    parser.add_argument("--allow-busy", action="store_true", help="Allow deleting GitHub runner records that are currently busy.")
    parser.add_argument("--remove-runner-user", action="store_true", help="Also remove the local github-runner user when running local cleanup.")
    parser.add_argument("--dry-run", action="store_true", help="Print what would be deleted without deleting anything.")
    parser.add_argument("--yes", action="store_true", help="Do not prompt before deleting.")
    args = parser.parse_args()

    if args.github_only and args.local_only:
        raise SystemExit("--github-only and --local-only cannot be used together.")
    if args.count < 1:
        raise SystemExit("--count must be at least 1.")
    return args


def run(
    cmd: list[str],
    *,
    cwd: Path | None = None,
    capture: bool = False,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
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


def required_labels(value: str) -> set[str]:
    return {label.strip().lower() for label in value.split(",") if label.strip()}


def github_runner_rows(repo: str) -> list[dict]:
    result = run(
        [
            "gh",
            "api",
            f"repos/{repo}/actions/runners",
            "--paginate",
            "--jq",
            ".runners[] | {id,name,status,busy,os,labels:[.labels[].name]} | @json",
        ],
        capture=True,
    )
    rows = []
    for line in result.stdout.splitlines():
        if line.strip():
            rows.append(json.loads(line))
    return rows


def matching_github_runners(args: argparse.Namespace) -> list[dict]:
    labels = required_labels(args.required_labels)
    name_pattern = re.compile(args.name_regex)
    matches = []

    for runner in github_runner_rows(args.repo):
        runner_labels = {str(label).lower() for label in runner.get("labels", [])}
        runner_os = str(runner.get("os", "")).lower()
        name = str(runner.get("name", ""))
        if runner_os != "linux":
            continue
        if not labels.issubset(runner_labels):
            continue
        if not name_pattern.fullmatch(name):
            continue
        if runner.get("busy") and not args.allow_busy:
            print(f"Skipping busy runner: {name}")
            continue
        matches.append(runner)

    return matches


def local_runner_names(args: argparse.Namespace, selected: list[dict]) -> list[str]:
    names = [str(runner["name"]) for runner in selected]
    generated = [f"{args.name_prefix}-{index:02d}" for index in range(1, args.count + 1)]
    return list(dict.fromkeys([*names, *generated]))


def local_runner_dirs(args: argparse.Namespace) -> list[Path]:
    root = Path(args.runner_root)
    return [root / f"runner-{index:02d}" for index in range(1, args.count + 1)]


def confirm(args: argparse.Namespace, selected: list[dict], do_github: bool, do_local: bool) -> None:
    print()
    print("Planned Linux runner cleanup:")
    print(f"  repo:          {args.repo}")
    print(f"  GitHub records:{' yes' if do_github else ' no'}")
    print(f"  local cleanup: {'yes' if do_local else 'no'}")
    print(f"  runner root:   {args.runner_root}")
    print(f"  dry run:       {'yes' if args.dry_run else 'no'}")
    if selected:
        print("  matched GitHub runners:")
        for runner in selected:
            print(f"    - {runner['name']} ({runner['id']}, {runner['status']})")
    else:
        print("  matched GitHub runners: none")
    print()

    if args.yes or args.dry_run:
        return

    answer = input("Delete these Linux runners? [y/N] ").strip().lower()
    if answer not in {"y", "yes"}:
        raise SystemExit("Aborted.")


def delete_github_records(args: argparse.Namespace, selected: list[dict]) -> None:
    for runner in selected:
        runner_id = runner["id"]
        runner_name = runner["name"]
        if args.dry_run:
            print(f"Would delete GitHub runner record: {runner_name} ({runner_id})")
            continue
        print(f"Deleting GitHub runner record: {runner_name} ({runner_id})")
        run(["gh", "api", f"repos/{args.repo}/actions/runners/{runner_id}", "--method", "DELETE"], capture=True)


def is_linux() -> bool:
    return platform.system().lower() == "linux"


def require_local_linux_root() -> None:
    if not is_linux():
        raise SystemExit("Local cleanup must be run on the Debian/Linux runner host.")
    if os.geteuid() != 0:
        raise SystemExit("Local cleanup must be run as root because runner services are systemd units.")


def safe_remove_path(path: Path) -> None:
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
    if len(resolved.parts) < 4:
        raise SystemExit(f"Refusing to delete shallow path: {resolved}")
    if resolved.exists():
        shutil.rmtree(resolved)


def matching_unit_files(runner_name: str) -> list[Path]:
    systemd_dir = Path("/etc/systemd/system")
    candidates = [systemd_dir / f"github-actions-{runner_name}.service"]
    candidates.extend(systemd_dir.glob(f"actions.runner.*{runner_name}*.service"))
    return sorted({path for path in candidates if path.exists()})


def remove_systemd_units(runner_name: str, *, dry_run: bool) -> None:
    for unit_path in matching_unit_files(runner_name):
        unit_name = unit_path.name
        if dry_run:
            print(f"Would stop/disable/remove systemd unit: {unit_name}")
            continue
        run(["systemctl", "stop", unit_name], check=False)
        run(["systemctl", "disable", unit_name], check=False)
        unit_path.unlink(missing_ok=True)


def remove_local_runner_services_and_files(args: argparse.Namespace, selected: list[dict]) -> None:
    require_local_linux_root()

    names = local_runner_names(args, selected)
    for runner_dir in local_runner_dirs(args):
        svc_script = runner_dir / "svc.sh"
        if svc_script.exists():
            if args.dry_run:
                print(f"Would stop/uninstall runner service from {runner_dir}")
            else:
                run(["./svc.sh", "stop"], cwd=runner_dir, check=False)
                run(["./svc.sh", "uninstall"], cwd=runner_dir, check=False)

    for runner_name in names:
        remove_systemd_units(runner_name, dry_run=args.dry_run)

    if args.dry_run:
        print(f"Would remove local runner root: {args.runner_root}")
    else:
        run(["systemctl", "daemon-reload"], check=False)
        safe_remove_path(Path(args.runner_root))
        print(f"Removed local runner root: {args.runner_root}")

    if args.remove_runner_user:
        if args.dry_run:
            print("Would remove local user: github-runner")
        else:
            run(["userdel", "-r", "github-runner"], check=False)


def main() -> int:
    args = parse_args()
    do_github = not args.local_only
    do_local = args.local_only or (not args.github_only and is_linux())

    selected = matching_github_runners(args) if do_github else []
    confirm(args, selected, do_github, do_local)

    if do_github:
        delete_github_records(args, selected)
    if do_local:
        remove_local_runner_services_and_files(args, selected)
    elif not args.github_only and not is_linux():
        print("Local Linux cleanup skipped because this machine is not the Debian runner host.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

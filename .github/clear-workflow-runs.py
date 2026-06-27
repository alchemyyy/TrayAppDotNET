#!/usr/bin/env python3
"""
Delete GitHub Actions workflow run history for a repository.

Requires GitHub CLI (`gh`) authenticated with permission to delete Actions runs.
By default, the script targets the current repository detected by `gh repo view`.
"""

from __future__ import annotations

import argparse
import subprocess
import sys


def run(cmd: list[str], *, capture: bool = False) -> subprocess.CompletedProcess[str]:
    print("+ " + " ".join(cmd))
    result = subprocess.run(
        cmd,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
        check=False,
    )
    if result.returncode != 0:
        if result.stdout:
            print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if result.stderr:
            print(result.stderr, file=sys.stderr, end="" if result.stderr.endswith("\n") else "\n")
        raise SystemExit(result.returncode)
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Clear GitHub Actions workflow run history.")
    parser.add_argument("--repo", help="Repository in owner/name form. Defaults to current gh repo.")
    parser.add_argument("--yes", action="store_true", help="Do not ask for confirmation.")
    return parser.parse_args()


def current_repo() -> str:
    result = run(["gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"], capture=True)
    repo = result.stdout.strip()
    if not repo:
        raise SystemExit("Unable to determine current repository. Pass --repo owner/name.")
    return repo


def workflow_run_ids(repo: str) -> list[str]:
    result = run(
        [
            "gh",
            "api",
            "--paginate",
            f"repos/{repo}/actions/runs",
            "--jq",
            ".workflow_runs[].id",
        ],
        capture=True,
    )
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def confirm(repo: str, count: int, assume_yes: bool) -> None:
    if assume_yes:
        return
    answer = input(f"Delete {count} workflow run(s) from {repo}? [y/N] ").strip().lower()
    if answer not in {"y", "yes"}:
        raise SystemExit("Aborted.")


def main() -> int:
    args = parse_args()
    repo = args.repo or current_repo()

    run_ids = workflow_run_ids(repo)
    print(f"Found {len(run_ids)} workflow run(s) in {repo}.")
    if not run_ids:
        return 0

    confirm(repo, len(run_ids), args.yes)

    for run_id in run_ids:
        print(f"Deleting run {run_id}")
        run(["gh", "api", "--method", "DELETE", f"repos/{repo}/actions/runs/{run_id}"])

    remaining = workflow_run_ids(repo)
    print(f"Remaining workflow run count: {len(remaining)}")
    return 0 if not remaining else 1


if __name__ == "__main__":
    raise SystemExit(main())

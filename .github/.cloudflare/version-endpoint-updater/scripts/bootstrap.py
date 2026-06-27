#!/usr/bin/env python3
"""Bootstrap the TrayAppDotNET Cloudflare version endpoint."""

from __future__ import annotations

import getpass
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
from pathlib import Path
from urllib.parse import urlsplit

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
UPDATER_DIRECTORY = SCRIPT_DIRECTORY.parent
CLOUDFLARE_DIRECTORY = UPDATER_DIRECTORY.parent
ENDPOINT_CONFIG = CLOUDFLARE_DIRECTORY / "version-endpoint" / "wrangler.jsonc"
GENERATED_MANIFEST = CLOUDFLARE_DIRECTORY / "version-endpoint" / "public" / "versions.xml"
WRANGLER_STATE_DIRECTORY = UPDATER_DIRECTORY / ".wrangler"

CORE_SECRET_NAMES = {
    "CLOUDFLARE_ACCOUNT_ID",
    "CLOUDFLARE_API_TOKEN",
    "TRIGGER_TOKEN",
}
KNOWN_SECRET_NAMES = CORE_SECRET_NAMES | {"GITHUB_WEBHOOK_SECRET"}
ACCOUNT_ID_PATTERN = re.compile(r"^[0-9a-fA-F]{32}$")
WORKERS_URL_PATTERN = re.compile(r"https://[A-Za-z0-9.-]+\.workers\.dev")
SUBPROCESS_ENCODING = "utf-8"


def executable(name: str) -> str:
    """Returns an executable path or fails with a useful prerequisite error."""
    path: str | None = shutil.which(name)
    if path is None:
        raise RuntimeError(f"Required executable was not found: {name}")
    return path


def command_environment() -> dict[str, str]:
    """Builds a predictable environment without terminal color sequences."""
    environment: dict[str, str] = dict(os.environ)
    environment["NO_COLOR"] = "1"
    return environment


def run(command: list[str], *, input_text: str | None = None) -> None:
    """Runs a command in the updater directory."""
    completed: subprocess.CompletedProcess[str] = subprocess.run(
        command,
        cwd=UPDATER_DIRECTORY,
        env=command_environment(),
        input=input_text,
        text=True,
        encoding=SUBPROCESS_ENCODING,
        errors="replace",
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(f"Command failed with exit code {completed.returncode}: {command[0]}")


def capture(command: list[str]) -> str:
    """Runs a command and returns its combined output."""
    completed: subprocess.CompletedProcess[str] = subprocess.run(
        command,
        cwd=UPDATER_DIRECTORY,
        env=command_environment(),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding=SUBPROCESS_ENCODING,
        errors="replace",
        check=False,
    )
    if completed.returncode != 0:
        print(completed.stdout, file=sys.stderr)
        raise RuntimeError(f"Command failed with exit code {completed.returncode}: {command[0]}")
    return completed.stdout


def deploy_and_capture(command: list[str]) -> str:
    """Runs a deployment while displaying and retaining its output."""
    process: subprocess.Popen[str] = subprocess.Popen(
        command,
        cwd=UPDATER_DIRECTORY,
        env=command_environment(),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding=SUBPROCESS_ENCODING,
        errors="replace",
    )
    if process.stdout is None:
        raise RuntimeError("Could not capture Wrangler deployment output")

    output_lines: list[str] = []
    for line in process.stdout:
        print(line, end="")
        output_lines.append(line)

    return_code: int = process.wait()
    if return_code != 0:
        raise RuntimeError(f"Wrangler deployment failed with exit code {return_code}")
    return "".join(output_lines)


def prompt_account_id() -> str:
    """Prompts until a syntactically valid Cloudflare account ID is entered."""
    while True:
        account_id: str = input("Cloudflare account ID: ").strip()
        if ACCOUNT_ID_PATTERN.fullmatch(account_id):
            return account_id.lower()
        print("The account ID must contain exactly 32 hexadecimal characters.")


def prompt_api_token() -> str:
    """Reads the Cloudflare API token without echoing it."""
    while True:
        api_token: str = getpass.getpass("Cloudflare API token (hidden): ").strip()
        if api_token != "":
            return api_token
        print("The API token cannot be empty.")


def parse_updater_base_url(deployment_output: str) -> str:
    """Extracts and validates the updater's workers.dev URL."""
    match: re.Match[str] | None = WORKERS_URL_PATTERN.search(deployment_output)
    candidate: str = match.group(0) if match is not None else ""
    while candidate == "":
        candidate = input("Updater workers.dev base URL: ").strip().rstrip("/")
        parsed = urlsplit(candidate)
        if parsed.scheme != "https" or parsed.hostname is None or not parsed.hostname.endswith(".workers.dev"):
            print("Enter the https://...workers.dev URL printed by Wrangler.")
            candidate = ""
    return candidate.rstrip("/")


def list_secret_names(npx: str) -> set[str]:
    """Returns the updater Worker's current secret binding names."""
    output: str = capture([npx, "wrangler", "secret", "list", "--format", "json"])
    records: object = json.loads(output)
    if not isinstance(records, list):
        raise TypeError("Wrangler returned an invalid secret list")

    names: set[str] = set()
    for record in records:
        if not isinstance(record, dict) or not isinstance(record.get("name"), str):
            raise TypeError("Wrangler returned an invalid secret record")
        names.add(record["name"])
    return names


def configure_secrets(npx: str, account_id: str, api_token: str, trigger_token: str) -> None:
    """Creates expected secrets and optionally deletes incorrectly named bindings."""
    current_names: set[str] = list_secret_names(npx)
    unexpected_names: set[str] = current_names - KNOWN_SECRET_NAMES
    delete_unexpected: bool = False
    if unexpected_names:
        answer: str = input(
            f"Delete {len(unexpected_names)} unexpected secret binding(s) from the updater? [Y/n]: "
        ).strip().lower()
        delete_unexpected = answer in {"", "y", "yes"}

    payload: dict[str, str | None] = {
        "CLOUDFLARE_ACCOUNT_ID": account_id,
        "CLOUDFLARE_API_TOKEN": api_token,
        "TRIGGER_TOKEN": trigger_token,
    }
    if delete_unexpected:
        for secret_name in unexpected_names:
            payload[secret_name] = None

    completed: subprocess.CompletedProcess[str] = subprocess.run(
        [npx, "wrangler", "secret", "bulk"],
        cwd=UPDATER_DIRECTORY,
        env=command_environment(),
        input=json.dumps(payload),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding=SUBPROCESS_ENCODING,
        errors="replace",
        check=False,
    )
    if completed.returncode != 0:
        redacted_output: str = completed.stdout
        for sensitive_value in (account_id, api_token, trigger_token, *unexpected_names):
            redacted_output = redacted_output.replace(sensitive_value, "<redacted>")
        print(redacted_output, file=sys.stderr)
        raise RuntimeError("Wrangler could not configure the updater secrets")

    final_names: set[str] = list_secret_names(npx)
    missing_names: set[str] = CORE_SECRET_NAMES - final_names
    if missing_names:
        raise RuntimeError(f"Updater secrets are missing: {', '.join(sorted(missing_names))}")
    print("Configured CLOUDFLARE_ACCOUNT_ID, CLOUDFLARE_API_TOKEN, and TRIGGER_TOKEN.")


def remove_generated_files() -> None:
    """Removes ignored bootstrap output and Wrangler's local state."""
    if GENERATED_MANIFEST.exists():
        GENERATED_MANIFEST.unlink()
    if WRANGLER_STATE_DIRECTORY.exists():
        shutil.rmtree(WRANGLER_STATE_DIRECTORY)


def main() -> int:
    """Runs Cloudflare bootstrap steps 2 through 5."""
    npm: str = executable("npm")
    npx: str = executable("npx")

    try:
        print("\n1/5 Installing and validating the updater")
        run([npm, "ci"])
        run([npm, "run", "typecheck"])
        run([npm, "test"])

        print("\n2/5 Checking Wrangler authentication")
        authentication: subprocess.CompletedProcess[str] = subprocess.run(
            [npx, "wrangler", "whoami"],
            cwd=UPDATER_DIRECTORY,
            env=command_environment(),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if authentication.returncode != 0:
            run([npx, "wrangler", "login"])

        print("\n3/5 Preparing and deploying the public static endpoint")
        run([
            npm,
            "run",
            "prepare-endpoint",
            "--",
            "--output",
            str(GENERATED_MANIFEST),
        ])
        run([npx, "wrangler", "deploy", "--config", str(ENDPOINT_CONFIG)])

        print("\n4/5 Deploying the endpoint updater")
        deployment_output: str = deploy_and_capture([npx, "wrangler", "deploy"])
        updater_base_url: str = parse_updater_base_url(deployment_output)

        print("\n5/5 Configuring updater secrets")
        account_id: str = prompt_account_id()
        api_token: str = prompt_api_token()
        trigger_token: str = secrets.token_hex(32)
        configure_secrets(npx, account_id, api_token, trigger_token)

        trigger_url: str = f"{updater_base_url}/{trigger_token}"
        print("\nBootstrap complete.")
        print("Set this complete URL as the GitHub Actions secret VERSION_ENDPOINT_TRIGGER_URL:")
        print(trigger_url)
        print("Do not post or screenshot this URL.")
        return 0
    except KeyboardInterrupt:
        print("\nBootstrap cancelled.", file=sys.stderr)
        return 130
    except (OSError, RuntimeError, TypeError, ValueError) as error:
        print(f"\nBootstrap failed: {error}", file=sys.stderr)
        return 1
    finally:
        remove_generated_files()


if __name__ == "__main__":
    raise SystemExit(main())

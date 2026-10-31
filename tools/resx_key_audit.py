#!/usr/bin/env python3
"""Audit the localization resx files of the six apps and TrayAppDotNETCommon.

Reports three things and exits with 1 when any of the first two is found:
  * unused keys: a key no C# file references as AppStrings.Key, Strings.Key, CommonStrings.Key or "Key"
    (an app key counts as used by its own app or by Common, which resolves app resources first);
  * shadowing keys: an app key that CommonStrings.resx also defines, outside the allowlist below
    (the CommonResourceOverrideTests test enforces the same rule at test time);
  * candidates for Common: keys absent from Common that three or more apps define (informational,
    per-app taglines, descriptions and search keywords are expected here).

Usage: python tools/resx_key_audit.py [--root <repo root>]
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

APPS = ("Battery", "Brightness", "FanControl", "Network", "TaskManager", "Volume")
COMMON = "Common"
SKIP_DIRECTORIES = {"bin", "obj", "vendor", "LibreHardwareMonitor", ".git", "node_modules"}

# Keep in sync with CommonResourceOverrideTests.AllowedOverrides
ALLOWED_OVERRIDES = {
    ("Brightness", "Settings_Hotkeys_SectionDescription"),
}

REFERENCE_RE = re.compile(r"\b(?:AppStrings|Strings|CommonStrings)\.([A-Za-z_][A-Za-z0-9_]*)|\"([A-Za-z_][A-Za-z0-9_]*)\"")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    root: Path = args.root.resolve()

    projects = {app: root / f"{app}TrayAppDotNET" for app in APPS}
    projects[COMMON] = root / "TrayAppDotNETCommon"
    resx = {app: projects[app] / "src" / "Localization" / "Strings.resx" for app in APPS}
    resx[COMMON] = projects[COMMON] / "src" / "Localization" / "CommonStrings.resx"

    keys = {name: load_keys(path) for name, path in resx.items()}
    references = {name: collect_references(path) for name, path in projects.items()}

    failed = False
    for name in (*APPS, COMMON):
        visible = set(references[name])
        if name == COMMON:
            for other in APPS:
                visible |= references[other]
        else:
            visible |= references[COMMON]
        unused = sorted(key for key in keys[name] if key not in visible)
        if unused:
            failed = True
            print(f"{relative(root, resx[name])}: {len(unused)} unused key(s)")
            for key in unused:
                print(f"  {key}")

    for app in APPS:
        shadowing = sorted(
            key for key in keys[app] if key in keys[COMMON] and (app, key) not in ALLOWED_OVERRIDES)
        if shadowing:
            failed = True
            print(f"{relative(root, resx[app])}: {len(shadowing)} key(s) shadow CommonStrings.resx")
            for key in shadowing:
                print(f"  {key}")

    shared: dict[str, list[str]] = {}
    for app in APPS:
        for key in keys[app]:
            if key not in keys[COMMON]:
                shared.setdefault(key, []).append(app)
    candidates = sorted((key, apps) for key, apps in shared.items() if len(apps) >= 3)
    if candidates:
        print(f"{len(candidates)} key(s) defined by three or more apps and absent from Common:")
        for key, apps in candidates:
            print(f"  {key}: {', '.join(apps)}")

    if not failed:
        print("No unused or shadowing resx keys.")
    return 1 if failed else 0


def load_keys(path: Path) -> set[str]:
    tree = ET.parse(path)
    return {element.get("name", "") for element in tree.getroot().findall("data")}


def collect_references(project: Path) -> set[str]:
    found: set[str] = set()
    for path in project.rglob("*.cs"):
        if SKIP_DIRECTORIES.intersection(path.relative_to(project).parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        for match in REFERENCE_RE.finditer(text):
            found.add(match.group(1) or match.group(2))
    return found


def relative(root: Path, path: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


if __name__ == "__main__":
    sys.exit(main())

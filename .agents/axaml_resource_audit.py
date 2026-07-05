#!/usr/bin/env python3
"""Audit C# calls that read AXAML resources by string name.

The script treats AXAML `x:Key="Prefix.Name"` entries as the source of truth,
then scans C# for resource-reader calls such as `.Double("Name")`.
It emits high-confidence candidates when the call method's expected type matches
the AXAML key suffix and, when possible, the inferred resource prefix.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parent
XAML_NAMESPACE = "{http://schemas.microsoft.com/winfx/2006/xaml}"

SKIP_PARTS = {
    ".git",
    ".vs",
    "bin",
    "obj",
}

READER_METHOD_KINDS = {
    "Double": {"Double", "Int"},
    "Int": {"Int", "Double"},
    "Thickness": {"Thickness"},
    "CornerRadius": {"CornerRadius"},
    "Color": {"Color", "ColorString"},
    "TranslateTransform": {"TranslateTransform"},
    "CloneTranslateTransform": {"TranslateTransform"},
}

RESOURCE_CALL_RE = re.compile(
    r"(?P<receiver>(?:[A-Za-z_][A-Za-z0-9_]*|\bthis\b)(?:\.[A-Za-z_][A-Za-z0-9_]*)*)"
    r"\s*\.\s*"
    r"(?P<method>Double|Int|Thickness|CornerRadius|Color|TranslateTransform|CloneTranslateTransform)"
    r"\s*\(\s*"
    r'"(?P<name>[A-Za-z_][A-Za-z0-9_]*)"'
)

HOT_RELOAD_RE = re.compile(
    r"(?P<target>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+HotReloadResourceReader\s*"
    r"\(\s*[^,]+,\s*\"(?P<prefix>[A-Za-z_][A-Za-z0-9_]*)\"\s*\)"
)

CONTROL_RESOURCES_RE = re.compile(
    r"(?P<target>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s*\(\s*"
    r"\"[^\"]+\.axaml\"\s*,\s*\"(?P<prefix>[A-Za-z_][A-Za-z0-9_]*)\"\s*\)"
    r"|(?P<target2>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+ControlAXAMLResources\s*"
    r"\(\s*\"[^\"]+\.axaml\"\s*,\s*\"(?P<prefix2>[A-Za-z_][A-Za-z0-9_]*)\"\s*\)"
)

RESOURCE_PREFIX_RE = re.compile(
    r"ResourcePrefix\s*=\s*\"(?P<prefix>[A-Za-z_][A-Za-z0-9_]*)\.\""
)


@dataclass(frozen=True)
class AxamlResource:
    key: str
    prefix: str
    name: str
    kind: str
    file: str
    line: int
    owner_class: str | None


@dataclass(frozen=True)
class ResourceCall:
    file: str
    line: int
    receiver: str
    method: str
    name: str
    inferred_prefix: str | None
    matches: list[AxamlResource]
    confidence: str
    current: str
    suggested: str | None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--json", type=Path, default=None, help="Optional JSON report path")
    parser.add_argument("--markdown", type=Path, default=ROOT / "axaml_resource_audit_report.md")
    parser.add_argument("--include-disk-toolkit", action="store_true")
    args = parser.parse_args()

    root = args.root.resolve()
    axaml_resources = collect_axaml_resources(root, args.include_disk_toolkit)
    calls = collect_resource_calls(root, axaml_resources, args.include_disk_toolkit)
    write_markdown_report(args.markdown, axaml_resources, calls)

    if args.json is not None:
        payload = {
            "resources": [asdict(resource) for resource in axaml_resources],
            "calls": [
                {
                    **asdict(call),
                    "matches": [asdict(match) for match in call.matches],
                }
                for call in calls
            ],
        }
        args.json.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    high = sum(1 for call in calls if call.confidence == "high")
    medium = sum(1 for call in calls if call.confidence == "medium")
    low = sum(1 for call in calls if call.confidence == "low")
    print(f"AXAML resources: {len(axaml_resources)}")
    print(f"Resource calls: {len(calls)}")
    print(f"High confidence: {high}")
    print(f"Medium confidence: {medium}")
    print(f"Low/report-only: {low}")
    print(f"Markdown report: {args.markdown}")
    if args.json is not None:
        print(f"JSON report: {args.json}")
    return 0


def collect_axaml_resources(root: Path, include_disk_toolkit: bool) -> list[AxamlResource]:
    resources: list[AxamlResource] = []
    for path in sorted(root.rglob("*.axaml")):
        if should_skip(path, include_disk_toolkit):
            continue

        text = path.read_text(encoding="utf-8-sig", errors="replace")
        owner_class = find_owner_class(text)
        line_by_key = line_numbers_by_key(text)
        try:
            document = ET.fromstring(text)
        except ET.ParseError:
            continue

        for element in document.iter():
            key = element.attrib.get(XAML_NAMESPACE + "Key")
            if not key:
                continue

            parts = key.split(".")
            if len(parts) != 2:
                continue

            prefix, name = parts
            if not is_identifier(prefix) or not is_identifier(name):
                continue

            kind = kind_from_element(local_name(element.tag), name)
            if kind is None:
                continue

            resources.append(
                AxamlResource(
                    key=key,
                    prefix=prefix,
                    name=name,
                    kind=kind,
                    file=relative(root, path),
                    line=line_by_key.get(key, 0),
                    owner_class=owner_class,
                )
            )

    return resources


def collect_resource_calls(
    root: Path,
    resources: list[AxamlResource],
    include_disk_toolkit: bool,
) -> list[ResourceCall]:
    resources_by_name: dict[str, list[AxamlResource]] = {}
    resources_by_key: dict[tuple[str, str], list[AxamlResource]] = {}
    for resource in resources:
        resources_by_name.setdefault(resource.name, []).append(resource)
        resources_by_key.setdefault((resource.prefix, resource.name), []).append(resource)

    calls: list[ResourceCall] = []
    for path in sorted(root.rglob("*.cs")):
        if should_skip(path, include_disk_toolkit):
            continue
        if path.name.endswith(".g.cs"):
            continue

        text = path.read_text(encoding="utf-8-sig", errors="replace")
        prefix_by_receiver = infer_prefixes(text)
        lines = text.splitlines()
        for line_index, line in enumerate(lines, start=1):
            for match in RESOURCE_CALL_RE.finditer(line):
                receiver = match.group("receiver")
                method = match.group("method")
                name = match.group("name")
                expected_kinds = READER_METHOD_KINDS[method]
                inferred_prefix = prefix_by_receiver.get(receiver) or prefix_by_receiver.get(receiver.split(".")[-1])

                if inferred_prefix is not None:
                    possible = resources_by_key.get((inferred_prefix, name), [])
                else:
                    possible = resources_by_name.get(name, [])

                compatible = [
                    resource
                    for resource in possible
                    if resource.kind in expected_kinds
                ]
                if not compatible:
                    continue

                confidence = classify(inferred_prefix, compatible)
                suggested = suggestion(method, inferred_prefix, compatible)
                calls.append(
                    ResourceCall(
                        file=relative(root, path),
                        line=line_index,
                        receiver=receiver,
                        method=method,
                        name=name,
                        inferred_prefix=inferred_prefix,
                        matches=compatible,
                        confidence=confidence,
                        current=match.group(0),
                        suggested=suggested,
                    )
                )

    return calls


def infer_prefixes(text: str) -> dict[str, str]:
    prefixes: dict[str, str] = {}

    for match in HOT_RELOAD_RE.finditer(text):
        prefixes[match.group("target")] = match.group("prefix")

    for match in CONTROL_RESOURCES_RE.finditer(text):
        target = match.group("target") or match.group("target2")
        prefix = match.group("prefix") or match.group("prefix2")
        if target and prefix:
            prefixes[target] = prefix

    resource_prefix = RESOURCE_PREFIX_RE.search(text)
    if resource_prefix:
        prefix = resource_prefix.group("prefix")
        for receiver in ("R", "Resources", "FlyoutUndockButtonResourceReader", "TrayAppDotNETAboutPageResourceReader"):
            prefixes[receiver] = prefix

    return prefixes


def classify(inferred_prefix: str | None, matches: list[AxamlResource]) -> str:
    if inferred_prefix is not None and len(matches) == 1:
        return "high"
    if inferred_prefix is not None:
        return "medium"

    prefixes = {match.prefix for match in matches}
    if len(prefixes) == 1 and len(matches) == 1:
        return "medium"

    return "low"


def suggestion(method: str, inferred_prefix: str | None, matches: list[AxamlResource]) -> str | None:
    if not matches:
        return None

    prefixes = {match.prefix for match in matches}
    if inferred_prefix is not None:
        prefix = inferred_prefix
    elif len(prefixes) == 1:
        prefix = next(iter(prefixes))
    else:
        return None

    resource = matches[0]
    accessor = f"Axaml{prefix}.{resource.name}"
    if method == "CloneTranslateTransform":
        return accessor
    return accessor


def write_markdown_report(path: Path, resources: list[AxamlResource], calls: list[ResourceCall]) -> None:
    lines: list[str] = []
    lines.append("# AXAML Resource Audit")
    lines.append("")
    lines.append(f"- AXAML resources: {len(resources)}")
    lines.append(f"- Resource calls: {len(calls)}")
    lines.append(f"- High confidence: {sum(1 for call in calls if call.confidence == 'high')}")
    lines.append(f"- Medium confidence: {sum(1 for call in calls if call.confidence == 'medium')}")
    lines.append(f"- Low/report-only: {sum(1 for call in calls if call.confidence == 'low')}")
    lines.append("")
    lines.append("## Resource prefixes")
    lines.append("")
    lines.append("| Prefix | Count | Files |")
    lines.append("| --- | ---: | --- |")
    for prefix in sorted({resource.prefix for resource in resources}):
        prefix_resources = [resource for resource in resources if resource.prefix == prefix]
        files = ", ".join(sorted({resource.file for resource in prefix_resources}))
        lines.append(f"| `{prefix}` | {len(prefix_resources)} | {files} |")

    lines.append("")
    lines.append("## C# resource-reader candidates")
    lines.append("")
    lines.append("| Confidence | Location | Call | Inferred prefix | Matches | Suggested |")
    lines.append("| --- | --- | --- | --- | --- | --- |")
    for call in sorted(calls, key=lambda item: (confidence_rank(item.confidence), item.file, item.line)):
        matches = ", ".join(f"`{match.key}`:{match.kind}" for match in call.matches)
        suggested = f"`{call.suggested}`" if call.suggested else ""
        inferred_prefix = f"`{call.inferred_prefix}`" if call.inferred_prefix else ""
        lines.append(
            f"| {call.confidence} | {call.file}:{call.line} | `{call.current}` | "
            f"{inferred_prefix} | {matches} | {suggested} |"
        )

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def confidence_rank(value: str) -> int:
    if value == "high":
        return 0
    if value == "medium":
        return 1
    return 2


def find_owner_class(text: str) -> str | None:
    match = re.search(r'x:Class\s*=\s*"([^"]+)"', text)
    return match.group(1) if match else None


def line_numbers_by_key(text: str) -> dict[str, int]:
    result: dict[str, int] = {}
    key_re = re.compile(r'x:Key\s*=\s*"([^"]+)"')
    for line_index, line in enumerate(text.splitlines(), start=1):
        match = key_re.search(line)
        if match:
            result[match.group(1)] = line_index
    return result


def kind_from_element(element_name: str, property_name: str) -> str | None:
    if element_name == "Double":
        return "Double"
    if element_name == "Int32":
        return "Int"
    if element_name == "Thickness":
        return "Thickness"
    if element_name == "CornerRadius":
        return "CornerRadius"
    if element_name == "TranslateTransform":
        return "TranslateTransform"
    if element_name == "Color":
        return "Color"
    if element_name == "String":
        return "ColorString" if property_name.endswith("Color") else "String"
    return None


def local_name(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[1]
    return tag


def should_skip(path: Path, include_disk_toolkit: bool) -> bool:
    parts = set(path.parts)
    if parts & SKIP_PARTS:
        return True
    if not include_disk_toolkit and "DiskInfoToolkit" in parts:
        return True
    return False


def is_identifier(value: str) -> bool:
    return bool(re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", value))


def relative(root: Path, path: Path) -> str:
    return str(path.resolve().relative_to(root)).replace("\\", "/")


if __name__ == "__main__":
    sys.exit(main())

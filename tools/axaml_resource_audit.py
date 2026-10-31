#!/usr/bin/env python3
"""Report AXAML resource keys and glyph catalog entries that nothing reads.

The AXAML property linker turns every `x:Key="Prefix.Name"` of a supported type into a typed getter
(`owner.AxamlPrefix.Name`), and the glyph catalogs expose each glyph as a `GlyphCatalog.NAME` property. A key is
reported when no C# file in its scope mentions `.Name` and no AXAML file references the full key; a catalog glyph
is reported when no other C# file mentions `NAME`. Common keys and glyphs are searched across every project; app
keys and glyphs only within their app. The search is textual, so a reported entry is unused, while an unreported
one may still be dead behind an unrelated member of the same name.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
XAML_NAMESPACE = "{http://schemas.microsoft.com/winfx/2006/xaml}"
COMMON_PROJECT = "TrayAppDotNETCommon"
SKIP_PARTS = {".git", ".vs", "bin", "obj", "vendor", "experiments", "node_modules"}

# Element names the property linker types; keep in sync with AxamlPropertyLinkerGenerator.ResourceKindFromElement
LINKED_ELEMENTS = {
    "Double", "Int32", "String", "Thickness", "CornerRadius", "TranslateTransform", "Color", "GlyphDefinition",
    "ThemeColor",
}

GLYPH_PROPERTY_RE = re.compile(r"public static Glyph (?P<name>[A-Z0-9_]+)\s*=>")
RESOURCE_REFERENCE_RE = re.compile(r"\{(?:StaticResource|DynamicResource)\s+(?P<key>[A-Za-z_][A-Za-z0-9_.]*)\s*\}")


@dataclass(frozen=True)
class Finding:
    file: str
    line: int
    entry: str


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    root = args.root.resolve()

    sources = {path: path.read_text(encoding="utf-8-sig", errors="replace") for path in files(root, "*.cs")}
    axaml = {path: path.read_text(encoding="utf-8-sig", errors="replace") for path in files(root, "*.axaml")}
    referenced_keys = {match.group("key") for text in axaml.values() for match in RESOURCE_REFERENCE_RE.finditer(text)}

    findings = unused_keys(root, axaml, sources, referenced_keys) + unused_glyphs(root, sources)
    for finding in findings:
        print(f"{finding.file}:{finding.line}: {finding.entry}")

    print(f"{len(findings)} unused AXAML key(s) or glyph catalog entr(y/ies)")
    return 1 if findings else 0


def unused_keys(
    root: Path,
    axaml: dict[Path, str],
    sources: dict[Path, str],
    referenced_keys: set[str],
) -> list[Finding]:
    findings: list[Finding] = []
    for path, text in sorted(axaml.items()):
        try:
            document = ET.fromstring(text)
        except ET.ParseError:
            continue

        scope = scope_sources(root, path, sources)
        for element in document.iter():
            key = element.attrib.get(XAML_NAMESPACE + "Key")
            if not key or local_name(element.tag) not in LINKED_ELEMENTS:
                continue

            parts = key.split(".")
            if len(parts) != 2 or key in referenced_keys:
                continue

            pattern = re.compile(r"\." + re.escape(parts[1]) + r"\b|\"" + re.escape(key) + "\"")
            if any(pattern.search(source) for source in scope):
                continue

            findings.append(Finding(relative(root, path), line_of(text, f'x:Key="{key}"'), key))

    return findings


def unused_glyphs(root: Path, sources: dict[Path, str]) -> list[Finding]:
    findings: list[Finding] = []
    for path, text in sorted(sources.items()):
        if path.name != "GlyphCatalog.cs":
            continue

        scope = [source for other, source in scope_items(root, path, sources) if other != path]
        for match in GLYPH_PROPERTY_RE.finditer(text):
            name = match.group("name")
            pattern = re.compile(r"\b" + re.escape(name) + r"\b")
            # The catalog may define a member in terms of another one
            uses_in_catalog = len(pattern.findall(text)) - 1
            if uses_in_catalog > 0 or any(pattern.search(source) for source in scope):
                continue

            findings.append(Finding(relative(root, path), text.count("\n", 0, match.start()) + 1, name))

    return findings


def scope_sources(root: Path, path: Path, sources: dict[Path, str]) -> list[str]:
    return [source for _, source in scope_items(root, path, sources)]


def scope_items(root: Path, path: Path, sources: dict[Path, str]) -> list[tuple[Path, str]]:
    project = path.resolve().relative_to(root).parts[0]
    if project == COMMON_PROJECT:
        return list(sources.items())

    return [(other, source) for other, source in sources.items() if other.resolve().relative_to(root).parts[0] == project]


def files(root: Path, pattern: str) -> list[Path]:
    return [
        path
        for path in root.rglob(pattern)
        if not SKIP_PARTS.intersection(path.relative_to(root).parts) and not path.name.endswith(".g.cs")
    ]


def line_of(text: str, needle: str) -> int:
    index = text.find(needle)
    return text.count("\n", 0, index) + 1 if index >= 0 else 0


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[1] if "}" in tag else tag


def relative(root: Path, path: Path) -> str:
    return str(path.resolve().relative_to(root)).replace("\\", "/")


if __name__ == "__main__":
    sys.exit(main())

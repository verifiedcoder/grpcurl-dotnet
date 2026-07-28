#!/usr/bin/env python3
"""Render THIRD-PARTY-NOTICES.md from packages.lock.json files + the local NuGet cache.

Invoked by generate-third-party-notices.sh; not meant to be run directly. Output is deterministic
(sorted, no timestamps, no machine paths) so `--check` can diff it against the committed file.
"""

from __future__ import annotations

import json
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Lock entries of type "Project" are project references, not distributable third-party packages.
SKIPPED_LOCK_TYPES = {"project"}


def nuget_root() -> Path:
    override = os.environ.get("NUGET_PACKAGES")
    if override:
        return Path(override)
    return Path.home() / ".nuget" / "packages"


def packages_from_lock(lock_path: Path) -> set[tuple[str, str]]:
    data = json.loads(lock_path.read_text(encoding="utf-8"))
    found: set[tuple[str, str]] = set()
    for framework in data.get("dependencies", {}).values():
        for package_id, entry in framework.items():
            if str(entry.get("type", "")).lower() in SKIPPED_LOCK_TYPES:
                continue
            resolved = entry.get("resolved")
            if resolved:
                found.add((package_id, resolved))
    return found


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def read_nuspec(package_dir: Path, package_id: str) -> dict[str, object]:
    """Parse the metadata we cite, namespace-agnostically (nuspec schemas vary by SDK vintage)."""
    nuspec = package_dir / f"{package_id.lower()}.nuspec"
    if not nuspec.is_file():
        candidates = sorted(package_dir.glob("*.nuspec"))
        if not candidates:
            raise FileNotFoundError(f"no .nuspec in {package_dir}")
        nuspec = candidates[0]

    metadata = None
    for element in ET.parse(nuspec).getroot():
        if local_name(element.tag) == "metadata":
            metadata = element
            break
    if metadata is None:
        raise ValueError(f"no <metadata> in {nuspec}")

    info: dict[str, object] = {"developmentDependency": False}
    for child in metadata:
        name = local_name(child.tag)
        text = (child.text or "").strip()
        if name == "license":
            info["licenseType"] = child.get("type", "expression")
            info["license"] = text
        elif name == "developmentDependency":
            info["developmentDependency"] = text.lower() == "true"
        elif name in ("authors", "projectUrl", "licenseUrl", "copyright", "title"):
            info[name] = text
    return info


def license_field(package_dir: Path, info: dict[str, object]) -> tuple[str, str | None]:
    """Return (summary, full text or None)."""
    value = str(info.get("license", "") or "")
    kind = str(info.get("licenseType", "") or "")
    if value and kind == "expression":
        return value, None
    if value and kind == "file":
        license_path = package_dir / value.replace("\\", "/")
        if license_path.is_file():
            return f"see the licence text below ({value})", license_path.read_text(
                encoding="utf-8", errors="replace"
            ).strip()
        return f"declared in the package as {value} (text not present in the package)", None
    url = str(info.get("licenseUrl", "") or "")
    if url:
        return url, None
    return "not declared in the package metadata", None


PREAMBLE = """# Third-party notices

GrpCurl.Net Studio, `grpcn` and `gql2grpc` are published as **self-contained** builds: each release
archive bundles the .NET runtime and the third-party libraries listed below alongside the product
code. The product itself is MIT licensed — see `LICENSE`, which ships in the same archive.

This file is generated from the committed `packages.lock.json` files of the five projects that ship
(`GrpCurl.Net`, `Gql2Grpc`, `GrpCurl.Net.Core`, `GrpCurl.Net.Studio`, `GrpCurl.Net.Studio.ViewModels`)
by `Scripts/package/generate-third-party-notices.sh`. **Do not edit it by hand** — CI regenerates it
and fails the build if it has drifted from the lock files. Build-time-only dependencies (analyzers,
code generators, test frameworks) are excluded: they are never distributed.

## .NET runtime and libraries

Portions of this software are distributed with the **.NET runtime and shared framework**
(`Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and the matching runtime packs), which the
self-contained publish embeds in every archive.

- Publisher: Microsoft Corporation and the .NET Foundation
- Project: <https://github.com/dotnet/runtime>
- Licence: MIT (<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>)

The exact runtime-pack versions are resolved from the SDK pinned in `global.json` at publish time
and are recorded per artifact in the release's CycloneDX SBOM (`*.cdx.json`).

## NuGet packages
"""


def main(argv: list[str]) -> int:
    lock_files = [Path(a) for a in argv[1:]]
    if not lock_files:
        print("usage: third_party_notices.py <packages.lock.json> ...", file=sys.stderr)
        return 2

    packages: set[tuple[str, str]] = set()
    for lock in lock_files:
        if not lock.is_file():
            print(f"third-party-notices: lock file not found: {lock}", file=sys.stderr)
            return 1
        packages |= packages_from_lock(lock)

    root = nuget_root()
    missing: list[str] = []
    entries: list[tuple[str, str, dict[str, object], Path]] = []

    for package_id, version in sorted(packages, key=lambda p: (p[0].lower(), p[1])):
        package_dir = root / package_id.lower() / version.lower()
        if not package_dir.is_dir():
            missing.append(f"{package_id} {version}")
            continue
        entries.append((package_id, version, read_nuspec(package_dir, package_id), package_dir))

    if missing:
        print(
            "third-party-notices: these packages are not in the NuGet cache "
            f"({root}) — run `dotnet restore --locked-mode GrpCurl.Net.slnx` first:",
            file=sys.stderr,
        )
        for name in missing:
            print(f"  - {name}", file=sys.stderr)
        return 1

    out: list[str] = [PREAMBLE]
    shipped = 0
    for package_id, version, info, package_dir in entries:
        if info.get("developmentDependency"):
            continue
        shipped += 1
        summary, text = license_field(package_dir, info)
        out.append(f"### {package_id} {version}\n")
        authors = str(info.get("authors", "") or "").strip()
        if authors:
            out.append(f"- Authors: {authors}")
        project_url = str(info.get("projectUrl", "") or "").strip()
        if project_url:
            out.append(f"- Project: <{project_url}>")
        out.append(f"- Licence: {summary}")
        copyright_text = str(info.get("copyright", "") or "").strip()
        if copyright_text:
            out.append(f"- Copyright: {copyright_text}")
        out.append("")
        if text:
            out.append("```text")
            out.append(text)
            out.append("```")
            out.append("")

    if shipped == 0:
        print("third-party-notices: no shipped packages found — refusing to write an empty file", file=sys.stderr)
        return 1

    sys.stdout.write("\n".join(out).rstrip() + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

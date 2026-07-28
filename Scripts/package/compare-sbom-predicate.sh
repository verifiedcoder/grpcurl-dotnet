#!/usr/bin/env bash
#
# compare-sbom-predicate.sh — assert a verified SBOM attestation describes the SBOM we are shipping.
#
# `gh attestation verify --predicate-type https://cyclonedx.org/bom` proves that *an* SBOM was signed
# for the artifact. It does not prove that the `*.cdx.json` asset published beside the archive is that
# same SBOM — filenames are not evidence. actions/attest embeds the SBOM document verbatim as the
# predicate, so the two must compare equal as JSON values.
#
# Usage: compare-sbom-predicate.sh <verified-predicate.json> <published.cdx.json>
set -euo pipefail

PREDICATE="${1:?usage: compare-sbom-predicate.sh <verified-predicate.json> <published.cdx.json>}"
PUBLISHED="${2:?usage: compare-sbom-predicate.sh <verified-predicate.json> <published.cdx.json>}"

python3 - "$PREDICATE" "$PUBLISHED" <<'PY'
import json
import sys

predicate_path, published_path = sys.argv[1], sys.argv[2]

with open(predicate_path, encoding="utf-8") as fh:
    predicate = json.load(fh)
with open(published_path, encoding="utf-8") as fh:
    published = json.load(fh)

if predicate == published:
    print(f"  sbom predicate matches {published_path}")
    raise SystemExit(0)


def identity(doc):
    meta = (doc.get("metadata") or {}).get("component") or {}
    return {
        "serialNumber": doc.get("serialNumber"),
        "component": f"{meta.get('name')}@{meta.get('version')}",
        "components": sorted(
            f"{c.get('name')}@{c.get('version')}" for c in (doc.get("components") or [])
        ),
    }


attested, shipped = identity(predicate), identity(published)
print(f"attested SBOM does not match the published {published_path}:", file=sys.stderr)
for field in ("serialNumber", "component"):
    if attested[field] != shipped[field]:
        print(f"  {field}: attested {attested[field]!r} != published {shipped[field]!r}", file=sys.stderr)

missing = sorted(set(shipped["components"]) - set(attested["components"]))
extra = sorted(set(attested["components"]) - set(shipped["components"]))
if missing:
    print(f"  components published but not attested: {', '.join(missing)}", file=sys.stderr)
if extra:
    print(f"  components attested but not published: {', '.join(extra)}", file=sys.stderr)
if not (missing or extra) and attested == shipped:
    print("  component sets agree; the documents differ elsewhere", file=sys.stderr)
raise SystemExit(1)
PY

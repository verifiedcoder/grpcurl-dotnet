#!/usr/bin/env bash
#
# generate-sbom.sh — emit a CycloneDX SBOM for each shipped product of one RID (PRD-002).
#
# Run AFTER publish.sh for the same RID: the SBOMs are produced with package restore disabled
# (`-dpr`), so they describe the exact dependency graph that publish just built from
# `obj/project.assets.json` — no second, possibly different, network restore, and no churn of the
# committed packages.lock.json files.
#
# Output lands beside the archives in <staging>/dist, so it flows through the release workflow's
# existing upload/download/SHA256SUMS/`release/*` steps unchanged:
#   grpcn-<rid>-<version>.cdx.json
#   gql2grpc-<rid>-<version>.cdx.json
#   GrpCurlNetStudio-<rid>-<version>.cdx.json
#
# Usage: generate-sbom.sh <rid> <version> [staging-dir]
#
# Note: the SBOM records the NuGet graph. The self-contained .NET runtime packs come from the SDK
# pinned in global.json and are not lock-verified at publish time (see PRD-008).
set -euo pipefail

RID="${1:?usage: generate-sbom.sh <rid> <version> [staging-dir]}"
VERSION="${2:?usage: generate-sbom.sh <rid> <version> [staging-dir]}"
STAGING="${3:-artifacts/release}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

DIST="$STAGING/dist"
mkdir -p "$DIST"

# Version-pinned in .config/dotnet-tools.json — no floating `dotnet tool install` in the release path.
dotnet tool restore

sbom() {
  local proj="$1" friendly="$2"
  local out="$friendly-$RID-$VERSION.cdx.json"
  echo "==> SBOM $out"
  if [ ! -f "$(dirname "$proj")/obj/project.assets.json" ]; then
    echo "generate-sbom: $proj has no obj/project.assets.json — run publish.sh $RID first" >&2
    exit 1
  fi
  dotnet dotnet-CycloneDX "$proj" \
    --output "$DIST" \
    --filename "$out" \
    --output-format Json \
    --runtime "$RID" \
    --set-name "$friendly" \
    --set-version "$VERSION" \
    --exclude-dev \
    --exclude-test-projects \
    --recursive \
    --disable-package-restore
  [ -s "$DIST/$out" ] || { echo "generate-sbom: $out was not written" >&2; exit 1; }
}

echo "### generate-sbom.sh  rid=$RID  version=$VERSION"

sbom "Src/GrpCurl.Net/GrpCurl.Net.csproj"                   "grpcn"
sbom "Src/Gql2Grpc/Gql2Grpc.csproj"                         "gql2grpc"
sbom "Src/GrpCurl.Net.Studio/GrpCurl.Net.Studio.csproj"     "GrpCurlNetStudio"

echo "### done: SBOMs in $DIST"
ls -1 "$DIST"/*.cdx.json

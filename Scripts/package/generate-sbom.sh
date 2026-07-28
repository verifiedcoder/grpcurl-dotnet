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

# shellcheck source=Scripts/package/runtime-pack.sh
. "$ROOT/Scripts/package/runtime-pack.sh"

DIST="$STAGING/dist"
mkdir -p "$DIST"

# Version-pinned in .config/dotnet-tools.json — no floating `dotnet tool install` in the release path.
dotnet tool restore

# add_runtime_packs <sbom-file> <project-csproj> — CycloneDX reads the NuGet project graph, which by
# construction excludes the .NET runtime pack a self-contained publish embeds (it is an SDK-resolved
# pack, absent from packages.lock.json and project.assets.json). Without this the SBOM would omit the
# largest single part of the shipped payload, so merge each pack in as a first-class component, with
# the same SHA-512 NuGet itself records for the package.
add_runtime_packs() {
  local sbom="$1" proj="$2" pack id ver nupkg hash rootref tmp added=0
  rootref="$(jq -r '.metadata.component["bom-ref"] // empty' "$sbom")"

  while read -r pack; do
    [ -n "$pack" ] || continue
    id="${pack%%/*}"; ver="${pack##*/}"
    # The install guide promises every SBOM records this hash, so a pack we cannot hash is a hard
    # failure, not a component quietly emitted without `hashes`. A pack resolved from the SDK's
    # packs/ folder has no .nupkg and lands here — deliberately loud, so the contract cannot rot.
    nupkg="$(runtime_pack_nupkg "$id" "$ver")" || {
      echo "generate-sbom: no .nupkg for $id $ver — cannot record the SHA-512 the release contract promises" >&2
      exit 1
    }
    hash="$(openssl dgst -sha512 -hex "$nupkg" | awk '{print $NF}')"
    if [[ ! "$hash" =~ ^[0-9a-f]{128}$ ]]; then
      echo "generate-sbom: openssl produced no usable SHA-512 for $nupkg (got '${hash}')" >&2
      exit 1
    fi
    tmp="$sbom.tmp"
    jq --arg id "$id" --arg ver "$ver" --arg hash "$hash" --arg rootref "$rootref" '
      ($ARGS.named.id + "@" + $ARGS.named.ver) as $ref
      | ("pkg:nuget/" + $ARGS.named.id + "@" + $ARGS.named.ver) as $purl
      | .components += [
          {
            "type": "framework",
            "bom-ref": $ref,
            "name": $id,
            "version": $ver,
            "purl": $purl,
            "publisher": "Microsoft Corporation",
            "description": "Self-contained .NET runtime pack embedded in this artifact.",
            "licenses": [ { "license": { "id": "MIT" } } ],
            "externalReferences": [
              { "type": "distribution", "url": "https://github.com/dotnet/runtime" },
              { "type": "license", "url": "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT" }
            ],
            "hashes": [ { "alg": "SHA-512", "content": $hash } ]
          }
        ]
      | if (.dependencies? and $rootref != "")
        then .dependencies = ((.dependencies | map(
               if .ref == $rootref then .dependsOn = ((.dependsOn // []) + [$ref] | unique) else . end))
               + [ { "ref": $ref, "dependsOn": [] } ])
        else . end
    ' "$sbom" > "$tmp" && mv "$tmp" "$sbom"
    echo "  + runtime pack $id $ver (sha512 recorded)"
    added=1
  done < <(runtime_packs "$proj" "$RID")

  if [ "$added" -eq 0 ]; then
    echo "generate-sbom: no runtime pack found for $proj ($RID) — the SBOM would omit the embedded runtime" >&2
    exit 1
  fi
}

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
  add_runtime_packs "$DIST/$out" "$proj"
}

echo "### generate-sbom.sh  rid=$RID  version=$VERSION"

sbom "Src/GrpCurl.Net/GrpCurl.Net.csproj"                   "grpcn"
sbom "Src/Gql2Grpc/Gql2Grpc.csproj"                         "gql2grpc"
sbom "Src/GrpCurl.Net.Studio/GrpCurl.Net.Studio.csproj"     "GrpCurlNetStudio"

echo "### done: SBOMs in $DIST"
ls -1 "$DIST"/*.cdx.json

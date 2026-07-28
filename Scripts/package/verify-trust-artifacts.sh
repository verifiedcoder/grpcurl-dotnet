#!/usr/bin/env bash
#
# verify-trust-artifacts.sh — gate a staged release directory before a GitHub Release is drafted.
#
# PRD-002 requires that a release cannot be created when any trust artifact is missing or invalid.
# This script is that gate, and it is deliberately a script (not inline workflow YAML) so the same
# checks can be run against a locally staged directory.
#
# For <release-dir> it asserts:
#   * every archive has a matching, well-formed CycloneDX SBOM;
#   * every archive ships LICENSE and THIRD-PARTY-NOTICES.md (in Contents/Resources for the .app);
#   * SHA256SUMS exists, is complete, and every recorded hash matches;
#   * SHA256SUMS.sigstore.json (the keyless cosign signature) exists;
#   * the full 6 RIDs x 3 products asset set is present.
#
# Usage: verify-trust-artifacts.sh <release-dir> [--allow-partial] [--skip-signature]
#   --allow-partial    do not require the complete 6-RID x 3-product matrix (single-RID local runs)
#   --skip-signature   do not require SHA256SUMS.sigstore.json (local runs without cosign)
set -euo pipefail

DIR="${1:?usage: verify-trust-artifacts.sh <release-dir> [--allow-partial] [--skip-signature]}"
shift || true

ALLOW_PARTIAL=0
SKIP_SIGNATURE=0
for arg in "$@"; do
  case "$arg" in
    --allow-partial)  ALLOW_PARTIAL=1 ;;
    --skip-signature) SKIP_SIGNATURE=1 ;;
    *) echo "verify-trust-artifacts: unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ ! -d "$DIR" ]; then
  echo "verify-trust-artifacts: not a directory: $DIR" >&2
  exit 1
fi
if ! command -v python3 >/dev/null 2>&1; then
  echo "verify-trust-artifacts: python3 is required" >&2
  exit 1
fi

PRODUCTS=(grpcn gql2grpc GrpCurlNetStudio)
RIDS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)
# The runtime files are not optional extras: a self-contained archive is mostly .NET runtime, and the
# runtime pack carries its own attributions that the NuGet-graph notices cannot cover.
LEGAL=(LICENSE THIRD-PARTY-NOTICES.md LICENSE.dotnet-runtime.txt THIRD-PARTY-NOTICES.dotnet-runtime.txt)

FAILURES=0
fail() { echo "  FAIL: $*" >&2; FAILURES=$((FAILURES + 1)); }
ok()   { echo "  ok:   $*"; }

# list_archive <archive> — print the archive's member paths, one per line.
list_archive() {
  case "$1" in
    *.tar.gz) tar -tzf "$1" ;;
    *.zip)    python3 -c 'import sys,zipfile;print("\n".join(zipfile.ZipFile(sys.argv[1]).namelist()))' "$1" ;;
    *)        echo "verify-trust-artifacts: unsupported archive: $1" >&2; return 1 ;;
  esac
}

echo "### verify-trust-artifacts: $DIR"

# ---------------------------------------------------------------- archives + SBOMs + legal material
shopt -s nullglob
ARCHIVES=("$DIR"/*.tar.gz "$DIR"/*.zip)
shopt -u nullglob

if [ ${#ARCHIVES[@]} -eq 0 ]; then
  fail "no archives found in $DIR"
fi

for archive in "${ARCHIVES[@]}"; do
  base="$(basename "$archive")"
  stem="${base%.zip}"; stem="${stem%.tar.gz}"

  # <product>-<rid>-<version>: the RID decides which runtime pack the SBOM must account for.
  archive_rid=""
  for rid in "${RIDS[@]}"; do
    case "$stem" in *"-$rid-"*) archive_rid="$rid" ;; esac
  done
  [ -n "$archive_rid" ] || fail "$base does not carry a known RID in its name"

  sbom="$DIR/$stem.cdx.json"
  if [ ! -f "$sbom" ]; then
    fail "$base has no SBOM ($stem.cdx.json)"
  else
    sbom_error="$(python3 -c '
import json, sys

path, rid = sys.argv[1], sys.argv[2]
try:
    with open(path, encoding="utf-8") as fh:
        doc = json.load(fh)
except Exception as exc:                      # noqa: BLE001 - reported verbatim to the operator
    print(f"not readable as JSON: {exc}")
    raise SystemExit(0)

if doc.get("bomFormat") != "CycloneDX":
    print("not a CycloneDX document")
    raise SystemExit(0)
components = doc.get("components") or []
if not components:
    print("lists no components")
    raise SystemExit(0)

# The self-contained runtime pack is the biggest single part of the payload and is absent from the
# NuGet project graph, so its presence is the check that the SBOM inventories what actually ships.
suffix = ".App.Runtime." + rid
runtime = [c for c in components if str(c.get("name", "")).endswith(suffix)]
if not runtime:
    print(f"no *{suffix} runtime-pack component")
    raise SystemExit(0)
for component in runtime:
    name = str(component.get("name"))
    if not component.get("version"):
        print("runtime component " + name + " has no version")
        raise SystemExit(0)
    # The install guide promises the pack is recorded with the SHA-512 NuGet holds for it, so an
    # unhashed component is a broken promise, not a cosmetic omission.
    digests = [
        str(h.get("content", ""))
        for h in (component.get("hashes") or [])
        if str(h.get("alg", "")).upper() == "SHA-512"
    ]
    if not digests:
        print("runtime component " + name + " has no SHA-512 hash")
        raise SystemExit(0)
    for digest in digests:
        if len(digest) != 128 or any(c not in "0123456789abcdef" for c in digest.lower()):
            print("runtime component " + name + " has a malformed SHA-512 (" + digest[:16] + "...)")
            raise SystemExit(0)
' "$sbom" "$archive_rid" 2>&1)"
    if [ -n "$sbom_error" ]; then
      fail "$stem.cdx.json $sbom_error"
    else
      ok "$base -> $stem.cdx.json"
    fi
  fi

  members="$(list_archive "$archive")" || { fail "$base could not be listed"; continue; }
  for legal in "${LEGAL[@]}"; do
    # Plain archives carry the file at the root; the macOS .app carries it in Contents/Resources.
    # A here-string, not a pipe: `grep -q` exits at the first match, and under `set -o pipefail`
    # the resulting SIGPIPE on the writer would fail the whole pipeline on long listings.
    if ! grep -qE "(^|/)(Contents/Resources/)?${legal}$" <<< "$members"; then
      fail "$base does not contain $legal"
    fi
  done
done

# ------------------------------------------------------------------------ completeness of the matrix
if [ "$ALLOW_PARTIAL" -eq 0 ]; then
  for rid in "${RIDS[@]}"; do
    case "$rid" in win-*) ext="zip" ;; *) ext="tar.gz" ;; esac
    for product in "${PRODUCTS[@]}"; do
      shopt -s nullglob
      found=("$DIR/$product-$rid-"*".$ext")
      shopt -u nullglob
      [ ${#found[@]} -gt 0 ] || fail "missing archive for $product / $rid"
    done
  done
fi

# --------------------------------------------------------------------------------- checksum manifest
if [ ! -f "$DIR/SHA256SUMS" ]; then
  fail "SHA256SUMS is missing"
else
  # sha256sum lines are "<hash>  <name>" — compare the name field exactly.
  manifest_has() { awk -v want="$1" '{ $1=""; sub(/^ +/, ""); if ($0 == want) found=1 } END { exit !found }' "$DIR/SHA256SUMS"; }
  for archive in "${ARCHIVES[@]}"; do
    base="$(basename "$archive")"
    manifest_has "$base" || fail "SHA256SUMS has no entry for $base"
  done
  for sbom in "$DIR"/*.cdx.json; do
    [ -e "$sbom" ] || continue
    base="$(basename "$sbom")"
    manifest_has "$base" || fail "SHA256SUMS has no entry for $base"
  done
  if ( cd "$DIR" && sha256sum -c SHA256SUMS >/dev/null 2>&1 ); then
    ok "SHA256SUMS verifies"
  else
    fail "SHA256SUMS does not verify against the staged files"
  fi
fi

# ------------------------------------------------------------------------------- publisher signature
if [ "$SKIP_SIGNATURE" -eq 0 ]; then
  if [ -f "$DIR/SHA256SUMS.sigstore.json" ]; then
    ok "SHA256SUMS.sigstore.json present"
  else
    fail "SHA256SUMS.sigstore.json (keyless cosign signature) is missing"
  fi
fi

if [ "$FAILURES" -ne 0 ]; then
  echo "verify-trust-artifacts: $FAILURES problem(s) — refusing to release" >&2
  exit 1
fi

SUMMARY="every asset has an SBOM, legal material, and a checksum"
[ "$SKIP_SIGNATURE" -eq 0 ] && SUMMARY="$SUMMARY, and the manifest is signed"
[ "$ALLOW_PARTIAL" -eq 1 ] && SUMMARY="$SUMMARY (partial set — matrix completeness not checked)"
echo "verify-trust-artifacts: OK — $SUMMARY"

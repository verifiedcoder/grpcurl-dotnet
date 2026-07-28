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
LEGAL=(LICENSE THIRD-PARTY-NOTICES.md)

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

  sbom="$DIR/$stem.cdx.json"
  if [ ! -f "$sbom" ]; then
    fail "$base has no SBOM ($stem.cdx.json)"
  elif ! python3 -c '
import json, sys
with open(sys.argv[1], encoding="utf-8") as fh:
    doc = json.load(fh)
assert doc.get("bomFormat") == "CycloneDX", "not a CycloneDX document"
assert doc.get("components"), "SBOM lists no components"
' "$sbom" 2>/dev/null; then
    fail "$stem.cdx.json is not a well-formed, non-empty CycloneDX SBOM"
  else
    ok "$base -> $stem.cdx.json"
  fi

  members="$(list_archive "$archive")" || { fail "$base could not be listed"; continue; }
  for legal in "${LEGAL[@]}"; do
    # Plain archives carry the file at the root; the macOS .app carries it in Contents/Resources.
    if ! printf '%s\n' "$members" | grep -qE "(^|/)(Contents/Resources/)?${legal}$"; then
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

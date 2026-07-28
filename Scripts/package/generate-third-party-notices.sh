#!/usr/bin/env bash
#
# generate-third-party-notices.sh — regenerate THIRD-PARTY-NOTICES.md from the committed lock files.
#
# Every release archive ships LICENSE + THIRD-PARTY-NOTICES.md (PRD-002). The notices are generated
# offline: the package set comes from the shipped projects' packages.lock.json, and the metadata
# (authors, license, project URL, copyright) comes from each package's .nuspec in the local NuGet
# cache — no network call and no extra tool download in the release path.
#
# Usage: generate-third-party-notices.sh [--check]
#   (no args)  rewrite THIRD-PARTY-NOTICES.md at the repo root
#   --check    regenerate into a temp file and diff; exit 1 if the committed file is stale (CI gate)
#
# Requires a completed `dotnet restore` (the .nuspec files must be in the NuGet cache) and python3.
set -euo pipefail

MODE="${1:-write}"
case "$MODE" in
  write|--check) ;;
  *) echo "usage: generate-third-party-notices.sh [--check]" >&2; exit 2 ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

if ! command -v python3 >/dev/null 2>&1; then
  echo "generate-third-party-notices: python3 is required" >&2
  exit 1
fi

OUT="$ROOT/THIRD-PARTY-NOTICES.md"
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

# A local `publish.sh` run restores unlocked and adds RID-specific entries (e.g.
# Microsoft.NET.ILLink.Tasks) to the lock files. Regenerating on top of that churn bakes packages
# into the notices that CI — which regenerates from the committed lock files — will not produce, and
# the --check gate then fails. A dependency bump makes the lock files dirty too, so warn, don't fail.
if [ "$MODE" = "write" ] && command -v git >/dev/null 2>&1; then
  DIRTY="$(git -C "$ROOT" status --porcelain -- '*packages.lock.json' 2>/dev/null || true)"
  if [ -n "$DIRTY" ]; then
    echo "generate-third-party-notices: WARNING — these lock files have uncommitted changes:" >&2
    printf '%s\n' "$DIRTY" >&2
    echo "If that is churn from a local publish.sh run, revert it first: git checkout -- '*packages.lock.json'" >&2
  fi
fi

# Only the projects that actually ship inside a release archive. Test/tool/benchmark projects are
# excluded deliberately — their dependencies are never distributed.
python3 Scripts/package/third_party_notices.py \
  Src/GrpCurl.Net/packages.lock.json \
  Src/Gql2Grpc/packages.lock.json \
  Src/GrpCurl.Net.Core/packages.lock.json \
  Src/GrpCurl.Net.Studio/packages.lock.json \
  Src/GrpCurl.Net.Studio.ViewModels/packages.lock.json \
  > "$TMP"

if [ "$MODE" = "--check" ]; then
  if diff -u "$OUT" "$TMP" > /dev/null 2>&1; then
    echo "generate-third-party-notices: OK — THIRD-PARTY-NOTICES.md is up to date"
    exit 0
  fi
  echo "generate-third-party-notices: STALE — THIRD-PARTY-NOTICES.md does not match the lock files." >&2
  echo "Run: bash Scripts/package/generate-third-party-notices.sh   (and commit the result)" >&2
  echo >&2
  diff -u "$OUT" "$TMP" >&2 || true
  exit 1
fi

cp "$TMP" "$OUT"
echo "generate-third-party-notices: wrote $OUT"

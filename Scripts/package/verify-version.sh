#!/usr/bin/env bash
#
# verify-version.sh — assert a published binary reports the version we built it with.
#
# Guards against a release whose in-app/CLI version disagrees with its tag, which would
# make the Studio update check mislead (show "update available" against itself, or miss one).
# Only meaningful for a host-runnable binary (same arch as the runner).
#
# Usage: verify-version.sh <expected-version> <path-to-executable>
#   <expected-version>  X.Y.Z (no leading v)
#   <path-to-executable> a self-contained grpcn/gql2grpc binary that accepts --version
set -euo pipefail

EXPECTED="${1:?usage: verify-version.sh <expected-version> <exe>}"
EXE="${2:?usage: verify-version.sh <expected-version> <exe>}"

if [ ! -x "$EXE" ]; then
  echo "verify-version: not executable: $EXE" >&2
  exit 1
fi

# System.CommandLine prints the full AssemblyInformationalVersion (e.g. "1.2.3+abc1234").
# Compare only the release-version core (the part before '+'), mirroring how Studio's
# UpdateService.ReadVersion() trims build metadata for the update check.
RAW="$("$EXE" --version 2>/dev/null | tr -d '\r' | head -1)"
ACTUAL="${RAW%%+*}"

if [ "$ACTUAL" = "$EXPECTED" ]; then
  echo "verify-version: OK — $EXE reports $RAW (core $ACTUAL)"
  exit 0
fi

echo "verify-version: MISMATCH — expected '$EXPECTED', got '$RAW' (core '$ACTUAL') from $EXE" >&2
exit 1

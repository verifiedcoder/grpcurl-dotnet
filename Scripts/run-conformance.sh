#!/usr/bin/env bash
# Runs the connectrpc/conformance suite (client mode) against the GrpCurl.Net
# conformance adapter, which drives the product's own DynamicInvoker code path.
#
# Usage:
#   Scripts/run-conformance.sh                       # full declared matrix
#   Scripts/run-conformance.sh --run '<pattern>' -v --trace   # iterate on one case
#
# Extra arguments are passed through to connectconformance.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFORMANCE_VERSION="v1.0.5"
TOOLS_DIR="$REPO_ROOT/Tests/GrpCurl.Net.Conformance/.tools"
RUNNER="$TOOLS_DIR/connectconformance"

cd "$REPO_ROOT"

if [[ ! -x "$RUNNER" ]]; then
    asset="connectconformance-$CONFORMANCE_VERSION-$(uname -s)-$(uname -m).tar.gz"
    echo "Downloading $asset ..." >&2
    mkdir -p "$TOOLS_DIR"
    curl -fsSL "https://github.com/connectrpc/conformance/releases/download/$CONFORMANCE_VERSION/$asset" \
        | tar -xz -C "$TOOLS_DIR"
fi

dotnet build Tests/GrpCurl.Net.Conformance/GrpCurl.Net.Conformance.csproj -c Release --nologo -v q

# IMPORTANT: the command under test must be `dotnet exec <dll>`, never `dotnet run` —
# build/restore chatter on stdout would corrupt the runner's binary frame protocol.
exec "$RUNNER" --mode client \
    --conf Tests/GrpCurl.Net.Conformance/conformance-config.yaml \
    --known-failing @Tests/GrpCurl.Net.Conformance/known-failing.txt \
    "$@" \
    -- dotnet exec Tests/GrpCurl.Net.Conformance/bin/Release/net10.0/GrpCurl.Net.Conformance.dll

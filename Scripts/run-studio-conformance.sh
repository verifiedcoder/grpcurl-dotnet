#!/usr/bin/env bash
# Runs the connectrpc/conformance suite against the GrpCurl.Net Studio adapter, which issues
# every RPC through the app's IInvocationService. Unary subset (E1.4). Usage mirrors
# run-conformance.sh; extra args (e.g. --run '<pattern>' -v --trace) are forwarded to the runner.
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

dotnet build Tests/GrpCurl.Net.Studio.Conformance/GrpCurl.Net.Studio.Conformance.csproj -c Release --nologo -v q

exec "$RUNNER" --mode client \
    --conf Tests/GrpCurl.Net.Studio.Conformance/conformance-config.yaml \
    --known-failing @Tests/GrpCurl.Net.Studio.Conformance/known-failing.txt \
    "$@" \
    -- dotnet exec Tests/GrpCurl.Net.Studio.Conformance/bin/Release/net10.0/GrpCurl.Net.Studio.Conformance.dll

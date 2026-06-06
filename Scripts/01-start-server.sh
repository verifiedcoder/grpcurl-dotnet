#!/bin/bash
# =============================================================================
# Script: 01-start-server.sh
# Purpose: Start the TestServer for demo scripts
# Prerequisites: dotnet SDK installed
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
PROJECT_FILE="$(grpcurl_dotnet_path "${GRPCURL_DOTNET_REPO_ROOT}/Tests/GrpCurl.Net.TestServer/GrpCurl.Net.TestServer.csproj")"

echo "=== Starting GrpCurl.Net TestServer ==="
echo ""
echo "Server will start on localhost:9090"
echo "Press Ctrl+C to stop the server"
echo ""

cd "$GRPCURL_DOTNET_REPO_ROOT"
exec "${GRPCURL_DOTNET_DOTNET}" run --project "$PROJECT_FILE"

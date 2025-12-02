#!/bin/bash
# =============================================================================
# Script: 01-start-server.sh
# Purpose: Start the TestServer for demo scripts
# Prerequisites: dotnet SDK installed
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."

echo "=== Starting GrpCurl.Net TestServer ==="
echo ""
echo "Server will start on localhost:9090"
echo "Press Ctrl+C to stop the server"
echo ""

cd "$PROJECT_DIR"
dotnet run --project Tests/GrpCurl.Net.TestServer

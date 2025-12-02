#!/bin/bash
# =============================================================================
# Script: 25-user-agent.sh
# Purpose: Set custom User-Agent header
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Custom User-Agent ==="
echo ""
echo "Use --user-agent to set a custom User-Agent header."
echo "Useful for identifying client applications in server logs."
echo ""

echo "--- Default User-Agent ---"
echo "Command: grpcurl invoke --plaintext -v $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext -v $SERVER testing.TestService/EmptyCall 2>&1 | head -20

echo ""
echo "--- Custom User-Agent ---"
echo "Command: grpcurl invoke --plaintext --user-agent 'MyApp/1.0 (Demo Script)' -v $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --user-agent "MyApp/1.0 (Demo Script)" -v $SERVER testing.TestService/EmptyCall 2>&1 | head -20

echo ""
echo "=== Done ==="

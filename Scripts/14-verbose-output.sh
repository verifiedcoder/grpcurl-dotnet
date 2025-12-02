#!/bin/bash
# =============================================================================
# Script: 14-verbose-output.sh
# Purpose: Demonstrate verbose and very verbose output modes
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Verbose Output Modes ==="
echo ""

echo "--- Standard output (no verbose) ---"
echo "Command: grpcurl invoke --plaintext $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Verbose output (-v) ---"
echo "Shows request/response metadata and headers"
echo "Command: grpcurl invoke --plaintext -v $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Very verbose output (--vv) ---"
echo "Shows detailed timing information"
echo "Command: grpcurl invoke --plaintext --vv $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --vv $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

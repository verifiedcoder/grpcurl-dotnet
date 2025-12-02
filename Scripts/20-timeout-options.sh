#!/bin/bash
# =============================================================================
# Script: 20-timeout-options.sh
# Purpose: Connection and operation timeouts
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Timeout Options ==="
echo ""
echo "GrpCurl.Net supports two timeout options:"
echo "  --connect-timeout : Maximum time to establish connection (default: 10s)"
echo "  --max-time        : Maximum total operation time (sets gRPC deadline)"
echo ""
echo "Supported formats: '10s', '500ms', '1m', '1h'"
echo ""

echo "--- Connect timeout (5 seconds) ---"
echo "Command: grpcurl invoke --plaintext --connect-timeout 5s $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --connect-timeout 5s $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Max operation time (30 seconds) ---"
echo "Command: grpcurl invoke --plaintext --max-time 30s $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --max-time 30s $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Both timeouts combined ---"
echo "Command: grpcurl invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

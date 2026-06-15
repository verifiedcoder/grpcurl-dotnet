#!/bin/bash
# =============================================================================
# Script: 14-verbose-output.sh
# Purpose: Demonstrate verbose and very verbose output modes
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Verbose Output Modes ==="
echo ""

echo "--- Standard output (no verbose) ---"
echo "Command: grpcn invoke --plaintext --max-time 10s $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --max-time 10s $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Verbose output (-v) ---"
echo "Shows request/response metadata and headers. Sensitive metadata is redacted by default."
echo "Command: grpcn invoke --plaintext --max-time 10s -v -H 'Authorization: Bearer demo-token' $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --max-time 10s -v -H "Authorization: Bearer demo-token" $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Unsafe verbose output (opt-in) ---"
echo "Use --unsafe-show-secrets only when the terminal/log destination is trusted."
echo "Command: grpcn invoke --plaintext --max-time 10s -v --unsafe-show-secrets -H 'Authorization: Bearer demo-token' $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --max-time 10s -v --unsafe-show-secrets -H "Authorization: Bearer demo-token" $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Very verbose output (--vv) ---"
echo "Shows detailed timing information"
echo "Command: grpcn invoke --plaintext --max-time 10s --vv $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --max-time 10s --vv $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

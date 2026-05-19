#!/bin/bash
# =============================================================================
# Script: 17-reflect-vs-rpc-headers.sh
# Purpose: Differentiate reflection vs RPC headers
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Header Types ==="
echo ""
echo "GrpCurl.Net supports three types of headers:"
echo "  -H, --header        : Sent with BOTH reflection and RPC calls"
echo "  --reflect-header    : Sent ONLY with reflection calls"
echo "  --rpc-header        : Sent ONLY with RPC calls"
echo ""

echo "--- Using -H (sent to both reflection and RPC) ---"
echo "Command: grpcurl.net invoke --plaintext --max-time 10s -H 'X-All: both' -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s -H "X-All: both" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Using --rpc-header (sent only to RPC, not reflection) ---"
echo "Command: grpcurl.net invoke --plaintext --max-time 10s --rpc-header 'X-RPC-Only: value' -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s --rpc-header "X-RPC-Only: value" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Using --reflect-header (sent only to reflection) ---"
echo "This is useful when the reflection endpoint requires different authentication."
echo "Command: grpcurl.net list --plaintext --max-time 10s --reflect-header 'X-Reflect-Auth: secret' $SERVER"
echo ""
grpcurl_net list --plaintext --max-time 10s --reflect-header "X-Reflect-Auth: secret" $SERVER

echo ""
echo "=== Done ==="

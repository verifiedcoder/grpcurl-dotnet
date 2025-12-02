#!/bin/bash
# =============================================================================
# Script: 17-reflect-vs-rpc-headers.sh
# Purpose: Differentiate reflection vs RPC headers
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Header Types ==="
echo ""
echo "GrpCurl.Net supports three types of headers:"
echo "  -H, --header        : Sent with BOTH reflection and RPC calls"
echo "  --reflect-header    : Sent ONLY with reflection calls"
echo "  --rpc-header        : Sent ONLY with RPC calls"
echo ""

echo "--- Using -H (sent to both reflection and RPC) ---"
echo "Command: grpcurl invoke --plaintext -H 'X-All: both' -v $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext -H "X-All: both" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Using --rpc-header (sent only to RPC, not reflection) ---"
echo "Command: grpcurl invoke --plaintext --rpc-header 'X-RPC-Only: value' -v $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --rpc-header "X-RPC-Only: value" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Using --reflect-header (sent only to reflection) ---"
echo "This is useful when the reflection endpoint requires different authentication."
echo "Command: grpcurl list --plaintext --reflect-header 'X-Reflect-Auth: secret' $SERVER"
echo ""
$GRPCURL list --plaintext --reflect-header "X-Reflect-Auth: secret" $SERVER

echo ""
echo "=== Done ==="

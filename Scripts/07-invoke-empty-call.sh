#!/bin/bash
# =============================================================================
# Script: 07-invoke-empty-call.sh
# Purpose: Basic unary RPC with empty message
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Invoke EmptyCall (Simplest Unary RPC) ==="
echo ""
echo "EmptyCall takes an empty request and returns an empty response."
echo "This is the simplest possible gRPC invocation."
echo ""
echo "Command: grpcurl invoke --plaintext $SERVER testing.TestService/EmptyCall"
echo ""

$GRPCURL invoke --plaintext $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

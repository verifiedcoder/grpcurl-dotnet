#!/bin/bash
# =============================================================================
# Script: 10-invoke-server-streaming.sh
# Purpose: Server streaming RPC - single request, multiple responses
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Server Streaming RPC ==="
echo ""
echo "StreamingOutputCall sends one request and receives multiple responses."
echo "The response_parameters array controls the size of each response."
echo ""

echo "--- Request for 3 responses of sizes 10, 20, 30 bytes ---"
REQUEST='{"response_parameters":[{"size":10},{"size":20},{"size":30}]}'
echo "Command: grpcurl invoke --plaintext -d '$REQUEST' $SERVER testing.TestService/StreamingOutputCall"
echo ""

$GRPCURL invoke --plaintext -d "$REQUEST" $SERVER testing.TestService/StreamingOutputCall

echo ""
echo "=== Done ==="

#!/bin/bash
# =============================================================================
# Script: 27-concatenated-json.sh
# Purpose: Demonstrate concatenated JSON input for streaming methods
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Concatenated JSON Input for Streaming ==="
echo ""
echo "GrpCurl.Net supports concatenated JSON objects as inline data for"
echo "streaming methods. Each top-level JSON object is sent as a separate"
echo "request message."
echo ""

# --- Client Streaming with Concatenated JSON ---
echo "--- Client Streaming: 3 messages via concatenated JSON ---"
echo "Command: grpcurl.net invoke --plaintext -d '{...} {...} {...}' \$SERVER testing.TestService/StreamingInputCall"
echo ""

$GRPCURL invoke --plaintext \
  -d '{"payload":{"body":"YQ=="}} {"payload":{"body":"YmI="}} {"payload":{"body":"Y2Nj"}}' \
  $SERVER testing.TestService/StreamingInputCall

echo ""

# --- Bidirectional Streaming with Concatenated JSON ---
echo "--- Bidirectional Streaming: 2 messages via concatenated JSON ---"
echo "Command: grpcurl.net invoke --plaintext -d '{...} {...}' \$SERVER testing.TestService/FullDuplexCall"
echo ""

$GRPCURL invoke --plaintext \
  -d '{"response_parameters":[{"size":5}]} {"response_parameters":[{"size":10}]}' \
  $SERVER testing.TestService/FullDuplexCall

echo ""
echo "=== Done ==="

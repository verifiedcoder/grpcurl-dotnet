#!/bin/bash
# =============================================================================
# Script: 27-concatenated-json.sh
# Purpose: Demonstrate concatenated JSON input for streaming methods
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Concatenated JSON Input for Streaming ==="
echo ""
echo "GrpCurl.Net supports concatenated JSON objects as inline data for"
echo "streaming methods. Each top-level JSON object is sent as a separate"
echo "request message."
echo "When you read streaming messages from stdin with -d @, use"
echo "--max-stdin-bytes to make the input budget explicit in scripts."
echo ""

# --- Client Streaming with Concatenated JSON ---
echo "--- Client Streaming: 3 messages via concatenated JSON ---"
echo "Command: grpcn invoke --plaintext --max-time 10s -d '{...} {...} {...}' \$SERVER testing.TestService/StreamingInputCall"
echo ""

grpcn invoke --plaintext \
  --max-time 10s \
  -d '{"payload":{"body":"YQ=="}} {"payload":{"body":"YmI="}} {"payload":{"body":"Y2Nj"}}' \
  $SERVER testing.TestService/StreamingInputCall

echo ""

# --- Bidirectional Streaming with Concatenated JSON ---
echo "--- Bidirectional Streaming: 2 messages via concatenated JSON ---"
echo "Command: grpcn invoke --plaintext --max-time 10s -d '{...} {...}' \$SERVER testing.TestService/FullDuplexCall"
echo ""

grpcn invoke --plaintext \
  --max-time 10s \
  -d '{"response_parameters":[{"size":5}]} {"response_parameters":[{"size":10}]}' \
  $SERVER testing.TestService/FullDuplexCall

echo ""
echo "--- Client Streaming: stdin with explicit max stdin size ---"
echo "Command: printf '<json objects>' | grpcn invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ \$SERVER testing.TestService/StreamingInputCall"
echo ""

printf '%s\n%s\n' \
  '{"payload":{"body":"ZA=="}}' \
  '{"payload":{"body":"ZWU="}}' | \
  grpcn invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ $SERVER testing.TestService/StreamingInputCall

echo ""
echo "=== Done ==="

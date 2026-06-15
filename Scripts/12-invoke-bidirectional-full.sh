#!/bin/bash
# =============================================================================
# Script: 12-invoke-bidirectional-full.sh
# Purpose: Full duplex bidirectional streaming
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Full Duplex Bidirectional Streaming ==="
echo ""
echo "FullDuplexCall allows simultaneous bidirectional communication."
echo "Server responds to each request immediately as it's received."
echo ""

# Create multiple requests
REQUESTS=$(cat <<'EOF'
{"response_parameters":[{"size":5}]}
{"response_parameters":[{"size":10}]}
{"response_parameters":[{"size":15}]}
EOF
)

echo "Sending 3 requests, each requesting a response of different size:"
echo "$REQUESTS"
echo ""

echo "Command: echo '<requests>' | grpcn invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ $SERVER testing.TestService/FullDuplexCall"
echo "Uses --max-stdin-bytes to make the stdin budget explicit for scripts."
echo ""

echo "$REQUESTS" | grpcn invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ $SERVER testing.TestService/FullDuplexCall

echo ""
echo "=== Done ==="

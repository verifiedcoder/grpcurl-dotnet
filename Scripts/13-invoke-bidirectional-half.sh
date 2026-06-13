#!/bin/bash
# =============================================================================
# Script: 13-invoke-bidirectional-half.sh
# Purpose: Half duplex bidirectional streaming (buffered)
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Half Duplex Bidirectional Streaming ==="
echo ""
echo "HalfDuplexCall buffers all client requests before sending responses."
echo "Unlike FullDuplexCall, responses are not sent until all requests are received."
echo ""

# Create multiple requests
REQUESTS=$(cat <<'EOF'
{"response_parameters":[{"size":8}]}
{"response_parameters":[{"size":16}]}
{"response_parameters":[{"size":24}]}
EOF
)

echo "Sending 3 requests (server buffers all, then responds):"
echo "$REQUESTS"
echo ""

echo "Command: echo '<requests>' | grpcn invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ $SERVER testing.TestService/HalfDuplexCall"
echo "Uses --max-stdin-bytes to make the stdin budget explicit for scripts."
echo ""

echo "$REQUESTS" | grpcurl_net invoke --plaintext --max-time 10s --max-stdin-bytes 1048576 -d @ $SERVER testing.TestService/HalfDuplexCall

echo ""
echo "=== Done ==="

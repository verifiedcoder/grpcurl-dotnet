#!/bin/bash
# =============================================================================
# Script: 13-invoke-bidirectional-half.sh
# Purpose: Half duplex bidirectional streaming (buffered)
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
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

echo "Command: echo '<requests>' | grpcurl invoke --plaintext -d @ $SERVER testing.TestService/HalfDuplexCall"
echo ""

echo "$REQUESTS" | $GRPCURL invoke --plaintext -d @ $SERVER testing.TestService/HalfDuplexCall

echo ""
echo "=== Done ==="

#!/bin/bash
# =============================================================================
# Script: 11-invoke-client-streaming.sh
# Purpose: Client streaming RPC - multiple requests, single response
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Client Streaming RPC ==="
echo ""
echo "StreamingInputCall sends multiple requests and receives one aggregated response."
echo "The response contains the total size of all payloads received."
echo ""

echo "--- Sending 3 requests with payloads ---"
echo "Each request has a base64-encoded payload. Server returns total size."
echo ""

# Create multiple JSON requests (one per line for streaming input)
# Payload bodies: "A" (1 byte), "BB" (2 bytes), "CCC" (3 bytes) = 6 bytes total
REQUESTS=$(cat <<'EOF'
{"payload":{"body":"QQ=="}}
{"payload":{"body":"QkI="}}
{"payload":{"body":"Q0ND"}}
EOF
)

echo "Requests being sent:"
echo "$REQUESTS"
echo ""

echo "Command: echo '<requests>' | grpcurl invoke --plaintext -d @ $SERVER testing.TestService/StreamingInputCall"
echo ""

echo "$REQUESTS" | $GRPCURL invoke --plaintext -d @ $SERVER testing.TestService/StreamingInputCall

echo ""
echo "Expected: aggregated_payload_size should be 6 (1+2+3 bytes)"
echo ""
echo "=== Done ==="

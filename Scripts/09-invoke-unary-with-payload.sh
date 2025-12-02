#!/bin/bash
# =============================================================================
# Script: 09-invoke-unary-with-payload.sh
# Purpose: Unary RPC with complex payload
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Invoke UnaryCall with Complex Payload ==="
echo ""
echo "This demonstrates sending a payload with binary data (base64 encoded)"
echo "and various request options."
echo ""

# "Hello, World!" in base64
PAYLOAD_BODY="SGVsbG8sIFdvcmxkIQ=="

echo "--- Request with payload body (base64: 'Hello, World!') ---"
echo "Command: grpcurl invoke --plaintext -d '{...}' $SERVER testing.TestService/UnaryCall"
echo ""

REQUEST=$(cat <<EOF
{
  "response_type": "COMPRESSABLE",
  "response_size": 32,
  "payload": {
    "type": "COMPRESSABLE",
    "body": "$PAYLOAD_BODY"
  },
  "fill_username": true,
  "fill_oauth_scope": true
}
EOF
)

echo "Request JSON:"
echo "$REQUEST" | head -10
echo ""

$GRPCURL invoke --plaintext -d "$REQUEST" $SERVER testing.TestService/UnaryCall

echo ""
echo "=== Done ==="

#!/bin/bash
# =============================================================================
# Script: 23-allow-unknown-fields.sh
# Purpose: Handle unknown fields in JSON input
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Allow Unknown Fields ==="
echo ""
echo "By default, GrpCurl.Net rejects JSON with unknown field names."
echo "Use --allow-unknown-fields to skip unknown fields instead of erroring."
echo ""

echo "--- Without --allow-unknown-fields (will error) ---"
echo "Request contains 'unknown_field' which doesn't exist in the proto."
echo "Command: grpcurl invoke --plaintext -d '{\"response_size\":10,\"unknown_field\":\"test\"}' $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext -d '{"response_size":10,"unknown_field":"test"}' $SERVER testing.TestService/UnaryCall 2>&1 || true

echo ""
echo "--- With --allow-unknown-fields (success) ---"
echo "Command: grpcurl invoke --plaintext --allow-unknown-fields -d '{\"response_size\":10,\"unknown_field\":\"test\"}' $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext --allow-unknown-fields -d '{"response_size":10,"unknown_field":"test"}' $SERVER testing.TestService/UnaryCall

echo ""
echo "=== Done ==="

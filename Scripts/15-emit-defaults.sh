#!/bin/bash
# =============================================================================
# Script: 15-emit-defaults.sh
# Purpose: Show default values in JSON output
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Emit Default Values ==="
echo ""
echo "By default, protobuf omits fields with default values in JSON output."
echo "Use --emit-defaults to include all fields, even those with default values."
echo ""

echo "--- Without --emit-defaults ---"
echo "Command: grpcurl invoke --plaintext $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext $SERVER testing.TestService/UnaryCall

echo ""
echo "--- With --emit-defaults ---"
echo "Command: grpcurl invoke --plaintext --emit-defaults $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext --emit-defaults $SERVER testing.TestService/UnaryCall

echo ""
echo "Notice the additional fields shown when --emit-defaults is used."
echo ""
echo "=== Done ==="

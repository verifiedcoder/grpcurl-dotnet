#!/bin/bash
# =============================================================================
# Script: 22-error-handling.sh
# Purpose: Handle gRPC errors gracefully
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Error Handling ==="
echo ""
echo "GrpCurl.Net provides informative error messages for various failure scenarios."
echo "Use --output json to emit errors (and responses) as JSON envelopes on stderr/stdout."
echo ""

echo "--- Invoke unimplemented service ---"
echo "Command: grpcn invoke --plaintext --max-time 10s $SERVER testing.UnimplementedService/UnimplementedCall"
echo ""
grpcn invoke --plaintext --max-time 10s $SERVER testing.UnimplementedService/UnimplementedCall 2>&1 || true

echo ""
echo "--- Request custom error via response_status ---"
echo "The TestService can return custom errors when response_status is set."
echo "Command: grpcn invoke --plaintext --max-time 10s -d '{\"response_status\":{\"code\":3,\"message\":\"Invalid argument test\"}}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcn invoke --plaintext --max-time 10s -d '{"response_status":{"code":3,"message":"Invalid argument test"}}' $SERVER testing.TestService/UnaryCall 2>&1 || true

echo ""
echo "--- Format error as JSON ---"
echo "Command: grpcn invoke --plaintext --max-time 10s --output json -d '{\"response_status\":{\"code\":5,\"message\":\"Not found test\"}}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcn invoke --plaintext --max-time 10s --output json -d '{"response_status":{"code":5,"message":"Not found test"}}' $SERVER testing.TestService/UnaryCall 2>&1 || true

echo ""
echo "=== Done ==="

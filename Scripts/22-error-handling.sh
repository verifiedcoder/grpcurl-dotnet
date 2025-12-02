#!/bin/bash
# =============================================================================
# Script: 22-error-handling.sh
# Purpose: Handle gRPC errors gracefully
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Error Handling ==="
echo ""
echo "GrpCurl.Net provides informative error messages for various failure scenarios."
echo "Use --format-error to output errors as JSON."
echo ""

echo "--- Invoke unimplemented service ---"
echo "Command: grpcurl invoke --plaintext $SERVER testing.UnimplementedService/UnimplementedCall"
echo ""
$GRPCURL invoke --plaintext $SERVER testing.UnimplementedService/UnimplementedCall 2>&1 || true

echo ""
echo "--- Request custom error via response_status ---"
echo "The TestService can return custom errors when response_status is set."
echo "Command: grpcurl invoke --plaintext -d '{\"response_status\":{\"code\":3,\"message\":\"Invalid argument test\"}}' $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext -d '{"response_status":{"code":3,"message":"Invalid argument test"}}' $SERVER testing.TestService/UnaryCall 2>&1 || true

echo ""
echo "--- Format error as JSON ---"
echo "Command: grpcurl invoke --plaintext --format-error -d '{\"response_status\":{\"code\":5,\"message\":\"Not found test\"}}' $SERVER testing.TestService/UnaryCall"
echo ""
$GRPCURL invoke --plaintext --format-error -d '{"response_status":{"code":5,"message":"Not found test"}}' $SERVER testing.TestService/UnaryCall 2>&1 || true

echo ""
echo "=== Done ==="

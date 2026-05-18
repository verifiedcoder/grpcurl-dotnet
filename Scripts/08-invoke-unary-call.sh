#!/bin/bash
# =============================================================================
# Script: 08-invoke-unary-call.sh
# Purpose: Unary RPC with request data
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Invoke UnaryCall with Request Data ==="
echo ""
echo "UnaryCall takes a SimpleRequest with response_size to control the"
echo "size of the returned payload."
echo ""

echo "--- Request with response_size: 10 ---"
echo "Command: grpcurl.net invoke --plaintext -d '{\"response_size\": 10}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcurl_net invoke --plaintext -d '{"response_size": 10}' $SERVER testing.TestService/UnaryCall

echo ""
echo "--- Request with fill_username: true ---"
echo "Command: grpcurl.net invoke --plaintext -d '{\"fill_username\": true}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcurl_net invoke --plaintext -d '{"fill_username": true}' $SERVER testing.TestService/UnaryCall

echo ""
echo "=== Done ==="

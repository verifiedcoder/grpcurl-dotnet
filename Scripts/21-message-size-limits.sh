#!/bin/bash
# =============================================================================
# Script: 21-message-size-limits.sh
# Purpose: Control max message sizes
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Message Size Limits ==="
echo ""
echo "Use --max-msg-sz to control the maximum message size."
echo "Default is 4MB. Supported formats: '4KB', '10MB', '1GB'"
echo ""

echo "--- Default message size (4MB) ---"
echo "Command: grpcurl.net invoke --plaintext --max-time 10s -d '{\"response_size\": 100}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s -d '{"response_size": 100}' $SERVER testing.TestService/UnaryCall

echo ""
echo "--- Custom max message size (10MB) ---"
echo "Command: grpcurl.net invoke --plaintext --max-time 10s --max-msg-sz 10MB -d '{\"response_size\": 100}' $SERVER testing.TestService/UnaryCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s --max-msg-sz 10MB -d '{"response_size": 100}' $SERVER testing.TestService/UnaryCall

echo ""
echo "--- Smaller max message size (1KB) - useful for testing size limits ---"
echo "Command: grpcurl.net invoke --plaintext --max-time 10s --max-msg-sz 1KB $SERVER testing.TestService/EmptyCall"
echo ""
# This call is expected to fail with ResourceExhausted (exit code 72)
grpcurl_net invoke --plaintext --max-time 10s --max-msg-sz 1KB $SERVER testing.TestService/EmptyCall || true

echo ""
echo "=== Done ==="

#!/bin/bash
# =============================================================================
# Script: 05-describe-message.sh
# Purpose: Describe message types
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Describe Message Types ==="
echo ""

echo "--- testing.SimpleRequest ---"
echo "Command: grpcn describe --plaintext --max-time 10s $SERVER testing.SimpleRequest"
echo ""
grpcn describe --plaintext --max-time 10s $SERVER testing.SimpleRequest

echo ""
echo "--- testing.Payload ---"
echo "Command: grpcn describe --plaintext --max-time 10s $SERVER testing.Payload"
echo ""
grpcn describe --plaintext --max-time 10s $SERVER testing.Payload

echo ""
echo "--- testing.PayloadType (enum) ---"
echo "Command: grpcn describe --plaintext --max-time 10s $SERVER testing.PayloadType"
echo ""
grpcn describe --plaintext --max-time 10s $SERVER testing.PayloadType

echo ""
echo "=== Done ==="

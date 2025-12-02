#!/bin/bash
# =============================================================================
# Script: 05-describe-message.sh
# Purpose: Describe message types
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Describe Message Types ==="
echo ""

echo "--- testing.SimpleRequest ---"
echo "Command: grpcurl describe --plaintext $SERVER testing.SimpleRequest"
echo ""
$GRPCURL describe --plaintext $SERVER testing.SimpleRequest

echo ""
echo "--- testing.Payload ---"
echo "Command: grpcurl describe --plaintext $SERVER testing.Payload"
echo ""
$GRPCURL describe --plaintext $SERVER testing.Payload

echo ""
echo "--- testing.PayloadType (enum) ---"
echo "Command: grpcurl describe --plaintext $SERVER testing.PayloadType"
echo ""
$GRPCURL describe --plaintext $SERVER testing.PayloadType

echo ""
echo "=== Done ==="

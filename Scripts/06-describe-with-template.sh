#!/bin/bash
# =============================================================================
# Script: 06-describe-with-template.sh
# Purpose: Generate JSON templates for messages
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Generate JSON Templates ==="
echo ""
echo "The --msg-template option outputs a JSON template with all fields"
echo "initialized to their default values. Useful for creating request payloads."
echo ""

echo "--- testing.SimpleRequest Template ---"
echo "Command: grpcurl describe --plaintext --msg-template $SERVER testing.SimpleRequest"
echo ""
$GRPCURL describe --plaintext --msg-template $SERVER testing.SimpleRequest

echo ""
echo "--- testing.StreamingOutputCallRequest Template ---"
echo "Command: grpcurl describe --plaintext --msg-template $SERVER testing.StreamingOutputCallRequest"
echo ""
$GRPCURL describe --plaintext --msg-template $SERVER testing.StreamingOutputCallRequest

echo ""
echo "=== Done ==="

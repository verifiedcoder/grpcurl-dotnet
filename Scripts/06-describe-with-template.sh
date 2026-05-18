#!/bin/bash
# =============================================================================
# Script: 06-describe-with-template.sh
# Purpose: Generate JSON templates for messages
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Generate JSON Templates ==="
echo ""
echo "The --msg-template option outputs a JSON template with all fields"
echo "initialized to their default values. Useful for creating request payloads."
echo ""

echo "--- testing.SimpleRequest Template ---"
echo "Command: grpcurl.net describe --plaintext --msg-template $SERVER testing.SimpleRequest"
echo ""
grpcurl_net describe --plaintext --msg-template $SERVER testing.SimpleRequest

echo ""
echo "--- testing.StreamingOutputCallRequest Template ---"
echo "Command: grpcurl.net describe --plaintext --msg-template $SERVER testing.StreamingOutputCallRequest"
echo ""
grpcurl_net describe --plaintext --msg-template $SERVER testing.StreamingOutputCallRequest

echo ""
echo "=== Done ==="

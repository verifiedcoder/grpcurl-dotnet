#!/bin/bash
# =============================================================================
# Script: 24-authority-header.sh
# Purpose: Override :authority header
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Authority Header Override ==="
echo ""
echo "The --authority option overrides the HTTP/2 :authority pseudo-header."
echo "This is useful for:"
echo "  - Virtual hosting (routing to different services)"
echo "  - Testing load balancers"
echo "  - Matching expected authority in TLS certificates"
echo ""

echo "--- Default authority (uses server address) ---"
echo "Command: grpcn invoke --plaintext --max-time 10s -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s -v $SERVER testing.TestService/EmptyCall 2>&1 | head -20

echo ""
echo "--- Custom authority ---"
echo "Command: grpcn invoke --plaintext --max-time 10s --authority custom.example.com -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext --max-time 10s --authority custom.example.com -v $SERVER testing.TestService/EmptyCall 2>&1 | head -20

echo ""
echo "=== Done ==="

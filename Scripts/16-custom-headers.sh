#!/bin/bash
# =============================================================================
# Script: 16-custom-headers.sh
# Purpose: Add custom headers to requests
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Custom Headers ==="
echo ""
echo "Use -H or --header to add custom metadata headers to requests."
echo "Multiple headers can be specified by repeating the option."
echo ""

echo "--- Single custom header ---"
echo "Command: grpcurl.net invoke --plaintext -H 'Authorization: Bearer token123' -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext -H "Authorization: Bearer token123" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Multiple custom headers ---"
echo "Command: grpcurl.net invoke --plaintext -H 'X-Request-Id: 12345' -H 'X-Custom-Header: value' -v $SERVER testing.TestService/EmptyCall"
echo ""
grpcurl_net invoke --plaintext -H "X-Request-Id: 12345" -H "X-Custom-Header: value" -v $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

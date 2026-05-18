#!/bin/bash
# =============================================================================
# Script: 04-describe-service.sh
# Purpose: Describe a service and its methods
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Describe testing.TestService ==="
echo ""
echo "Command: grpcurl.net describe --plaintext $SERVER testing.TestService"
echo ""

grpcurl_net describe --plaintext $SERVER testing.TestService

echo ""
echo "=== Done ==="

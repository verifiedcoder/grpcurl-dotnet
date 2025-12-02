#!/bin/bash
# =============================================================================
# Script: 04-describe-service.sh
# Purpose: Describe a service and its methods
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== Describe testing.TestService ==="
echo ""
echo "Command: grpcurl describe --plaintext $SERVER testing.TestService"
echo ""

$GRPCURL describe --plaintext $SERVER testing.TestService

echo ""
echo "=== Done ==="

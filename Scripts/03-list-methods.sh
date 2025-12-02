#!/bin/bash
# =============================================================================
# Script: 03-list-methods.sh
# Purpose: List methods for a specific service
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== List Methods for testing.TestService ==="
echo ""
echo "Command: grpcurl list --plaintext $SERVER testing.TestService"
echo ""

$GRPCURL list --plaintext $SERVER testing.TestService

echo ""
echo "=== Done ==="

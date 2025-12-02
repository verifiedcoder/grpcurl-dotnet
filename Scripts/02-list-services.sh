#!/bin/bash
# =============================================================================
# Script: 02-list-services.sh
# Purpose: List all services via server reflection
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"

echo "=== List All Services ==="
echo ""
echo "Command: grpcurl list --plaintext $SERVER"
echo ""

$GRPCURL list --plaintext $SERVER

echo ""
echo "=== Done ==="

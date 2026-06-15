#!/bin/bash
# =============================================================================
# Script: 02-list-services.sh
# Purpose: List all services via server reflection
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== List All Services ==="
echo ""
echo "Command: grpcn list --plaintext --max-time 10s $SERVER"
echo ""

grpcurl_net list --plaintext --max-time 10s $SERVER

echo ""
echo "=== Done ==="

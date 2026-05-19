#!/bin/bash
# =============================================================================
# Script: 28-gql-simple-query.sh
# Purpose: Execute a simple GraphQL query via gql2grpc (reflection-based)
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== gql2grpc: Simple reflection-based query ==="
echo ""
echo "No mapping file, no protoset. Convention resolves 'EmptyCall' -> testing.TestService/EmptyCall."
echo "Uses --max-time to bound reflection discovery and the RPC in scripts."
echo ""
echo "Command: gql2grpc --plaintext --max-time 10s --default-service testing.TestService $SERVER 'query { EmptyCall }'"
echo ""

gql2grpc_cli --plaintext --max-time 10s --default-service testing.TestService $SERVER 'query { EmptyCall }'

echo ""
echo "=== Done ==="

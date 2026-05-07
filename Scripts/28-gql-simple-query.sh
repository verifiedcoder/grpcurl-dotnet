#!/bin/bash
# =============================================================================
# Script: 28-gql-simple-query.sh
# Purpose: Execute a simple GraphQL query via gql2grpc (reflection-based)
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GQL2GRPC="$SCRIPT_DIR/../Src/Gql2Grpc/bin/Debug/net10.0/Gql2Grpc"
SERVER="localhost:9090"

echo "=== gql2grpc: Simple reflection-based query ==="
echo ""
echo "No mapping file, no protoset. Convention resolves 'EmptyCall' -> testing.TestService/EmptyCall."
echo ""
echo "Command: gql2grpc --plaintext --default-service testing.TestService $SERVER 'query { EmptyCall }'"
echo ""

$GQL2GRPC --plaintext --default-service testing.TestService $SERVER 'query { EmptyCall }'

echo ""
echo "=== Done ==="

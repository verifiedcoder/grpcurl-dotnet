#!/bin/bash
# =============================================================================
# Script: 32-gql-introspection.sh
# Purpose: Run a GraphQL introspection query; schema is synthesised from descriptors
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== gql2grpc: Schema introspection ==="
echo ""
echo "The schema is synthesised from the reflected FileDescriptorSet, answered entirely client-side."
echo ""
echo "Command: gql2grpc --plaintext --default-service testing.TestService $SERVER 'query { __schema { queryType { name } types { kind name } } }'"
echo ""

gql2grpc_cli --plaintext --default-service testing.TestService $SERVER \
    'query { __schema { queryType { name } types { kind name } } }' | head -c 2000
echo ""
echo "...(output truncated for readability)"
echo ""
echo "=== Done ==="

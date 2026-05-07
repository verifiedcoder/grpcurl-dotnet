#!/bin/bash
# =============================================================================
# Script: 31-gql-error-envelope.sh
# Purpose: Trigger a gRPC error and show the GraphQL error envelope
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set +e  # allow non-zero exit for demonstration

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GQL2GRPC="$SCRIPT_DIR/../Src/Gql2Grpc/bin/Debug/net10.0/Gql2Grpc"
SERVER="localhost:9090"

echo "=== gql2grpc: GraphQL error envelope with gRPC status extensions ==="
echo ""
echo "The 'fail-early' header tells the TestServer to fail immediately with the given code."
echo "We force InvalidArgument (3). Exit code should be 64 + 3 = 67."
echo ""
echo "Command: gql2grpc --plaintext --default-service testing.TestService -H 'fail-early: 3' $SERVER 'query { EmptyCall }'"
echo ""

$GQL2GRPC --plaintext --default-service testing.TestService -H "fail-early: 3" $SERVER 'query { EmptyCall }'
STATUS=$?

echo ""
echo "Exit code: $STATUS (expected 67)"
echo ""
echo "=== Done ==="

#!/bin/bash
# =============================================================================
# Script: 29-gql-mapping-file.sh
# Purpose: Execute a GraphQL query using a mapping file
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GQL2GRPC="$SCRIPT_DIR/../Src/Gql2Grpc/bin/Debug/net10.0/Gql2Grpc"
SERVER="localhost:9090"
MAPPING="$(mktemp --suffix=.yaml)"

cat > "$MAPPING" <<'EOF'
version: 1
defaults:
  service: testing.TestService
operations:
  - graphqlField: unaryCall
    operationType: query
    method: UnaryCall
    arguments:
      input: { path: . }
EOF

echo "=== gql2grpc: Mapping file with nested request spread ==="
echo ""
echo "Mapping file:"
sed 's/^/    /' "$MAPPING"
echo ""
echo "Command: gql2grpc --plaintext --mapping $MAPPING $SERVER 'query { unaryCall(input: { payload: { body: \"aGVsbG8=\" } }) { payload { body } } }'"
echo ""

$GQL2GRPC --plaintext --mapping "$MAPPING" $SERVER 'query { unaryCall(input: { payload: { body: "aGVsbG8=" } }) { payload { body } } }'

rm -f "$MAPPING"

echo ""
echo "=== Done ==="

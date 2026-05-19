#!/bin/bash
# =============================================================================
# Script: 29-gql-mapping-file.sh
# Purpose: Execute a GraphQL query using a mapping file
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"
MAPPING="$(mktemp "${TMPDIR:-/tmp}/gql2grpc-mapping.XXXXXX")"
trap 'rm -f "$MAPPING"' EXIT

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
echo "Mapping files are capped at 4 MiB before parsing."
echo ""
echo "Mapping file:"
sed 's/^/    /' "$MAPPING"
echo ""
echo "Command: gql2grpc --plaintext --max-time 10s --mapping $MAPPING $SERVER 'query { unaryCall(input: { payload: { body: \"aGVsbG8=\" } }) { payload { body } } }'"
echo ""

gql2grpc_cli --plaintext --max-time 10s --mapping "$MAPPING" $SERVER 'query { unaryCall(input: { payload: { body: "aGVsbG8=" } }) { payload { body } } }'

echo ""
echo "=== Done ==="

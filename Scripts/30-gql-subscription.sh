#!/bin/bash
# =============================================================================
# Script: 30-gql-subscription.sh
# Purpose: Run a GraphQL subscription mapped to server-streaming gRPC
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
  - graphqlField: streamingOutput
    operationType: subscription
    method: StreamingOutputCall
    kind: serverStreaming
    arguments:
      input: { path: . }
EOF

echo "=== gql2grpc: Subscription -> server-streaming RPC (NDJSON) ==="
echo ""
echo "Each line of stdout is a self-contained GraphQL envelope."
echo ""
echo "Command: gql2grpc --plaintext --mapping <file> $SERVER 'subscription { streamingOutput(input: { responseParameters: [{ size: 1 }, { size: 2 }, { size: 3 }] }) { payload { body } } }'"
echo ""

gql2grpc_cli --plaintext --mapping "$MAPPING" $SERVER \
    'subscription { streamingOutput(input: { responseParameters: [{ size: 1 }, { size: 2 }, { size: 3 }] }) { payload { body } } }'

echo ""
echo "=== Done ==="

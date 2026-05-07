#!/bin/bash
# =============================================================================
# Script: 30-gql-subscription.sh
# Purpose: Run a GraphQL subscription mapped to server-streaming gRPC
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

$GQL2GRPC --plaintext --mapping "$MAPPING" $SERVER \
    'subscription { streamingOutput(input: { responseParameters: [{ size: 1 }, { size: 2 }, { size: 3 }] }) { payload { body } } }'

rm -f "$MAPPING"

echo ""
echo "=== Done ==="

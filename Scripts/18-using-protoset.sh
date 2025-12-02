#!/bin/bash
# =============================================================================
# Script: 18-using-protoset.sh
# Purpose: Use protoset file instead of server reflection
# Prerequisites: TestServer running on localhost:9090 (optional for this demo)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"
PROTOSET="$SCRIPT_DIR/../Tests/TestProtosets/test.protoset"

echo "=== Using Protoset Files ==="
echo ""
echo "Protoset files contain pre-compiled protobuf definitions."
echo "Use --protoset to load definitions without querying the server for reflection."
echo ""
echo "This is useful when:"
echo "  - Server doesn't support reflection"
echo "  - You want to validate against a specific proto version"
echo "  - Offline documentation/development"
echo ""

echo "--- List services from protoset (no server needed for this) ---"
echo "Command: grpcurl list --protoset $PROTOSET"
echo ""
$GRPCURL list --protoset "$PROTOSET"

echo ""
echo "--- Describe a message from protoset ---"
echo "Command: grpcurl describe --protoset $PROTOSET testing.SimpleRequest"
echo ""
$GRPCURL describe --protoset "$PROTOSET" testing.SimpleRequest

echo ""
echo "--- Invoke using protoset (still needs server for actual RPC) ---"
echo "Command: grpcurl invoke --plaintext --protoset $PROTOSET $SERVER testing.TestService/EmptyCall"
echo ""
$GRPCURL invoke --plaintext --protoset "$PROTOSET" $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

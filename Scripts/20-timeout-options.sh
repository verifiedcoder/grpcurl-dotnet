#!/bin/bash
# =============================================================================
# Script: 20-timeout-options.sh
# Purpose: Connection and operation timeouts
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"

echo "=== Timeout Options ==="
echo ""
echo "GrpCurl.Net supports two timeout options:"
echo "  --connect-timeout : Maximum time to establish connection (default: 10s)"
echo "  --max-time        : Maximum total operation time for list, describe, invoke, and gql2grpc"
echo ""
echo "Supported formats: '10s', '500ms', '1m', '1h'"
echo ""

echo "--- Discovery timeout for reflection-backed list (10 seconds) ---"
echo "Command: grpcn list --plaintext --max-time 10s $SERVER"
echo ""
grpcn list --plaintext --max-time 10s $SERVER

echo ""
echo "--- Discovery timeout for describe (10 seconds) ---"
echo "Command: grpcn describe --plaintext --max-time 10s $SERVER testing.TestService"
echo ""
grpcn describe --plaintext --max-time 10s $SERVER testing.TestService

echo ""
echo "--- Connect timeout (5 seconds) ---"
echo "Command: grpcn invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Max operation time (30 seconds) ---"
echo "Command: grpcn invoke --plaintext --max-time 30s $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --max-time 30s $SERVER testing.TestService/EmptyCall

echo ""
echo "--- Both timeouts combined ---"
echo "Command: grpcn invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall"
echo ""
grpcn invoke --plaintext --connect-timeout 5s --max-time 30s $SERVER testing.TestService/EmptyCall

echo ""
echo "=== Done ==="

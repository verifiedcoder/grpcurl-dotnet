#!/bin/bash
# =============================================================================
# Script: 26-all-features-demo.sh
# Purpose: Comprehensive demo combining multiple GrpCurl.Net features
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"
PROTOSET="$SCRIPT_DIR/../Tests/TestProtosets/test.protoset"
EXPORT_FILE="/tmp/demo-exported.protoset"

echo "============================================================================="
echo "                    GrpCurl.Net Comprehensive Demo"
echo "============================================================================="
echo ""

# ============================================================================
# SECTION 1: Discovery
# ============================================================================
echo "=== SECTION 1: Service Discovery ==="
echo ""

echo "1.1 List all services:"
$GRPCURL list --plaintext $SERVER
echo ""

echo "1.2 List methods for TestService:"
$GRPCURL list --plaintext $SERVER testing.TestService
echo ""

echo "1.3 Describe with JSON template:"
$GRPCURL describe --plaintext --msg-template $SERVER testing.SimpleRequest
echo ""

# ============================================================================
# SECTION 2: Unary RPC
# ============================================================================
echo "=== SECTION 2: Unary RPC with Options ==="
echo ""

echo "2.1 UnaryCall with payload, headers, and verbose output:"
$GRPCURL invoke --plaintext \
    -H "X-Request-Id: demo-12345" \
    -H "Authorization: Bearer demo-token" \
    --emit-defaults \
    -v \
    -d '{"response_size": 20, "fill_username": true}' \
    $SERVER testing.TestService/UnaryCall
echo ""

# ============================================================================
# SECTION 3: Streaming
# ============================================================================
echo "=== SECTION 3: Streaming RPC ==="
echo ""

echo "3.1 Server streaming (3 responses):"
$GRPCURL invoke --plaintext \
    -d '{"response_parameters":[{"size":5},{"size":10},{"size":15}]}' \
    $SERVER testing.TestService/StreamingOutputCall
echo ""

echo "3.2 Bidirectional streaming:"
echo '{"response_parameters":[{"size":8}]}
{"response_parameters":[{"size":16}]}' | \
$GRPCURL invoke --plaintext -d @ $SERVER testing.TestService/FullDuplexCall
echo ""

# ============================================================================
# SECTION 4: Protoset Export
# ============================================================================
echo "=== SECTION 4: Protoset Export ==="
echo ""

echo "4.1 Export protoset from server:"
$GRPCURL list --plaintext --protoset-out "$EXPORT_FILE" $SERVER
echo "Exported to: $EXPORT_FILE"
echo ""

echo "4.2 Use exported protoset (offline):"
$GRPCURL list --protoset "$EXPORT_FILE"
echo ""

# ============================================================================
# SECTION 5: Timeouts and Limits
# ============================================================================
echo "=== SECTION 5: Timeouts and Limits ==="
echo ""

echo "5.1 With connection timeout and max operation time:"
$GRPCURL invoke --plaintext \
    --connect-timeout 5s \
    --max-time 30s \
    --max-msg-sz 10MB \
    $SERVER testing.TestService/EmptyCall
echo "Success with custom timeouts"
echo ""

# ============================================================================
# SECTION 6: Very Verbose (Timing)
# ============================================================================
echo "=== SECTION 6: Very Verbose Output (Timing) ==="
echo ""

echo "6.1 UnaryCall with very verbose timing:"
$GRPCURL invoke --plaintext \
    --vv \
    -d '{"response_size": 10}' \
    $SERVER testing.TestService/UnaryCall
echo ""

# ============================================================================
# Cleanup
# ============================================================================
rm -f "$EXPORT_FILE"

echo "============================================================================="
echo "                         Demo Complete!"
echo "============================================================================="
echo ""
echo "This demo showed:"
echo "  - Service discovery (list, describe, templates)"
echo "  - Unary RPC with headers and verbose output"
echo "  - Server and bidirectional streaming"
echo "  - Protoset export and offline usage"
echo "  - Timeouts, message size limits"
echo "  - Very verbose timing output"
echo ""
echo "Run individual scripts (02-25) for detailed examples of each feature."
echo ""

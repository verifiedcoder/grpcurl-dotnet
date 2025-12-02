#!/bin/bash
# =============================================================================
# Script: 19-export-protoset.sh
# Purpose: Export FileDescriptorSet to a file
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL="$SCRIPT_DIR/../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net"
SERVER="localhost:9090"
OUTPUT_FILE="/tmp/exported-service.protoset"

echo "=== Export Protoset ==="
echo ""
echo "Use --protoset-out to export the FileDescriptorSet from the server."
echo "This captures the proto definitions for offline use."
echo ""

echo "--- Export protoset during list operation ---"
echo "Command: grpcurl list --plaintext --protoset-out $OUTPUT_FILE $SERVER"
echo ""
$GRPCURL list --plaintext --protoset-out "$OUTPUT_FILE" $SERVER

echo ""
echo "--- Verify exported file ---"
if [ -f "$OUTPUT_FILE" ]; then
    echo "Exported protoset to: $OUTPUT_FILE"
    echo "File size: $(stat -c %s "$OUTPUT_FILE" 2>/dev/null || stat -f %z "$OUTPUT_FILE") bytes"
    echo ""

    echo "--- Use exported protoset ---"
    echo "Command: grpcurl list --protoset $OUTPUT_FILE"
    $GRPCURL list --protoset "$OUTPUT_FILE"

    # Cleanup
    rm -f "$OUTPUT_FILE"
    echo ""
    echo "Cleaned up temporary file."
else
    echo "Export failed - file not created"
fi

echo ""
echo "=== Done ==="

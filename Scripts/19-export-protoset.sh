#!/bin/bash
# =============================================================================
# Script: 19-export-protoset.sh
# Purpose: Export FileDescriptorSet to a file
# Prerequisites: TestServer running on localhost:9090
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/common.sh"
SERVER="localhost:9090"
OUTPUT_FILE="$(mktemp "${TMPDIR:-/tmp}/grpcurl-dotnet-export.XXXXXX")"
rm -f "$OUTPUT_FILE"
trap 'rm -f "$OUTPUT_FILE"' EXIT

echo "=== Export Protoset ==="
echo ""
echo "Use --protoset-out to export the FileDescriptorSet from the server."
echo "This captures the proto definitions for offline use."
echo ""

echo "--- Export protoset during list operation ---"
echo "Command: grpcurl.net list --plaintext --max-time 10s --protoset-out $OUTPUT_FILE $SERVER"
echo ""
grpcurl_net list --plaintext --max-time 10s --protoset-out "$OUTPUT_FILE" $SERVER

echo ""
echo "--- Verify exported file ---"
if [ -f "$OUTPUT_FILE" ]; then
    echo "Exported protoset to: $OUTPUT_FILE"
    echo "File size: $(stat -c %s "$OUTPUT_FILE" 2>/dev/null || stat -f %z "$OUTPUT_FILE") bytes"
    echo ""

    echo "--- Use exported protoset ---"
    echo "Command: grpcurl.net list --max-time 10s --protoset $OUTPUT_FILE"
    grpcurl_net list --max-time 10s --protoset "$OUTPUT_FILE"

    echo ""
    echo "Temporary protoset will be cleaned up when the script exits."
else
    echo "Export failed - file not created"
fi

echo ""
echo "=== Done ==="

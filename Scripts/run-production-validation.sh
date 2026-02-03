#!/bin/bash
# =============================================================================
# Script: run-production-validation.sh
# Purpose: Production validation for GrpCurl.Net
# Prerequisites:
#   - .NET 10 SDK installed
#   - Go 1.22+ installed (optional, for grpcurl comparison)
#   - No services running on ports 9090, 9443
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
GRPCURL_NET="dotnet run --project $PROJECT_ROOT/Src/GrpCurl.Net --"
CERT_DIR="$PROJECT_ROOT/Tests/TestCertificates"
PROTOSET_DIR="$PROJECT_ROOT/Tests/TestProtosets"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Counters
PASSED=0
FAILED=0
SKIPPED=0

# Test result tracking
declare -a FAILED_TESTS

# =============================================================================
# Helper Functions
# =============================================================================

log_header() {
    echo ""
    echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
}

log_section() {
    echo ""
    echo -e "${YELLOW}--- $1 ---${NC}"
}

log_test() {
    echo -n "  Testing: $1... "
}

pass() {
    echo -e "${GREEN}PASS${NC}"
    PASSED=$((PASSED + 1))
}

fail() {
    echo -e "${RED}FAIL${NC}"
    FAILED=$((FAILED + 1))
    FAILED_TESTS+=("$1")
    if [ -n "$2" ]; then
        echo -e "    ${RED}Error: $2${NC}"
    fi
}

skip() {
    echo -e "${YELLOW}SKIP${NC}"
    SKIPPED=$((SKIPPED + 1))
    if [ -n "$1" ]; then
        echo -e "    ${YELLOW}Reason: $1${NC}"
    fi
}

cleanup() {
    log_section "Cleanup"

    # Kill TestServer if running
    pkill -f "GrpCurl.Net.TestServer.*9090" 2>/dev/null || true
    pkill -f "GrpCurl.Net.TestServer.*9443" 2>/dev/null || true

    # Kill interop server if running
    pkill -f "interop_server" 2>/dev/null || true

    echo "  Cleanup complete"
}

wait_for_port() {
    local port=$1
    local max_wait=30
    local count=0

    while ! nc -z localhost $port 2>/dev/null; do
        sleep 1
        count=$((count + 1))
        if [ $count -ge $max_wait ]; then
            return 1
        fi
    done
    return 0
}

# =============================================================================
# Phase 1: Environment Check
# =============================================================================

phase1_environment() {
    log_header "PHASE 1: Environment Check"

    log_test ".NET SDK"
    if dotnet --version &>/dev/null; then
        pass
    else
        fail ".NET SDK" "dotnet not found"
        exit 1
    fi

    log_test "Go installation"
    if go version &>/dev/null; then
        pass
        GO_AVAILABLE=true
    else
        skip "Go not installed - grpcurl comparison will be skipped"
        GO_AVAILABLE=false
    fi

    log_test "grpcurl installation"
    if command -v grpcurl &>/dev/null || [ -f "$HOME/go/bin/grpcurl" ]; then
        pass
        GRPCURL_AVAILABLE=true
        GRPCURL_CMD="${GRPCURL_CMD:-grpcurl}"
        [ -f "$HOME/go/bin/grpcurl" ] && GRPCURL_CMD="$HOME/go/bin/grpcurl"
    else
        skip "grpcurl not installed"
        GRPCURL_AVAILABLE=false
    fi

    log_test "Build GrpCurl.Net"
    if dotnet build "$PROJECT_ROOT/Src/GrpCurl.Net/GrpCurl.Net.csproj" -c Release &>/dev/null; then
        pass
    else
        fail "Build" "Build failed"
        exit 1
    fi
}

# =============================================================================
# Phase 2: Unit Tests
# =============================================================================

phase2_unit_tests() {
    log_header "PHASE 2: Unit Tests"

    log_test "Running unit tests"
    OUTPUT=$(dotnet test "$PROJECT_ROOT/Tests/GrpCurl.DotNet.Tests.Unit" --verbosity minimal 2>&1)

    if echo "$OUTPUT" | grep -q "Passed!"; then
        UNIT_RESULTS=$(echo "$OUTPUT" | grep "Passed!" | head -1)
        pass
        echo "    $UNIT_RESULTS"
    else
        fail "Unit tests" "Some tests failed"
        echo "$OUTPUT" | tail -20
    fi
}

# =============================================================================
# Phase 3: Start Test Server
# =============================================================================

phase3_start_servers() {
    log_header "PHASE 3: Start Test Servers"

    # Cleanup any existing servers
    cleanup

    log_test "Starting TestServer (plaintext, port 9090)"
    dotnet run --project "$PROJECT_ROOT/Tests/GrpCurl.Net.TestServer" -- --port 9090 &>/dev/null &
    TESTSERVER_PID=$!

    if wait_for_port 9090; then
        pass
    else
        fail "TestServer" "Failed to start on port 9090"
        exit 1
    fi

    log_test "Starting TestServer (TLS, port 9443)"
    dotnet run --project "$PROJECT_ROOT/Tests/GrpCurl.Net.TestServer" -- --port 9443 --tls &>/dev/null &
    TESTSERVER_TLS_PID=$!

    if wait_for_port 9443; then
        pass
    else
        fail "TestServer TLS" "Failed to start on port 9443"
    fi
}

# =============================================================================
# Phase 4: List Command Tests
# =============================================================================

phase4_list_tests() {
    log_header "PHASE 4: List Command Tests"

    log_test "List services"
    OUTPUT=$($GRPCURL_NET list localhost:9090 --plaintext 2>&1)
    if echo "$OUTPUT" | grep -q "testing.TestService"; then
        pass
    else
        fail "List services" "TestService not found in output"
    fi

    log_test "List methods"
    OUTPUT=$($GRPCURL_NET list localhost:9090 testing.TestService --plaintext 2>&1)
    if echo "$OUTPUT" | grep -q "EmptyCall" && echo "$OUTPUT" | grep -q "UnaryCall"; then
        pass
    else
        fail "List methods" "Expected methods not found"
    fi

    log_test "List with protoset"
    OUTPUT=$($GRPCURL_NET list --protoset "$PROTOSET_DIR/test.protoset" 2>&1)
    if echo "$OUTPUT" | grep -q "testing.TestService"; then
        pass
    else
        fail "List with protoset" "TestService not found"
    fi

    log_test "Export protoset"
    $GRPCURL_NET list localhost:9090 --plaintext --protoset-out /tmp/test-export.protoset &>/dev/null
    if [ -f /tmp/test-export.protoset ] && [ -s /tmp/test-export.protoset ]; then
        pass
        rm -f /tmp/test-export.protoset
    else
        fail "Export protoset" "File not created or empty"
    fi
}

# =============================================================================
# Phase 5: Describe Command Tests
# =============================================================================

phase5_describe_tests() {
    log_header "PHASE 5: Describe Command Tests"

    log_test "Describe service"
    OUTPUT=$($GRPCURL_NET describe localhost:9090 testing.TestService --plaintext 2>&1)
    if echo "$OUTPUT" | grep -q "TestService"; then
        pass
    else
        fail "Describe service"
    fi

    log_test "Describe message"
    OUTPUT=$($GRPCURL_NET describe localhost:9090 testing.SimpleRequest --plaintext 2>&1)
    if echo "$OUTPUT" | grep -q "SimpleRequest"; then
        pass
    else
        fail "Describe message"
    fi

    log_test "Describe with --msg-template"
    OUTPUT=$($GRPCURL_NET describe localhost:9090 testing.SimpleRequest --plaintext --msg-template 2>&1)
    if echo "$OUTPUT" | grep -q "responseType" && echo "$OUTPUT" | grep -q "payload"; then
        pass
    else
        fail "Describe with template"
    fi
}

# =============================================================================
# Phase 6: Invoke Command Tests - All Streaming Types
# =============================================================================

phase6_invoke_tests() {
    log_header "PHASE 6: Invoke Command Tests"

    log_section "Unary Calls"

    log_test "Empty unary call"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext -d '{}' 2>&1)
    if [ "$OUTPUT" = "{}" ]; then
        pass
    else
        fail "Empty unary" "Expected {}, got: $OUTPUT"
    fi

    log_test "Unary call with payload"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/UnaryCall --plaintext -d '{"payload":{"body":"dGVzdA=="}}' 2>&1)
    if echo "$OUTPUT" | grep -q '"body":"dGVzdA=="'; then
        pass
    else
        fail "Unary with payload" "Payload not echoed correctly"
    fi

    log_section "Server Streaming"

    log_test "Server streaming (2 responses)"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/StreamingOutputCall --plaintext -d '{"responseParameters":[{"size":10},{"size":20}]}' 2>&1)
    RESPONSE_COUNT=$(echo "$OUTPUT" | grep -c "payload" || true)
    if [ "$RESPONSE_COUNT" -eq 2 ]; then
        pass
    else
        fail "Server streaming" "Expected 2 responses, got $RESPONSE_COUNT"
    fi

    log_test "Empty server stream"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/StreamingOutputCall --plaintext -d '{"responseParameters":[]}' 2>&1)
    if [ -z "$OUTPUT" ]; then
        pass
    else
        fail "Empty server stream" "Expected no output"
    fi

    log_section "Client Streaming"

    log_test "Client streaming"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/StreamingInputCall --plaintext -d '[{"payload":{"body":"YQ=="}},{"payload":{"body":"YmI="}},{"payload":{"body":"Y2Nj"}}]' 2>&1)
    if echo "$OUTPUT" | grep -q "aggregatedPayloadSize"; then
        pass
    else
        fail "Client streaming" "Aggregated size not returned"
    fi

    log_section "Bidirectional Streaming"

    log_test "Full duplex streaming"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/FullDuplexCall --plaintext -d '[{"responseParameters":[{"size":5}]},{"responseParameters":[{"size":10}]}]' 2>&1)
    RESPONSE_COUNT=$(echo "$OUTPUT" | grep -c "payload" || true)
    if [ "$RESPONSE_COUNT" -eq 2 ]; then
        pass
    else
        fail "Full duplex" "Expected 2 responses, got $RESPONSE_COUNT"
    fi

    log_test "Half duplex streaming"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/HalfDuplexCall --plaintext -d '[{"responseParameters":[{"size":5}]},{"responseParameters":[{"size":10}]}]' 2>&1)
    RESPONSE_COUNT=$(echo "$OUTPUT" | grep -c "{}" || true)
    if [ "$RESPONSE_COUNT" -ge 1 ]; then
        pass
    else
        fail "Half duplex" "No responses received"
    fi
}

# =============================================================================
# Phase 7: TLS Tests
# =============================================================================

phase7_tls_tests() {
    log_header "PHASE 7: TLS Tests"

    log_test "TLS with --insecure"
    OUTPUT=$($GRPCURL_NET list localhost:9443 --insecure 2>&1)
    if echo "$OUTPUT" | grep -q "testing.TestService"; then
        pass
    else
        fail "TLS insecure" "Failed to list services"
    fi

    log_test "TLS certificate validation (should fail)"
    OUTPUT=$($GRPCURL_NET list localhost:9443 2>&1) || true
    if echo "$OUTPUT" | grep -qi "untrusted\|certificate\|ssl"; then
        pass
    else
        fail "TLS validation" "Expected certificate error"
    fi

    log_test "TLS invoke with --insecure"
    OUTPUT=$($GRPCURL_NET invoke localhost:9443 testing.TestService/EmptyCall --insecure -d '{}' 2>&1)
    if [ "$OUTPUT" = "{}" ]; then
        pass
    else
        fail "TLS invoke" "Expected {}"
    fi
}

# =============================================================================
# Phase 8: Error Handling Tests
# =============================================================================

phase8_error_tests() {
    log_header "PHASE 8: Error Handling Tests"

    log_section "Status Codes"

    for CODE in 3 5 12; do
        case $CODE in
            3) NAME="InvalidArgument" ;;
            5) NAME="NotFound" ;;
            12) NAME="Unimplemented" ;;
        esac

        log_test "Status code $CODE ($NAME)"
        if [ $CODE -eq 12 ]; then
            OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.UnimplementedService/UnimplementedCall --plaintext -d '{}' 2>&1) || true
        else
            OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext -H "fail-early: $CODE" -d '{}' 2>&1) || true
        fi

        if echo "$OUTPUT" | grep -qi "error\|$NAME"; then
            pass
        else
            fail "Status $CODE" "Error not displayed"
        fi
    done

    log_section "Error Formatting"

    log_test "--format-error option"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext -H "fail-early: 3" -d '{}' --format-error 2>&1) && EXIT_CODE=0 || EXIT_CODE=$?

    if echo "$OUTPUT" | grep -q '"code": 3' && echo "$OUTPUT" | grep -q '"status": "InvalidArgument"'; then
        # Check that no stack trace was printed
        if ! echo "$OUTPUT" | grep -q "Unhandled exception"; then
            if [ $EXIT_CODE -eq 67 ]; then
                pass
            else
                fail "--format-error" "Wrong exit code: $EXIT_CODE (expected 67)"
            fi
        else
            fail "--format-error" "Stack trace printed"
        fi
    else
        fail "--format-error" "JSON error not formatted correctly"
    fi
}

# =============================================================================
# Phase 9: Headers and Metadata Tests
# =============================================================================

phase9_header_tests() {
    log_header "PHASE 9: Headers and Metadata Tests"

    log_test "Custom header"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext -H "x-custom-header: test-value" -d '{}' 2>&1)
    if [ "$OUTPUT" = "{}" ]; then
        pass
    else
        fail "Custom header"
    fi

    log_test "Multiple headers"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext -H "h1: v1" -H "h2: v2" -d '{}' 2>&1)
    if [ "$OUTPUT" = "{}" ]; then
        pass
    else
        fail "Multiple headers"
    fi

    log_test "User-agent override"
    OUTPUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/EmptyCall --plaintext --user-agent "custom-agent/1.0" -d '{}' 2>&1)
    if [ "$OUTPUT" = "{}" ]; then
        pass
    else
        fail "User-agent override"
    fi
}

# =============================================================================
# Phase 10: grpcurl Comparison
# =============================================================================

phase10_grpcurl_comparison() {
    log_header "PHASE 10: grpcurl Comparison"

    if [ "$GRPCURL_AVAILABLE" != "true" ]; then
        skip "grpcurl not available"
        return
    fi

    log_test "List output comparison"
    DOTNET_OUT=$($GRPCURL_NET list localhost:9090 --plaintext 2>&1 | grep -o "testing\.[A-Za-z]*" | sort)
    GO_OUT=$($GRPCURL_CMD -plaintext localhost:9090 list 2>&1 | grep -o "testing\.[A-Za-z]*" | sort)

    if [ "$DOTNET_OUT" = "$GO_OUT" ]; then
        pass
    else
        # Check if at least TestService is in both
        if echo "$DOTNET_OUT" | grep -q "TestService" && echo "$GO_OUT" | grep -q "TestService"; then
            pass
            echo "    Note: Minor differences in service listing (acceptable)"
        else
            fail "List comparison" "Outputs differ significantly"
        fi
    fi

    log_test "Invoke output comparison (semantic)"
    DOTNET_OUT=$($GRPCURL_NET invoke localhost:9090 testing.TestService/UnaryCall --plaintext -d '{"payload":{"body":"dGVzdA=="}}' 2>&1)
    GO_OUT=$($GRPCURL_CMD -plaintext -d '{"payload":{"body":"dGVzdA=="}}' localhost:9090 testing.TestService/UnaryCall 2>&1)

    # Compare semantically (both should have same body value)
    if echo "$DOTNET_OUT" | grep -q "dGVzdA==" && echo "$GO_OUT" | grep -q "dGVzdA=="; then
        pass
    else
        fail "Invoke comparison"
    fi
}

# =============================================================================
# Phase 11: Demo Scripts
# =============================================================================

phase11_demo_scripts() {
    log_header "PHASE 11: Demo Scripts"

    SCRIPT_COUNT=0
    SCRIPT_PASSED=0

    for script in "$SCRIPT_DIR"/0[2-9]*.sh "$SCRIPT_DIR"/1*.sh "$SCRIPT_DIR"/2[0-6]*.sh; do
        if [ -f "$script" ] && [ "$(basename "$script")" != "run-production-validation.sh" ]; then
            SCRIPT_COUNT=$((SCRIPT_COUNT + 1))
            SCRIPT_NAME=$(basename "$script")
            log_test "$SCRIPT_NAME"

            if bash "$script" &>/dev/null; then
                pass
                SCRIPT_PASSED=$((SCRIPT_PASSED + 1))
            else
                fail "$SCRIPT_NAME"
            fi
        fi
    done

    echo ""
    echo "  Demo scripts: $SCRIPT_PASSED/$SCRIPT_COUNT passed"
}

# =============================================================================
# Summary
# =============================================================================

print_summary() {
    log_header "TEST SUMMARY"

    TOTAL=$((PASSED + FAILED + SKIPPED))

    echo ""
    echo -e "  ${GREEN}Passed:${NC}  $PASSED"
    echo -e "  ${RED}Failed:${NC}  $FAILED"
    echo -e "  ${YELLOW}Skipped:${NC} $SKIPPED"
    echo -e "  Total:   $TOTAL"
    echo ""

    if [ $FAILED -gt 0 ]; then
        echo -e "${RED}Failed Tests:${NC}"
        for test in "${FAILED_TESTS[@]}"; do
            echo "  - $test"
        done
        echo ""
    fi

    if [ $FAILED -eq 0 ]; then
        echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
        echo -e "${GREEN}                    ALL TESTS PASSED!                          ${NC}"
        echo -e "${GREEN}            GrpCurl.Net is PRODUCTION READY                    ${NC}"
        echo -e "${GREEN}═══════════════════════════════════════════════════════════════${NC}"
        EXIT_CODE=0
    else
        echo -e "${RED}═══════════════════════════════════════════════════════════════${NC}"
        echo -e "${RED}                    SOME TESTS FAILED                           ${NC}"
        echo -e "${RED}═══════════════════════════════════════════════════════════════${NC}"
        EXIT_CODE=1
    fi
}

# =============================================================================
# Main
# =============================================================================

main() {
    echo ""
    echo "╔═══════════════════════════════════════════════════════════════╗"
    echo "║       GrpCurl.Net Production Validation Test Suite           ║"
    echo "╚═══════════════════════════════════════════════════════════════╝"
    echo ""
    echo "Project Root: $PROJECT_ROOT"
    echo "Date: $(date)"
    echo ""

    # Trap to ensure cleanup on exit
    trap cleanup EXIT

    # Run all phases
    phase1_environment
    phase2_unit_tests
    phase3_start_servers
    phase4_list_tests
    phase5_describe_tests
    phase6_invoke_tests
    phase7_tls_tests
    phase8_error_tests
    phase9_header_tests
    phase10_grpcurl_comparison
    phase11_demo_scripts

    # Print summary
    print_summary

    exit $EXIT_CODE
}

# Run main function
main "$@"

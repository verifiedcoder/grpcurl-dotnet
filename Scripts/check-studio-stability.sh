#!/usr/bin/env bash
# PRD-044: repeated complete Studio unit runs; a crash must never count as a pass.
# Build Release first. Usage: Scripts/check-studio-stability.sh [runs=5] [log-directory]
set -euo pipefail
runs="${1:-5}"
case "$runs" in ''|*[!0-9]*|0) echo 'runs must be a positive integer' >&2; exit 2 ;; esac
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
log_directory="${2:-$repo_root/TestResults/stability}"
mkdir -p "$log_directory"
cd "$repo_root"
for ((iteration=1; iteration<=runs; iteration++)); do
    log_file="$log_directory/run-$iteration.txt"
    exit_code=0
    dotnet test --project Tests/GrpCurl.Net.Studio.Tests.Unit -c Release --no-build --no-restore \
        -- --filter-not-trait 'Category=Performance' > "$log_file" 2>&1 || exit_code=$?
    # Some runners print a zero-failure partial summary after a native host crash.
    if grep -Eq "exit code[^[:cntrl:]]*139|Segmentation fault|SIGSEGV|fatal signal 11" "$log_file"; then
        echo "Run $iteration: native crash detected; see $log_file" >&2
        exit 139
    fi
    if ((exit_code != 0)); then
        echo "Run $iteration: failed with exit $exit_code; see $log_file" >&2
        exit "$exit_code"
    fi
    echo "Run $iteration/$runs passed: $log_file"
done

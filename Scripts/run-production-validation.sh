#!/usr/bin/env bash
# =============================================================================
# Script: run-production-validation.sh
# Purpose: Thin Unix-only wrapper around the canonical cross-platform validation
#          runner. Kept as a back-compat entry point for existing CI hooks; new
#          callers should invoke ValidationRunner directly.
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
. "${SCRIPT_DIR}/common.sh"
PROJECT_FILE="$(grpcurl_dotnet_path "${REPO_ROOT}/Scripts/ValidationRunner/ValidationRunner.csproj")"

echo "==> Delegating to Scripts/ValidationRunner (cross-platform)."
echo "    For Windows or PowerShell, run: dotnet run --project Scripts/ValidationRunner --configuration Release"

cd "${REPO_ROOT}"
exec "${GRPCURL_DOTNET_DOTNET}" run --project "${PROJECT_FILE}" --configuration Release -- --ci

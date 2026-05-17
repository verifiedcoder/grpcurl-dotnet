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

echo "==> Delegating to Scripts/ValidationRunner (cross-platform)."
echo "    For Windows or PowerShell, run: dotnet run --project Scripts/ValidationRunner --configuration Release"

cd "${REPO_ROOT}"
exec dotnet run --project Scripts/ValidationRunner --configuration Release -- --ci

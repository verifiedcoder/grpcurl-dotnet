#!/usr/bin/env bash
# Shared helpers for the numbered demonstration scripts.

GRPCURL_DOTNET_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GRPCURL_DOTNET_REPO_ROOT="$(cd "${GRPCURL_DOTNET_SCRIPT_DIR}/.." && pwd)"

grpcurl_dotnet_find_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        command -v dotnet
        return
    fi

    if command -v dotnet.exe >/dev/null 2>&1; then
        command -v dotnet.exe
        return
    fi

    echo "dotnet SDK not found on PATH. Install .NET 10 or expose dotnet/dotnet.exe to this shell." >&2
    return 127
}

GRPCURL_DOTNET_DOTNET="$(grpcurl_dotnet_find_dotnet)"

grpcurl_dotnet_is_windows_dotnet_on_wsl() {
    [[ "${GRPCURL_DOTNET_DOTNET}" == *dotnet.exe ]] && [[ -n "${WSL_DISTRO_NAME:-}" ]] && command -v wslpath >/dev/null 2>&1
}

grpcurl_dotnet_path() {
    local path="$1"

    if grpcurl_dotnet_is_windows_dotnet_on_wsl; then
        wslpath -w "$path"
        return
    fi

    printf '%s\n' "$path"
}

grpcurl_dotnet_run_project() {
    local project="$1"
    shift

    local converted_args=()
    local arg

    if grpcurl_dotnet_is_windows_dotnet_on_wsl; then
        project="$(wslpath -w "$project")"
        for arg in "$@"; do
            if [[ "$arg" == /* ]]; then
                converted_args+=("$(wslpath -w "$arg")")
            else
                converted_args+=("$arg")
            fi
        done
    else
        converted_args=("$@")
    fi

    "${GRPCURL_DOTNET_DOTNET}" run --project "$project" -- "${converted_args[@]}"
}

grpcurl_net() {
    grpcurl_dotnet_run_project "${GRPCURL_DOTNET_REPO_ROOT}/Src/GrpCurl.Net/GrpCurl.Net.csproj" "$@"
}

gql2grpc_cli() {
    grpcurl_dotnet_run_project "${GRPCURL_DOTNET_REPO_ROOT}/Src/Gql2Grpc/Gql2Grpc.csproj" "$@"
}

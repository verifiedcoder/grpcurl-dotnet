#!/usr/bin/env bash
# =============================================================================
# Script: install-go.sh
# Purpose: Developer-machine bootstrap. Installs Go, grpcurl, and the grpc-go
#          interop server into the *current user's* HOME (no sudo) so the local
#          dev environment can compare GrpCurl.Net behaviour against upstream.
# Scope:   Development machines only — NOT a production install.
# Hardening: pinned versions, checksum verification (SHA-256), HOME-scoped
#          install path, pinned grpc-go commit, no /usr/local clobber.
# =============================================================================
set -euo pipefail

GO_VERSION="1.22.0"
# Pinned SHA-256 from https://go.dev/dl/. Recompute if you change GO_VERSION.
GO_SHA256_LINUX_AMD64="f6c8a87aa03b92c4b0bf3d558e28ea03006eb29db78917daec5cfb6ec1046265"
GO_SHA256_LINUX_ARM64="6c33e52a5b26e7aa021b94475587fce80043a727a54ceb0eee2f9fc160646434"
GO_SHA256_DARWIN_AMD64="3d9568f3993ed8c4180cf2c3a93ed7a2a93cdac3f4f8d7037e7e5b14d92ba8e0"
GO_SHA256_DARWIN_ARM64="6e6b56be7378df3722d3ac2c43e1d0bcbfa0e80b6d2bd6c89f55de4eb9bff14a"

# Pin grpcurl to a known release rather than @latest so reruns are reproducible.
GRPCURL_VERSION="v1.9.1"

# Pin grpc-go to a specific commit rather than HEAD so reruns are reproducible.
GRPCGO_COMMIT="v1.66.0"

case "$(uname -s)" in
    Linux)  OS="linux";;
    Darwin) OS="darwin";;
    *)      echo "Unsupported OS: $(uname -s)" >&2; exit 1;;
esac

case "$(uname -m)" in
    x86_64|amd64)  ARCH="amd64";;
    arm64|aarch64) ARCH="arm64";;
    *)             echo "Unsupported architecture: $(uname -m)" >&2; exit 1;;
esac

GO_TARBALL="go${GO_VERSION}.${OS}-${ARCH}.tar.gz"
GO_URL="https://go.dev/dl/${GO_TARBALL}"

case "${OS}-${ARCH}" in
    linux-amd64)   EXPECTED_SHA="${GO_SHA256_LINUX_AMD64}";;
    linux-arm64)   EXPECTED_SHA="${GO_SHA256_LINUX_ARM64}";;
    darwin-amd64)  EXPECTED_SHA="${GO_SHA256_DARWIN_AMD64}";;
    darwin-arm64)  EXPECTED_SHA="${GO_SHA256_DARWIN_ARM64}";;
    *)             echo "No pinned SHA for ${OS}-${ARCH}" >&2; exit 1;;
esac

GO_PREFIX="${HOME}/.local/go-${GO_VERSION}"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

echo "=== Installing Go ${GO_VERSION} to ${GO_PREFIX} (user-scoped, no sudo) ==="
echo "Downloading ${GO_URL} ..."
curl --proto '=https' --tlsv1.2 -fsSLo "${WORK_DIR}/${GO_TARBALL}" "${GO_URL}"

echo "Verifying checksum..."
ACTUAL_SHA="$(sha256sum "${WORK_DIR}/${GO_TARBALL}" | awk '{print $1}')"

if [ "${ACTUAL_SHA}" != "${EXPECTED_SHA}" ]; then
    echo "Checksum mismatch for ${GO_TARBALL}" >&2
    echo "  expected: ${EXPECTED_SHA}" >&2
    echo "  actual:   ${ACTUAL_SHA}" >&2
    exit 1
fi

echo "Extracting..."
rm -rf "${GO_PREFIX}"
mkdir -p "${GO_PREFIX}"
tar -C "${GO_PREFIX}" --strip-components=1 -xzf "${WORK_DIR}/${GO_TARBALL}"

export GOROOT="${GO_PREFIX}"
export GOPATH="${HOME}/go"
export PATH="${GOROOT}/bin:${GOPATH}/bin:${PATH}"

mkdir -p "${GOPATH}/bin" "${HOME}/.local/bin"

echo ""
echo "=== Verifying Go installation ==="
go version

echo ""
echo "=== Installing grpcurl ${GRPCURL_VERSION} ==="
go install "github.com/fullstorydev/grpcurl/cmd/grpcurl@${GRPCURL_VERSION}"

echo ""
echo "=== Verifying grpcurl ==="
"${GOPATH}/bin/grpcurl" --version

echo ""
echo "=== Building gRPC-Go interop server (pinned ${GRPCGO_COMMIT}) ==="
INTEROP_SRC="${WORK_DIR}/grpc-go"
git clone --quiet --depth 1 --branch "${GRPCGO_COMMIT}" \
    https://github.com/grpc/grpc-go.git "${INTEROP_SRC}"
(cd "${INTEROP_SRC}/interop/server" && go build -o "${HOME}/.local/bin/grpcgo-interop-server" .)

echo ""
echo "=== Verifying interop server ==="
ls -l "${HOME}/.local/bin/grpcgo-interop-server"

cat <<EOM

==============================================
Installation complete.

Components installed (no sudo, no /usr/local edits):
  - Go ${GO_VERSION}                  at ${GO_PREFIX}
  - grpcurl ${GRPCURL_VERSION}        at ${GOPATH}/bin/grpcurl
  - gRPC-Go interop server      at \$HOME/.local/bin/grpcgo-interop-server

Add these to your shell profile to make Go available in new sessions:

    export GOROOT="${GO_PREFIX}"
    export GOPATH="\${HOME}/go"
    export PATH="\${GOROOT}/bin:\${GOPATH}/bin:\${HOME}/.local/bin:\${PATH}"

==============================================
EOM
